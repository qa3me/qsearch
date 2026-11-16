using Qsearch.Core.Indexing;

namespace Qsearch.Index.Tests;

public sealed class SearchRankingTests
{
    [Fact]
    public async Task EmptySearch_ReturnsNewestItemsWithDescendingIdTieBreak()
    {
        await using TestIndex index = await TestIndex.CreateAsync();
        DateTimeOffset older = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset newer = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        IndexItem itemB = TestIndex.Item("same-b.txt", "item-b") with { LastWriteTimeUtc = newer };
        IndexItem itemA = TestIndex.Item("same-a.txt", "item-a") with { LastWriteTimeUtc = newer };
        IndexItem old = TestIndex.Item("old.txt", "item-old") with { LastWriteTimeUtc = older };
        await index.Store.ApplyBatchAsync(
            new IndexBatch(
                "scope-a",
                new[] { old, itemB, itemA }
                    .Select(item => new IndexChange(IndexChangeKind.Upsert, item.ScopeId, item))
                    .ToArray()),
            TestContext.Current.CancellationToken);

        SearchPage page = await index.Store.SearchAsync(
            TestIndex.Request(string.Empty),
            TestContext.Current.CancellationToken);

        Assert.Equal(["item-b", "item-a", "item-old"], page.Results.Select(result => result.Id));
    }

