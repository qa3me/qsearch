using Qsearch.Core.Indexing;
using Qsearch.Core.Text;

namespace Qsearch.Benchmarks;

public static class SyntheticCorpus
{
    private static readonly string[] Tokens =
    [
        "alpha", "budget", "client", "delta", "falcon", "green", "invoice", "project",
        "report", "sample", "tehran", "winter", "آرشیو", "تصویر", "گزارش", "نمونه",
    ];

    public static IndexItem[] Create(int recordCount, ulong seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);
        DeterministicRandom random = new(seed);
        IndexItem[] items = new IndexItem[recordCount];
        for (int position = 0; position < items.Length; position++)
        {
            string token = Tokens[random.Next(Tokens.Length)];
            string name = CreateName(position, token, position % 5);
            string id = $"benchmark-{position:x8}";
            string path = Path.Combine(@"C:\QsearchBenchmark", $"bucket-{position % 100:d3}", name);
            items[position] = new IndexItem(
                id,
                "benchmark",
                name,
                SearchTextNormalizer.Normalize(name),
                path,
                null,
                IndexItemType.File,
                512 + random.Next(8 * 1024 * 1024),
                DateTimeOffset.UnixEpoch.AddSeconds(position),
                FileAttributes.Normal);
        }

        return items;
    }

    private static string CreateName(int position, string token, int nameClass)
    {
        return nameClass switch
        {
            0 => $"report-{token}-{position:d7}.pdf",
            1 => $"invoice_{token}_{position:d7}.xlsx",
            2 => $"تصویر-{token}-{position:d7}.jpg",
            3 => $"archive-{token}-{position:d7}.zip",
            _ => $"notes-{token}-{position:d7}.txt",
        };
    }

    private struct DeterministicRandom(ulong seed)
    {
        private ulong _state = seed == 0 ? 0x9E3779B97F4A7C15 : seed;

        public int Next(int maximumExclusive)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExclusive);
            ulong value = _state;
            value ^= value >> 12;
            value ^= value << 25;
            value ^= value >> 27;
            _state = value;
            return (int)((value * 0x2545F4914F6CDD1DUL) % (uint)maximumExclusive);
        }
    }
}
