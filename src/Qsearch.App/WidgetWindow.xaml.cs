using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Qsearch.App.Services;
using Qsearch.App.ViewModels;
using Qsearch.Windows.Shell;
using DrawingRectangle = System.Drawing.Rectangle;
using FormsScreen = System.Windows.Forms.Screen;

namespace Qsearch.App;

public partial class WidgetWindow : Window, IDisposable
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private readonly WidgetViewModel _viewModel;
    private readonly WindowBackdropService _backdropService;
    private readonly ThemeService _themeService;
    private readonly SearchInputDebouncer _searchDebouncer;
    private bool _lightDismissArmed;
    private bool _isDisposed;

    public WidgetWindow(
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
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                FocusSearch();
            }
        };
    }

    public event EventHandler? OpenMainRequested;

    public event EventHandler? OpenAboutRequested;

    public void ShowNear(FormsScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        nint handle = new WindowInteropHelper(this).EnsureHandle();
        _lightDismissArmed = false;
        PositionWindow(handle, screen);
        Show();
        PositionWindow(handle, screen);
        _ = Dispatcher.BeginInvoke(
            () =>
            {
                Activate();
                FocusSearch();
                _lightDismissArmed = true;
            },
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        string text = WidgetSearchBox.Text;
        await _searchDebouncer.SubmitAsync(_ => _viewModel.SearchAsync(text));
    }

    private async void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await _viewModel.OpenAsync(CancellationToken.None);
        Hide();
    }

    private async void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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
                await _viewModel.OpenAsync(CancellationToken.None);
                Hide();
                e.Handled = true;
                break;
        }
    }

    private async void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string tag } &&
            Enum.TryParse(tag, ignoreCase: false, out WidgetFilter filter))
        {
            await _viewModel.ApplyFilterAsync(filter);
            UpdateFilterStyles(filter);
            FocusSearch();
        }
    }

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

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnOpenMainClick(object sender, RoutedEventArgs e)
    {
        Hide();
        OpenMainRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        Hide();
        OpenAboutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MoveSelection(int offset)
    {
        if (WidgetResults.Items.Count == 0)
        {
            return;
        }

        int current = WidgetResults.SelectedIndex;
        int next = current < 0
            ? offset > 0 ? 0 : WidgetResults.Items.Count - 1
            : Math.Clamp(current + offset, 0, WidgetResults.Items.Count - 1);
        WidgetResults.SelectedIndex = next;
        WidgetResults.ScrollIntoView(WidgetResults.SelectedItem);
    }

    private void FocusSearch()
    {
        WidgetSearchBox.Focus();
        WidgetSearchBox.SelectAll();
    }

    private void UpdateFilterStyles(WidgetFilter filter)
    {
        Style selected = (Style)FindResource("Qsearch.SecondaryButton");
        Style idle = (Style)FindResource("Qsearch.GhostButton");
        AllFilterButton.Style = filter == WidgetFilter.All ? selected : idle;
        FilesFilterButton.Style = filter == WidgetFilter.Files ? selected : idle;
        FoldersFilterButton.Style = filter == WidgetFilter.Folders ? selected : idle;
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

    public void RefreshTheme()
    {
        ApplyThemeSurface();
        InvalidateVisual();
    }

    private void ApplyThemeSurface()
    {
        Background = new SolidColorBrush(_themeService.CurrentVariant == AppThemeVariant.Dark
            ? System.Windows.Media.Color.FromArgb(1, 6, 9, 13)
            : System.Windows.Media.Color.FromArgb(1, 244, 247, 246));
        Surface.Background = ((System.Windows.Media.Brush)FindResource("Qsearch.WindowOverlayBrush")).CloneCurrentValue();
        LayoutRoot.Background = ((System.Windows.Media.Brush)FindResource("Qsearch.GridBrush")).CloneCurrentValue();
    }

    private void PositionWindow(nint handle, FormsScreen screen)
    {
        DrawingRectangle work = screen.WorkingArea;
        ScreenRect workArea = new(work.Left, work.Top, work.Width, work.Height);
        ScreenRect taskbar = GetTaskbarRect(screen.Bounds, work);
        double scale = Math.Max(GetDpiForWindow(handle), 96) / 96d;
        ScreenSize physicalSize = new(Width * scale, Height * scale);
        ScreenRect placement = TaskbarPopoverPlacement.Calculate(
            workArea,
            taskbar,
            physicalSize,
            FlowDirection == System.Windows.FlowDirection.RightToLeft
                ? PopoverFlowDirection.RightToLeft
                : PopoverFlowDirection.LeftToRight);
        _ = SetWindowPos(
            handle,
            0,
            (int)Math.Round(placement.X),
            (int)Math.Round(placement.Y),
            (int)Math.Round(placement.Width),
            (int)Math.Round(placement.Height),
            SwpNoActivate | SwpNoZOrder);
    }

    private static ScreenRect GetTaskbarRect(DrawingRectangle bounds, DrawingRectangle work)
    {
        if (work.Top > bounds.Top)
        {
            return new ScreenRect(bounds.Left, bounds.Top, bounds.Width, work.Top - bounds.Top);
        }

        if (work.Left > bounds.Left)
        {
            return new ScreenRect(bounds.Left, bounds.Top, work.Left - bounds.Left, bounds.Height);
        }

        if (work.Right < bounds.Right)
        {
            return new ScreenRect(work.Right, bounds.Top, bounds.Right - work.Right, bounds.Height);
        }

        if (work.Bottom < bounds.Bottom)
        {
            return new ScreenRect(bounds.Left, work.Bottom, bounds.Width, bounds.Bottom - work.Bottom);
        }

        return new ScreenRect(bounds.Left, bounds.Bottom, bounds.Width, 1);
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
