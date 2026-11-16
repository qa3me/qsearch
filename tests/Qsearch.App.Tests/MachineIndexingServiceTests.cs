using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Qsearch.App.Services;
using Qsearch.Core.Indexing;
using Qsearch.Core.Search;
using Qsearch.Core.Settings;
using Qsearch.Windows.Elevation;
using Qsearch.Windows.Usn;
using Qsearch.Windows.Volumes;

namespace Qsearch.App.Tests;

public sealed class MachineIndexingServiceTests
{
    [Fact]
    public async Task StartAsync_RequiresConsentBeforeLaunchingHelper()
    {
        FakeSettingsStore settings = new(QsearchSettings.Default);
        FakeElevatedClient elevated = new();
        await using MachineIndexingService service = CreateService(settings, elevated);

        MachineIndexStartResult result = await service.StartAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(MachineIndexStartResult.ConsentRequired, result);
        Assert.Equal(0, elevated.ConnectCount);
        Assert.Equal(IndexFreshness.Unavailable, service.Snapshot.Freshness);
    }

    [Fact]
    public async Task RetryElevation_DeclineDoesNotPersistConsentOrPromptAgain()
    {
        FakeSettingsStore settings = new(QsearchSettings.Default);
        FakeElevatedClient elevated = new() { Decline = true };
        await using MachineIndexingService service = CreateService(settings, elevated);

        MachineIndexStartResult retry = await service.RetryElevationAsync(
            TestContext.Current.CancellationToken);
        MachineIndexStartResult nextStart = await service.StartAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(MachineIndexStartResult.ElevationDeclined, retry);
        Assert.Equal(MachineIndexStartResult.ConsentRequired, nextStart);
        Assert.False(settings.Current.MachineIndexConsent);
        Assert.Equal(1, elevated.ConnectCount);
    }

    [Fact]
    public async Task RetryElevation_IndexesEveryFixedVolumeAndPublishesSnapshot()
    {
        FakeSettingsStore settings = new(QsearchSettings.Default);
        FakeElevatedClient elevated = new();
        FakeIndexStore index = new();
        FakeSourceFactory sources = new();
        await using MachineIndexingService service = CreateService(
            settings,
            elevated,
            index,
            sources);

        MachineIndexStartResult result = await service.RetryElevationAsync(
            TestContext.Current.CancellationToken);
        await service.Completion;

        Assert.Equal(MachineIndexStartResult.Started, result);
        Assert.True(settings.Current.MachineIndexConsent);
        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C:\\", "D:\\" }, elevated.LastConsent!.AllowedVolumes);
        Assert.Equal(2, service.Snapshot.TotalVolumes);
        Assert.Equal(2, service.Snapshot.FreshVolumes);
        Assert.Equal(2, service.Snapshot.IndexedItems);
        Assert.Equal(IndexFreshness.Fresh, service.Snapshot.Freshness);
        Assert.Equal(2, index.Batches.Select(batch => batch.ScopeId).Distinct().Count());
    }

    [Fact]
    public async Task StartReturnsBeforeScanAndFirstCommittedBatchIsImmediatelySearchable()
    {
        FakeSettingsStore settings = new(QsearchSettings.Default with { MachineIndexConsent = true });
        FakeElevatedClient elevated = new();
        FakeIndexStore index = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingSourceFactory sources = new(release.Task);
        await using MachineIndexingService service = CreateService(
            settings,
            elevated,
            index,
            sources,
            [Volume("C:\\")]);

        MachineIndexStartResult result = await service.StartAsync(
            TestContext.Current.CancellationToken);
        await index.FirstBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(MachineIndexStartResult.Started, result);
        Assert.Equal(1, await index.CountAsync(TestContext.Current.CancellationToken));
        SearchPage page = await index.SearchAsync(
            new SearchRequest(SearchQuery.Empty),
            TestContext.Current.CancellationToken);
        Assert.Single(page.Results);

        release.TrySetResult();
        await service.Completion;
    }

    [Fact]
    public async Task Dispose_CancelsWatchAndClosesElevatedSession()
    {
        FakeSettingsStore settings = new(QsearchSettings.Default with { MachineIndexConsent = true });
        FakeElevatedClient elevated = new();
        MachineIndexingService service = CreateService(
            settings,
            elevated,
            sourceFactory: new WatchingSourceFactory(),
            volumes: [Volume("C:\\")]);

        Assert.Equal(
            MachineIndexStartResult.Started,
            await service.StartAsync(TestContext.Current.CancellationToken));
        await service.DisposeAsync();

        Assert.True(elevated.Session.Disposed);
        Assert.True(service.Completion.IsCompleted);
    }

    private static MachineIndexingService CreateService(
        FakeSettingsStore settings,
        FakeElevatedClient elevated,
        FakeIndexStore? index = null,
        IMachineIndexSourceFactory? sourceFactory = null,
        IReadOnlyList<IndexableVolume>? volumes = null)
    {
        return new MachineIndexingService(
            new FakeVolumeDiscovery(volumes ?? [Volume("C:\\"), Volume("D:\\")]),
            elevated,
            index ?? new FakeIndexStore(),
            settings,
            sourceFactory ?? new FakeSourceFactory(),
            "S-1-5-21-test",
            TimeProvider.System);
    }

    private static IndexableVolume Volume(string root) =>
        new(root, root[..1], DriveType.Fixed, "NTFS");

    private sealed class FakeVolumeDiscovery(IReadOnlyList<IndexableVolume> volumes)
        : IFixedVolumeDiscovery
    {
        public IReadOnlyList<IndexableVolume> Discover() => volumes;
    }

