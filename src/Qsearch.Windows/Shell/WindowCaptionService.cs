using System.Runtime.InteropServices;

namespace Qsearch.Windows.Shell;

public enum WindowCaptionTheme
{
    Dark,
    Light,
}

public interface IWindowCaptionPlatform
{
    bool IsSupported { get; }

    int SetAttribute(nint windowHandle, int attribute, int value);
}

public sealed class WindowCaptionService
{
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private readonly IWindowCaptionPlatform _platform;

    public WindowCaptionService(IWindowCaptionPlatform? platform = null)
    {
        _platform = platform ?? new WindowsCaptionPlatform();
    }

    public bool Apply(nint windowHandle, WindowCaptionTheme theme, bool highContrast)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        if (highContrast || !_platform.IsSupported)
        {
            return false;
        }

        int darkMode = theme == WindowCaptionTheme.Dark ? 1 : 0;
        int caption = theme == WindowCaptionTheme.Dark ? 0x000D0906 : 0x00F6F7F4;
        int text = theme == WindowCaptionTheme.Dark ? 0x00F2F5ED : 0x001C2010;
        int border = theme == WindowCaptionTheme.Dark ? 0x00312A1D : 0x00C5CBB9;
        return _platform.SetAttribute(windowHandle, UseImmersiveDarkMode, darkMode) == 0 &&
            _platform.SetAttribute(windowHandle, CaptionColor, caption) == 0 &&
            _platform.SetAttribute(windowHandle, TextColor, text) == 0 &&
            _platform.SetAttribute(windowHandle, BorderColor, border) == 0;
    }

    private sealed class WindowsCaptionPlatform : IWindowCaptionPlatform
    {
        public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

        public int SetAttribute(nint windowHandle, int attribute, int value)
        {
            return DwmSetWindowAttribute(windowHandle, attribute, ref value, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            nint windowHandle,
            int attribute,
            ref int value,
            int valueSize);
    }
}
