using Microsoft.Data.Sqlite;
using Qsearch.Core.Indexing;

namespace Qsearch.Index.Tests;

public sealed class SqliteIndexStoreTests
{
    [Theory]
    [InlineData("item_type", "ix_items_type_sort")]
    [InlineData("last_write_utc", "ix_items_modified_sort")]
    public async Task DescendingTieSort_UsesCompoundIndexWithoutTemporarySort(
        string field,
        string expectedIndex)
    {
        await using TestIndex index = await TestIndex.WithNamesAsync("report.txt");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SqliteConnection connection = new(
            $"Data Source={index.DatabasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            EXPLAIN QUERY PLAN
            SELECT id FROM items AS i
            ORDER BY i.{field} DESC, i.id DESC
            LIMIT 101;
            """;
        List<string> plan = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            plan.Add(reader.GetString(3));
        }

        Assert.Contains(plan, detail => detail.Contains(expectedIndex, StringComparison.Ordinal));
        Assert.DoesNotContain(
            plan,
            detail => detail.Contains("TEMP B-TREE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApplyBatch_SkipsAnIdenticalUpsertButAppliesMetadataChanges()
    {
        await using TestIndex index = await TestIndex.WithNamesAsync("report.txt");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SqliteConnection connection = new(
            $"Data Source={index.DatabasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand audit = connection.CreateCommand();
        audit.CommandText = """
            CREATE TABLE audit_updates (item_id TEXT NOT NULL);
            CREATE TRIGGER audit_items AFTER UPDATE ON items BEGIN
                INSERT INTO audit_updates(item_id) VALUES(new.id);
            END;
            """;
        await audit.ExecuteNonQueryAsync(cancellationToken);
        IndexItem identical = TestIndex.Item("report.txt", "item-0");
        IndexItem resized = identical with { Size = 512, ParentId = "parent" };
        await index.Store.ApplyBatchAsync(
            new IndexBatch("scope-a", [
                new IndexChange(IndexChangeKind.Upsert, "scope-a", identical),
                new IndexChange(IndexChangeKind.Upsert, "scope-a", resized),
            ]),
            cancellationToken);

        audit.CommandText = "SELECT COUNT(*) FROM audit_updates;";
        Assert.Equal(1L, Assert.IsType<long>(await audit.ExecuteScalarAsync(cancellationToken)));
        SearchPage page = await index.Store.SearchAsync(
            TestIndex.Request("report"),
            cancellationToken);
        Assert.Equal(512L, Assert.Single(page.Results).Size);
    }

    [Fact]
    public async Task ResetScope_RemovesOnlyThatVolumeAndItsCheckpoint()
    {
        await using TestIndex index = await TestIndex.CreateAsync();
        IndexItem volumeC = TestIndex.Item("c.txt", "c-item") with { ScopeId = "volume-c" };
        IndexItem volumeD = TestIndex.Item("d.txt", "d-item") with { ScopeId = "volume-d" };
        await index.Store.ApplyBatchAsync(
            new IndexBatch(
                "volume-c",
                [new IndexChange(IndexChangeKind.Upsert, "volume-c", volumeC)],
                new IndexCheckpoint("volume-c", 1, 10)),
            TestContext.Current.CancellationToken);
        await index.Store.ApplyBatchAsync(
            new IndexBatch(
                "volume-d",
                [new IndexChange(IndexChangeKind.Upsert, "volume-d", volumeD)],
                new IndexCheckpoint("volume-d", 2, 20)),
            TestContext.Current.CancellationToken);

        await index.Store.ResetScopeAsync("volume-c", TestContext.Current.CancellationToken);

        Assert.Equal(1, await index.Store.CountAsync(TestContext.Current.CancellationToken));
        Assert.Null(await index.Store.GetCheckpointAsync("volume-c", TestContext.Current.CancellationToken));
        Assert.Equal(
            20,
            (await index.Store.GetCheckpointAsync("volume-d", TestContext.Current.CancellationToken))!.NextUsn);
    }

    [Fact]
    public async Task ApplyBatchAndCheckpoint_AreAtomic()
    {
        await using TestIndex index = await TestIndex.CreateAsync();
        List<IndexChange> changes = Enumerable.Range(0, 3)
            .Select(position => TestIndex.Item($"file-{position}.txt", $"item-{position}"))
            .Select(item => new IndexChange(IndexChangeKind.Upsert, item.ScopeId, item))
            .ToList();
        IndexCheckpoint checkpoint = new("scope-a", 7, 42);

        await index.Store.ApplyBatchAsync(
            new IndexBatch("scope-a", changes, checkpoint),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, await index.Store.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            42,
            (await index.Store.GetCheckpointAsync("scope-a", TestContext.Current.CancellationToken))!.NextUsn);
    }

    [Fact]
    public async Task InvalidChange_RollsBackWholeBatch()
    {
        await using TestIndex index = await TestIndex.CreateAsync();
        IndexItem item = TestIndex.Item("visible.txt", "valid");
        IndexBatch batch = new(
            "scope-a",
            [
                new IndexChange(IndexChangeKind.Upsert, "scope-a", item),
                new IndexChange(IndexChangeKind.Upsert, "scope-a"),
            ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => index.Store.ApplyBatchAsync(batch, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, await index.Store.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReviewCache_RoundTripsExactFreshnessKey()
    {
        await using TestIndex index = await TestIndex.WithNamesAsync("review.exe");
        ReviewCacheRecord review = new(
            "item-0",
            "high",
            true,
            100,
            new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
            1);

        await index.Store.SaveReviewAsync(review, TestContext.Current.CancellationToken);

        Assert.Equal(
            review,
            await index.Store.GetReviewAsync("item-0", TestContext.Current.CancellationToken));
    }
}
