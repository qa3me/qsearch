using Qsearch.App.Services;

namespace Qsearch.App.Tests;

public sealed class FilePreviewServiceTests
{
    [Fact]
    public void MetadataProbe_RunsAwayFromTheCallingThread()
    {
        int callingThread = 0;
        ThreadRecordingMetadataReader reader = new();
        LocalFilePreviewService service = new(reader);
        string path = Path.Combine(Path.GetTempPath(), "Qsearch", "VirtualFolder");
        FilePreviewData? preview = null;
        Exception? failure = null;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Thread caller = new(() =>
        {
            try
            {
                callingThread = Environment.CurrentManagedThreadId;
                preview = service.LoadAsync(path, cancellationToken).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        caller.Start();
        Assert.True(caller.Join(TimeSpan.FromSeconds(5)));

        Assert.Null(failure);
        Assert.NotNull(preview);
        Assert.True(preview.IsDirectory);
        Assert.NotEqual(callingThread, reader.ThreadId);
    }

    [Fact]
    public async Task TextPreview_IsBoundedAndReportsCompleteMetadata()
    {
        using PreviewDirectory directory = new();
        string path = Path.Combine(directory.Path, "notes.txt");
        await File.WriteAllTextAsync(
            path,
            new string('a', LocalFilePreviewService.MaximumTextBytes + 500),
            TestContext.Current.CancellationToken);
        LocalFilePreviewService service = new();

        FilePreviewData preview = await service.LoadAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(LocalPreviewKind.Text, preview.PreviewKind);
        Assert.Equal("notes.txt", preview.Name);
        Assert.Equal(".txt", preview.Extension);
        Assert.Equal(LocalFilePreviewService.MaximumTextBytes, preview.TextPreview!.Length);
        Assert.True(preview.IsTruncated);
        Assert.True(preview.Size > LocalFilePreviewService.MaximumTextBytes);
        Assert.NotEqual(default, preview.ModifiedAt);
    }

    [Fact]
    public async Task ImagePreview_IsDownscaledAndDoesNotKeepTheFileOpen()
    {
        using PreviewDirectory directory = new();
        string path = Path.Combine(directory.Path, "pixel.png");
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZPp8AAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(path, png, TestContext.Current.CancellationToken);
        LocalFilePreviewService service = new();

        FilePreviewData preview = await service.LoadAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(LocalPreviewKind.Image, preview.PreviewKind);
        Assert.NotNull(preview.ImagePreview);
        using FileStream exclusive = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanWrite);
    }

    [Fact]
    public async Task DirectoryPreview_ReturnsMetadataWithoutReadingContent()
    {
        using PreviewDirectory directory = new();
        string path = Path.Combine(directory.Path, "Archive");
        Directory.CreateDirectory(path);
        LocalFilePreviewService service = new();

        FilePreviewData preview = await service.LoadAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.True(preview.IsDirectory);
        Assert.Equal(LocalPreviewKind.Folder, preview.PreviewKind);
        Assert.Null(preview.TextPreview);
        Assert.Null(preview.ImagePreview);
    }

    [Fact]
    public async Task ContentFailure_ReturnsTheMetadataAlreadyRead()
    {
        using PreviewDirectory directory = new();
        string path = Path.Combine(directory.Path, "gone.txt");
        LocalFileMetadata metadata = new(
            "gone.txt",
            path,
            directory.Path,
            ".txt",
            false,
            true,
            42,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(1),
            DateTimeOffset.UnixEpoch,
            FileAttributes.Archive);
        LocalFilePreviewService service = new(new FixedMetadataReader(metadata));

        FilePreviewData preview = await service.LoadAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(LocalPreviewKind.Metadata, preview.PreviewKind);
        Assert.Equal("preview-unavailable", preview.PreviewMessage);
        Assert.Equal(path, preview.FullPath);
        Assert.Equal(42, preview.Size);
        Assert.Equal(metadata.ModifiedAt, preview.ModifiedAt);
        Assert.Equal(FileAttributes.Archive, preview.Attributes);
        Assert.Null(preview.TextPreview);
        Assert.Null(preview.ImagePreview);
    }

    private sealed class PreviewDirectory : IDisposable
    {
        public PreviewDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Qsearch.Preview.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class ThreadRecordingMetadataReader : ILocalFileMetadataReader
    {
        public int ThreadId { get; private set; }

        public LocalFileMetadata Read(string fullPath)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            return new LocalFileMetadata(
                "VirtualFolder",
                fullPath,
                Path.GetDirectoryName(fullPath) ?? string.Empty,
                string.Empty,
                true,
                true,
                0,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                FileAttributes.Directory);
        }
    }

    private sealed class FixedMetadataReader(LocalFileMetadata metadata) : ILocalFileMetadataReader
    {
        public LocalFileMetadata Read(string fullPath) => metadata;
    }
}
