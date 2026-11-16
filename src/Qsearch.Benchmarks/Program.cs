using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Qsearch.Benchmarks;
using Qsearch.Core.Indexing;
using Qsearch.Core.Search;
using Qsearch.Index.Sqlite;

return await BenchmarkProgram.RunAsync(args, CancellationToken.None);

internal static class BenchmarkProgram
{
    private const ulong PublishedSeed = 0x5133534541524348;
    private const int WarmupCount = 30;
    private const int BatchSize = 2000;
    private static readonly JsonSerializerOptions ReportJsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        BenchmarkOptions options;
        try
        {
            options = BenchmarkOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "Qsearch.Benchmarks",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string databasePath = Path.Combine(temporaryDirectory, "benchmark.db");
        try
        {
            IndexItem[] corpus = SyntheticCorpus.Create(options.Records, options.Seed);
            BenchmarkQuery[] workload = QueryWorkload.Create(corpus, options.Queries, options.Seed ^ 0xB3UL);
            long startupStarted = Stopwatch.GetTimestamp();
            await using SqliteIndexStore store = new(databasePath, TimeProvider.System);
            await store.InitializeAsync(cancellationToken);
            int firstBatchSize = Math.Min(BatchSize, corpus.Length);
            await ApplyCorpusBatchAsync(store, corpus, 0, firstBatchSize, cancellationToken);
            SearchPage firstPage = await store.SearchAsync(
                new SearchRequest(
                    SearchQueryParser.Parse(string.Empty),
                    1,
                    Sort: new SearchSort(SearchSortField.Modified, SearchSortDirection.Descending)),
                cancellationToken);
            if (firstPage.Results.Count == 0)
            {
                throw new InvalidOperationException("The startup batch was not searchable.");
            }

            double startupToSearchableMilliseconds =
                Stopwatch.GetElapsedTime(startupStarted).TotalMilliseconds;
            for (int offset = firstBatchSize; offset < corpus.Length; offset += BatchSize)
            {
                await ApplyCorpusBatchAsync(store, corpus, offset, BatchSize, cancellationToken);
            }

            BenchmarkOperation emptyQuerySort = await MeasureEmptyQuerySortAsync(store, cancellationToken);
            BenchmarkOperation journalCatchUp = await MeasureJournalCatchUpAsync(store, corpus, cancellationToken);

            for (int position = 0; position < WarmupCount; position++)
            {
                BenchmarkQuery query = workload[position % workload.Length];
                await store.SearchAsync(
                    new SearchRequest(SearchQueryParser.Parse(query.Text), 100),
                    cancellationToken);
            }

            List<double> samples = new(workload.Length);
            long rowsReturned = 0;
            foreach (BenchmarkQuery query in workload)
            {
                long started = Stopwatch.GetTimestamp();
                SearchPage result = await store.SearchAsync(
                    new SearchRequest(SearchQueryParser.Parse(query.Text), 100),
                    cancellationToken);
                samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                rowsReturned += result.Results.Count;
            }

            Process process = Process.GetCurrentProcess();
            process.Refresh();
            BenchmarkReport report = new(
                CreateEnvironment(),
                new BenchmarkCorpus(options.Records, $"0x{options.Seed:X16}", DatabaseBytes(databasePath)),
                WarmupCount,
                workload.Length,
                startupToSearchableMilliseconds,
                emptyQuerySort,
                journalCatchUp,
                CreateLatency(samples),
                rowsReturned,
                process.PeakWorkingSet64,
                samples);
            string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await File.WriteAllTextAsync(
                options.OutputPath,
                JsonSerializer.Serialize(report, ReportJsonOptions),
                cancellationToken);
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Qsearch benchmark: {options.Records:N0} records, startup {report.StartupToSearchableMilliseconds:F3} ms, query p95 {report.Latency.P95Milliseconds:F3} ms"));
            return 0;
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static async ValueTask ApplyCorpusBatchAsync(
        SqliteIndexStore store,
        IndexItem[] corpus,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        IndexChange[] changes = corpus
            .Skip(offset)
            .Take(count)
            .Select(item => new IndexChange(IndexChangeKind.Upsert, item.ScopeId, item))
            .ToArray();
        if (changes.Length > 0)
        {
            await store.ApplyBatchAsync(new IndexBatch("benchmark", changes), cancellationToken);
        }
    }

    private static async ValueTask<BenchmarkOperation> MeasureEmptyQuerySortAsync(
        SqliteIndexStore store,
        CancellationToken cancellationToken)
    {
        SearchSortField[] fields = Enum.GetValues<SearchSortField>();
        List<double> samples = new(fields.Length * 2);
        foreach (SearchSortField field in fields)
        {
            foreach (SearchSortDirection direction in Enum.GetValues<SearchSortDirection>())
            {
                long started = Stopwatch.GetTimestamp();
                await store.SearchAsync(
                    new SearchRequest(
                        SearchQueryParser.Parse(string.Empty),
                        100,
                        Sort: new SearchSort(field, direction)),
                    cancellationToken);
                samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }

        return CreateOperation(samples, 100);
    }

    private static async ValueTask<BenchmarkOperation> MeasureJournalCatchUpAsync(
        SqliteIndexStore store,
        IndexItem[] corpus,
        CancellationToken cancellationToken)
    {
        const int operationCount = 10;
        int changesPerOperation = Math.Min(100, corpus.Length);
        List<double> samples = new(operationCount);
        for (int operation = 0; operation < operationCount; operation++)
        {
            IndexChange[] changes = Enumerable
                .Range(0, changesPerOperation)
                .Select(position => corpus[(operation * changesPerOperation + position) % corpus.Length])
                .Select(item => new IndexChange(
                    IndexChangeKind.Upsert,
                    item.ScopeId,
                    item with
                    {
                        Size = item.Size + operation + 1,
                        LastWriteTimeUtc = item.LastWriteTimeUtc.AddTicks(operation + 1),
                    }))
                .ToArray();
            IndexCheckpoint checkpoint = new("benchmark", 1, (operation + 1) * changesPerOperation);
            long started = Stopwatch.GetTimestamp();
            await store.ApplyBatchAsync(
                new IndexBatch("benchmark", changes, checkpoint),
                cancellationToken);
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return CreateOperation(samples, changesPerOperation);
    }

    private static BenchmarkOperation CreateOperation(List<double> samples, int itemsPerOperation)
    {
        return new BenchmarkOperation(
            samples.Count,
            itemsPerOperation,
            samples.Sum(),
            CreateLatency(samples));
    }

    private static BenchmarkLatency CreateLatency(List<double> samples)
    {
        return new BenchmarkLatency(
            Percentile(samples, 0.50),
            Percentile(samples, 0.95),
            Percentile(samples, 0.99));
    }

    private static BenchmarkEnvironment CreateEnvironment()
    {
        return new BenchmarkEnvironment(
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown");
    }

    private static long DatabaseBytes(string databasePath)
    {
        return new[] { databasePath, databasePath + "-wal", databasePath + "-shm" }
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);
    }

    private static double Percentile(List<double> values, double percentile)
    {
        double[] ordered = values.Order().ToArray();
        int index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private sealed record BenchmarkOptions(int Records, int Queries, ulong Seed, string OutputPath)
    {
        public static BenchmarkOptions Parse(string[] args)
        {
            int records = args.Contains("--smoke", StringComparer.Ordinal) ? 10_000 : 1_000_000;
            int queries = args.Contains("--smoke", StringComparer.Ordinal) ? 50 : 500;
            ulong seed = PublishedSeed;
            string? output = null;
            for (int position = 0; position < args.Length; position++)
            {
                switch (args[position])
                {
                    case "--smoke":
                        break;
                    case "--records":
                        records = ParsePositiveInt(Next(args, ref position), "records");
                        break;
                    case "--queries":
                        queries = ParsePositiveInt(Next(args, ref position), "queries");
                        break;
                    case "--seed":
                        string value = Next(args, ref position);
                        seed = ulong.Parse(
                            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture);
                        break;
                    case "--output":
                        output = Next(args, ref position);
                        break;
                    default:
                        throw new ArgumentException($"Unknown benchmark option: {args[position]}");
                }
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                throw new ArgumentException("--output is required.");
            }

            return new BenchmarkOptions(records, queries, seed, output);
        }

        private static string Next(string[] args, ref int position)
        {
            if (++position >= args.Length)
            {
                throw new ArgumentException("A benchmark option value is missing.");
            }

            return args[position];
        }

        private static int ParsePositiveInt(string value, string name)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
            {
                throw new ArgumentException($"{name} must be a positive integer.");
            }

            return parsed;
        }
    }
}
