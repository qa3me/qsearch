using System.Runtime.CompilerServices;
using Qsearch.Core.Indexing;
using Qsearch.Windows.Indexing;
using Qsearch.Windows.Usn;

namespace Qsearch.Windows.Tests;

public sealed class UsnIndexSourceTests
{
    [Fact]
    public async Task JournalIdentityChange_RequiresRebuild()
    {
        IndexScope scope = Scope();
        FakeUsnReader reader = new(new UsnJournalState(200, 500, 900));
        UsnIndexSource source = new(reader, new IndexScopeFilter(scope));

        List<IndexChange> changes = await CollectAsync(
            source.WatchAsync(
                scope,
                new IndexCheckpoint(scope.Id, 100, 450),
                TestContext.Current.CancellationToken));

        Assert.Contains(changes, change => change.Diagnostic?.Code == "journal-rebuild-required");
        Assert.DoesNotContain(changes, change => change.Kind == IndexChangeKind.Upsert);
    }

    [Fact]
    public async Task CheckpointBeforeFirstUsn_RequiresRebuild()
    {
        IndexScope scope = Scope();
        FakeUsnReader reader = new(new UsnJournalState(200, 500, 900));
        UsnIndexSource source = new(reader, new IndexScopeFilter(scope));

        List<IndexChange> changes = await CollectAsync(
            source.WatchAsync(
                scope,
                new IndexCheckpoint(scope.Id, 200, 499),
                TestContext.Current.CancellationToken));

        Assert.Contains(changes, change => change.Diagnostic?.Code == "journal-rebuild-required");
    }

    [Fact]
    public async Task Scan_ReconstructsPathsAndFiltersToSelectedFolder()
    {
        IndexScope scope = Scope();
        FakeUsnReader reader = new(
            new UsnJournalState(7, 0, 100),
            [
                Record(1, 1, "", directory: true),
                Record(2, 1, "Users", directory: true),
                Record(3, 2, "QA3", directory: true),
                Record(4, 3, "گزارش.txt"),
                Record(5, 1, "Windows", directory: true),
                Record(6, 5, "outside.exe"),
            ]);
        UsnIndexSource source = new(reader, new IndexScopeFilter(scope));

        List<IndexChange> changes = await CollectAsync(
            source.ScanAsync(scope, TestContext.Current.CancellationToken));

        Assert.Contains(changes, change => change.Item?.FullPath == @"C:\Users\QA3\گزارش.txt");
        Assert.DoesNotContain(changes, change => change.Item?.Name == "outside.exe");
    }

    [Fact]
    public async Task DeleteRecord_UsesStableIdentityWithoutResolvingMissingPath()
    {
        IndexScope scope = Scope();
        UsnRecord deleted = Record(4, 3, "gone.txt", reason: UsnReason.FileDelete);
        FakeUsnReader reader = new(
            new UsnJournalState(7, 0, 100),
            [Record(1, 1, "", directory: true)],
            [new UsnReadBatch([deleted], 101, [])]);
        UsnIndexSource source = new(reader, new IndexScopeFilter(scope));

        List<IndexChange> changes = await CollectAsync(
            source.WatchAsync(
                scope,
                new IndexCheckpoint(scope.Id, 7, 100),
                TestContext.Current.CancellationToken));

        Assert.Contains(
            changes,
            change => change.Kind == IndexChangeKind.Delete && change.ItemId == "scope-a:0000000000000004");
        Assert.Contains(changes, change => change.Checkpoint?.NextUsn == 101);
    }

    [Fact]
    public async Task NativeReaderIntegration_IsOptIn()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("QSEARCH_RUN_USN_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Set QSEARCH_RUN_USN_INTEGRATION=1 to run the read-only NTFS integration test.");
        }

        WindowsUsnVolumeReader reader = new();
        UsnJournalState state = await reader.QueryJournalAsync(
            @"C:\",
            TestContext.Current.CancellationToken);
        Assert.True(state.NextUsn >= state.FirstUsn);
    }

    private static IndexScope Scope()
    {
        return new IndexScope("scope-a", @"C:\Users\QA3");
    }

    private static UsnRecord Record(
        ulong id,
        ulong parent,
        string name,
        bool directory = false,
        UsnReason reason = UsnReason.FileCreate)
    {
        return new UsnRecord(
            new FileReferenceId(id),
            new FileReferenceId(parent),
            1,
            reason,
            directory ? FileAttributes.Directory : FileAttributes.Normal,
            name,
            2);
    }

    private static async ValueTask<List<IndexChange>> CollectAsync(
        IAsyncEnumerable<IndexChange> source)
    {
        List<IndexChange> results = [];
        await foreach (IndexChange change in source)
        {
            results.Add(change);
        }

        return results;
    }

    private sealed class FakeUsnReader(
        UsnJournalState state,
        IReadOnlyList<UsnRecord>? records = null,
        IReadOnlyList<UsnReadBatch>? batches = null) : IUsnVolumeReader
    {
        public ValueTask<UsnJournalState> QueryJournalAsync(
            string volumeRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(state);
        }

        public async IAsyncEnumerable<UsnRecord> EnumerateAsync(
            string volumeRoot,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (UsnRecord record in records ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return record;
            }

            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<UsnReadBatch> ReadChangesAsync(
            string volumeRoot,
            long startUsn,
            long journalId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (UsnReadBatch batch in batches ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return batch;
            }

            await Task.CompletedTask;
        }
    }
}
