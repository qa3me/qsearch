using System.IO.Enumeration;
using DirectoryEntry = System.IO.Enumeration.FileSystemEntry;

namespace Qsearch.Windows.FileSystem;

public sealed class PhysicalFileSystemFacade : IFileSystemFacade
{
    private static readonly EnumerationOptions EntriesOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryPath)
    {
        FileSystemEnumerable<FileSystemEntry> entries = new(
            directoryPath,
            static (ref DirectoryEntry entry) => new FileSystemEntry(
                entry.ToFullPath(),
                entry.FileName.ToString(),
                entry.IsDirectory,
                entry.IsDirectory ? 0 : entry.Length,
                entry.LastWriteTimeUtc,
                entry.Attributes),
            EntriesOptions);
        foreach (FileSystemEntry entry in entries)
        {
            yield return entry;
        }
    }
}
