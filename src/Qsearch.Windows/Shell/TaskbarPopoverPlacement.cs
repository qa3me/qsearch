namespace Qsearch.Windows.Shell;

public enum PopoverFlowDirection
{
    LeftToRight,
    RightToLeft,
}

public readonly record struct ScreenSize(double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public static class TaskbarPopoverPlacement
{
    public static ScreenRect Calculate(
        ScreenRect workArea,
        ScreenRect taskbar,
        ScreenSize popover,
        PopoverFlowDirection flowDirection)
    {
        if (workArea.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea));
        }

        if (popover.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(popover));
        }

        TaskbarEdge edge = FindClosestEdge(workArea, taskbar);
        double x;
        double y;
        switch (edge)
        {
            case TaskbarEdge.Top:
                x = flowDirection == PopoverFlowDirection.RightToLeft
                    ? workArea.Left
                    : workArea.Right - popover.Width;
                y = workArea.Top;
                break;
            case TaskbarEdge.Left:
                x = workArea.Left;
                y = workArea.Bottom - popover.Height;
                break;
            case TaskbarEdge.Right:
                x = workArea.Right - popover.Width;
                y = workArea.Bottom - popover.Height;
                break;
            default:
                x = flowDirection == PopoverFlowDirection.RightToLeft
                    ? workArea.Left
                    : workArea.Right - popover.Width;
                y = workArea.Bottom - popover.Height;
                break;
        }

        x = ClampPosition(x, workArea.Left, workArea.Right, popover.Width);
        y = ClampPosition(y, workArea.Top, workArea.Bottom, popover.Height);
        return new ScreenRect(x, y, popover.Width, popover.Height);
    }

    private static TaskbarEdge FindClosestEdge(ScreenRect workArea, ScreenRect taskbar)
    {
        if (taskbar.IsEmpty)
        {
            return TaskbarEdge.Bottom;
        }

        (TaskbarEdge Edge, double Distance)[] candidates =
        [
            (TaskbarEdge.Top, Math.Abs(taskbar.Bottom - workArea.Top)),
            (TaskbarEdge.Bottom, Math.Abs(taskbar.Top - workArea.Bottom)),
            (TaskbarEdge.Left, Math.Abs(taskbar.Right - workArea.Left)),
            (TaskbarEdge.Right, Math.Abs(taskbar.Left - workArea.Right)),
        ];
        return candidates.MinBy(candidate => candidate.Distance).Edge;
    }

    private static double ClampPosition(double value, double start, double end, double length)
    {
        if (length >= end - start)
        {
            return start;
        }

        return Math.Clamp(value, start, end - length);
    }

    private enum TaskbarEdge
    {
        Top,
        Bottom,
        Left,
        Right,
    }
}
