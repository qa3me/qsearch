using Qsearch.App.Services;
using Qsearch.App.ViewModels;
using Qsearch.Core.Indexing;

namespace Qsearch.App.Tests;

public sealed class FirstRunViewModelTests
{
    [Fact]
    public async Task Initialize_ShowsLocalPrivacyDisclosureBeforePermission()
    {
        StubMachineIndexingService service = new(MachineIndexStartResult.ConsentRequired);
        FirstRunViewModel viewModel = new(service);

        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsVisible);
        Assert.Equal("consent-required", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Accept_HidesDisclosureOnlyAfterHelperConnects()
    {
        StubMachineIndexingService service = new(
            MachineIndexStartResult.ConsentRequired,
            MachineIndexStartResult.Started);
        FirstRunViewModel viewModel = new(service);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        await viewModel.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.IsVisible);
        Assert.Equal("indexing-started", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DeclineLeavesRetryVisibleAndFolderOnlyChoiceDoesNotRetry()
    {
        StubMachineIndexingService service = new(
            MachineIndexStartResult.ConsentRequired,
            MachineIndexStartResult.ElevationDeclined);
        FirstRunViewModel viewModel = new(service);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        await viewModel.AcceptAsync(TestContext.Current.CancellationToken);
        viewModel.ContinueFolderOnly();

        Assert.False(viewModel.IsVisible);
        Assert.Equal("folder-only", viewModel.StatusMessage);
        Assert.Equal(1, service.RetryCount);
    }

    [Fact]
    public async Task SuppressedPrompt_StaysHiddenWhenElevationIsDeclined()
    {
        StubMachineIndexingService service = new(
            MachineIndexStartResult.ConsentRequired,
            MachineIndexStartResult.ElevationDeclined);
        FirstRunViewModel viewModel = new(service);
        System.Reflection.MethodInfo? suppress = viewModel.GetType().GetMethod("SuppressPrompt");
        Assert.NotNull(suppress);
        suppress.Invoke(viewModel, null);

        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        await viewModel.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.IsVisible);
        Assert.Equal(1, service.RetryCount);
    }

    private sealed class StubMachineIndexingService(
        MachineIndexStartResult start,
        MachineIndexStartResult retry = MachineIndexStartResult.Started) : IMachineIndexingService
    {
        public int RetryCount { get; private set; }
        public IndexingSnapshot Snapshot { get; } = new(0, 0, 0, IndexFreshness.Unavailable, null, []);
        public Task Completion => Task.CompletedTask;
        public event Action<IndexingSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public ValueTask<MachineIndexStartResult> StartAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(start);

        public ValueTask<MachineIndexStartResult> RetryElevationAsync(CancellationToken cancellationToken)
        {
            RetryCount++;
            return ValueTask.FromResult(retry);
        }

        public ValueTask RebuildAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
