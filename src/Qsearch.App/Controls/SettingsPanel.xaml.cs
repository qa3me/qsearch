using System.Windows;
using System.Windows.Controls;
using Qsearch.App.ViewModels;
using Qsearch.Core.Settings;

namespace Qsearch.App.Controls;

public partial class SettingsPanel : System.Windows.Controls.UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void OnStartupFullClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetStartupSurfaceAsync(StartupSurface.Full, CancellationToken.None);
        }
    }

    private async void OnStartupCompactClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetStartupSurfaceAsync(StartupSurface.Compact, CancellationToken.None);
        }
    }

    private async void OnThemeSystemClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetThemePreferenceAsync(AppThemePreference.System, CancellationToken.None);
        }
    }

    private async void OnThemeDarkClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetThemePreferenceAsync(AppThemePreference.Dark, CancellationToken.None);
        }
    }

    private async void OnThemeLightClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetThemePreferenceAsync(AppThemePreference.Light, CancellationToken.None);
        }
    }

    private async void OnHotkeyControlAltSpaceClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetHotkeyChoiceAsync(HotkeyChoice.ControlAltSpace, CancellationToken.None);
        }
    }

    private async void OnHotkeyWindowsSClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetHotkeyChoiceAsync(HotkeyChoice.WindowsS, CancellationToken.None);
        }
    }

    private async void OnHotkeyControlShiftSpaceClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetHotkeyChoiceAsync(HotkeyChoice.ControlShiftSpace, CancellationToken.None);
        }
    }

    private async void OnHotkeyAltQClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetHotkeyChoiceAsync(HotkeyChoice.AltQ, CancellationToken.None);
        }
    }
}
