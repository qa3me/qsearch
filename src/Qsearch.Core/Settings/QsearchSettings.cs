using Qsearch.Core.Indexing;

namespace Qsearch.Core.Settings;

public enum StartupSurface
{
    Full,
    Compact,
}

public enum AppThemePreference
{
    System,
    Dark,
    Light,
}

public enum HotkeyChoice
{
    ControlAltSpace,
    ControlShiftSpace,
    AltQ,
    WindowsS,
}

public sealed record QsearchSettings(
    bool MachineIndexConsent,
    SearchSort Sort,
    string Language,
    StartupSurface StartupSurface = StartupSurface.Full,
    AppThemePreference ThemePreference = AppThemePreference.System,
    HotkeyChoice HotkeyChoice = HotkeyChoice.WindowsS,
    bool MachineIndexPromptHandled = false,
    bool SettingsOnboardingCompleted = false,
    int Version = 5)
{
    public static QsearchSettings Default { get; } = new(
        false,
        new SearchSort(SearchSortField.Modified, SearchSortDirection.Descending),
        "en");

    public bool IsValid()
    {
        return Version == 5 &&
            Enum.IsDefined(Sort.Field) &&
            Enum.IsDefined(Sort.Direction) &&
            Enum.IsDefined(StartupSurface) &&
            Enum.IsDefined(ThemePreference) &&
            Enum.IsDefined(HotkeyChoice) &&
            Language is "en" or "fa";
    }

    public bool CanUpgrade()
    {
        return Version is >= 1 and <= 4 &&
            Enum.IsDefined(Sort.Field) &&
            Enum.IsDefined(Sort.Direction) &&
            (Version == 1 || Enum.IsDefined(StartupSurface)) &&
            (Version < 3 ||
                (Enum.IsDefined(ThemePreference) && Enum.IsDefined(HotkeyChoice))) &&
            Language is "en" or "fa";
    }

    public QsearchSettings Upgrade()
    {
        if (IsValid())
        {
            return this;
        }

        if (!CanUpgrade())
        {
            throw new InvalidOperationException("The settings cannot be upgraded.");
        }

        return this with
        {
            StartupSurface = Version == 1 ? StartupSurface.Full : StartupSurface,
            ThemePreference = Version < 3 ? AppThemePreference.System : ThemePreference,
            HotkeyChoice = Version < 3 || HotkeyChoice == HotkeyChoice.ControlAltSpace
                ? HotkeyChoice.WindowsS
                : HotkeyChoice,
            MachineIndexPromptHandled = MachineIndexConsent,
            SettingsOnboardingCompleted = false,
            Version = 5,
        };
    }
}
