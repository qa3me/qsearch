using System.Windows;
using System.Windows.Media.Animation;

namespace Qsearch.App.Controls;

public partial class QsearchMark : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty IsAnimatedProperty = DependencyProperty.Register(
        nameof(IsAnimated),
        typeof(bool),
        typeof(QsearchMark),
        new PropertyMetadata(false, OnAnimationStateChanged));

    public static readonly DependencyProperty OrbitAngleProperty = DependencyProperty.Register(
        nameof(OrbitAngle),
        typeof(double),
        typeof(QsearchMark),
        new PropertyMetadata(0d));

    public QsearchMark()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => StopAnimation();
    }

    public bool IsAnimated
    {
        get => (bool)GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    public double OrbitAngle
    {
        get => (double)GetValue(OrbitAngleProperty);
        private set => SetValue(OrbitAngleProperty, value);
    }

    private static void OnAnimationStateChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        ((QsearchMark)dependencyObject).UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        if (!IsLoaded || !IsAnimated || !SystemParameters.ClientAreaAnimation)
        {
            StopAnimation();
            return;
        }

        DoubleAnimation animation = new(0, 360, TimeSpan.FromSeconds(1.8))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        BeginAnimation(OrbitAngleProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void StopAnimation()
    {
        BeginAnimation(OrbitAngleProperty, null);
        OrbitAngle = 0;
    }
}
