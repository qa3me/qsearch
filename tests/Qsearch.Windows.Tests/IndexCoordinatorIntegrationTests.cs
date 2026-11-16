using System.Runtime.CompilerServices;
using Qsearch.Core.Indexing;
using Qsearch.Core.Search;
using Qsearch.Core.Text;
using Qsearch.Index.Sqlite;
using Qsearch.Windows.FileSystem;
using Qsearch.Windows.Indexing;

namespace Qsearch.Windows.Tests;

public sealed class IndexCoordinatorIntegrationTests
{
    [Fact]
    public async Task JournalRollover_RebuildsOnlyItsCoordinatorScope()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Qsearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using SqliteIndexStore store = new(Path.Combine(directory, "rollover.db"), TimeProvider.System);
        try
        {
            await store.InitializeAsync(TestContext.Current.CancellationToken);
            IndexScope scope = new("volume-c", @"C:\");
            RolloverSource source = new();
            IndexCoordinator coordinator = new(store, TimeProvider.System);

            await coordinator.RunAsync(scope, source, TestContext.Current.CancellationToken);

            Assert.Equal(2, source.ScanCount);
            Assert.Equal(IndexFreshness.Fresh, coordinator.State.Freshness);
            Assert.Equal(1, await store.CountAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CrawlSearchRenameAndDelete_StayConsistent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Qsearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "index.db");
        string persianPath = Path.Combine(directory, "گزارش.txt");
        string englishPath = Path.Combine(directory, "notes.txt");
        await File.WriteAllTextAsync(persianPath, "local", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(englishPath, "local", TestContext.Current.CancellationToken);

        await using SqliteIndexStore store = new(databasePath, TimeProvider.System);
        try
        {
            await store.InitializeAsync(TestContext.Current.CancellationToken);
            IndexScope scope = new("scope-a", directory);
            IndexCoordinator coordinator = new(store, TimeProvider.System);
            await coordinator.RunAsync(
                scope,
                new FileSystemCrawler(new PhysicalFileSystemFacade()),
                TestContext.Current.CancellationToken);

            SearchPage persian = await store.SearchAsync(Request("گزارش"), TestContext.Current.CancellationToken);
            SearchPage english = await store.SearchAsync(Request("notes"), TestContext.Current.CancellationToken);
            Assert.Single(persian.Results);
            Assert.Single(english.Results);

            string renamedPath = Path.Combine(directory, "یادداشت.txt");
            File.Move(persianPath, renamedPath);
            File.Delete(englishPath);
            IndexItem renamed = ItemFromPath(scope.Id, renamedPath, persian.Results[0].Id);
            ScriptedIndexSource changes = new(
                [
                    new IndexChange(IndexChangeKind.Upsert, scope.Id, renamed),
                    new IndexChange(IndexChangeKind.Delete, scope.Id, ItemId: english.Results[0].Id),
                ]);
            await coordinator.RunAsync(scope, changes, TestContext.Current.CancellationToken);

            Assert.Empty((await store.SearchAsync(
                Request("گزارش"),
                TestContext.Current.CancellationToken)).Results);
            Assert.Empty((await store.SearchAsync(
                Request("notes"),
                TestContext.Current.CancellationToken)).Results);
            Assert.Single((await store.SearchAsync(
                Request("یادداشت"),
                TestContext.Current.CancellationToken)).Results);
            Assert.Equal(IndexFreshness.Fresh, coordinator.State.Freshness);
        }
        finally
        {
            await store.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ContinuousChanges_FlushBeforeTheSourceGoesIdle()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Qsearch.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            BatchClock clock = new();
            await using SqliteIndexStore store = new(Path.Combine(directory, "index.db"), clock);
            await store.InitializeAsync(cancellationToken);
            IndexScope scope = new("scope-a", directory);
            ContinuousChangeSource source = new(clock);
            Task run = new IndexCoordinator(store, clock)
                .RunAsync(scope, source, cancellationToken)
                .AsTask();
            try
            {
                await source.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                Assert.Equal(3, await store.CountAsync(cancellationToken));
            }
            finally
            {
                source.Release.TrySetResult();
                await run;
            }

            Assert.Equal(4, await store.CountAsync(cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SearchRequest Request(string text)
    {
        return new SearchRequest(SearchQueryParser.Parse(text));
    }

    private static IndexItem ItemFromPath(string scopeId, string path, string id)
    {
        FileInfo info = new(path);
        return new IndexItem(
            id,
            scopeId,
            info.Name,
            SearchTextNormalizer.Normalize(info.Name),
            info.FullName,
            null,
            IndexItemType.File,
            info.Length,
            info.LastWriteTimeUtc,
            info.Attributes);
    }

    private sealed class ContinuousChangeSource(BatchClock clock) : IIndexSource
    {
        public TaskCompletionSource Waiting { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<IndexChange> ScanAsync(
            IndexScope scope,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (int index = 0; index < 4; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                clock.AdvanceBy(TimeSpan.FromMilliseconds(40));
                string name = $"file-{index}.txt";
                yield return new IndexChange(
                    IndexChangeKind.Upsert,
                    scope.Id,
                    new IndexItem(
                        name,
                        scope.Id,
                        name,
                        name,
                        Path.Combine(scope.RootPath, name),
                        null,
                        IndexItemType.File,
                        1,
                        DateTimeOffset.UnixEpoch,
                        FileAttributes.Normal));
            }

            Waiting.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        public async IAsyncEnumerable<IndexChange> WatchAsync(
            IndexScope scope,
            IndexCheckpoint? checkpoint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class BatchClock : TimeProvider
    {
        private readonly List<BatchTimer> _timers = [];
        private TimeSpan _elapsed;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + _elapsed;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            BatchTimer timer = new(callback, state, _elapsed + dueTime);
            _timers.Add(timer);
            return timer;
        }

        public void AdvanceBy(TimeSpan elapsed)
        {
            _elapsed += elapsed;
            foreach (BatchTimer timer in _timers)
            {
                timer.FireIfDue(_elapsed);
            }
        }

        private sealed class BatchTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime) : ITimer
        {
            private int _finished;

            public bool Change(TimeSpan dueTime, TimeSpan period) =>
                throw new NotSupportedException();

            public void Dispose() => Interlocked.Exchange(ref _finished, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireIfDue(TimeSpan elapsed)
            {
                if (elapsed >= dueTime && Interlocked.Exchange(ref _finished, 1) == 0)
                {
                    callback(state);
                }
            }
        }
    }

    private sealed class ScriptedIndexSource(IReadOnlyList<IndexChange> changes) : IIndexSource
    {
        public async IAsyncEnumerable<IndexChange> ScanAsync(
            IndexScope scope,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<IndexChange> WatchAsync(
            IndexScope scope,
            IndexCheckpoint? checkpoint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (IndexChange change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return change;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class RolloverSource : IIndexSource
    {
        public int ScanCount { get; private set; }

        public async IAsyncEnumerable<IndexChange> ScanAsync(
            IndexScope scope,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ScanCount++;
            string name = $"scan-{ScanCount}.txt";
            yield return new IndexChange(
                IndexChangeKind.Upsert,
                scope.Id,
                new IndexItem(
                    name,
                    scope.Id,
                    name,
                    name,
                    Path.Combine(scope.RootPath, name),
                    null,
                    IndexItemType.File,
                    1,
                    DateTimeOffset.UnixEpoch,
                    FileAttributes.Normal));
            yield return new IndexChange(
                IndexChangeKind.Checkpoint,
                scope.Id,
                Checkpoint: new IndexCheckpoint(scope.Id, ScanCount, ScanCount));
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<IndexChange> WatchAsync(
            IndexScope scope,
            IndexCheckpoint? checkpoint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (ScanCount == 1)
            {
                yield return new IndexChange(
                    IndexChangeKind.Diagnostic,
                    scope.Id,
                    Diagnostic: new IndexDiagnostic(
                        "journal-rebuild-required",
                        "The journal rolled over."));
            }

            await Task.CompletedTask;
        }
    }
}
