using System.Globalization;

namespace Qsearch.Safety.Evidence;

public sealed class FileEvidenceReader : IFileEvidenceReader
{
    private const int MaximumHeaderBytes = 4096;
    private const int MaximumShortcutBytes = 1024 * 1024;

    public ValueTask<FileReviewMetadata> ReadMetadataAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileInfo file = new(path);
        file.Refresh();
        return ValueTask.FromResult(new FileReviewMetadata(
            file.FullName,
            file.Name,
            file.Length,
            file.LastWriteTimeUtc,
            file.Attributes));
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadHeaderAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        int length = Math.Clamp(maximumBytes, 0, MaximumHeaderBytes);
        byte[] buffer = new byte[length];
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        int read = await stream.ReadAsync(buffer, cancellationToken);
        return buffer.AsMemory(0, read);
    }

    public async ValueTask<MarkOfTheWeb?> ReadMarkOfTheWebAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string alternateStream = path + ":Zone.Identifier";
        try
        {
            await using FileStream stream = new(
                alternateStream,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using StreamReader reader = new(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(
                        line.AsSpan("ZoneId=".Length),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int zoneId))
                {
                    return new MarkOfTheWeb(zoneId);
                }
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }

        return null;
    }

    public async ValueTask<double?> ReadEntropyAsync(
        string path,
        int sampleSize,
        int maximumSamples,
        CancellationToken cancellationToken)
    {
        if (sampleSize <= 0 || maximumSamples <= 0)
        {
            return null;
        }

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: sampleSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (stream.Length == 0)
        {
            return 0;
        }

        long[] candidateOffsets =
        [
            0,
            Math.Max(0, (stream.Length - sampleSize) / 2),
            Math.Max(0, stream.Length - sampleSize),
        ];
        long[] offsets = candidateOffsets.Distinct().Take(maximumSamples).ToArray();
        long[] frequencies = new long[256];
        long totalBytes = 0;
        byte[] buffer = new byte[sampleSize];
        foreach (long offset in offsets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = offset;
            int read = await stream.ReadAsync(buffer, cancellationToken);
            totalBytes += read;
            for (int index = 0; index < read; index++)
            {
                frequencies[buffer[index]]++;
            }
        }

        if (totalBytes == 0)
        {
            return 0;
        }

        double entropy = 0;
        foreach (long count in frequencies)
        {
            if (count == 0)
            {
                continue;
            }

            double probability = (double)count / totalBytes;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    public async ValueTask<ShortcutMetadata?> ReadShortcutAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        int length = checked((int)Math.Min(stream.Length, MaximumShortcutBytes));
        byte[] bytes = new byte[length];
        int read = await stream.ReadAsync(bytes, cancellationToken);
        return ShortcutInspector.Inspect(bytes.AsSpan(0, read));
    }
}
