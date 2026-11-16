using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Qsearch.App.Services;
using Qsearch.App.ViewModels;
using Qsearch.Windows.Shell;
using FormsScreen = System.Windows.Forms.Screen;

namespace Qsearch.App;

public partial class LauncherWindow : Window, IDisposable
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private readonly WidgetViewModel _viewModel;
    private readonly WindowBackdropService _backdropService;
    private readonly ThemeService _themeService;
    private readonly SearchInputDebouncer _searchDebouncer;
    private bool _lightDismissArmed;
    private bool _isDisposed;

    public LauncherWindow(
        WidgetViewModel viewModel,
        WindowBackdropService? backdropService = null,
        ThemeService? themeService = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _backdropService = backdropService ?? new WindowBackdropService();
        _themeService = themeService ?? new ThemeService();
        _searchDebouncer = new SearchInputDebouncer(TimeSpan.FromMilliseconds(85));
        DataContext = viewModel;
        InitializeComponent();
        Closed += OnClosed;
        SourceInitialized += (_, _) => ApplyBackdrop();
    }

    public event EventHandler? OpenMainRequested;

    public void ShowCentered(FormsScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        nint handle = new WindowInteropHelper(this).EnsureHandle();
        _lightDismissArmed = false;
        PositionCentered(handle, screen);
        Show();
        PositionCentered(handle, screen);
        _ = Dispatcher.BeginInvoke(
            () =>
            {
                Activate();
                LauncherSearchBox.Focus();
                LauncherSearchBox.SelectAll();
                _lightDismissArmed = true;
            },
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    public void RefreshTheme()
    {
        ApplyThemeSurface();
        InvalidateVisual();
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        string text = LauncherSearchBox.Text;
        await _searchDebouncer.SubmitAsync(_ => _viewModel.SearchAsync(text));
    }

    private void OnResultClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: ResultItemViewModel result })
        {
            _viewModel.SelectedResult = result;
            ReturnToMain();
        }
    }

    private void OnOpenMainClick(object sender, RoutedEventArgs e) => ReturnToMain();

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_lightDismissArmed)
        {
            Hide();
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs) => Dispose();

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _searchDebouncer.Dispose();
        Closed -= OnClosed;
        GC.SuppressFinalize(this);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter when _viewModel.SelectedResult is not null:
                ReturnToMain();
                e.Handled = true;
                break;
        }
    }

    private void ReturnToMain()
    {
        Hide();
        OpenMainRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MoveSelection(int offset)
    {
        if (LauncherResults.Items.Count == 0)
        {
            return;
        }

        int current = LauncherResults.SelectedIndex;
        int next = current < 0
            ? offset > 0 ? 0 : LauncherResults.Items.Count - 1
            : Math.Clamp(current + offset, 0, LauncherResults.Items.Count - 1);
        LauncherResults.SelectedIndex = next;
        LauncherResults.ScrollIntoView(LauncherResults.SelectedItem);
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
    }

    private void ApplyThemeSurface()
    {
        Background = new SolidColorBrush(_themeService.CurrentVariant == AppThemeVariant.Dark
            ? System.Windows.Media.Color.FromArgb(1, 6, 9, 13)
            : System.Windows.Media.Color.FromArgb(1, 244, 247, 246));
        Surface.Background = ((System.Windows.Media.Brush)FindResource("Qsearch.WindowOverlayBrush")).CloneCurrentValue();
        LayoutRoot.Background = ((System.Windows.Media.Brush)FindResource("Qsearch.GridBrush")).CloneCurrentValue();
    }

    private void PositionCentered(nint handle, FormsScreen screen)
    {
        double scale = Math.Max(GetDpiForWindow(handle), 96) / 96d;
        double width = Width * scale;
        double height = Height * scale;
        int x = (int)Math.Round(screen.WorkingArea.Left + ((screen.WorkingArea.Width - width) / 2d));
        int y = (int)Math.Round(screen.WorkingArea.Top + ((screen.WorkingArea.Height - height) / 2d));
        _ = SetWindowPos(
            handle,
            0,
            x,
            y,
            (int)Math.Round(width),
            (int)Math.Round(height),
            SwpNoActivate | SwpNoZOrder);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint windowHandle);
}
