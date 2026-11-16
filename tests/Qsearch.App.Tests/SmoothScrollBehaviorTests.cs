using Qsearch.App.Services;

namespace Qsearch.App.Tests;

public sealed class SmoothScrollBehaviorTests
{
    [Theory]
    [InlineData(-50, 300, 0)]
    [InlineData(125, 300, 125)]
    [InlineData(450, 300, 300)]
    public void ClampTarget_StaysInsideScrollableRange(
        double requested,
        double scrollableHeight,
        double expected)
    {
        Assert.Equal(expected, SmoothScrollMath.ClampTarget(requested, scrollableHeight));
    }

    [Theory]
    [InlineData(0, 400, 120, false)]
    [InlineData(20, 400, 120, true)]
    [InlineData(380, 400, -120, true)]
    [InlineData(400, 400, -120, false)]
    [InlineData(0, 0, -120, false)]
    public void CanScroll_HandsWheelToAnOuterViewerAtTheInnerEdge(
        double verticalOffset,
        double scrollableHeight,
        int wheelDelta,
        bool expected)
    {
        Assert.Equal(
            expected,
            SmoothScrollMath.CanScroll(verticalOffset, scrollableHeight, wheelDelta));
    }

    [Theory]
    [InlineData(120, -72)]
    [InlineData(-120, 72)]
    [InlineData(-240, 144)]
    public void WheelDistance_UsesPredictablePixelSteps(int wheelDelta, double expected)
    {
        Assert.Equal(expected, SmoothScrollMath.ToPixelDistance(wheelDelta));
    }
}
