using System.Windows;
using Microsoft.Win32;
using Qsearch.Core.Settings;

namespace Qsearch.App.Services;

public enum AppThemeVariant
{
    Dark,
    Light,
}

public interface IWindowsThemeReader
{
    bool UsesLightApps();
}

public static class ThemeResolver
{
    public static AppThemeVariant Resolve(
        AppThemePreference preference,
        bool windowsUsesLight) => preference switch
        {
            AppThemePreference.Dark => AppThemeVariant.Dark,
            AppThemePreference.Light => AppThemeVariant.Light,
            AppThemePreference.System => windowsUsesLight
                ? AppThemeVariant.Light
                : AppThemeVariant.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(preference)),
        };
}

public sealed class ThemeService
{
    private readonly IWindowsThemeReader _windowsTheme;

    public ThemeService(IWindowsThemeReader? windowsTheme = null)
    {
        _windowsTheme = windowsTheme ?? new WindowsThemeReader();
    }

    public AppThemeVariant CurrentVariant { get; private set; } = AppThemeVariant.Dark;

    public AppThemeVariant Apply(
        ResourceDictionary resources,
        AppThemePreference preference)
    {
        ArgumentNullException.ThrowIfNull(resources);
        AppThemeVariant variant = ThemeResolver.Resolve(preference, _windowsTheme.UsesLightApps());
        ResourceDictionary palette = new()
        {
            Source = new Uri(
                $"pack://application:,,,/Qsearch;component/Resources/Palette.{variant}.xaml",
                UriKind.Absolute),
        };

        int currentIndex = -1;
        for (int index = 0; index < resources.MergedDictionaries.Count; index++)
        {
            string source = resources.MergedDictionaries[index].Source?.OriginalString ?? string.Empty;
            if (source.Contains("/Palette.", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("Resources/Palette.", StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = index;
                break;
            }
        }

        if (currentIndex >= 0)
        {
            resources.MergedDictionaries[currentIndex] = palette;
        }
        else
        {
            resources.MergedDictionaries.Insert(0, palette);
        }

        CurrentVariant = variant;
        return variant;
    }
}

public sealed class WindowsThemeReader : IWindowsThemeReader
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public bool UsesLightApps()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }
}
