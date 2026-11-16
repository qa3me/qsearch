using Qsearch.App.Services;
using Qsearch.Core.Indexing;
using Qsearch.Core.Search;
using Qsearch.Index.Sqlite;

namespace Qsearch.App.Tests;

public sealed class FolderIndexingServiceTests
{
    [Fact]
    public void NormalizeDefaultFolders_RemovesMissingNestedAndDuplicatePaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "Qsearch.DefaultFolders", Guid.NewGuid().ToString("N"));
        string documents = Path.Combine(root, "Documents");
        string nested = Path.Combine(documents, "Nested");
        Directory.CreateDirectory(nested);
        try
        {
            IReadOnlyList<string> result = DefaultFolderSet.Normalize(
                [documents, documents.ToUpperInvariant(), nested, Path.Combine(root, "Missing")]);

            Assert.Equal([Path.GetFullPath(documents)], result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StartDefaultAsync_IndexesEveryDiscoveredFolderAndPublishesCompletedProgress()
    {
        string root = Path.Combine(Path.GetTempPath(), "Qsearch.DefaultIndex", Guid.NewGuid().ToString("N"));
        string documents = Path.Combine(root, "Documents");
        string pictures = Path.Combine(root, "Pictures");
        Directory.CreateDirectory(documents);
        Directory.CreateDirectory(pictures);
        await File.WriteAllTextAsync(
            Path.Combine(documents, "notes.txt"),
            "local",
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(pictures, "photo.jpg"),
            [1, 2, 3],
            TestContext.Current.CancellationToken);

        await using SqliteIndexStore store = new(Path.Combine(root, "qsearch.db"), TimeProvider.System);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        await using FolderIndexingService service = new(
            store,
            TimeProvider.System,
            new StaticDefaultFolderDiscovery([documents, pictures]));
        List<IndexingSnapshot> snapshots = [];
        service.SnapshotChanged += snapshots.Add;
        try
        {
            await service.StartDefaultAsync(TestContext.Current.CancellationToken);
            await service.Completion.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            SearchPage page = await store.SearchAsync(
                new SearchRequest(SearchQuery.Empty, 10),
                TestContext.Current.CancellationToken);
            Assert.Equal(["notes.txt", "photo.jpg"], page.Results.Select(item => item.Name).Order());
            Assert.Equal(2, service.Snapshot.TotalVolumes);
            Assert.Equal(2, service.Snapshot.FreshVolumes);
            Assert.Equal(1, service.Snapshot.Progress);
            Assert.Equal(IndexFreshness.Fresh, service.Snapshot.Freshness);
            Assert.Contains(snapshots, item => item.IndexedItems >= 1);
        }
        finally
        {
            await service.DisposeAsync();
            await store.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StaticDefaultFolderDiscovery(IReadOnlyList<string> paths)
        : IDefaultFolderDiscovery
    {
        public IReadOnlyList<string> Discover() => paths;
    }
}
