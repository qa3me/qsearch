using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Qsearch.App.Services;
using Qsearch.App.ViewModels;
using Qsearch.Core.Indexing;
using Qsearch.Windows.Shell;

namespace Qsearch.App;

public partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly LocalizationService _localization;
    private readonly WindowBackdropService _backdropService;
    private readonly WindowCaptionService _captionService;
    private readonly ThemeService _themeService;
    private readonly SmoothScrollBehavior _smoothScroll;
    private readonly SearchInputDebouncer _searchDebouncer;
    private bool _isDisposed;

    public event EventHandler? ShowCompactRequested;

    public event EventHandler? ShowAboutRequested;

    public event EventHandler? ShowSettingsRequested;

    public MainWindow(
        MainViewModel viewModel,
        LocalizationService localization,
        WindowBackdropService? backdropService = null,
        WindowCaptionService? captionService = null,
        ThemeService? themeService = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(localization);
        _viewModel = viewModel;
        _localization = localization;
        _backdropService = backdropService ?? new WindowBackdropService();
        _captionService = captionService ?? new WindowCaptionService();
        _themeService = themeService ?? new ThemeService();
        _searchDebouncer = new SearchInputDebouncer(TimeSpan.FromMilliseconds(85));
        DataContext = viewModel;
        InitializeComponent();
        ApplyLanguageDirection();
        _smoothScroll = SmoothScrollBehavior.Attach(LayoutRoot);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
        SourceInitialized += (_, _) => ApplyBackdrop();
        Loaded += async (_, _) =>
        {
            ApplyResponsiveState(useTransitions: false);
            SearchBox.Focus();
            await _viewModel.SetSearchTextAsync(_viewModel.SearchText);
            await _viewModel.MarkSettingsOnboardingShownAsync(CancellationToken.None);
        };
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        string text = SearchBox.Text;
        await _searchDebouncer.SubmitAsync(
            _ => _viewModel.SetSearchTextAsync(text));
    }

    private async void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await _viewModel.OpenAsync(CancellationToken.None);
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = FindResource("Scope.Choose") as string ?? "Choose a folder",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.AddFolderAsync(dialog.FolderName, CancellationToken.None);
        }
    }

    private async void OnPersianClick(object sender, RoutedEventArgs e)
    {
        _localization.Apply("fa-IR");
        ApplyLanguageDirection();
        await _viewModel.SetLanguageAsync("fa", CancellationToken.None);
        await _viewModel.RefreshLocalizedDetailsAsync(
            _localization.CurrentCulture,
            CancellationToken.None);
    }

    private async void OnEnglishClick(object sender, RoutedEventArgs e)
    {
        _localization.Apply("en-US");
        ApplyLanguageDirection();
        await _viewModel.SetLanguageAsync("en", CancellationToken.None);
        await _viewModel.RefreshLocalizedDetailsAsync(
            _localization.CurrentCulture,
            CancellationToken.None);
    }

    private void ApplyLanguageDirection()
    {
        FlowDirection = _localization.CurrentCulture.TwoLetterISOLanguageName == "fa"
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;
    }

    private void OnCompactClick(object sender, RoutedEventArgs e)
    {
        ShowCompactRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        ShowAboutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.HideSettingsOnboarding();
        ShowSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnMachineAccessClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.DismissMachineIndexPromptAsync(CancellationToken.None);
        if (_viewModel.FirstRun is not null)
        {
            await _viewModel.FirstRun.AcceptAsync(CancellationToken.None);
            if (_viewModel.FirstRun.StatusMessage == "indexing-started")
            {
                await _viewModel.ConfirmMachineIndexConsentAsync(CancellationToken.None);
            }
        }
    }

    private async void OnFolderOnlyClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.DismissMachineIndexPromptAsync(CancellationToken.None);
        _viewModel.FirstRun?.ContinueFolderOnly();
    }

    private async void OnSortHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewColumnHeader { Tag: string fieldName } &&
            Enum.TryParse(fieldName, ignoreCase: false, out SearchSortField field))
        {
            await _viewModel.SetSortAsync(field);
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveState(useTransitions: IsLoaded);
    }

    private void ApplyResponsiveState(bool useTransitions)
    {
        bool wide = ActualWidth >= 1120;
        bool medium = ActualWidth >= 880;
        bool showRightRail = medium && _viewModel.ShouldShowRightRail;
        string state = wide ? "WideState" : medium ? "MediumState" : "NarrowState";
        RightRail.Visibility = showRightRail ? Visibility.Visible : Visibility.Collapsed;
        RightRailColumn.Width = showRightRail
            ? new GridLength(wide ? 336 : 292)
            : new GridLength(0);
        ResultsHost.Margin = showRightRail ? new Thickness(0, 0, 12, 0) : new Thickness(0);
        CompactButton.Visibility = medium ? Visibility.Visible : Visibility.Collapsed;
        HeaderCopy.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        VisualStateManager.GoToElementState(LayoutRoot, state, useTransitions);
    }

    private void ApplyBackdrop()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        WindowBackdropResult result = _backdropService.Apply(
            handle,
            WindowBackdropKind.Main,
            SystemParameters.HighContrast);
        if (result.Applied)
        {
            ApplyThemeSurface();
        }

        ApplyCaption();
    }

    public void RefreshTheme()
    {
        ApplyThemeSurface();
        ApplyCaption();
        InvalidateVisual();
    }

    private void ApplyThemeSurface()
    {
        Background = new SolidColorBrush(_themeService.CurrentVariant == AppThemeVariant.Dark
            ? System.Windows.Media.Color.FromArgb(1, 6, 9, 13)
            : System.Windows.Media.Color.FromArgb(1, 244, 247, 246));
        LayoutRoot.Background = ((System.Windows.Media.Brush)FindResource("Qsearch.GridBrush")).CloneCurrentValue();
    }

    private void ApplyCaption()
    {
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
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(MainViewModel.ShouldShowRightRail))
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ApplyResponsiveState(useTransitions: IsLoaded);
        }
        else
        {
            _ = Dispatcher.InvokeAsync(() => ApplyResponsiveState(useTransitions: IsLoaded));
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _searchDebouncer.Dispose();
        _smoothScroll.Dispose();
        Closed -= OnClosed;
        GC.SuppressFinalize(this);
    }
}
