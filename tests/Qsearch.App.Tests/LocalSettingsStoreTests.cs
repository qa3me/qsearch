using Qsearch.App.Services;
using Qsearch.Core.Indexing;
using Qsearch.Core.Settings;

namespace Qsearch.App.Tests;

public sealed class LocalSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_MissingFileReturnsPrivacySafeDefaults()
    {
        using SettingsDirectory directory = new();
        LocalSettingsStore store = new(directory.SettingsPath, TimeProvider.System);

        QsearchSettings settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(settings.MachineIndexConsent);
        Assert.Equal(new SearchSort(SearchSortField.Modified, SearchSortDirection.Descending), settings.Sort);
        Assert.Equal("en", settings.Language);
        Assert.Equal(StartupSurface.Full, settings.StartupSurface);
        Assert.Equal(AppThemePreference.System, settings.ThemePreference);
        Assert.Equal(HotkeyChoice.WindowsS, settings.HotkeyChoice);
        Assert.False(ReadBoolean(settings, "MachineIndexPromptHandled"));
        Assert.False(ReadBoolean(settings, "SettingsOnboardingCompleted"));
        Assert.Equal(5, settings.Version);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsAndLeavesNoTemporaryFile()
    {
        using SettingsDirectory directory = new();
        LocalSettingsStore store = new(directory.SettingsPath, TimeProvider.System);
        QsearchSettings expected = new(
            true,
            new SearchSort(SearchSortField.Name, SearchSortDirection.Ascending),
            "fa",
            StartupSurface.Compact);

        await store.SaveAsync(expected, TestContext.Current.CancellationToken);

        Assert.Equal(expected, await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(directory.SettingsPath + ".tmp"));
    }

    [Fact]
    public async Task LoadAsync_VersionOneSettingsUpgradeWithoutLosingUserChoices()
    {
        using SettingsDirectory directory = new();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(
            directory.SettingsPath,
            """{"machineIndexConsent":true,"sort":{"field":0,"direction":0},"language":"fa","version":1}""",
            TestContext.Current.CancellationToken);
        LocalSettingsStore store = new(directory.SettingsPath, TimeProvider.System);

        QsearchSettings settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(settings.MachineIndexConsent);
        Assert.Equal(new SearchSort(SearchSortField.Name, SearchSortDirection.Ascending), settings.Sort);
        Assert.Equal("fa", settings.Language);
        Assert.Equal(StartupSurface.Full, settings.StartupSurface);
        Assert.Equal(AppThemePreference.System, settings.ThemePreference);
        Assert.Equal(HotkeyChoice.WindowsS, settings.HotkeyChoice);
        Assert.True(ReadBoolean(settings, "MachineIndexPromptHandled"));
        Assert.False(ReadBoolean(settings, "SettingsOnboardingCompleted"));
        Assert.Equal(5, settings.Version);
        Assert.Empty(Directory.GetFiles(directory.Path, "settings.corrupt-*.json"));
    }

    [Fact]
    public async Task LoadAsync_VersionTwoSettingsPreserveStartupAndAddAppearanceDefaults()
    {
        using SettingsDirectory directory = new();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(
            directory.SettingsPath,
            """{"machineIndexConsent":true,"sort":{"field":0,"direction":0},"language":"fa","startupSurface":1,"version":2}""",
            TestContext.Current.CancellationToken);
        LocalSettingsStore store = new(directory.SettingsPath, TimeProvider.System);

        QsearchSettings settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(StartupSurface.Compact, settings.StartupSurface);
        Assert.Equal(AppThemePreference.System, settings.ThemePreference);
        Assert.Equal(HotkeyChoice.WindowsS, settings.HotkeyChoice);
        Assert.True(ReadBoolean(settings, "MachineIndexPromptHandled"));
        Assert.False(ReadBoolean(settings, "SettingsOnboardingCompleted"));
        Assert.Equal(5, settings.Version);
    }

    [Fact]
    public async Task LoadAsync_VersionThreeSettingsAddOneTimeOnboardingState()
    {
        using SettingsDirectory directory = new();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(
            directory.SettingsPath,
            """{"machineIndexConsent":false,"sort":{"field":4,"direction":1},"language":"en","startupSurface":0,"themePreference":2,"hotkeyChoice":2,"version":3}""",
            TestContext.Current.CancellationToken);
        LocalSettingsStore store = new(directory.SettingsPath, TimeProvider.System);

        QsearchSettings settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppThemePreference.Light, settings.ThemePreference);
        Assert.Equal(HotkeyChoice.AltQ, settings.HotkeyChoice);
        Assert.False(ReadBoolean(settings, "MachineIndexPromptHandled"));
        Assert.False(ReadBoolean(settings, "SettingsOnboardingCompleted"));
        Assert.Equal(5, settings.Version);
        Assert.Empty(Directory.GetFiles(directory.Path, "settings.corrupt-*.json"));
    }

    [Fact]
    public async Task LoadAsync_VersionFourDefaultShortcutMigratesToWinSButExplicitChoiceIsPreserved()
    {
        using SettingsDirectory directory = new();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(
            directory.SettingsPath,
            """{"machineIndexConsent":false,"sort":{"field":4,"direction":1},"language":"en","startupSurface":0,"themePreference":0,"hotkeyChoice":0,"version":4}""",
            TestContext.Current.CancellationToken);
        LocalSettingsStore store = new(directory.SettingsPath, TimeProvider.System);

        QsearchSettings migrated = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HotkeyChoice.WindowsS, migrated.HotkeyChoice);
        Assert.Equal(5, migrated.Version);

        await File.WriteAllTextAsync(
            directory.SettingsPath,
            """{"machineIndexConsent":false,"sort":{"field":4,"direction":1},"language":"en","startupSurface":0,"themePreference":0,"hotkeyChoice":2,"version":4}""",
            TestContext.Current.CancellationToken);
        QsearchSettings explicitChoice = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HotkeyChoice.AltQ, explicitChoice.HotkeyChoice);
    }

    [Fact]
    public async Task LoadAsync_CorruptFileIsPreservedAndDefaultsAreReturned()
    {
        using SettingsDirectory directory = new();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(
            directory.SettingsPath,
            "{not-json",
            TestContext.Current.CancellationToken);
        LocalSettingsStore store = new(
            directory.SettingsPath,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 30, 0, TimeSpan.Zero)));

        QsearchSettings settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(QsearchSettings.Default, settings);
        Assert.False(File.Exists(directory.SettingsPath));
        string[] preserved = Directory.GetFiles(directory.Path, "settings.corrupt-*.json");
        Assert.Single(preserved);
        Assert.Equal("{not-json", await File.ReadAllTextAsync(preserved[0], TestContext.Current.CancellationToken));
    }

    private sealed class SettingsDirectory : IDisposable
    {
        public SettingsDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Qsearch.Settings.Tests",
                Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public string SettingsPath => System.IO.Path.Combine(Path, "settings.json");

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static bool ReadBoolean(QsearchSettings settings, string propertyName)
    {
        System.Reflection.PropertyInfo? property = settings.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property.GetValue(settings));
    }
}