    private sealed class FakeSettingsStore(QsearchSettings current) : ILocalSettingsStore
    {
        public QsearchSettings Current { get; private set; } = current;

        public ValueTask<QsearchSettings> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Current);

        public ValueTask SaveAsync(QsearchSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeElevatedClient : IElevatedIndexerClient
    {
        public int ConnectCount { get; private set; }
        public bool Decline { get; init; }
        public VolumeConsent? LastConsent { get; private set; }
        public FakeSession Session { get; } = new();

        public ValueTask<IElevatedIndexerSession> ConnectAsync(
            VolumeConsent consent,
            CancellationToken cancellationToken)
        {
            ConnectCount++;
            LastConsent = consent;
            return Decline
                ? ValueTask.FromException<IElevatedIndexerSession>(new ElevationDeclinedException())
                : ValueTask.FromResult<IElevatedIndexerSession>(Session);
        }
    }

    private sealed class FakeSession : IElevatedIndexerSession
    {
        public bool Disposed { get; private set; }

        public ValueTask<UsnJournalState> QueryJournalAsync(string volumeRoot, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new UsnJournalState(1, 0, 1));

        public async IAsyncEnumerable<UsnRecord> EnumerateAsync(string volumeRoot, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<UsnReadBatch> ReadChangesAsync(string volumeRoot, long startUsn, long journalId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private class FakeSourceFactory : IMachineIndexSourceFactory
    {
        public virtual IIndexSource Create(IElevatedIndexerSession session, IndexScope scope) =>
            new OneItemSource(scope.Id, scope.RootPath);
    }

    private sealed class BlockingSourceFactory(Task release) : FakeSourceFactory
    {
        public override IIndexSource Create(IElevatedIndexerSession session, IndexScope scope) =>
            new OneItemSource(scope.Id, scope.RootPath, release);
    }

    private sealed class WatchingSourceFactory : IMachineIndexSourceFactory
    {
        public IIndexSource Create(IElevatedIndexerSession session, IndexScope scope) =>
            new WatchingSource(scope.Id, scope.RootPath);
    }

    private sealed class OneItemSource(string scopeId, string root, Task? release = null) : IIndexSource
    {
        public async IAsyncEnumerable<IndexChange> ScanAsync(IndexScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new IndexChange(IndexChangeKind.Upsert, scopeId, Item(scopeId, root));
            if (release is not null)
            {
                await release.WaitAsync(cancellationToken);
            }

            yield return new IndexChange(
                IndexChangeKind.Checkpoint,
                scopeId,
                Checkpoint: new IndexCheckpoint(scopeId, 1, 1));
        }

        public async IAsyncEnumerable<IndexChange> WatchAsync(IndexScope scope, IndexCheckpoint? checkpoint, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class WatchingSource(string scopeId, string root) : IIndexSource
    {
        public async IAsyncEnumerable<IndexChange> ScanAsync(IndexScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new IndexChange(IndexChangeKind.Upsert, scopeId, Item(scopeId, root));
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<IndexChange> WatchAsync(IndexScope scope, IndexCheckpoint? checkpoint, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    private static IndexItem Item(string scopeId, string root) => new(
        scopeId + ":item",
        scopeId,
        "item.txt",
        "item.txt",
        Path.Combine(root, "item.txt"),
        null,
        IndexItemType.File,
        1,
        DateTimeOffset.UnixEpoch,
        FileAttributes.Normal);

    private sealed class FakeIndexStore : IIndexStore
    {
        private readonly ConcurrentDictionary<string, IndexItem> _items = [];
        private readonly ConcurrentDictionary<string, IndexCheckpoint> _checkpoints = [];

        public ConcurrentQueue<IndexBatch> Batches { get; } = [];
        public TaskCompletionSource FirstBatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ApplyBatchAsync(IndexBatch batch, CancellationToken cancellationToken)
        {
            Batches.Enqueue(batch);
            foreach (IndexChange change in batch.Changes)
            {
                if (change.Item is not null)
                {
                    _items[change.Item.Id] = change.Item;
                }
                else if (change.ItemId is not null)
                {
                    _items.TryRemove(change.ItemId, out _);
                }
            }

            if (batch.Checkpoint is not null)
            {
                _checkpoints[batch.ScopeId] = batch.Checkpoint;
            }

            FirstBatch.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask<SearchPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SearchPage(
                _items.Values.Select(item => new SearchResult(item.Id, item.ScopeId, item.Name, item.FullPath, item.Type, item.Size, item.LastWriteTimeUtc)).ToArray(),
                IndexFreshness.Fresh));

        public ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult((long)_items.Count);

        public ValueTask ResetScopeAsync(string scopeId, CancellationToken cancellationToken)
        {
            foreach ((string id, IndexItem item) in _items)
            {
                if (string.Equals(item.ScopeId, scopeId, StringComparison.Ordinal))
                {
                    _items.TryRemove(id, out _);
                }
            }

            _checkpoints.TryRemove(scopeId, out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IndexCheckpoint?> GetCheckpointAsync(string scopeId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_checkpoints.TryGetValue(scopeId, out IndexCheckpoint? checkpoint) ? checkpoint : null);

        public ValueTask SaveCheckpointAsync(IndexCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            _checkpoints[checkpoint.ScopeId] = checkpoint;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ReviewCacheRecord?> GetReviewAsync(string itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ReviewCacheRecord?>(null);

        public ValueTask SaveReviewAsync(ReviewCacheRecord review, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
