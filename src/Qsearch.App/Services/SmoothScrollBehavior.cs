using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Qsearch.App.Services;

public static class SmoothScrollMath
{
    private const double PixelsPerWheelNotch = 72d;
    private const double EdgeTolerance = 0.5d;

    public static double ClampTarget(double requested, double scrollableHeight)
    {
        if (!double.IsFinite(requested))
        {
            return 0d;
        }

        double maximum = double.IsFinite(scrollableHeight)
            ? Math.Max(0d, scrollableHeight)
            : 0d;
        return Math.Clamp(requested, 0d, maximum);
    }

    public static bool CanScroll(double verticalOffset, double scrollableHeight, int wheelDelta)
    {
        if (wheelDelta == 0 ||
            !double.IsFinite(verticalOffset) ||
            !double.IsFinite(scrollableHeight) ||
            scrollableHeight <= EdgeTolerance)
        {
            return false;
        }

        return wheelDelta > 0
            ? verticalOffset > EdgeTolerance
            : verticalOffset < scrollableHeight - EdgeTolerance;
    }

    public static double ToPixelDistance(int wheelDelta) =>
        -(wheelDelta / 120d) * PixelsPerWheelNotch;
}

public sealed class SmoothScrollBehavior : IDisposable
{
    private readonly FrameworkElement _root;
    private readonly MouseWheelEventHandler _wheelHandler;
    private readonly Dictionary<ScrollViewer, ScrollAnimation> _animations = [];
    private bool _disposed;

    private SmoothScrollBehavior(FrameworkElement root)
    {
        _root = root;
        _wheelHandler = OnPreviewMouseWheel;
        _root.AddHandler(UIElement.PreviewMouseWheelEvent, _wheelHandler, handledEventsToo: true);
    }

    public static SmoothScrollBehavior Attach(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new SmoothScrollBehavior(root);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _root.RemoveHandler(UIElement.PreviewMouseWheelEvent, _wheelHandler);
        foreach (ScrollAnimation animation in _animations.Values)
        {
            animation.Dispose();
        }

        _animations.Clear();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (_disposed || eventArgs.Delta == 0 || eventArgs.OriginalSource is not DependencyObject source)
        {
            return;
        }

        ScrollViewer? viewer = FindScrollableViewer(source, eventArgs.Delta);
        if (viewer is null)
        {
            return;
        }

        if (!_animations.TryGetValue(viewer, out ScrollAnimation? animation))
        {
            animation = new ScrollAnimation(viewer);
            _animations.Add(viewer, animation);
        }

        animation.AddDistance(SmoothScrollMath.ToPixelDistance(eventArgs.Delta));
        eventArgs.Handled = true;
    }

    private ScrollViewer? FindScrollableViewer(DependencyObject source, int wheelDelta)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ScrollViewer viewer &&
                SmoothScrollMath.CanScroll(
                    _animations.TryGetValue(viewer, out ScrollAnimation? animation)
                        ? animation.TargetOffset
                        : viewer.VerticalOffset,
                    viewer.ScrollableHeight,
                    wheelDelta))
            {
                return viewer;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is Visual or Visual3D)
        {
            return VisualTreeHelper.GetParent(current);
        }

        return current switch
        {
            FrameworkContentElement content => content.Parent,
            _ => LogicalTreeHelper.GetParent(current),
        };
    }

    private sealed class ScrollAnimation : IDisposable
    {
        private const double DurationMilliseconds = 150d;
        private readonly ScrollViewer _viewer;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _clock = new();
        private double _startOffset;
        private double _targetOffset;

        public double TargetOffset => _targetOffset;

        public ScrollAnimation(ScrollViewer viewer)
        {
            _viewer = viewer;
            _targetOffset = viewer.VerticalOffset;
            _timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Input,
                OnTick,
                viewer.Dispatcher);
            _timer.Stop();
        }

        public void AddDistance(double distance)
        {
            double current = _viewer.VerticalOffset;
            double origin = _timer.IsEnabled ? _targetOffset : current;
            _startOffset = current;
            _targetOffset = SmoothScrollMath.ClampTarget(
                origin + distance,
                _viewer.ScrollableHeight);

            if (Math.Abs(_targetOffset - current) <= 0.1d)
            {
                StopAtTarget();
                return;
            }

            if (!SystemParameters.ClientAreaAnimation)
            {
                StopAtTarget();
                return;
            }

            _clock.Restart();
            _timer.Start();
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _clock.Stop();
        }

        private void OnTick(object? sender, EventArgs eventArgs)
        {
            double progress = Math.Clamp(
                _clock.Elapsed.TotalMilliseconds / DurationMilliseconds,
                0d,
                1d);
            double eased = 1d - Math.Pow(1d - progress, 3d);
            double clampedTarget = SmoothScrollMath.ClampTarget(
                _targetOffset,
                _viewer.ScrollableHeight);
            _viewer.ScrollToVerticalOffset(
                _startOffset + ((clampedTarget - _startOffset) * eased));

            if (progress >= 1d)
            {
                _targetOffset = clampedTarget;
                StopAtTarget();
            }
        }

        private void StopAtTarget()
        {
            _timer.Stop();
            _clock.Stop();
            _targetOffset = SmoothScrollMath.ClampTarget(
                _targetOffset,
                _viewer.ScrollableHeight);
            _viewer.ScrollToVerticalOffset(_targetOffset);
        }
    }
}
