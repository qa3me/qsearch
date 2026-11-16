namespace Qsearch.Benchmarks;

public sealed record BenchmarkEnvironment(
    string OperatingSystem,
    string Framework,
    string OsArchitecture,
    string ProcessArchitecture,
    int ProcessorCount,
    string Processor);

public sealed record BenchmarkCorpus(
    int Records,
    string Seed,
    long IndexBytes);

public sealed record BenchmarkLatency(
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds);

public sealed record BenchmarkOperation(
    int Operations,
    int ItemsPerOperation,
    double TotalMilliseconds,
    BenchmarkLatency Latency);

public sealed record BenchmarkReport(
    BenchmarkEnvironment Environment,
    BenchmarkCorpus Corpus,
    int WarmupQueries,
    int MeasuredQueries,
    double StartupToSearchableMilliseconds,
    BenchmarkOperation EmptyQuerySort,
    BenchmarkOperation JournalCatchUp,
    BenchmarkLatency Latency,
    long RowsReturned,
    long PeakWorkingSetBytes,
    IReadOnlyList<double> RawSamplesMilliseconds);
