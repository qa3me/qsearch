using Qsearch.Core.Search;

namespace Qsearch.Core.Indexing;

public enum IndexItemType
{
    File,
    Folder,
}

public enum IndexChangeKind
{
    Upsert,
    Delete,
    Diagnostic,
    Checkpoint,
}

public enum IndexFreshness
{
    Fresh,
    CatchingUp,
    Stale,
    Unavailable,
}

public sealed record IndexScope(
    string Id,
    string RootPath,
    bool IncludeHidden = false,
    bool IncludeSystem = false,
    IReadOnlySet<string>? ExcludedSubtrees = null,
    IReadOnlySet<string>? ExcludedExtensions = null);

public sealed record IndexItem(
    string Id,
    string ScopeId,
    string Name,
    string NormalizedName,
    string FullPath,
    string? ParentId,
    IndexItemType Type,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    FileAttributes Attributes);

public sealed record IndexDiagnostic(string Code, string Message, string? Path = null);

public sealed record IndexChange(
    IndexChangeKind Kind,
    string ScopeId,
    IndexItem? Item = null,
    string? ItemId = null,
    IndexDiagnostic? Diagnostic = null,
    IndexCheckpoint? Checkpoint = null);

public sealed record IndexCheckpoint(
    string ScopeId,
    long JournalId,
    long NextUsn,
    DateTimeOffset? RecordedAtUtc = null);

public sealed record IndexBatch(
    string ScopeId,
    IReadOnlyList<IndexChange> Changes,
    IndexCheckpoint? Checkpoint = null);

public enum SearchSortField
{
    Name,
    Path,
    Type,
    Size,
    Modified,
}

public enum SearchSortDirection
{
    Ascending,
    Descending,
}

public sealed record SearchSort(SearchSortField Field, SearchSortDirection Direction);

public sealed record SearchRequest(
    SearchQuery Query,
    int Limit = 100,
    string? ContinuationToken = null,
    SearchSort? Sort = null);

public sealed record SearchResult(
    string Id,
    string ScopeId,
    string Name,
    string FullPath,
    IndexItemType Type,
    long Size,
    DateTimeOffset LastWriteTimeUtc);

public sealed record SearchPage(
    IReadOnlyList<SearchResult> Results,
    IndexFreshness Freshness,
    string? ContinuationToken = null);

public sealed record ReviewCacheRecord(
    string ItemId,
    string State,
    bool IsComplete,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    int PolicyVersion);

public interface IIndexStore
{
    ValueTask InitializeAsync(CancellationToken cancellationToken);

    ValueTask ApplyBatchAsync(IndexBatch batch, CancellationToken cancellationToken);

    ValueTask<SearchPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken);

    ValueTask<long> CountAsync(CancellationToken cancellationToken);

    ValueTask ResetScopeAsync(string scopeId, CancellationToken cancellationToken);

    ValueTask<IndexCheckpoint?> GetCheckpointAsync(
        string scopeId,
        CancellationToken cancellationToken);

    ValueTask SaveCheckpointAsync(
        IndexCheckpoint checkpoint,
        CancellationToken cancellationToken);

    ValueTask<ReviewCacheRecord?> GetReviewAsync(
        string itemId,
        CancellationToken cancellationToken);

    ValueTask SaveReviewAsync(
        ReviewCacheRecord review,
        CancellationToken cancellationToken);
}

public interface IIndexSource
{
    IAsyncEnumerable<IndexChange> ScanAsync(
        IndexScope scope,
        CancellationToken cancellationToken);

    IAsyncEnumerable<IndexChange> WatchAsync(
        IndexScope scope,
        IndexCheckpoint? checkpoint,
        CancellationToken cancellationToken);
}
