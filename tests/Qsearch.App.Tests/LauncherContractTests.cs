namespace Qsearch.App.Tests;

public sealed class LauncherContractTests
{
    [Fact]
    public void ShortcutLauncher_IsCenteredGlassAndReturnsSelectionToMainWindow()
    {
        string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LauncherWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "LauncherWindow.xaml.cs"));
        string app = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "App.xaml.cs"));

        Assert.Contains("Width=\"720\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Qsearch.WindowOverlayBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("OnResultClick", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenMainRequested", code, StringComparison.Ordinal);
        Assert.Contains("ShowCentered", code, StringComparison.Ordinal);
        Assert.Contains("_windowsSHotkey.Pressed +=", app, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.InvokeAsync(ShowLauncher)", app, StringComparison.Ordinal);
        Assert.Contains("window.Hide();", app, StringComparison.Ordinal);
        Assert.Contains("ActivationSurface.Compact", app, StringComparison.Ordinal);
        Assert.DoesNotContain("await _mainViewModel.SetSearchTextAsync(string.Empty);\n            ShowWidget", app, StringComparison.Ordinal);
        int initializer = app.IndexOf("_indexInitializationTask =", StringComparison.Ordinal);
        Assert.True(initializer > app.IndexOf(
            "ShowWidget(System.Windows.Forms.Screen.FromPoint",
            StringComparison.Ordinal));
        Assert.True(initializer > app.IndexOf("ShowLauncher();", StringComparison.Ordinal));
        Assert.DoesNotContain("_mainViewModel.SetSearchTextAsync(string.Empty)", app, StringComparison.Ordinal);
        Assert.Contains("Status.Initializing", xaml, StringComparison.Ordinal);
        Assert.Contains("IsAnimated", xaml, StringComparison.Ordinal);
    }
}
