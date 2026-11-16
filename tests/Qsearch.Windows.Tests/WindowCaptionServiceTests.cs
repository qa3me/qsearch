using Qsearch.Windows.Shell;

namespace Qsearch.Windows.Tests;

public sealed class WindowCaptionServiceTests
{
    [Theory]
    [InlineData(WindowCaptionTheme.Dark, 1, 0x000D0906, 0x00F2F5ED)]
    [InlineData(WindowCaptionTheme.Light, 0, 0x00F6F7F4, 0x001C2010)]
    public void Apply_ColorsTheNativeCaptionToMatchTheApp(
        WindowCaptionTheme theme,
        int expectedDarkMode,
        int expectedCaption,
        int expectedText)
    {
        RecordingCaptionPlatform platform = new();
        WindowCaptionService service = new(platform);

        bool applied = service.Apply((nint)42, theme, highContrast: false);

        Assert.True(applied);
        Assert.Equal(expectedDarkMode, platform.Values[20]);
        Assert.Equal(expectedCaption, platform.Values[35]);
        Assert.Equal(expectedText, platform.Values[36]);
    }

    [Fact]
    public void HighContrast_DoesNotOverrideWindowsCaptionColors()
    {
        RecordingCaptionPlatform platform = new();
        WindowCaptionService service = new(platform);

        bool applied = service.Apply((nint)42, WindowCaptionTheme.Dark, highContrast: true);

        Assert.False(applied);
        Assert.Empty(platform.Values);
    }

    private sealed class RecordingCaptionPlatform : IWindowCaptionPlatform
    {
        public Dictionary<int, int> Values { get; } = [];

        public bool IsSupported => true;

        public int SetAttribute(nint windowHandle, int attribute, int value)
        {
            Values[attribute] = value;
            return 0;
        }
    }
}
