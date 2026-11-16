using System.Text;
using System.Text.Json;
using Qsearch.Core.Indexing;
using Qsearch.Core.Text;

namespace Qsearch.Index.Search;

internal sealed record SearchSql(string CommandText, IReadOnlyDictionary<string, object> Parameters);

internal static class SearchSqlBuilder
{
    private sealed record Cursor(
        int Version,
        SearchSortField Field,
        SearchSortDirection Direction,
        string Value,
        string Id);

    public static SearchSql Build(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Dictionary<string, object> parameters = new(StringComparer.Ordinal);
        StringBuilder joins = new();
        List<string> conditions = [];
        List<string> longTerms = request.Query.IncludeTerms
            .Select(term => term.Value)
            .Where(term => term.Length >= 3)
            .ToList();

        if (longTerms.Count > 0)
        {
            joins.AppendLine("JOIN items_fts AS f ON f.rowid = i.row_id");
            conditions.Add("f.normalized_name MATCH $fts_query");
            parameters["$fts_query"] = string.Join(" AND ", longTerms.Select(QuoteFtsTerm));
        }

        for (int index = 0; index < request.Query.IncludeTerms.Count; index++)
        {
            string term = request.Query.IncludeTerms[index].Value;
            if (term.Length >= 3)
            {
                continue;
            }

            string parameterName = $"$include_{index}";
            conditions.Add($"instr(i.normalized_name, {parameterName}) > 0");
            parameters[parameterName] = term;
        }

        for (int index = 0; index < request.Query.ExcludeTerms.Count; index++)
        {
            string parameterName = $"$exclude_{index}";
            conditions.Add($"instr(i.normalized_name, {parameterName}) = 0");
            parameters[parameterName] = request.Query.ExcludeTerms[index].Value;
        }

        if (request.Query.Extensions.Count > 0)
        {
            List<string> extensionParameters = [];
            for (int index = 0; index < request.Query.Extensions.Count; index++)
            {
                string parameterName = $"$extension_{index}";
                extensionParameters.Add(parameterName);
                parameters[parameterName] = request.Query.Extensions[index];
            }

            conditions.Add($"i.extension IN ({string.Join(", ", extensionParameters)})");
        }

        if (request.Query.ItemType is not null)
        {
            parameters["$item_type"] = request.Query.ItemType == "folder"
                ? (int)IndexItemType.Folder
                : (int)IndexItemType.File;
            conditions.Add("i.item_type = $item_type");
        }

        if (request.Query.PathScope is not null)
        {
            string pathPrefix = request.Query.PathScope.TrimEnd('\\', '/') + "\\";
            parameters["$path_prefix"] = pathPrefix;
            conditions.Add(
                "substr(i.full_path, 1, length($path_prefix)) COLLATE NOCASE = $path_prefix COLLATE NOCASE");
        }

        AddRiskFilter(request, joins, conditions, parameters);

        SearchSort? sort = EffectiveSort(request);
        string orderBy;
        if (sort is null)
        {
            string rankTerm = request.Query.IncludeTerms.Count == 0
                ? string.Empty
                : request.Query.IncludeTerms[0].Value;
            parameters["$rank_term"] = rankTerm;
            orderBy = """
                CASE
                    WHEN $rank_term = '' THEN 4
                    WHEN i.normalized_name = $rank_term THEN 0
                    WHEN substr(i.normalized_name, 1, length($rank_term)) = $rank_term THEN 1
                    WHEN instr(
                        ' ' || replace(replace(replace(i.normalized_name, '-', ' '), '_', ' '), '.', ' '),
                        ' ' || $rank_term) > 0 THEN 2
                    ELSE 3
                END,
                length(i.normalized_name),
                i.path_depth,
                i.normalized_name COLLATE BINARY,
                i.id
                """;
            if (!string.IsNullOrWhiteSpace(request.ContinuationToken))
            {
                throw new ArgumentException(
                    "A continuation token requires a deterministic field sort.",
                    nameof(request));
            }
        }
        else
        {
            Cursor? cursor = DecodeCursor(request.ContinuationToken);
            if (cursor is not null)
            {
                if (cursor.Field != sort.Field || cursor.Direction != sort.Direction)
                {
                    throw new ArgumentException(
                        "The continuation token does not match the requested sort.",
                        nameof(request));
                }

                string expression = SortExpression(sort.Field);
                string comparison = sort.Direction == SearchSortDirection.Ascending ? ">" : "<";
                conditions.Add(
                    $"({expression}, i.id) {comparison} ($after_value, $after_id)");
                parameters["$after_value"] = ParseCursorValue(cursor);
                parameters["$after_id"] = cursor.Id;
            }

            string direction = sort.Direction == SearchSortDirection.Ascending ? "ASC" : "DESC";
            orderBy = $"{SortExpression(sort.Field)} {direction}, i.id {direction}";
        }

        parameters["$limit"] = Math.Clamp(request.Limit, 1, 100) + 1;

        string whereClause = conditions.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", conditions);
        string commandText = $$"""
            SELECT
                i.id,
                i.scope_id,
                i.name,
                i.full_path,
                i.item_type,
                i.size,
                i.last_write_utc
            FROM items AS i
            {{joins}}{{whereClause}}
            ORDER BY {{orderBy}}
            LIMIT $limit;
            """;

        return new SearchSql(commandText, parameters);
    }

