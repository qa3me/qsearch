using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Qsearch.App.Services;
using Qsearch.App.ViewModels;
using Qsearch.Windows.Shell;

namespace Qsearch.App;

public partial class AboutWindow : Window
{
    private readonly AboutViewModel _viewModel;
    private readonly IExternalLinkService _externalLinks;
    private readonly WindowBackdropService _backdropService;
    private readonly WindowCaptionService _captionService;
    private readonly ThemeService _themeService;

    public AboutWindow(
        AboutViewModel viewModel,
        IExternalLinkService? externalLinks = null,
        WindowBackdropService? backdropService = null,
        WindowCaptionService? captionService = null,
        ThemeService? themeService = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _externalLinks = externalLinks ?? new ExternalLinkService();
        _backdropService = backdropService ?? new WindowBackdropService();
        _captionService = captionService ?? new WindowCaptionService();
        _themeService = themeService ?? new ThemeService();
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyBackdrop();
    }

    private void OnDeveloperClick(object sender, RoutedEventArgs e)
    {
        _externalLinks.OpenHttps(_viewModel.DeveloperUri);
    }

    private void OnProductClick(object sender, RoutedEventArgs e)
    {
        _externalLinks.OpenHttps(_viewModel.ProductUri);
    }

    private void OnGitHubClick(object sender, RoutedEventArgs e)
    {
        _externalLinks.OpenHttps(_viewModel.GitHubUri);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

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
