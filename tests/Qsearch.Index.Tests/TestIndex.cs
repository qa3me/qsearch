using Microsoft.Data.Sqlite;
using Qsearch.Core.Indexing;
using Qsearch.Core.Search;
using Qsearch.Core.Text;
using Qsearch.Index.Sqlite;

namespace Qsearch.Index.Tests;

internal sealed class TestIndex : IAsyncDisposable
{
    private TestIndex(string directoryPath, string databasePath, SqliteIndexStore store)
    {
        DirectoryPath = directoryPath;
        DatabasePath = databasePath;
        Store = store;
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public SqliteIndexStore Store { get; }

    public static async ValueTask<TestIndex> CreateAsync()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), "Qsearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        string databasePath = Path.Combine(directoryPath, "index.db");
        SqliteIndexStore store = new(databasePath, TimeProvider.System);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        return new TestIndex(directoryPath, databasePath, store);
    }

    public static async ValueTask<TestIndex> WithNamesAsync(params string[] names)
    {
        TestIndex index = await CreateAsync();
        List<IndexChange> changes = [];
        for (int position = 0; position < names.Length; position++)
        {
            IndexItem item = Item(names[position], $"item-{position}");
            changes.Add(new IndexChange(IndexChangeKind.Upsert, item.ScopeId, item));
        }

        await index.Store.ApplyBatchAsync(
            new IndexBatch("scope-a", changes),
            TestContext.Current.CancellationToken);
        return index;
    }

    public static IndexItem Item(
        string name,
        string id,
        string root = @"C:\scope",
        IndexItemType type = IndexItemType.File,
        long size = 100)
    {
        string path = Path.Combine(root, name);
        return new IndexItem(
            id,
            "scope-a",
            name,
            SearchTextNormalizer.Normalize(name),
            path,
            null,
            type,
            size,
            new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
            FileAttributes.Normal);
    }

    public static SearchRequest Request(string text, int limit = 100)
    {
        return new SearchRequest(SearchQueryParser.Parse(text), limit);
    }

    public async ValueTask SeedReviewAsync(
        string itemId,
        string state,
        bool complete,
        long size = 100,
        string? lastWriteUtc = null)
    {
        await using SqliteConnection connection = new($"Data Source={DatabasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO review_cache(item_id, state, is_complete, size, last_write_utc, policy_version)
            VALUES($item_id, $state, $is_complete, $size, $last_write_utc, 1)
            ON CONFLICT(item_id) DO UPDATE SET
                state = excluded.state,
                is_complete = excluded.is_complete,
                size = excluded.size,
                last_write_utc = excluded.last_write_utc,
                policy_version = excluded.policy_version;
            """;
        command.Parameters.AddWithValue("$item_id", itemId);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$is_complete", complete ? 1 : 0);
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue(
            "$last_write_utc",
            lastWriteUtc ?? "2026-11-14T12:00:00.0000000+00:00");
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Store.DisposeAsync();
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
