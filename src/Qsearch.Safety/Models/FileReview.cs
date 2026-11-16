namespace Qsearch.Safety.Models;

public enum ReviewState
{
    NotReviewed,
    NoNotableSignals,
    ReviewSuggested,
    HighCaution,
    Incomplete,
}

public enum FileKind
{
    Unknown,
    PortableExecutable,
    Script,
    Installer,
    Shortcut,
    MacroDocument,
    PdfDocument,
    Archive,
}

public sealed record FileReviewRequest(string Path, bool DeepReview = false);

public sealed record ReviewEvidence(
    string RuleId,
    int Score,
    string Explanation,
    string Guidance);

public sealed record FileReviewResult(
    ReviewState State,
    int Score,
    FileKind Kind,
    IReadOnlyList<ReviewEvidence> Evidence,
    bool IsRiskSearchable,
    string? IncompleteReason = null);
