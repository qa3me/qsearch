using Qsearch.App.Services;
using Qsearch.Core.Settings;

namespace Qsearch.App.Tests;

public sealed class ThemeResolverTests
{
    [Theory]
    [InlineData(AppThemePreference.Dark, true, AppThemeVariant.Dark)]
    [InlineData(AppThemePreference.Light, false, AppThemeVariant.Light)]
    [InlineData(AppThemePreference.System, false, AppThemeVariant.Dark)]
    [InlineData(AppThemePreference.System, true, AppThemeVariant.Light)]
    public void Resolve_HonorsExplicitChoiceAndWindowsPreference(
        AppThemePreference preference,
        bool windowsUsesLight,
        AppThemeVariant expected)
    {
        Assert.Equal(expected, ThemeResolver.Resolve(preference, windowsUsesLight));
    }

}
