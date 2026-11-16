using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Qsearch.Core.Indexing;
using Qsearch.Core.Settings;
using Qsearch.Windows.Elevation;
using Qsearch.Windows.Indexing;
using Qsearch.Windows.Usn;
using Qsearch.Windows.Volumes;

namespace Qsearch.App.Services;

public interface IMachineIndexSourceFactory
{
    IIndexSource Create(IElevatedIndexerSession session, IndexScope scope);
}

public sealed class UsnMachineIndexSourceFactory : IMachineIndexSourceFactory
{
    public IIndexSource Create(IElevatedIndexerSession session, IndexScope scope)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scope);
        return new UsnIndexSource(session, new IndexScopeFilter(scope));
    }
}

public sealed class MachineIndexingService : IMachineIndexingService
{
    private readonly IFixedVolumeDiscovery _volumeDiscovery;
    private readonly IElevatedIndexerClient _elevatedClient;
    private readonly IIndexStore _store;
    private readonly ILocalSettingsStore _settingsStore;
    private readonly IMachineIndexSourceFactory _sourceFactory;
    private readonly string _userSid;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _snapshotLock = new();
    private readonly HashSet<string> _freshScopes = new(StringComparer.Ordinal);
    private readonly Queue<double> _recentRates = new();
    private CancellationTokenSource? _runCancellation;
    private IElevatedIndexerSession? _session;
    private Task _completion = Task.CompletedTask;
    private long _lastCount;
    private DateTimeOffset _lastRateAt;
    private bool _disposed;

