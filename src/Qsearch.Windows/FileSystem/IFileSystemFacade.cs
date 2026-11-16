namespace Qsearch.Windows.FileSystem;

public sealed record FileSystemEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    FileAttributes Attributes,
    string? StableId = null);

public interface IFileSystemFacade
{
    IEnumerable<FileSystemEntry> EnumerateEntries(string directoryPath);
}
