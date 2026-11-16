using Qsearch.Windows.Shell;

namespace Qsearch.Windows.Tests;

public sealed class TaskbarPopoverPlacementTests
{
    private static readonly ScreenSize Popover = new(400, 620);

    [Fact]
    public void BottomTaskbar_LtrAnchorsToNotificationSide()
    {
        ScreenRect result = TaskbarPopoverPlacement.Calculate(
            new ScreenRect(0, 0, 1920, 1040),
            new ScreenRect(0, 1040, 1920, 40),
            Popover,
            PopoverFlowDirection.LeftToRight);

        Assert.Equal(new ScreenRect(1520, 420, 400, 620), result);
    }

    [Fact]
    public void BottomTaskbar_RtlHonorsNegativeSecondaryMonitorCoordinates()
    {
        ScreenRect result = TaskbarPopoverPlacement.Calculate(
            new ScreenRect(-1920, 0, 1920, 1040),
            new ScreenRect(-1920, 1040, 1920, 40),
            Popover,
            PopoverFlowDirection.RightToLeft);

        Assert.Equal(new ScreenRect(-1920, 420, 400, 620), result);
    }

    [Theory]
    [InlineData(48, 0, 1872, 1080, 0, 0, 48, 1080, 48, 460)]
    [InlineData(0, 40, 1920, 1040, 0, 0, 1920, 40, 1520, 40)]
    [InlineData(0, 0, 1872, 1080, 1872, 0, 48, 1080, 1472, 460)]
    public void NonBottomTaskbars_StayAdjacentAndInsideWorkArea(
        double workX,
        double workY,
        double workWidth,
        double workHeight,
        double barX,
        double barY,
        double barWidth,
        double barHeight,
        double expectedX,
        double expectedY)
    {
        ScreenRect result = TaskbarPopoverPlacement.Calculate(
            new ScreenRect(workX, workY, workWidth, workHeight),
            new ScreenRect(barX, barY, barWidth, barHeight),
            Popover,
            PopoverFlowDirection.LeftToRight);

        Assert.Equal(new ScreenRect(expectedX, expectedY, 400, 620), result);
    }

    [Fact]
    public void ScaledOrSmallWorkArea_ClampsAtItsOrigin()
    {
        ScreenRect result = TaskbarPopoverPlacement.Calculate(
            new ScreenRect(120, 80, 300, 200),
            new ScreenRect(120, 280, 300, 32),
            Popover,
            PopoverFlowDirection.LeftToRight);

        Assert.Equal(new ScreenRect(120, 80, 400, 620), result);
    }
}
