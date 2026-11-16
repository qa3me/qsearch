using System.IO;
using System.Security.Cryptography;
using System.Text;
using Qsearch.Core.Indexing;
using Qsearch.Windows.FileSystem;
using Qsearch.Windows.Indexing;

namespace Qsearch.App.Services;

public interface IFolderIndexingService
{
    ValueTask IndexFolderAsync(string path, CancellationToken cancellationToken);
}

public interface IDefaultFolderIndexingService : IFolderIndexingService, IAsyncDisposable
{
    event Action<IndexingSnapshot>? SnapshotChanged;

    IndexingSnapshot Snapshot { get; }

    Task Completion { get; }

    ValueTask StartDefaultAsync(CancellationToken cancellationToken);
}

public sealed class FolderIndexingService : IDefaultFolderIndexingService
{
    private readonly IIndexStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IDefaultFolderDiscovery _defaultFolders;
    private readonly object _snapshotLock = new();
    private readonly Queue<double> _recentRates = new();
    private CancellationTokenSource? _runCancellation;
    private Task _completion = Task.CompletedTask;
    private long _lastCount;
    private DateTimeOffset _lastRateAt;
    private bool _disposed;

    public FolderIndexingService(
        IIndexStore store,
        TimeProvider timeProvider,
        IDefaultFolderDiscovery? defaultFolders = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _timeProvider = timeProvider;
        _defaultFolders = defaultFolders ?? new DefaultFolderDiscovery();
        _lastRateAt = timeProvider.GetUtcNow();
    }

    public event Action<IndexingSnapshot>? SnapshotChanged;

    public IndexingSnapshot Snapshot { get; private set; } =
        new(0, 0, 0, IndexFreshness.Unavailable, null, []);

    public Task Completion => _completion;

    public async ValueTask StartDefaultAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_completion.IsCompleted)
        {
            return;
        }

        IReadOnlyList<string> roots = DefaultFolderSet.Normalize(_defaultFolders.Discover());
        _lastCount = await _store.CountAsync(cancellationToken);
        _lastRateAt = _timeProvider.GetUtcNow();
        _recentRates.Clear();
        if (roots.Count == 0)
        {
            Publish(new IndexingSnapshot(
                _lastCount,
                0,
                0,
                IndexFreshness.Fresh,
                1,
                []));
            return;
        }

        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Publish(new IndexingSnapshot(
            _lastCount,
            roots.Count,
            0,
            IndexFreshness.CatchingUp,
            0,
            []));
        CancellationToken runToken = _runCancellation.Token;
        _completion = Task.Run(() => RunDefaultFoldersAsync(roots, runToken), CancellationToken.None);
    }

    public async ValueTask IndexFolderAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string scopeId = CreateFolderScopeId(fullPath);
        IndexScope scope = new(scopeId, fullPath);
        FileSystemCrawler crawler = new(new PhysicalFileSystemFacade());
        IndexCoordinator coordinator = new(_store, _timeProvider);
        await coordinator.RunAsync(scope, crawler, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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

        GC.SuppressFinalize(this);
    }

    private async Task RunDefaultFoldersAsync(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken)
    {
        int completed = 0;
        TrackingIndexStore trackingStore = new(
            _store,
            () => RefreshCountAsync(roots.Count, completed));
        foreach (string root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                IndexScope scope = new(CreateVolumeScopeId(root), root);
                IndexCoordinator coordinator = new(trackingStore, _timeProvider);
                await coordinator.RunAsync(
                    scope,
                    new FileSystemCrawler(new PhysicalFileSystemFacade()),
                    cancellationToken);
                completed++;
                await RefreshCountAsync(roots.Count, completed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                completed++;
                Publish(Snapshot with
                {
                    FreshVolumes = completed,
                    Freshness = IndexFreshness.Stale,
                    Progress = (double)completed / roots.Count,
                });
            }
        }

        Publish(Snapshot with
        {
            FreshVolumes = completed,
            Freshness = Snapshot.Freshness == IndexFreshness.Stale
                ? IndexFreshness.Stale
                : IndexFreshness.Fresh,
            Progress = 1,
        });
    }

    private async ValueTask RefreshCountAsync(int total, int completed)
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
            PublishLocked(Snapshot with
            {
                IndexedItems = count,
                TotalVolumes = total,
                FreshVolumes = completed,
                Freshness = completed == total
                    ? IndexFreshness.Fresh
                    : IndexFreshness.CatchingUp,
                Progress = total == 0 ? 1 : (double)completed / total,
                RecentRates = _recentRates.ToArray(),
            });
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

    private static string CreateFolderScopeId(string path)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(path.ToUpperInvariant());
        return "folder-" + Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
    }

    private static string CreateVolumeScopeId(string path)
    {
        string root = Path.GetPathRoot(path) ?? path;
        byte[] bytes = Encoding.UTF8.GetBytes(root.ToUpperInvariant());
        return "volume-" + Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
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