    [Theory]
    [InlineData(SearchSortField.Name, SearchSortDirection.Ascending, "item-c,item-a,item-b")]
    [InlineData(SearchSortField.Name, SearchSortDirection.Descending, "item-b,item-a,item-c")]
    [InlineData(SearchSortField.Path, SearchSortDirection.Ascending, "item-c,item-a,item-b")]
    [InlineData(SearchSortField.Type, SearchSortDirection.Ascending, "item-a,item-c,item-b")]
    [InlineData(SearchSortField.Size, SearchSortDirection.Descending, "item-c,item-b,item-a")]
    [InlineData(SearchSortField.Modified, SearchSortDirection.Ascending, "item-a,item-b,item-c")]
    public async Task Search_AppliesWhitelistedStableSort(
        SearchSortField field,
        SearchSortDirection direction,
        string expectedIds)
    {
        await using TestIndex index = await TestIndex.CreateAsync();
        IndexItem a = TestIndex.Item("bravo.txt", "item-a", @"D:\z", size: 10) with
        {
            LastWriteTimeUtc = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero),
        };
        IndexItem b = TestIndex.Item("charlie.txt", "item-b", @"E:\z", size: 20, type: IndexItemType.Folder) with
        {
            LastWriteTimeUtc = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero),
        };
        IndexItem c = TestIndex.Item("alpha.txt", "item-c", @"C:\a", size: 30) with
        {
            LastWriteTimeUtc = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
        };
        await index.Store.ApplyBatchAsync(
            new IndexBatch(
                "scope-a",
                new[] { a, b, c }
                    .Select(item => new IndexChange(IndexChangeKind.Upsert, item.ScopeId, item))
                    .ToArray()),
            TestContext.Current.CancellationToken);

        SearchPage page = await index.Store.SearchAsync(
            new SearchRequest(
                Qsearch.Core.Search.SearchQueryParser.Parse(string.Empty),
                Sort: new SearchSort(field, direction)),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedIds.Split(','), page.Results.Select(result => result.Id));
    }

    [Fact]
    public async Task Search_ContinuationVisitsEveryItemExactlyOnce()
    {
        await using TestIndex index = await TestIndex.WithNamesAsync(
            "alpha.txt",
            "bravo.txt",
            "charlie.txt",
            "delta.txt",
            "echo.txt");
        SearchSort sort = new(SearchSortField.Name, SearchSortDirection.Ascending);

        SearchPage first = await index.Store.SearchAsync(
            new SearchRequest(
                Qsearch.Core.Search.SearchQueryParser.Parse(string.Empty),
                Limit: 2,
                Sort: sort),
            TestContext.Current.CancellationToken);
        SearchPage second = await index.Store.SearchAsync(
            new SearchRequest(
                Qsearch.Core.Search.SearchQueryParser.Parse(string.Empty),
                Limit: 2,
                ContinuationToken: first.ContinuationToken,
                Sort: sort),
            TestContext.Current.CancellationToken);
        SearchPage third = await index.Store.SearchAsync(
            new SearchRequest(
                Qsearch.Core.Search.SearchQueryParser.Parse(string.Empty),
                Limit: 2,
                ContinuationToken: second.ContinuationToken,
                Sort: sort),
            TestContext.Current.CancellationToken);

        string[] ids = first.Results.Concat(second.Results).Concat(third.Results)
            .Select(result => result.Id)
            .ToArray();
        Assert.Equal(["item-0", "item-1", "item-2", "item-3", "item-4"], ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Null(third.ContinuationToken);
    }

    [Theory]
    [InlineData(SearchSortField.Type)]
    [InlineData(SearchSortField.Modified)]
    public async Task Search_DescendingContinuationVisitsTiedItemsExactlyOnce(SearchSortField field)
    {
        await using TestIndex index = await TestIndex.CreateAsync();
        DateTimeOffset older = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        IndexItem[] items =
        [
            TestIndex.Item("one.txt", "item-a") with { LastWriteTimeUtc = older },
            TestIndex.Item("two", "item-b", type: IndexItemType.Folder),
            TestIndex.Item("three.txt", "item-c") with { LastWriteTimeUtc = older },
            TestIndex.Item("four", "item-d", type: IndexItemType.Folder),
            TestIndex.Item("five.txt", "item-e") with { LastWriteTimeUtc = older },
            TestIndex.Item("six", "item-f", type: IndexItemType.Folder),
        ];
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await index.Store.ApplyBatchAsync(
            new IndexBatch("scope-a", items.Select(item =>
                new IndexChange(IndexChangeKind.Upsert, item.ScopeId, item)).ToArray()),
            cancellationToken);
        SearchRequest request = TestIndex.Request(string.Empty, limit: 2) with
        {
            Sort = new SearchSort(field, SearchSortDirection.Descending),
        };

        SearchPage first = await index.Store.SearchAsync(request, cancellationToken);
        Assert.NotNull(first.ContinuationToken);
        SearchPage second = await index.Store.SearchAsync(
            request with { ContinuationToken = first.ContinuationToken }, cancellationToken);
        Assert.NotNull(second.ContinuationToken);
        SearchPage third = await index.Store.SearchAsync(
            request with { ContinuationToken = second.ContinuationToken }, cancellationToken);

        Assert.Equal(["item-f", "item-d"], first.Results.Select(result => result.Id));
        Assert.Equal(["item-b", "item-e"], second.Results.Select(result => result.Id));
        Assert.Equal(["item-c", "item-a"], third.Results.Select(result => result.Id));
        Assert.Null(third.ContinuationToken);
    }

    [Fact]
    public async Task Search_LegacyDescendingCursorRequiresRestart()
    {
        await using TestIndex index = await TestIndex.WithNamesAsync("alpha.txt", "bravo.txt");
        string token = Convert.ToBase64String(
            """{"Version":1,"Field":0,"Direction":1,"Value":"bravo.txt","Id":"item-1"}"""u8).TrimEnd('=');
        SearchRequest request = TestIndex.Request(string.Empty) with
        {
            Sort = new SearchSort(SearchSortField.Name, SearchSortDirection.Descending),
            ContinuationToken = token,
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => index.Store.SearchAsync(request, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Search_LegacyAscendingCursorStillResumes()
    {
        await using TestIndex index = await TestIndex.WithNamesAsync("alpha.txt", "bravo.txt");
        string token = Convert.ToBase64String(
            """{"Version":1,"Field":0,"Direction":0,"Value":"alpha.txt","Id":"item-0"}"""u8).TrimEnd('=');
        SearchRequest request = TestIndex.Request(string.Empty) with
        {
            Sort = new SearchSort(SearchSortField.Name, SearchSortDirection.Ascending),
            ContinuationToken = token,
        };

        SearchPage page = await index.Store.SearchAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(["item-1"], page.Results.Select(result => result.Id));
    }

    [Fact]
    public async Task Search_RanksExactThenPrefixThenSubstring()
    {
        await using TestIndex index = await TestIndex.WithNamesAsync("report", "report-final", "old-report");

        SearchPage page = await index.Store.SearchAsync(
            TestIndex.Request("report"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["report", "report-final", "old-report"], page.Results.Select(result => result.Name));
    }

    [Fact]
    public async Task Search_AppliesPathTypeExtensionAndNegativeFilters()
    {
        await using TestIndex index = await TestIndex.CreateAsync();
        IndexItem keep = TestIndex.Item("report-final.pdf", "keep", @"C:\scope\work");
        IndexItem draft = TestIndex.Item("report-draft.pdf", "draft", @"C:\scope\work");
        IndexItem elsewhere = TestIndex.Item("report-final.pdf", "elsewhere", @"D:\archive");
        IndexItem folder = TestIndex.Item("report-final.pdf", "folder", @"C:\scope\work", IndexItemType.Folder);
        await index.Store.ApplyBatchAsync(
            new IndexBatch(
                "scope-a",
                new[] { keep, draft, elsewhere, folder }
                    .Select(item => new IndexChange(IndexChangeKind.Upsert, item.ScopeId, item))
                    .ToList()),
            TestContext.Current.CancellationToken);

        SearchPage page = await index.Store.SearchAsync(
            TestIndex.Request("report ext:pdf type:file in:\"C:\\scope\\work\" -draft"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["keep"], page.Results.Select(result => result.Id));
    }

    [Fact]
    public async Task Search_LimitsResultsToOneHundred()
    {
        string[] names = Enumerable.Range(0, 120).Select(position => $"report-{position:D3}.txt").ToArray();
        await using TestIndex index = await TestIndex.WithNamesAsync(names);

        SearchPage page = await index.Store.SearchAsync(
            TestIndex.Request("report", limit: 500),
            TestContext.Current.CancellationToken);

        Assert.Equal(100, page.Results.Count);
    }

    [Fact]
    public async Task DeleteAndRename_InvalidateOldSearchRows()
    {
        await using TestIndex index = await TestIndex.WithNamesAsync("old-name.txt", "delete-me.txt");
        IndexItem renamed = TestIndex.Item("new-name.txt", "item-0");
        await index.Store.ApplyBatchAsync(
            new IndexBatch(
                "scope-a",
                [
                    new IndexChange(IndexChangeKind.Upsert, "scope-a", renamed),
                    new IndexChange(IndexChangeKind.Delete, "scope-a", ItemId: "item-1"),
                ]),
            TestContext.Current.CancellationToken);

        SearchPage oldPage = await index.Store.SearchAsync(
            TestIndex.Request("old-name"),
            TestContext.Current.CancellationToken);
        SearchPage deletedPage = await index.Store.SearchAsync(
            TestIndex.Request("delete-me"),
            TestContext.Current.CancellationToken);
        SearchPage newPage = await index.Store.SearchAsync(
            TestIndex.Request("new-name"),
            TestContext.Current.CancellationToken);

        Assert.Empty(oldPage.Results);
        Assert.Empty(deletedPage.Results);
        Assert.Equal(["item-0"], newPage.Results.Select(result => result.Id));
    }

    [Fact]
    public async Task RiskFilter_ExcludesIncompleteAndStaleReviews()
    {
        await using TestIndex index = await TestIndex.WithNamesAsync("current.exe", "incomplete.exe", "stale.exe");
        await index.SeedReviewAsync("item-0", "high", complete: true);
        await index.SeedReviewAsync("item-1", "high", complete: false);
        await index.SeedReviewAsync("item-2", "high", complete: true, size: 999);

        SearchPage page = await index.Store.SearchAsync(
            TestIndex.Request("risk:high"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["item-0"], page.Results.Select(result => result.Id));
    }

    [Theory]
    [InlineData("ReviewSuggested", "risk:review", true)]
    [InlineData("ReviewSuggested", "risk:high", false)]
    [InlineData("HighCaution", "risk:review", true)]
    [InlineData("HighCaution", "risk:high", true)]
    public async Task RiskFilter_AcceptsLegacyReviewStateNames(
        string legacyState,
        string query,
        bool expected)
    {
        await using TestIndex index = await TestIndex.WithNamesAsync("legacy.exe");
        await index.SeedReviewAsync("item-0", legacyState, complete: true);

        SearchPage page = await index.Store.SearchAsync(
            TestIndex.Request(query),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected ? 1 : 0, page.Results.Count);
    }

    [Fact]
    public async Task Search_CanceledRequestStopsBeforeQuery()
    {
        await using TestIndex index = await TestIndex.WithNamesAsync("report.txt");
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => index.Store.SearchAsync(TestIndex.Request("report"), cancellation.Token).AsTask());
    }
}
