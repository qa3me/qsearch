using System.Text;
using Qsearch.Core.Text;

namespace Qsearch.Core.Search;

public static class SearchQueryParser
{
    private static readonly HashSet<string> ItemTypes = new(StringComparer.Ordinal)
    {
        "file",
        "folder",
    };

    private static readonly HashSet<string> RiskLevels = new(StringComparer.Ordinal)
    {
        "review",
        "high",
    };

    public static SearchQuery Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<SearchTerm> includeTerms = [];
        List<SearchTerm> excludeTerms = [];
        List<string> extensions = [];
        List<string> riskLevels = [];
        List<SearchQueryDiagnostic> diagnostics = [];
        string? itemType = null;
        string? pathScope = null;

        List<QueryToken> tokens = Tokenize(input, diagnostics);
        foreach (QueryToken token in tokens)
        {
            string value = token.Value;
            bool excluded = value.Length > 1 && value[0] == '-';
            if (excluded)
            {
                value = value[1..];
            }

            int colonIndex = value.IndexOf(':');
            if (!excluded && colonIndex > 0)
            {
                string filterName = value[..colonIndex].ToLowerInvariant();
                string filterValue = value[(colonIndex + 1)..];

                switch (filterName)
                {
                    case "ext":
                        AddExtension(filterValue, token.Value, extensions, diagnostics);
                        continue;
                    case "type":
                        itemType = ParseKnownValue(filterValue, token.Value, ItemTypes, diagnostics);
                        continue;
                    case "in":
                        pathScope = ParsePath(filterValue, token.Value, diagnostics);
                        continue;
                    case "risk":
                        AddKnownValue(filterValue, token.Value, RiskLevels, riskLevels, diagnostics);
                        continue;
                    default:
                        if (IsFilterName(filterName))
                        {
                            diagnostics.Add(new SearchQueryDiagnostic("unknown-filter", token.Value));
                            continue;
                        }

                        break;
                }
            }

            string normalized = SearchTextNormalizer.Normalize(value);
            if (normalized.Length == 0)
            {
                continue;
            }

            SearchTerm term = new(normalized, token.WasQuoted);
            (excluded ? excludeTerms : includeTerms).Add(term);
        }

        return new SearchQuery(
            includeTerms,
            excludeTerms,
            extensions,
            itemType,
            pathScope,
            riskLevels,
            diagnostics);
    }

    private static List<QueryToken> Tokenize(
        string input,
        List<SearchQueryDiagnostic> diagnostics)
    {
        List<QueryToken> tokens = [];
        StringBuilder current = new();
        bool inQuote = false;
        bool wasQuoted = false;

        foreach (char character in input)
        {
            if (character == '"')
            {
                inQuote = !inQuote;
                wasQuoted = true;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuote)
            {
                AddToken(tokens, current, wasQuoted);
                wasQuoted = false;
                continue;
            }

            current.Append(character);
        }

        AddToken(tokens, current, wasQuoted);
        if (inQuote)
        {
            diagnostics.Add(new SearchQueryDiagnostic("unclosed-quote", input));
        }

        return tokens;
    }

    private static void AddToken(ICollection<QueryToken> tokens, StringBuilder value, bool wasQuoted)
    {
        if (value.Length == 0)
        {
            return;
        }

        tokens.Add(new QueryToken(value.ToString(), wasQuoted));
        value.Clear();
    }

    private static void AddExtension(
        string value,
        string token,
        List<string> extensions,
        List<SearchQueryDiagnostic> diagnostics)
    {
        string extension = SearchTextNormalizer.Normalize(value.TrimStart('.'));
        if (extension.Length == 0)
        {
            diagnostics.Add(new SearchQueryDiagnostic("missing-filter-value", token));
            return;
        }

        extensions.Add(extension);
    }

    private static string? ParseKnownValue(
        string value,
        string token,
        IReadOnlySet<string> knownValues,
        List<SearchQueryDiagnostic> diagnostics)
    {
        string normalized = SearchTextNormalizer.Normalize(value);
        if (!knownValues.Contains(normalized))
        {
            diagnostics.Add(new SearchQueryDiagnostic(
                normalized.Length == 0 ? "missing-filter-value" : "invalid-filter-value",
                token));
            return null;
        }

        return normalized;
    }

    private static void AddKnownValue(
        string value,
        string token,
        IReadOnlySet<string> knownValues,
        List<string> destination,
        List<SearchQueryDiagnostic> diagnostics)
    {
        string? parsed = ParseKnownValue(value, token, knownValues, diagnostics);
        if (parsed is not null)
        {
            destination.Add(parsed);
        }
    }

    private static string? ParsePath(
        string value,
        string token,
        List<SearchQueryDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(new SearchQueryDiagnostic("missing-filter-value", token));
            return null;
        }

        return value;
    }

    private static bool IsFilterName(string value)
    {
        return value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    private sealed record QueryToken(string Value, bool WasQuoted);
}
