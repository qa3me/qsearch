using Qsearch.App.Services;

namespace Qsearch.App.Tests;

public sealed class TrayClickRouterTests
{
    [Fact]
    public void SingleLeftClick_TogglesPopoverOnce()
    {
        int toggles = 0;
        TrayClickRouter router = new(() => toggles++);

        router.Handle(TrayPointerButton.Left, clickCount: 1);

        Assert.Equal(1, toggles);
    }

    [Theory]
    [InlineData(TrayPointerButton.Right, 1)]
    [InlineData(TrayPointerButton.Middle, 1)]
    [InlineData(TrayPointerButton.Left, 2)]
    public void OtherPointerActions_DoNotToggle(TrayPointerButton button, int clickCount)
    {
        int toggles = 0;
        TrayClickRouter router = new(() => toggles++);

        router.Handle(button, clickCount);

        Assert.Equal(0, toggles);
    }
}
