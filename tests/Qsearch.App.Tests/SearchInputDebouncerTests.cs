using Qsearch.App.Services;

namespace Qsearch.App.Tests;

public sealed class SearchInputDebouncerTests
{
    [Fact]
    public async Task SubmitAsync_CancelsTheObsoleteKeystrokeAndRunsOnlyTheLatest()
    {
        using SearchInputDebouncer debouncer = new(TimeSpan.FromMilliseconds(35));
        List<string> searches = [];

        ValueTask first = debouncer.SubmitAsync(
            _ =>
            {
                searches.Add("old");
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);
        await Task.Delay(5, TestContext.Current.CancellationToken);
        ValueTask second = debouncer.SubmitAsync(
            _ =>
            {
                searches.Add("new");
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        await first;
        await second;

        Assert.Equal(["new"], searches);
    }
}
