using Qsearch.Core.Search;

namespace Qsearch.Core.Tests.Search;

public sealed class SearchQueryParserTests
{
    [Fact]
    public void Parse_MixedTermsAndFilters_UsesAndSemantics()
    {
        SearchQuery query = SearchQueryParser.Parse("گزارش final ext:pdf in:\"C:\\Users\\QA3\" -draft");

        Assert.Equal(["گزارش", "final"], query.IncludeTerms.Select(term => term.Value));
        Assert.Equal(["draft"], query.ExcludeTerms.Select(term => term.Value));
        Assert.Equal(["pdf"], query.Extensions);
        Assert.Equal(@"C:\Users\QA3", query.PathScope);
    }

    [Fact]
    public void Parse_UnknownFilter_IsReportedWithoutBroadening()
    {
        SearchQuery query = SearchQueryParser.Parse("report owner:qa3");

        Assert.Contains(query.Diagnostics, diagnostic => diagnostic.Code == "unknown-filter");
        Assert.Equal(["report"], query.IncludeTerms.Select(term => term.Value));
    }
}
