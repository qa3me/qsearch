using Qsearch.Core.Indexing;

namespace Qsearch.Benchmarks;

public enum QueryClass
{
    Prefix,
    Contains,
    ExactName,
    ExtensionFilter,
    Exclusion,
}

public sealed record BenchmarkQuery(QueryClass Class, string Text);

public static class QueryWorkload
{
    private static readonly string[] Extensions = ["pdf", "xlsx", "jpg", "zip", "txt"];

    public static BenchmarkQuery[] Create(
        IReadOnlyList<IndexItem> corpus,
        int queryCount,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentOutOfRangeException.ThrowIfNegative(queryCount);
        if (corpus.Count == 0 && queryCount > 0)
        {
            throw new ArgumentException("A non-empty corpus is required for queries.", nameof(corpus));
        }

        BenchmarkQuery[] queries = new BenchmarkQuery[queryCount];
        ulong state = seed == 0 ? 1 : seed;
        for (int position = 0; position < queryCount; position++)
        {
            state = Next(state);
            IndexItem item = corpus[(int)(state % (uint)corpus.Count)];
            QueryClass queryClass = (QueryClass)(position % 5);
            queries[position] = new BenchmarkQuery(queryClass, CreateQuery(queryClass, item, position));
        }

        return queries;
    }

    private static string CreateQuery(QueryClass queryClass, IndexItem item, int position)
    {
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(item.Name);
        string[] parts = nameWithoutExtension.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        string token = parts.Length > 1 ? parts[1] : parts[0];
        return queryClass switch
        {
            QueryClass.Prefix => nameWithoutExtension[..Math.Min(7, nameWithoutExtension.Length)],
            QueryClass.Contains => token,
            QueryClass.ExactName => $"\"{item.Name}\"",
            QueryClass.ExtensionFilter => "ext:" + Extensions[position % Extensions.Length],
            QueryClass.Exclusion => token + " -missing",
            _ => token,
        };
    }

    private static ulong Next(ulong value)
    {
        value ^= value >> 12;
        value ^= value << 25;
        value ^= value >> 27;
        return value * 0x2545F4914F6CDD1DUL;
    }
}
