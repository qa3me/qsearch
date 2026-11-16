using Qsearch.App.Services;
using Qsearch.Core.Indexing;

namespace Qsearch.App.Tests;

public sealed class BackgroundIndexInitializerTests
{
    [Fact]
    public async Task SynchronousInitialize_ReturnsBeforeStoreIsReleased()
    {
        BlockingInitializationStore store = new();
        TaskCompletionSource<Task<IndexInitializationResult>> returned = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Thread caller = new(() =>
        {
            Task<IndexInitializationResult> task = BackgroundIndexInitializer.InitializeAsync(
                store,
                CancellationToken.None);
            returned.TrySetResult(task);
        });

        caller.Start();
        Task<IndexInitializationResult> initialization = await returned.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await store.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Assert.False(initialization.IsCompleted);

        store.Release.Set();
        Assert.Equal(
            IndexInitializationResult.Ready,
            await initialization.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));
        Assert.True(caller.Join(TimeSpan.FromSeconds(2)));
    }

    private sealed class BlockingInitializationStore : IIndexStore
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim Release { get; } = new(initialState: false);

        public ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            Release.Wait(cancellationToken);
            return ValueTask.CompletedTask;
        }

        public ValueTask<SearchPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SearchPage([], IndexFreshness.Fresh));

        public ValueTask ApplyBatchAsync(IndexBatch batch, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<long> CountAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0L);
        public ValueTask ResetScopeAsync(string scopeId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IndexCheckpoint?> GetCheckpointAsync(string scopeId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IndexCheckpoint?>(null);
        public ValueTask SaveCheckpointAsync(IndexCheckpoint checkpoint, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public ValueTask<ReviewCacheRecord?> GetReviewAsync(string itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ReviewCacheRecord?>(null);
        public ValueTask SaveReviewAsync(ReviewCacheRecord review, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
