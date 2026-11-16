using Qsearch.Core.Indexing;
using Qsearch.Windows.FileSystem;
using Qsearch.Windows.Indexing;

namespace Qsearch.Windows.Tests;

public sealed class FileSystemCrawlerTests
{
    [Fact]
    public async Task Crawl_AccessDeniedAndJunctionLoop_ContinuesWithoutRecursion()
    {
        FakeFileSystem fileSystem = new();
        fileSystem.Add(
            @"C:\scope",
            Entry.File(@"C:\scope\visible.txt"),
            Entry.Folder(@"C:\scope\private"),
            Entry.Folder(@"C:\scope\loop", FileAttributes.Directory | FileAttributes.ReparsePoint));
        fileSystem.Deny(@"C:\scope\private");
        FileSystemCrawler crawler = new(fileSystem);

        List<IndexChange> changes = await CollectAsync(
            crawler.ScanAsync(new IndexScope("scope-a", @"C:\scope"), TestContext.Current.CancellationToken));

        Assert.Contains(changes, change => change.Item?.Name == "visible.txt");
        Assert.Contains(changes, change => change.Diagnostic?.Code == "access-denied");
        Assert.DoesNotContain(fileSystem.EnumeratedPaths, path => path.Contains("loop\\loop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Crawl_AppliesHiddenSystemExtensionAndSubtreeExclusions()
    {
        FakeFileSystem fileSystem = new();
        fileSystem.Add(
            @"C:\scope",
            Entry.File(@"C:\scope\keep.txt"),
            Entry.File(@"C:\scope\skip.tmp"),
            Entry.File(@"C:\scope\hidden.txt", FileAttributes.Hidden),
            Entry.Folder(@"C:\scope\cache"));
        fileSystem.Add(@"C:\scope\cache", Entry.File(@"C:\scope\cache\nested.txt"));
        IndexScope scope = new(
            "scope-a",
            @"C:\scope",
            ExcludedSubtrees: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\scope\cache" },
            ExcludedExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tmp" });

        List<IndexChange> changes = await CollectAsync(
            new FileSystemCrawler(fileSystem).ScanAsync(scope, TestContext.Current.CancellationToken));

        Assert.Equal(["keep.txt"], changes.Where(change => change.Item is not null).Select(change => change.Item!.Name));
    }

    [Fact]
    public void PhysicalEnumeration_PreservesFileAndDirectoryMetadata()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "Qsearch.Tests",
            Guid.NewGuid().ToString("N"));
        string folderPath = Path.Combine(root, "cache");
        string filePath = Path.Combine(root, "گزارش.txt");
        Directory.CreateDirectory(folderPath);
        try
        {
            DateTime modified = new(2025, 1, 2, 3, 4, 6, DateTimeKind.Utc);
            File.WriteAllBytes(filePath, [1, 2, 3]);
            File.SetLastWriteTimeUtc(filePath, modified);
            File.SetAttributes(filePath, FileAttributes.Hidden | FileAttributes.System);
            Directory.SetLastWriteTimeUtc(folderPath, modified);

            FileSystemEntry[] entries = new PhysicalFileSystemFacade()
                .EnumerateEntries(root)
                .ToArray();

            FileSystemEntry folder = Assert.Single(entries, entry => entry.IsDirectory);
            Assert.Equal("cache", folder.Name);
            Assert.Equal(folderPath, folder.FullPath);
            Assert.Equal(0, folder.Size);
            Assert.Equal(modified, folder.LastWriteTimeUtc);
            Assert.True(folder.Attributes.HasFlag(FileAttributes.Directory));

            FileSystemEntry file = Assert.Single(entries, entry => !entry.IsDirectory);
            Assert.Equal("گزارش.txt", file.Name);
            Assert.Equal(filePath, file.FullPath);
            Assert.Equal(3, file.Size);
            Assert.Equal(modified, file.LastWriteTimeUtc);
            Assert.True(file.Attributes.HasFlag(FileAttributes.Hidden | FileAttributes.System));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static async ValueTask<List<IndexChange>> CollectAsync(
        IAsyncEnumerable<IndexChange> source)
    {
        List<IndexChange> results = [];
        await foreach (IndexChange item in source)
        {
            results.Add(item);
        }

        return results;
    }

    private sealed class FakeFileSystem : IFileSystemFacade
    {
        private readonly Dictionary<string, IReadOnlyList<FileSystemEntry>> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _denied = new(StringComparer.OrdinalIgnoreCase);

        public List<string> EnumeratedPaths { get; } = [];

        public void Add(string path, params FileSystemEntry[] entries)
        {
            _entries[path] = entries;
        }

        public void Deny(string path)
        {
            _denied.Add(path);
        }

        public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryPath)
        {
            EnumeratedPaths.Add(directoryPath);
            if (_denied.Contains(directoryPath))
            {
                throw new UnauthorizedAccessException(directoryPath);
            }

            return _entries.GetValueOrDefault(directoryPath) ?? [];
        }
    }

    private static class Entry
    {
        public static FileSystemEntry File(
            string path,
            FileAttributes attributes = FileAttributes.Normal)
        {
            return new FileSystemEntry(
                path,
                Path.GetFileName(path),
                false,
                10,
                DateTimeOffset.UnixEpoch,
                attributes);
        }

        public static FileSystemEntry Folder(
            string path,
            FileAttributes attributes = FileAttributes.Directory)
        {
            return new FileSystemEntry(
                path,
                Path.GetFileName(path),
                true,
                0,
                DateTimeOffset.UnixEpoch,
                attributes);
        }
    }
}