    public static SearchSort? EffectiveSort(SearchRequest request)
    {
        return request.Sort ?? (request.Query.IncludeTerms.Count == 0
            ? new SearchSort(SearchSortField.Modified, SearchSortDirection.Descending)
            : null);
    }

    public static string? CreateContinuationToken(SearchRequest request, SearchResult result)
    {
        SearchSort? sort = EffectiveSort(request);
        if (sort is null)
        {
            return null;
        }

        string value = sort.Field switch
        {
            SearchSortField.Name => SearchTextNormalizer.Normalize(result.Name),
            SearchSortField.Path => result.FullPath,
            SearchSortField.Type => ((int)result.Type).ToString(System.Globalization.CultureInfo.InvariantCulture),
            SearchSortField.Size => result.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SearchSortField.Modified => result.LastWriteTimeUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            new Cursor(2, sort.Field, sort.Direction, value, result.Id));
        return Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static Cursor? DecodeCursor(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            string base64 = token.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            Cursor cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(base64)) ??
                throw new FormatException();
            bool supportedVersion = cursor.Version == 2 ||
                (cursor.Version == 1 && cursor.Direction == SearchSortDirection.Ascending);
            if (!supportedVersion || string.IsNullOrWhiteSpace(cursor.Id))
            {
                throw new FormatException();
            }

            return cursor;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException or OverflowException)
        {
            throw new ArgumentException("The continuation token is invalid.", nameof(token), exception);
        }
    }

    private static object ParseCursorValue(Cursor cursor)
    {
        return cursor.Field switch
        {
            SearchSortField.Type => int.Parse(cursor.Value, System.Globalization.CultureInfo.InvariantCulture),
            SearchSortField.Size => long.Parse(cursor.Value, System.Globalization.CultureInfo.InvariantCulture),
            _ => cursor.Value,
        };
    }

    private static string SortExpression(SearchSortField field)
    {
        return field switch
        {
            SearchSortField.Name => "i.normalized_name COLLATE BINARY",
            SearchSortField.Path => "i.full_path COLLATE NOCASE",
            SearchSortField.Type => "i.item_type",
            SearchSortField.Size => "i.size",
            SearchSortField.Modified => "i.last_write_utc",
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static void AddRiskFilter(
        SearchRequest request,
        StringBuilder joins,
        List<string> conditions,
        IDictionary<string, object> parameters)
    {
        if (request.Query.RiskLevels.Count == 0)
        {
            return;
        }

        joins.AppendLine(
            "JOIN review_cache AS r ON r.item_id = i.id AND r.is_complete = 1 " +
            "AND r.size = i.size AND r.last_write_utc = i.last_write_utc");
        List<string> riskConditions = [];
        if (request.Query.RiskLevels.Contains("high", StringComparer.Ordinal))
        {
            riskConditions.Add("r.state IN ('high', 'HighCaution')");
        }

        if (request.Query.RiskLevels.Contains("review", StringComparer.Ordinal))
        {
            riskConditions.Add(
                "r.state IN ('review', 'high', 'ReviewSuggested', 'HighCaution')");
        }

        if (riskConditions.Count == 0)
        {
            conditions.Add("0 = 1");
            return;
        }

        conditions.Add("(" + string.Join(" OR ", riskConditions) + ")");
    }

    private static string QuoteFtsTerm(string term)
    {
        return '"' + term.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}
