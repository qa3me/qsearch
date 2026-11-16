namespace Qsearch.Core.Search;

public sealed record SearchTerm(string Value, bool IsPhrase = false);

public sealed record SearchQueryDiagnostic(string Code, string Token);

public sealed record SearchQuery(
    IReadOnlyList<SearchTerm> IncludeTerms,
    IReadOnlyList<SearchTerm> ExcludeTerms,
    IReadOnlyList<string> Extensions,
    string? ItemType,
    string? PathScope,
    IReadOnlyList<string> RiskLevels,
    IReadOnlyList<SearchQueryDiagnostic> Diagnostics)
{
    public static SearchQuery Empty { get; } = new(
        [],
        [],
        [],
        null,
        null,
        [],
        []);
}
