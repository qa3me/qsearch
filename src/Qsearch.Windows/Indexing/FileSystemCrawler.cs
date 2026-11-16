using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Qsearch.Core.Indexing;
using Qsearch.Core.Text;
using Qsearch.Windows.FileSystem;

namespace Qsearch.Windows.Indexing;

public sealed class FileSystemCrawler : IIndexSource
{
    private readonly IFileSystemFacade _fileSystem;

    public FileSystemCrawler(IFileSystemFacade fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    public async IAsyncEnumerable<IndexChange> ScanAsync(
        IndexScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        IndexScopeFilter filter = new(scope);
        Stack<string> pendingDirectories = new();
        HashSet<string> visitedDirectories = new(StringComparer.OrdinalIgnoreCase);
        pendingDirectories.Push(scope.RootPath);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pendingDirectories.Pop();
            if (!visitedDirectories.Add(directory))
            {
                continue;
            }

            IReadOnlyList<FileSystemEntry> entries;
            IndexDiagnostic? enumerationDiagnostic = null;
            try
            {
                entries = _fileSystem.EnumerateEntries(directory).ToArray();
            }
            catch (Exception exception) when (TryMapDiagnostic(exception, directory, out IndexDiagnostic? diagnostic))
            {
                entries = [];
                enumerationDiagnostic = diagnostic;
            }

            if (enumerationDiagnostic is not null)
            {
                yield return new IndexChange(
                    IndexChangeKind.Diagnostic,
                    scope.Id,
                    Diagnostic: enumerationDiagnostic);
                continue;
            }

            foreach (FileSystemEntry entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!filter.ShouldInclude(entry))
                {
                    continue;
                }

                yield return new IndexChange(
                    IndexChangeKind.Upsert,
                    scope.Id,
                    CreateItem(scope, entry));

                if (filter.ShouldRecurse(entry))
                {
                    pendingDirectories.Push(entry.FullPath);
                }
            }
        }

        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<IndexChange> WatchAsync(
        IndexScope scope,
        IndexCheckpoint? checkpoint,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }

    public static string CreatePathIdentity(string scopeId, string fullPath)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant());
        return scopeId + ":" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static IndexItem CreateItem(IndexScope scope, FileSystemEntry entry)
    {
        string? parentPath = Path.GetDirectoryName(entry.FullPath);
        string? parentId = string.IsNullOrEmpty(parentPath) ||
            string.Equals(parentPath, scope.RootPath, StringComparison.OrdinalIgnoreCase)
                ? null
                : CreatePathIdentity(scope.Id, parentPath);
        return new IndexItem(
            entry.StableId ?? CreatePathIdentity(scope.Id, entry.FullPath),
            scope.Id,
            entry.Name,
            SearchTextNormalizer.Normalize(entry.Name),
            entry.FullPath,
            parentId,
            entry.IsDirectory ? IndexItemType.Folder : IndexItemType.File,
            entry.Size,
            entry.LastWriteTimeUtc,
            entry.Attributes);
    }

    private static bool TryMapDiagnostic(
        Exception exception,
        string path,
        out IndexDiagnostic? diagnostic)
    {
        string? code = exception switch
        {
            UnauthorizedAccessException or SecurityException => "access-denied",
            DirectoryNotFoundException or FileNotFoundException => "not-found",
            PathTooLongException => "path-too-long",
            IOException => "io-error",
            _ => null,
        };

        diagnostic = code is null
            ? null
            : new IndexDiagnostic(code, "The path could not be read during indexing.", path);
        return code is not null;
    }
}
