using System.Runtime.CompilerServices;
using Qsearch.Core.Indexing;
using Qsearch.Core.Text;
using Qsearch.Windows.FileSystem;
using Qsearch.Windows.Indexing;

namespace Qsearch.Windows.Usn;

public sealed class UsnIndexSource : IIndexSource
{
    private const int MissingParentSaturationLimit = 64;
    private readonly IUsnVolumeReader _reader;
    private readonly IndexScopeFilter _filter;
    private PathGraph? _pathGraph;

    public UsnIndexSource(IUsnVolumeReader reader, IndexScopeFilter filter)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(filter);
        _reader = reader;
        _filter = filter;
    }

    public async IAsyncEnumerable<IndexChange> ScanAsync(
        IndexScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        string volumeRoot = GetVolumeRoot(scope.RootPath);
        UsnJournalState journal = await _reader.QueryJournalAsync(volumeRoot, cancellationToken);
        PathGraph graph = new(volumeRoot);
        IndexDiagnostic? parseDiagnostic = null;

        try
        {
            await foreach (UsnRecord record in _reader.EnumerateAsync(volumeRoot, cancellationToken))
            {
                graph.Apply(record);
            }
        }
        catch (UsnParseException exception)
        {
            parseDiagnostic = exception.Diagnostics.Count == 0
                ? new IndexDiagnostic(
                    "journal-parse-failed",
                    "The journal contained an unrecoverable record.")
                : exception.Diagnostics[0];
        }

        if (parseDiagnostic is not null)
        {
            yield return RebuildDiagnostic(scope.Id, parseDiagnostic.Code);
            yield break;
        }

        _pathGraph = graph;
        int missingParents = 0;
        foreach (UsnRecord record in graph.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!graph.TryResolvePath(record.FileReferenceNumber, out string path))
            {
                missingParents++;
                if (missingParents >= MissingParentSaturationLimit)
                {
                    yield return RebuildDiagnostic(scope.Id, "missing-parent-saturation");
                    yield break;
                }

                continue;
            }

            FileSystemEntry entry = CreateEntry(record, path);
            if (_filter.ShouldInclude(entry))
            {
                yield return new IndexChange(
                    IndexChangeKind.Upsert,
                    scope.Id,
                    CreateItem(scope.Id, record, entry));
            }
        }

        IndexCheckpoint checkpoint = new(
            scope.Id,
            journal.JournalId,
            journal.NextUsn,
            DateTimeOffset.UtcNow);
        yield return new IndexChange(
            IndexChangeKind.Checkpoint,
            scope.Id,
            Checkpoint: checkpoint);
    }

    public async IAsyncEnumerable<IndexChange> WatchAsync(
        IndexScope scope,
        IndexCheckpoint? checkpoint,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        string volumeRoot = GetVolumeRoot(scope.RootPath);
        UsnJournalState journal = await _reader.QueryJournalAsync(volumeRoot, cancellationToken);
        if (checkpoint is null ||
            checkpoint.JournalId != journal.JournalId ||
            checkpoint.NextUsn < journal.FirstUsn)
        {
            yield return RebuildDiagnostic(scope.Id, "journal-rebuild-required");
            yield break;
        }

        PathGraph graph = _pathGraph ??= new PathGraph(volumeRoot);
        int missingParents = 0;
        await foreach (UsnReadBatch batch in _reader.ReadChangesAsync(
                           volumeRoot,
                           checkpoint.NextUsn,
                           journal.JournalId,
                           cancellationToken))
        {
            if (batch.Diagnostics.Count > 0)
            {
                yield return RebuildDiagnostic(scope.Id, batch.Diagnostics[0].Code);
                yield break;
            }

            foreach (UsnRecord record in batch.Records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((record.Reason & UsnReason.FileDelete) != 0)
                {
                    graph.Remove(record.FileReferenceNumber);
                    yield return new IndexChange(
                        IndexChangeKind.Delete,
                        scope.Id,
                        ItemId: StableId(scope.Id, record.FileReferenceNumber));
                    continue;
                }

                graph.Apply(record);
                if (!graph.TryResolvePath(record.FileReferenceNumber, out string path))
                {
                    missingParents++;
                    if (missingParents >= MissingParentSaturationLimit)
                    {
                        yield return RebuildDiagnostic(scope.Id, "missing-parent-saturation");
                        yield break;
                    }

                    continue;
                }

                FileSystemEntry entry = CreateEntry(record, path);
                if (_filter.ShouldInclude(entry))
                {
                    yield return new IndexChange(
                        IndexChangeKind.Upsert,
                        scope.Id,
                        CreateItem(scope.Id, record, entry));
                }
            }

            yield return new IndexChange(
                IndexChangeKind.Checkpoint,
                scope.Id,
                Checkpoint: new IndexCheckpoint(
                    scope.Id,
                    journal.JournalId,
                    batch.NextUsn,
                    DateTimeOffset.UtcNow));
        }
    }

    private static FileSystemEntry CreateEntry(UsnRecord record, string path)
    {
        bool directory = (record.Attributes & FileAttributes.Directory) != 0;
        return new FileSystemEntry(
            path,
            record.Name,
            directory,
            0,
            DateTimeOffset.UnixEpoch,
            record.Attributes,
            record.FileReferenceNumber.ToString());
    }

    private static IndexItem CreateItem(
        string scopeId,
        UsnRecord record,
        FileSystemEntry entry)
    {
        string? parentId = record.ParentFileReferenceNumber == record.FileReferenceNumber
            ? null
            : StableId(scopeId, record.ParentFileReferenceNumber);
        return new IndexItem(
            StableId(scopeId, record.FileReferenceNumber),
            scopeId,
            entry.Name,
            SearchTextNormalizer.Normalize(entry.Name),
            entry.FullPath,
            parentId,
            entry.IsDirectory ? IndexItemType.Folder : IndexItemType.File,
            entry.Size,
            entry.LastWriteTimeUtc,
            entry.Attributes);
    }

    private static string StableId(string scopeId, FileReferenceId reference)
    {
        return scopeId + ":" + reference;
    }

    private static string GetVolumeRoot(string path)
    {
        return Path.GetPathRoot(path) ?? path;
    }

    private static IndexChange RebuildDiagnostic(string scopeId, string reason)
    {
        return new IndexChange(
            IndexChangeKind.Diagnostic,
            scopeId,
            Diagnostic: new IndexDiagnostic(
                "journal-rebuild-required",
                $"The NTFS journal cannot be resumed safely ({reason})."));
    }
}
