using Qsearch.Windows.Shell;

namespace Qsearch.Windows.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondInstance_ForwardsSearchToPrimary()
    {
        string instanceKey = "test-" + Guid.NewGuid().ToString("N");
        TaskCompletionSource<ActivationMessage> received = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using SingleInstanceCoordinator primary = new(
            instanceKey,
            message =>
            {
                received.TrySetResult(message);
                return ValueTask.CompletedTask;
            },
            restrictToCurrentUser: false);
        InstanceAcquisition first = await primary.AcquireOrForwardAsync(
            new ActivationMessage(null),
            TestContext.Current.CancellationToken);

        await using SingleInstanceCoordinator secondary = new(
            instanceKey,
            _ => ValueTask.CompletedTask,
            restrictToCurrentUser: false);
        InstanceAcquisition second = await secondary.AcquireOrForwardAsync(
            new ActivationMessage("invoice", ActivationSurface.Compact),
            TestContext.Current.CancellationToken);
        ActivationMessage message = await received.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.True(first.IsPrimary);
        Assert.False(first.Forwarded);
        Assert.False(second.IsPrimary);
        Assert.True(second.Forwarded);
        Assert.Equal("invoice", message.SearchText);
        Assert.Equal(ActivationSurface.Compact, message.Surface);
    }

    [Fact]
    public async Task OversizedActivation_IsRejectedBeforePipeWrite()
    {
        string instanceKey = "test-" + Guid.NewGuid().ToString("N");
        await using SingleInstanceCoordinator primary = new(instanceKey, _ => ValueTask.CompletedTask);
        await primary.AcquireOrForwardAsync(
            new ActivationMessage(null),
            TestContext.Current.CancellationToken);
        await using SingleInstanceCoordinator secondary = new(instanceKey, _ => ValueTask.CompletedTask);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await secondary.AcquireOrForwardAsync(
                new ActivationMessage(new string('x', 5000)),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProductDefault_RestrictsPipeToCurrentWindowsUser()
    {
        await using SingleInstanceCoordinator coordinator = new(
            "test-" + Guid.NewGuid().ToString("N"),
            _ => ValueTask.CompletedTask);

        Assert.True(coordinator.RestrictsToCurrentUser);
    }
}
