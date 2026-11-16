using System.Runtime.CompilerServices;
using Qsearch.Core.Indexing;
using Qsearch.Windows.Indexing;

namespace Qsearch.Windows.Tests;

public sealed class ScopeReconcilerTests
{
    [Fact]
    public async Task WatcherOverflow_MarksScopeStaleAndSchedulesReconciliation()
    {
        StubChangeSource source = new(ChangeFeedEvent.Overflow("scope-a"));
        ScopeReconciler reconciler = new(source);

        await reconciler.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(IndexFreshness.Stale, reconciler.State.Freshness);
        Assert.Equal(["scope-a"], reconciler.ScheduledScopes);
    }

    private sealed class StubChangeSource(ChangeFeedEvent change) : IChangeNotificationSource
    {
        public async IAsyncEnumerable<ChangeFeedEvent> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return change;
            await Task.CompletedTask;
        }
    }
}
