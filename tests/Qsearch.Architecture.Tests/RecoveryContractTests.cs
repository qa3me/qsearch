using Qsearch.Core.Indexing;
using Qsearch.Index.Sqlite;

namespace Qsearch.Architecture.Tests;

public sealed class RecoveryContractTests
{
    [Fact]
    public async Task CancelledBatch_DoesNotAdvanceCheckpoint()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Qsearch.Architecture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "index.db");
        try
        {
            await using SqliteIndexStore store = new(databasePath, TimeProvider.System);
            await store.InitializeAsync(TestContext.Current.CancellationToken);
            IndexCheckpoint original = new("scope-a", 7, 10);
            await store.SaveCheckpointAsync(original, TestContext.Current.CancellationToken);
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.ApplyBatchAsync(
                    new IndexBatch("scope-a", [], new IndexCheckpoint("scope-a", 7, 20)),
                    cancellation.Token).AsTask());

            IndexCheckpoint saved = (await store.GetCheckpointAsync(
                "scope-a",
                TestContext.Current.CancellationToken))!;
            Assert.Equal(original.ScopeId, saved.ScopeId);
            Assert.Equal(original.JournalId, saved.JournalId);
            Assert.Equal(original.NextUsn, saved.NextUsn);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
