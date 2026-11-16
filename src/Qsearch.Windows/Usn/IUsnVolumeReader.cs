using Qsearch.Core.Indexing;

namespace Qsearch.Windows.Usn;

public sealed record UsnJournalState(long JournalId, long FirstUsn, long NextUsn);

public sealed record UsnReadBatch(
    IReadOnlyList<UsnRecord> Records,
    long NextUsn,
    IReadOnlyList<IndexDiagnostic> Diagnostics);

public interface IUsnVolumeReader
{
    ValueTask<UsnJournalState> QueryJournalAsync(
        string volumeRoot,
        CancellationToken cancellationToken);

    IAsyncEnumerable<UsnRecord> EnumerateAsync(
        string volumeRoot,
        CancellationToken cancellationToken);

    IAsyncEnumerable<UsnReadBatch> ReadChangesAsync(
        string volumeRoot,
        long startUsn,
        long journalId,
        CancellationToken cancellationToken);
}
