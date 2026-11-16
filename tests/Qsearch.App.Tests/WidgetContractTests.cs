using Qsearch.App.Services;
using Qsearch.App.ViewModels;
using Qsearch.Core.Indexing;
using Qsearch.Safety.Models;
using Qsearch.Windows.Shell;

namespace Qsearch.App.Tests;

public sealed class WidgetContractTests
{
    [Fact]
    public void Widget_IsPhoneProportionedTaskbarPopover()
    {
        string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WidgetWindow.xaml"));

        Assert.Contains("Width=\"400\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"620\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"NoResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Deactivated=\"OnDeactivated\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Widget.OpenMain", xaml, StringComparison.Ordinal);
        Assert.Contains("Widget.About", xaml, StringComparison.Ordinal);
        Assert.Contains("Status.Ready", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Widgets Board", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Widget_UsesTheSameSearchStateAsTheMainWindow()
    {
        MainViewModel main = new(new EmptyIndexStore(), new EmptyShell(), new EmptyReview());
        WidgetViewModel widget = new(main);

        widget.SearchText = "shared";

        Assert.Equal("shared", main.SearchText);
        Assert.Same(main.Results, widget.Results);
    }

    [Fact]
    public async Task WidgetFilters_ApplyAndRemoveTypedSearchFilters()
    {
        MainViewModel main = new(new EmptyIndexStore(), new EmptyShell(), new EmptyReview());
        WidgetViewModel widget = new(main)
        {
            SearchText = "report",
        };

        await widget.ApplyFilterAsync(WidgetFilter.Files);
        Assert.Equal("type:file report", main.SearchText);
        Assert.Equal("report", widget.SearchText);

        await widget.ApplyFilterAsync(WidgetFilter.All);
        Assert.Equal("report", main.SearchText);
    }

    private sealed class EmptyIndexStore : IIndexStore
    {
        public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ApplyBatchAsync(IndexBatch batch, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<SearchPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SearchPage([], IndexFreshness.Fresh));
        public ValueTask<long> CountAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0L);
        public ValueTask ResetScopeAsync(string scopeId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IndexCheckpoint?> GetCheckpointAsync(string scopeId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IndexCheckpoint?>(null);
        public ValueTask SaveCheckpointAsync(IndexCheckpoint checkpoint, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<ReviewCacheRecord?> GetReviewAsync(string itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ReviewCacheRecord?>(null);
        public ValueTask SaveReviewAsync(ReviewCacheRecord review, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class EmptyShell : IShellActionService
    {
        public ValueTask OpenAsync(string path, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevealAsync(string path, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class EmptyReview : IFileReviewService
    {
        public ValueTask<FileReviewResult> ReviewAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new FileReviewResult(ReviewState.NotReviewed, 0, FileKind.Unknown, [], false));
    }
}
