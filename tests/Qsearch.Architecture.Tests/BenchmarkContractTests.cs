using System.Text.Json;
using Qsearch.Benchmarks;

namespace Qsearch.Architecture.Tests;

public sealed class BenchmarkContractTests
{
    [Fact]
    public void Corpus_WithPublishedSeed_IsDeterministic()
    {
        const ulong seed = 0x5133534541524348;

        string[] first = SyntheticCorpus.Create(1000, seed).Select(item => item.Name).ToArray();
        string[] second = SyntheticCorpus.Create(1000, seed).Select(item => item.Name).ToArray();

        Assert.Equal(first, second);
        Assert.Equal(1000, first.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Workload_HasFiveEqualDeterministicQueryClasses()
    {
        IReadOnlyList<Qsearch.Core.Indexing.IndexItem> corpus = SyntheticCorpus.Create(100, 17);

        BenchmarkQuery[] workload = QueryWorkload.Create(corpus, 50, 23);

        Assert.Equal(50, workload.Length);
        Assert.All(
            Enum.GetValues<QueryClass>(),
            queryClass => Assert.Equal(10, workload.Count(query => query.Class == queryClass)));
    }

    [Fact]
    public void Report_ExposesStartupEmptySortJournalMemoryAndQueryEvidence()
    {
        string[] properties = typeof(BenchmarkReport)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains("StartupToSearchableMilliseconds", properties);
        Assert.Contains("EmptyQuerySort", properties);
        Assert.Contains("JournalCatchUp", properties);
        Assert.Contains("PeakWorkingSetBytes", properties);
        Assert.Contains("Latency", properties);

        string root = RepositoryRoot.Find();
        using JsonDocument schema = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "docs", "benchmarks", "report.schema.json")));
        string[] required = schema.RootElement
            .GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .ToArray();

        Assert.Contains("startupToSearchableMilliseconds", required);
        Assert.Contains("emptyQuerySort", required);
        Assert.Contains("journalCatchUp", required);
        Assert.Contains("peakWorkingSetBytes", required);
    }
}
