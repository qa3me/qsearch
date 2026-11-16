using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Qsearch.App.Services;
using Qsearch.App.ViewModels;
using Qsearch.Windows.Shell;

namespace Qsearch.App;

public partial class SettingsWindow : Window
{
    private readonly WindowBackdropService _backdropService;
    private readonly WindowCaptionService _captionService;
    private readonly ThemeService _themeService;
    private readonly SmoothScrollBehavior _smoothScroll;

    public SettingsWindow(
        MainViewModel viewModel,
        WindowBackdropService? backdropService = null,
        WindowCaptionService? captionService = null,
        ThemeService? themeService = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _backdropService = backdropService ?? new WindowBackdropService();
        _captionService = captionService ?? new WindowCaptionService();
        _themeService = themeService ?? new ThemeService();
        DataContext = viewModel;
        InitializeComponent();
        _smoothScroll = SmoothScrollBehavior.Attach(LayoutRoot);
        Closed += OnClosed;
        SourceInitialized += (_, _) => ApplyBackdrop();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _smoothScroll.Dispose();
        Closed -= OnClosed;
    }

    private void ApplyBackdrop()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        WindowBackdropResult result = _backdropService.Apply(
            handle,
            WindowBackdropKind.Transient,
            SystemParameters.HighContrast);
        if (result.Applied)
        {
            ApplyThemeSurface();
        }

        RefreshTheme();
    }

    public void RefreshTheme()
    {
        ApplyThemeSurface();
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return;
        }

        _captionService.Apply(
            handle,
            _themeService.CurrentVariant == AppThemeVariant.Dark
                ? WindowCaptionTheme.Dark
                : WindowCaptionTheme.Light,
            SystemParameters.HighContrast);
        InvalidateVisual();
    }

    private void ApplyThemeSurface()
    {
        Background = new SolidColorBrush(_themeService.CurrentVariant == AppThemeVariant.Dark
            ? System.Windows.Media.Color.FromArgb(1, 6, 9, 13)
            : System.Windows.Media.Color.FromArgb(1, 244, 247, 246));
        LayoutRoot.Background = ((System.Windows.Media.Brush)FindResource("Qsearch.GridBrush")).CloneCurrentValue();
    }
}
