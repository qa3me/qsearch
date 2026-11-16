namespace Qsearch.Safety.Evidence;

public sealed record FileReviewMetadata(
    string Path,
    string Name,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    FileAttributes Attributes);

public sealed record MarkOfTheWeb(int ZoneId);

public sealed record ShortcutMetadata(string? Target, string? Arguments);

public interface IFileEvidenceReader
{
    ValueTask<FileReviewMetadata> ReadMetadataAsync(
        string path,
        CancellationToken cancellationToken);

    ValueTask<ReadOnlyMemory<byte>> ReadHeaderAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken);

    ValueTask<MarkOfTheWeb?> ReadMarkOfTheWebAsync(
        string path,
        CancellationToken cancellationToken);

    ValueTask<double?> ReadEntropyAsync(
        string path,
        int sampleSize,
        int maximumSamples,
        CancellationToken cancellationToken);

    ValueTask<ShortcutMetadata?> ReadShortcutAsync(
        string path,
        CancellationToken cancellationToken);
}