    public MachineIndexingService(
        IFixedVolumeDiscovery volumeDiscovery,
        IElevatedIndexerClient elevatedClient,
        IIndexStore store,
        ILocalSettingsStore settingsStore,
        IMachineIndexSourceFactory sourceFactory,
        string userSid,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(volumeDiscovery);
        ArgumentNullException.ThrowIfNull(elevatedClient);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(sourceFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _volumeDiscovery = volumeDiscovery;
        _elevatedClient = elevatedClient;
        _store = store;
        _settingsStore = settingsStore;
        _sourceFactory = sourceFactory;
        _userSid = userSid;
        _timeProvider = timeProvider;
        _lastRateAt = timeProvider.GetUtcNow();
    }

    public event Action<IndexingSnapshot>? SnapshotChanged;

    public IndexingSnapshot Snapshot { get; private set; } =
        new(0, 0, 0, IndexFreshness.Unavailable, null, []);

    public Task Completion => _completion;

    public async ValueTask<MachineIndexStartResult> StartAsync(
        CancellationToken cancellationToken)
    {
        QsearchSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        if (!settings.MachineIndexConsent)
        {
            return MachineIndexStartResult.ConsentRequired;
        }

        return await BeginAsync(settings, cancellationToken);
    }

    public async ValueTask<MachineIndexStartResult> RetryElevationAsync(
        CancellationToken cancellationToken)
    {
        QsearchSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        return await BeginAsync(settings, cancellationToken);
    }

    public async ValueTask RebuildAsync(CancellationToken cancellationToken)
    {
        QsearchSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        await StopRunAsync();
        MachineIndexStartResult result = await BeginAsync(settings, cancellationToken);
        if (result is not MachineIndexStartResult.Started and not MachineIndexStartResult.NoVolumes)
        {
            throw new InvalidOperationException($"The machine index could not be rebuilt ({result}).");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopRunAsync();
        _lifecycle.Dispose();
        GC.SuppressFinalize(this);
    }

    private async ValueTask<MachineIndexStartResult> BeginAsync(
        QsearchSettings settings,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (!_completion.IsCompleted)
            {
                return MachineIndexStartResult.Started;
            }

            IReadOnlyList<IndexableVolume> volumes = _volumeDiscovery.Discover();
            if (volumes.Count == 0)
            {
                Publish(new IndexingSnapshot(0, 0, 0, IndexFreshness.Unavailable, null, []));
                return MachineIndexStartResult.NoVolumes;
            }

            HashSet<string> allowedVolumes = volumes
                .Select(volume => volume.RootPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IElevatedIndexerSession session;
            try
            {
                session = await _elevatedClient.ConnectAsync(
                    new VolumeConsent(_userSid, allowedVolumes),
                    cancellationToken);
            }
            catch (ElevationDeclinedException)
            {
                return MachineIndexStartResult.ElevationDeclined;
            }

            _session = session;
            if (!settings.MachineIndexConsent)
            {
                await _settingsStore.SaveAsync(
                    settings with { MachineIndexConsent = true },
                    cancellationToken);
            }

            _freshScopes.Clear();
            _recentRates.Clear();
            _lastCount = await _store.CountAsync(cancellationToken);
            _lastRateAt = _timeProvider.GetUtcNow();
            _runCancellation = new CancellationTokenSource();
            Publish(new IndexingSnapshot(
                _lastCount,
                volumes.Count,
                0,
                IndexFreshness.CatchingUp,
                0,
                []));
            CancellationToken runToken = _runCancellation.Token;
            _completion = Task.Run(
                () => RunVolumesAsync(session, volumes, runToken),
                CancellationToken.None);
            return MachineIndexStartResult.Started;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session = null;
            }

            Publish(Snapshot with { Freshness = IndexFreshness.Unavailable });
            return MachineIndexStartResult.Unavailable;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task RunVolumesAsync(
        IElevatedIndexerSession session,
        IReadOnlyList<IndexableVolume> volumes,
        CancellationToken cancellationToken)
    {
        TrackingIndexStore trackingStore = new(_store, RefreshCountAsync);
        Task[] tasks = volumes
            .Select(volume => RunVolumeAsync(session, trackingStore, volume, cancellationToken))
            .ToArray();
        await Task.WhenAll(tasks);
        await RefreshCountAsync(forceFresh: true);
    }

    private async Task RunVolumeAsync(
        IElevatedIndexerSession session,
        IIndexStore trackingStore,
        IndexableVolume volume,
        CancellationToken cancellationToken)
    {
        string scopeId = CreateScopeId(volume.RootPath);
        IndexScope scope = new(
            scopeId,
            volume.RootPath,
            IncludeHidden: true,
            IncludeSystem: true);
        IndexCoordinator coordinator = new(trackingStore, _timeProvider);
        coordinator.StateChanged += state => ApplyCoordinatorState(scopeId, state);
        try
        {
            await coordinator.RunAsync(
                scope,
                _sourceFactory.Create(session, scope),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            lock (_snapshotLock)
            {
                _freshScopes.Remove(scopeId);
                PublishLocked(Snapshot with { Freshness = IndexFreshness.Stale });
            }
        }
    }

    private void ApplyCoordinatorState(string scopeId, IndexCoordinatorState state)
    {
        lock (_snapshotLock)
        {
            if (state.Freshness == IndexFreshness.Fresh)
            {
                _freshScopes.Add(scopeId);
            }
            else
            {
                _freshScopes.Remove(scopeId);
            }

            int fresh = _freshScopes.Count;
            IndexFreshness freshness = fresh == Snapshot.TotalVolumes
                ? IndexFreshness.Fresh
                : state.Freshness == IndexFreshness.Stale
                    ? IndexFreshness.Stale
                    : IndexFreshness.CatchingUp;
            PublishLocked(Snapshot with
            {
                FreshVolumes = fresh,
                Freshness = freshness,
                Progress = Snapshot.TotalVolumes == 0
                    ? null
                    : (double)fresh / Snapshot.TotalVolumes,
            });
        }
    }

    private ValueTask RefreshCountAsync() => RefreshCountAsync(forceFresh: false);

    private async ValueTask RefreshCountAsync(bool forceFresh)
    {
        long count = await _store.CountAsync(CancellationToken.None);
        lock (_snapshotLock)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            double seconds = (now - _lastRateAt).TotalSeconds;
            if (seconds > 0 && count >= _lastCount)
            {
                _recentRates.Enqueue((count - _lastCount) / seconds);
                while (_recentRates.Count > 20)
                {
                    _recentRates.Dequeue();
                }
            }

            _lastCount = count;
            _lastRateAt = now;
            IndexingSnapshot next = Snapshot with
            {
                IndexedItems = count,
                RecentRates = _recentRates.ToArray(),
            };
            if (forceFresh && next.FreshVolumes == next.TotalVolumes)
            {
                next = next with { Freshness = IndexFreshness.Fresh, Progress = 1 };
            }

            PublishLocked(next);
        }
    }

    private async ValueTask StopRunAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_runCancellation is not null)
            {
                await _runCancellation.CancelAsync();
                try
                {
                    await _completion;
                }
                catch (OperationCanceledException)
                {
                }

                _runCancellation.Dispose();
                _runCancellation = null;
            }

            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session = null;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void Publish(IndexingSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            PublishLocked(snapshot);
        }
    }

    private void PublishLocked(IndexingSnapshot snapshot)
    {
        Snapshot = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }

    private static string CreateScopeId(string rootPath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rootPath.ToUpperInvariant()));
        return "volume-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class TrackingIndexStore(
        IIndexStore inner,
        Func<ValueTask> batchCommitted) : IIndexStore
    {
        public ValueTask InitializeAsync(CancellationToken cancellationToken) =>
            inner.InitializeAsync(cancellationToken);

        public async ValueTask ApplyBatchAsync(IndexBatch batch, CancellationToken cancellationToken)
        {
            await inner.ApplyBatchAsync(batch, cancellationToken);
            await batchCommitted();
        }

        public ValueTask<SearchPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken) =>
            inner.SearchAsync(request, cancellationToken);

        public ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
            inner.CountAsync(cancellationToken);

        public ValueTask ResetScopeAsync(string scopeId, CancellationToken cancellationToken) =>
            inner.ResetScopeAsync(scopeId, cancellationToken);

        public ValueTask<IndexCheckpoint?> GetCheckpointAsync(string scopeId, CancellationToken cancellationToken) =>
            inner.GetCheckpointAsync(scopeId, cancellationToken);

        public ValueTask SaveCheckpointAsync(IndexCheckpoint checkpoint, CancellationToken cancellationToken) =>
            inner.SaveCheckpointAsync(checkpoint, cancellationToken);

        public ValueTask<ReviewCacheRecord?> GetReviewAsync(string itemId, CancellationToken cancellationToken) =>
            inner.GetReviewAsync(itemId, cancellationToken);

        public ValueTask SaveReviewAsync(ReviewCacheRecord review, CancellationToken cancellationToken) =>
            inner.SaveReviewAsync(review, cancellationToken);
    }
}
