using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Qsearch.App.Services;

namespace Qsearch.App.Controls;

public partial class IndexPulse : System.Windows.Controls.UserControl
{
    private const int PointCount = 24;
    private static readonly Duration TransitionDuration = new(TimeSpan.FromMilliseconds(280));
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot),
        typeof(IndexingSnapshot),
        typeof(IndexPulse),
        new PropertyMetadata(null, OnSnapshotChanged));

    public static readonly DependencyProperty AnimatedProgressProperty = DependencyProperty.Register(
        nameof(AnimatedProgress),
        typeof(double),
        typeof(IndexPulse),
        new PropertyMetadata(0d));

    private static readonly DependencyProperty PulseTransitionProperty = DependencyProperty.Register(
        "PulseTransition",
        typeof(double),
        typeof(IndexPulse),
        new PropertyMetadata(1d, OnPulseTransitionChanged));

    private PointCollection _fromPoints = [];
    private PointCollection _targetPoints = [];

    public IndexPulse()
    {
        InitializeComponent();
    }

    public IndexingSnapshot? Snapshot
    {
        get => (IndexingSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public double AnimatedProgress
    {
        get => (double)GetValue(AnimatedProgressProperty);
        private set => SetValue(AnimatedProgressProperty, value);
    }

    private double PulseTransition
    {
        get => (double)GetValue(PulseTransitionProperty);
        set => SetValue(PulseTransitionProperty, value);
    }

    private static void OnSnapshotChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        IndexPulse pulse = (IndexPulse)dependencyObject;
        pulse.AnimateProgress();
        pulse.AnimatePulse();
    }

    private static void OnPulseTransitionChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        ((IndexPulse)dependencyObject).RenderPulse((double)eventArgs.NewValue);
    }

    private void OnPulseSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SetPulseImmediately();
    }

    private void AnimateProgress()
    {
        double target = Math.Clamp(Snapshot?.Progress ?? 0, 0, 1);
        double current = AnimatedProgress;
        BeginAnimation(AnimatedProgressProperty, null);
        AnimatedProgress = current;
        DoubleAnimation animation = new(current, target, TransitionDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) =>
        {
            BeginAnimation(AnimatedProgressProperty, null);
            AnimatedProgress = target;
        };
        BeginAnimation(AnimatedProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimatePulse()
    {
        PointCollection current = PulseLine.Points.Count == PointCount
            ? new PointCollection(PulseLine.Points)
            : BuildPoints(Snapshot?.RecentRates ?? []);
        _fromPoints = current;
        _targetPoints = BuildPoints(Snapshot?.RecentRates ?? []);
        BeginAnimation(PulseTransitionProperty, null);
        PulseTransition = 0;
        DoubleAnimation animation = new(0d, 1d, TransitionDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) =>
        {
            BeginAnimation(PulseTransitionProperty, null);
            PulseTransition = 1;
        };
        BeginAnimation(PulseTransitionProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void SetPulseImmediately()
    {
        BeginAnimation(PulseTransitionProperty, null);
        _targetPoints = BuildPoints(Snapshot?.RecentRates ?? []);
        _fromPoints = new PointCollection(_targetPoints);
        PulseTransition = 1;
        PulseLine.Points = new PointCollection(_targetPoints);
    }

    private PointCollection BuildPoints(IReadOnlyList<double> rates)
    {
        double width = Math.Max(1, PulseCanvas.ActualWidth);
        double height = Math.Max(1, PulseCanvas.ActualHeight);
        if (rates.Count == 0)
        {
            PointCollection baseline = [];
            for (int index = 0; index < PointCount; index++)
            {
                baseline.Add(new System.Windows.Point(
                    width * index / (PointCount - 1),
                    height * 0.65));
            }

            return baseline;
        }

        double maximum = Math.Max(1, rates.Max());
        PointCollection points = [];
        for (int index = 0; index < PointCount; index++)
        {
            double position = rates.Count == 1
                ? 0
                : (double)index * (rates.Count - 1) / (PointCount - 1);
            int left = (int)Math.Floor(position);
            int right = Math.Min(left + 1, rates.Count - 1);
            double fraction = position - left;
            double rate = rates[left] + ((rates[right] - rates[left]) * fraction);
            double x = width * index / (PointCount - 1);
            double y = height - ((rate / maximum) * height * 0.8) - (height * 0.1);
            points.Add(new System.Windows.Point(x, y));
        }

        return points;
    }

    private void RenderPulse(double progress)
    {
        if (_fromPoints.Count != PointCount || _targetPoints.Count != PointCount)
        {
            return;
        }

        PointCollection points = [];
        for (int index = 0; index < PointCount; index++)
        {
            System.Windows.Point start = _fromPoints[index];
            System.Windows.Point target = _targetPoints[index];
            points.Add(new System.Windows.Point(
                start.X + ((target.X - start.X) * progress),
                start.Y + ((target.Y - start.Y) * progress)));
        }

        PulseLine.Points = points;
    }
}
