using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Qsearch.App.Services;

public enum LocalPreviewKind
{
    Metadata,
    Text,
    Image,
    Folder,
}

public sealed record FilePreviewData(
    string Name,
    string FullPath,
    string ParentPath,
    string Extension,
    bool IsDirectory,
    long Size,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    DateTimeOffset AccessedAt,
    FileAttributes Attributes,
    LocalPreviewKind PreviewKind,
    string? TextPreview,
    ImageSource? ImagePreview,
    bool IsTruncated,
    string? PreviewMessage);

public interface IFilePreviewService
{
    ValueTask<FilePreviewData> LoadAsync(string path, CancellationToken cancellationToken);
}

public sealed record LocalFileMetadata(
    string Name,
    string FullPath,
    string ParentPath,
    string Extension,
    bool IsDirectory,
    bool Exists,
    long Size,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    DateTimeOffset AccessedAt,
    FileAttributes Attributes);

public interface ILocalFileMetadataReader
{
    LocalFileMetadata Read(string fullPath);
}

public sealed class LocalFileMetadataReader : ILocalFileMetadataReader
{
    public LocalFileMetadata Read(string fullPath)
    {
        if (Directory.Exists(fullPath))
        {
            DirectoryInfo directory = new(fullPath);
            directory.Refresh();
            return new LocalFileMetadata(
                directory.Name,
                directory.FullName,
                directory.Parent?.FullName ?? directory.Root.FullName,
                string.Empty,
                true,
                true,
                0,
                directory.CreationTimeUtc,
                directory.LastWriteTimeUtc,
                directory.LastAccessTimeUtc,
                directory.Attributes);
        }

        FileInfo file = new(fullPath);
        file.Refresh();
        return new LocalFileMetadata(
            file.Name,
            file.FullName,
            file.DirectoryName ?? string.Empty,
            file.Extension,
            false,
            file.Exists,
            file.Exists ? file.Length : 0,
            file.Exists ? file.CreationTimeUtc : default,
            file.Exists ? file.LastWriteTimeUtc : default,
            file.Exists ? file.LastAccessTimeUtc : default,
            file.Exists ? file.Attributes : default);
    }
}

public sealed class LocalFilePreviewService : IFilePreviewService
{
    public const int MaximumTextBytes = 64 * 1024;
    public const long MaximumImageBytes = 64L * 1024 * 1024;
    public const int MaximumImagePixels = 720;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".csv", ".log", ".ini", ".config",
        ".yaml", ".yml", ".cs", ".xaml", ".ps1", ".bat", ".cmd", ".js",
        ".ts", ".html", ".css", ".sql",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".ico",
    };

    private readonly ILocalFileMetadataReader _metadataReader;

    public LocalFilePreviewService(ILocalFileMetadataReader? metadataReader = null)
    {
        _metadataReader = metadataReader ?? new LocalFileMetadataReader();
    }

    public async ValueTask<FilePreviewData> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        LocalFileMetadata metadata = await Task.Run(
            () => _metadataReader.Read(fullPath),
            cancellationToken).ConfigureAwait(false);
        if (metadata.IsDirectory)
        {
            return new FilePreviewData(
                metadata.Name,
                metadata.FullPath,
                metadata.ParentPath,
                metadata.Extension,
                true,
                metadata.Size,
                metadata.CreatedAt,
                metadata.ModifiedAt,
                metadata.AccessedAt,
                metadata.Attributes,
                LocalPreviewKind.Folder,
                null,
                null,
                false,
                null);
        }

        if (!metadata.Exists)
        {
            throw new FileNotFoundException("The selected file is no longer available.", fullPath);
        }

        string extension = metadata.Extension;
        string? text = null;
        ImageSource? image = null;
        bool truncated = false;
        string? message = null;
        LocalPreviewKind kind = LocalPreviewKind.Metadata;
        try
        {
            if (TextExtensions.Contains(extension))
            {
                (text, truncated) = await ReadTextAsync(
                    metadata.FullPath,
                    metadata.Size,
                    cancellationToken);
                if (text is not null)
                {
                    kind = LocalPreviewKind.Text;
                }
                else
                {
                    message = "binary-text-preview-unavailable";
                }
            }
            else if (ImageExtensions.Contains(extension))
            {
                if (metadata.Size <= MaximumImageBytes)
                {
                    image = await Task.Run(
                        () => LoadImage(metadata.FullPath),
                        cancellationToken);
                    kind = image is null ? LocalPreviewKind.Metadata : LocalPreviewKind.Image;
                    message = image is null ? "image-preview-unavailable" : null;
                }
                else
                {
                    truncated = true;
                    message = "image-preview-too-large";
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            text = null;
            image = null;
            truncated = false;
            kind = LocalPreviewKind.Metadata;
            message = "preview-unavailable";
        }

        return new FilePreviewData(
            metadata.Name,
            metadata.FullPath,
            metadata.ParentPath,
            extension,
            false,
            metadata.Size,
            metadata.CreatedAt,
            metadata.ModifiedAt,
            metadata.AccessedAt,
            metadata.Attributes,
            kind,
            text,
            image,
            truncated,
            message);
    }

    private static async ValueTask<(string? Text, bool Truncated)> ReadTextAsync(
        string path,
        long fileLength,
        CancellationToken cancellationToken)
    {
        int length = checked((int)Math.Min(fileLength, MaximumTextBytes));
        byte[] bytes = new byte[length];
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        if (bytes.AsSpan(0, offset).Contains((byte)0))
        {
            return (null, fileLength > MaximumTextBytes);
        }

        return (Encoding.UTF8.GetString(bytes, 0, offset), fileLength > MaximumTextBytes);
    }

    private static BitmapImage? LoadImage(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = MaximumImagePixels;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }
}
