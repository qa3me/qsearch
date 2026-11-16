using System.Data.Common;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Qsearch.App.Services;
using Qsearch.App.ViewModels;
using Qsearch.Index.Sqlite;
using Qsearch.Safety;
using Qsearch.Safety.Evidence;
using Qsearch.Safety.Policy;
using Qsearch.Windows.Elevation;
using Qsearch.Windows.Shell;
using Qsearch.Windows.Volumes;

namespace Qsearch.App;

public partial class App : System.Windows.Application, IDisposable
{
    private SqliteIndexStore? _indexStore;
    private MainViewModel? _mainViewModel;
    private WidgetViewModel? _widgetViewModel;
    private WidgetWindow? _widgetWindow;
    private LauncherWindow? _launcherWindow;
    private AboutWindow? _aboutWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIconService? _trayIcon;
    private GlobalHotkeyService? _hotkeyService;
    private WindowsSHotkeyService? _windowsSHotkey;
    private HwndSource? _windowSource;
    private SingleInstanceCoordinator? _singleInstance;
    private MachineIndexingService? _machineIndexing;
    private FolderIndexingService? _folderIndexing;
    private readonly LocalizationService _localization = new();
    private readonly ThemeService _themeService = new();
    private Qsearch.Core.Settings.QsearchSettings _settings = Qsearch.Core.Settings.QsearchSettings.Default;
    private ActivationMessage? _pendingActivation;
    private bool _exitRequested;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Task<IndexInitializationResult>? _indexInitializationTask;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--diagnostics-offline-smoke", StringComparer.Ordinal))
        {
            Shutdown(await RunOfflineSmokeAsync());
            return;
        }

        string instanceKey = Environment.GetEnvironmentVariable("QSEARCH_INSTANCE_KEY") ??
            "desktop-app";
        _singleInstance = new SingleInstanceCoordinator(instanceKey, ReceiveActivationAsync);
        ActivationMessage launchMessage = CreateActivationMessage(e.Args);
        InstanceAcquisition acquisition = await _singleInstance.AcquireOrForwardAsync(
            launchMessage,
            CancellationToken.None);
        if (!acquisition.IsPrimary)
        {
            await _singleInstance.DisposeAsync();
            _singleInstance = null;
            _exitRequested = true;
            Shutdown();
            return;
        }

        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Qsearch");
        _indexStore = new SqliteIndexStore(Path.Combine(dataDirectory, "qsearch.db"), TimeProvider.System);

        FileReviewEngine reviewEngine = new(
            new FileEvidenceReader(),
            new AuthenticodeVerifier(),
            ReviewPolicy.VersionOne,
            TimeProvider.System);
        LocalSettingsStore settingsStore = new(
            Path.Combine(dataDirectory, "settings.json"),
            TimeProvider.System);
        Qsearch.Core.Settings.QsearchSettings settings = await settingsStore.LoadAsync(
            CancellationToken.None);
        _settings = settings;
        _themeService.Apply(Resources, settings.ThemePreference);
        _localization.Apply(settings.Language == "fa" ? "fa-IR" : "en-US");
        SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;
        string helperPath = Path.Combine(AppContext.BaseDirectory, "Qsearch.ElevatedIndexer.exe");
        string userSid = WindowsIdentity.GetCurrent().User?.Value ??
            throw new InvalidOperationException("The current Windows user identity is unavailable.");
        _machineIndexing = new MachineIndexingService(
            new FixedVolumeDiscovery(),
            new ElevatedIndexerLauncher(helperPath),
            _indexStore,
            settingsStore,
            new UsnMachineIndexSourceFactory(),
            userSid,
            TimeProvider.System);
        FirstRunViewModel firstRun = new(
            _machineIndexing,
            settings.MachineIndexPromptHandled || settings.MachineIndexConsent);
        _folderIndexing = new FolderIndexingService(
            _indexStore,
            TimeProvider.System,
            new DefaultFolderDiscovery());
        _mainViewModel = new MainViewModel(
            _indexStore,
            new ShellActionService(),
            new LocalFileReviewService(reviewEngine),
            _folderIndexing,
            firstRun,
            settingsStore,
            settings,
            new LocalFilePreviewService(),
            new WindowsFileAssociationService(),
            registerHotkey: ApplyHotkeyChoice,
            startupState: IndexStartupState.Initializing);
        _mainViewModel.SettingsChanged += OnSettingsChanged;
        _machineIndexing.SnapshotChanged += OnIndexingSnapshotChanged;
        _folderIndexing.SnapshotChanged += OnIndexingSnapshotChanged;

        MainWindow window = new(
            _mainViewModel,
            _localization,
            themeService: _themeService);
        ApplyWindowIcon(window);
        MainWindow = window;
        window.ShowCompactRequested += (_, _) =>
        {
            window.Hide();
            ShowWidget(System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle));
        };
        window.ShowAboutRequested += (_, _) => ShowAbout();
        window.ShowSettingsRequested += (_, _) => ShowSettings();
        window.Closing += (_, eventArgs) =>
        {
            if (!_exitRequested)
            {
                eventArgs.Cancel = true;
                window.Hide();
            }
        };
        window.SourceInitialized += (_, _) => ConfigureHotkey(window);
        _trayIcon = new TrayIconService(
            ToggleWidgetFromTray,
            ShowMainWindow,
            ShowAbout,
            RebuildIndex,
            ExitApplication,
            GetThemedIconPath());
        ActivationSurface initialSurface = launchMessage.Surface == ActivationSurface.Default
            ? settings.StartupSurface == Qsearch.Core.Settings.StartupSurface.Compact
                ? ActivationSurface.Compact
                : ActivationSurface.Full
            : launchMessage.Surface;
        if (initialSurface == ActivationSurface.Compact)
        {
            _ = new WindowInteropHelper(window).EnsureHandle();
            ShowWidget(System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position));
        }
        else if (initialSurface == ActivationSurface.Launcher)
        {
            _ = new WindowInteropHelper(window).EnsureHandle();
            ShowLauncher();
        }
        else
        {
            window.Show();
        }

        _indexInitializationTask = BackgroundIndexInitializer.InitializeAsync(
            _indexStore,
            _lifetimeCancellation.Token);
        IndexInitializationResult initialization;
        try
        {
            initialization = await _indexInitializationTask;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (initialization == IndexInitializationResult.Ready)
        {
            await _mainViewModel.MarkIndexReadyAsync();
            _ = InitializeIndexingAsync(firstRun, settings);
        }
        else
        {
            _mainViewModel.MarkIndexUnavailable();
        }

        if (_pendingActivation is not null)
        {
            ApplyActivation(_pendingActivation);
            _pendingActivation = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
        if (_singleInstance is not null)
        {
            _singleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _singleInstance = null;
        }

        _windowSource?.RemoveHook(WindowHook);
        _windowSource = null;
        _hotkeyService?.Dispose();
        _hotkeyService = null;
        _windowsSHotkey?.Dispose();
        _windowsSHotkey = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _aboutWindow?.Close();
        _aboutWindow = null;
        _settingsWindow?.Close();
        _settingsWindow = null;
        _launcherWindow?.Close();
        _launcherWindow = null;
        _widgetWindow?.Close();
        _widgetWindow = null;
        _widgetViewModel?.Dispose();
        _widgetViewModel = null;
        if (_mainViewModel is not null)
        {
            _mainViewModel.SettingsChanged -= OnSettingsChanged;
            _mainViewModel.Dispose();
        }
        _mainViewModel = null;
        if (_folderIndexing is not null)
        {
            _folderIndexing.SnapshotChanged -= OnIndexingSnapshotChanged;
            _folderIndexing.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _folderIndexing = null;
        }

        if (_machineIndexing is not null)
        {
            _machineIndexing.SnapshotChanged -= OnIndexingSnapshotChanged;
            _machineIndexing.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _machineIndexing = null;
        }

        SqliteIndexStore? indexStore = _indexStore;
        _indexStore = null;
        if (indexStore is not null && _indexInitializationTask is { IsCompleted: false } initialization)
        {
            _ = initialization.ContinueWith(
                async _ => await indexStore.DisposeAsync(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
        }
        else if (indexStore is not null)
        {
            indexStore.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        GC.SuppressFinalize(this);
    }

    private ValueTask ReceiveActivationAsync(ActivationMessage message)
    {
        System.Windows.Threading.DispatcherOperation operation = Dispatcher.InvokeAsync(
            () => ApplyActivation(message));
        return new ValueTask(operation.Task);
    }

    private void ApplyActivation(ActivationMessage message)
    {
        if (_mainViewModel is null || MainWindow is null)
        {
            _pendingActivation = message;
            return;
        }

        ActivationSurface surface = message.Surface == ActivationSurface.Default
            ? _settings.StartupSurface == Qsearch.Core.Settings.StartupSurface.Compact
                ? ActivationSurface.Compact
                : ActivationSurface.Full
            : message.Surface;
        switch (surface)
        {
            case ActivationSurface.Compact:
                MainWindow.Hide();
                ShowWidget(System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position));
                break;
            case ActivationSurface.Launcher:
                ShowLauncher();
                break;
            default:
                ShowMainWindow();
                break;
        }
        if (!string.IsNullOrWhiteSpace(message.SearchText))
        {
            _ = SearchFromActivationAsync(message.SearchText);
        }
    }

    private async Task SearchFromActivationAsync(string searchText)
    {
        if (_mainViewModel is not null)
        {
            await _mainViewModel.SetSearchTextAsync(searchText);
        }
    }

    private void ConfigureHotkey(MainWindow window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource.AddHook(WindowHook);
        _hotkeyService = new GlobalHotkeyService(new WindowsHotkeyNative(), handle);
        _hotkeyService.Pressed += (_, _) => ShowLauncher();
        HotkeyRegistrationResult result = ApplyHotkeyChoice(_settings.HotkeyChoice);
        if (result.State == HotkeyRegistrationState.Conflict)
        {
            _mainViewModel?.ReportHotkeyConflict();
        }
    }

    private HotkeyRegistrationResult ApplyHotkeyChoice(Qsearch.Core.Settings.HotkeyChoice choice)
    {
        if (_hotkeyService is null)
        {
            return new HotkeyRegistrationResult(HotkeyRegistrationState.Conflict);
        }

        if (choice == Qsearch.Core.Settings.HotkeyChoice.WindowsS)
        {
            if (_windowsSHotkey is null)
            {
                _windowsSHotkey = new WindowsSHotkeyService();
                _windowsSHotkey.Pressed += (_, _) =>
                    _ = Dispatcher.InvokeAsync(ShowLauncher);
            }

            HotkeyRegistrationResult result = _windowsSHotkey.Start();
            if (result.State == HotkeyRegistrationState.Registered)
            {
                _hotkeyService.Unregister();
            }

            return result;
        }

        HotkeyRegistrationResult registered = _hotkeyService.Register(
            HotkeyGesture.FromChoice(choice));
        if (registered.State == HotkeyRegistrationState.Registered)
        {
            _windowsSHotkey?.Dispose();
            _windowsSHotkey = null;
        }

        return registered;
    }

    private void OnSettingsChanged(
        object? sender,
        QsearchSettingsChangedEventArgs eventArgs)
    {
        Qsearch.Core.Settings.QsearchSettings settings = eventArgs.Current;
        bool themeChanged = settings.ThemePreference != _settings.ThemePreference;
        _settings = settings;
        if (themeChanged)
        {
            _themeService.Apply(Resources, settings.ThemePreference);
            (MainWindow as MainWindow)?.RefreshTheme();
            _aboutWindow?.RefreshTheme();
            _settingsWindow?.RefreshTheme();
            _widgetWindow?.RefreshTheme();
            _launcherWindow?.RefreshTheme();
            ApplyWindowIcon(MainWindow);
            ApplyWindowIcon(_aboutWindow);
            ApplyWindowIcon(_settingsWindow);
            ApplyWindowIcon(_widgetWindow);
            ApplyWindowIcon(_launcherWindow);
            _trayIcon?.UpdateIcon(GetThemedIconPath());
        }

    }

    private void OnWindowsPreferenceChanged(object sender, UserPreferenceChangedEventArgs eventArgs)
    {
        if (_settings.ThemePreference != Qsearch.Core.Settings.AppThemePreference.System)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            _themeService.Apply(Resources, _settings.ThemePreference);
            (MainWindow as MainWindow)?.RefreshTheme();
            _aboutWindow?.RefreshTheme();
            _settingsWindow?.RefreshTheme();
            _widgetWindow?.RefreshTheme();
            _launcherWindow?.RefreshTheme();
            ApplyWindowIcon(MainWindow);
            ApplyWindowIcon(_aboutWindow);
            ApplyWindowIcon(_settingsWindow);
            ApplyWindowIcon(_widgetWindow);
            ApplyWindowIcon(_launcherWindow);
            _trayIcon?.UpdateIcon(GetThemedIconPath());
        });
    }

    private IntPtr WindowHook(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        handled = _hotkeyService?.HandleMessage(message, wParam) == true;
        return IntPtr.Zero;
    }

    private void ShowMainWindow()
    {
        if (MainWindow is not MainWindow window)
        {
            return;
        }

        _widgetWindow?.Hide();
        _launcherWindow?.Hide();
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.FocusSearch();
    }

    private void ToggleWidgetFromTray()
    {
        if (_widgetWindow?.IsVisible == true)
        {
            _widgetWindow.Hide();
            return;
        }

        ShowWidget(System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position));
    }

    private void ShowWidget(System.Windows.Forms.Screen screen)
    {
        if (_mainViewModel is null)
        {
            return;
        }

        MainWindow?.Hide();
        _launcherWindow?.Hide();

        if (_widgetWindow is null)
        {
            _widgetWindow = new WidgetWindow(GetQuickViewModel(), themeService: _themeService);
            ApplyWindowIcon(_widgetWindow);
            _widgetWindow.OpenMainRequested += (_, _) => ShowMainWindow();
            _widgetWindow.OpenAboutRequested += (_, _) => ShowAbout();
        }

        _widgetWindow.FlowDirection = _localization.CurrentCulture.TwoLetterISOLanguageName == "fa"
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;
        _widgetWindow.ShowNear(screen);
    }

    private void ShowLauncher()
    {
        if (_mainViewModel is null)
        {
            return;
        }

        _widgetWindow?.Hide();
        if (_launcherWindow is null)
        {
            _launcherWindow = new LauncherWindow(GetQuickViewModel(), themeService: _themeService);
            ApplyWindowIcon(_launcherWindow);
            _launcherWindow.OpenMainRequested += (_, _) => ShowMainWindow();
        }

        _launcherWindow.FlowDirection = _localization.CurrentCulture.TwoLetterISOLanguageName == "fa"
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;
        _launcherWindow.ShowCentered(
            System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position));
    }

    private WidgetViewModel GetQuickViewModel()
    {
        _widgetViewModel ??= new WidgetViewModel(
            _mainViewModel ?? throw new InvalidOperationException("The main view model is unavailable."));
        return _widgetViewModel;
    }

    private static ActivationMessage CreateActivationMessage(IReadOnlyList<string> arguments)
    {
        ActivationSurface surface = ActivationSurface.Default;
        List<string> searchTerms = [];
        foreach (string argument in arguments)
        {
            switch (argument.ToLowerInvariant())
            {
                case "--compact":
                    surface = ActivationSurface.Compact;
                    break;
                case "--full":
                    surface = ActivationSurface.Full;
                    break;
                case "--launcher":
                    surface = ActivationSurface.Launcher;
                    break;
                default:
                    searchTerms.Add(argument);
                    break;
            }
        }

        string? searchText = searchTerms.Count == 0 ? null : string.Join(' ', searchTerms);
        return new ActivationMessage(searchText, surface);
    }

    private void ShowAbout()
    {
        _widgetWindow?.Hide();
        if (_aboutWindow is null)
        {
            _aboutWindow = new AboutWindow(new AboutViewModel(), themeService: _themeService);
            ApplyWindowIcon(_aboutWindow);
            if (MainWindow?.IsVisible == true)
            {
                _aboutWindow.Owner = MainWindow;
            }

            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        }

        _aboutWindow.FlowDirection = _localization.CurrentCulture.TwoLetterISOLanguageName == "fa"
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    private void ShowSettings()
    {
        if (_mainViewModel is null)
        {
            return;
        }

        _widgetWindow?.Hide();
        _launcherWindow?.Hide();
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_mainViewModel, themeService: _themeService);
            ApplyWindowIcon(_settingsWindow);
            if (MainWindow?.IsVisible == true)
            {
                _settingsWindow.Owner = MainWindow;
            }

            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.FlowDirection = _localization.CurrentCulture.TwoLetterISOLanguageName == "fa"
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void RebuildIndex()
    {
        _ = RebuildIndexAsync();
    }

    private async Task RebuildIndexAsync()
    {
        if (_machineIndexing is null || _mainViewModel?.IndexStartupState != IndexStartupState.Ready)
        {
            return;
        }

        string status = TryFindResource("Status.Rebuilding") as string ??
            "Rebuilding the local index";
        _mainViewModel?.ReportStatus(status);
        try
        {
            await _machineIndexing.RebuildAsync(CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is DbException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            string unavailable = TryFindResource("Status.IndexUnavailable") as string ??
                "The local index is unavailable";
            _mainViewModel?.ReportStatus(unavailable);
        }
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Shutdown();
    }

    private async Task InitializeIndexingAsync(
        FirstRunViewModel firstRun,
        Qsearch.Core.Settings.QsearchSettings settings)
    {
        try
        {
            if (!settings.MachineIndexConsent && _folderIndexing is not null)
            {
                await _folderIndexing.StartDefaultAsync(CancellationToken.None);
            }

            await firstRun.InitializeAsync(CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is DbException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _mainViewModel?.ReportStatus("index-unavailable");
        }
    }

    private void OnIndexingSnapshotChanged(IndexingSnapshot snapshot)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_mainViewModel is not null)
            {
                _ = _mainViewModel.ApplyIndexingSnapshotAsync(snapshot).AsTask();
            }
        });
    }

    private static async Task<int> RunOfflineSmokeAsync()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Qsearch.OfflineSmoke",
            Guid.NewGuid().ToString("N"));
        try
        {
            await using SqliteIndexStore store = new(
                Path.Combine(directory, "smoke.db"),
                TimeProvider.System);
            await store.InitializeAsync(CancellationToken.None);
            await store.SearchAsync(
                new Qsearch.Core.Indexing.SearchRequest(
                    Qsearch.Core.Search.SearchQueryParser.Parse("offline-smoke"),
                    1),
                CancellationToken.None);
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return 2;
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private string GetThemedIconPath()
    {
        string fileName = _themeService.CurrentVariant == AppThemeVariant.Dark
            ? "qsearch-dark.ico"
            : "qsearch-light.ico";
        return Path.Combine(AppContext.BaseDirectory, fileName);
    }

    private void ApplyWindowIcon(Window? window)
    {
        if (window is null)
        {
            return;
        }

        string path = GetThemedIconPath();
        if (File.Exists(path))
        {
            window.Icon = BitmapFrame.Create(new Uri(path, UriKind.Absolute));
        }
    }
}
