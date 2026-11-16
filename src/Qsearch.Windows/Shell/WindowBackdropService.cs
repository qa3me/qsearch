using System.Runtime.InteropServices;

namespace Qsearch.Windows.Shell;

public enum WindowBackdropKind
{
    Main,
    Transient,
}

public sealed record WindowBackdropResult(
    bool Applied,
    bool UsedOpaqueFallback,
    int? NativeError = null);

public interface IWindowBackdropPlatform
{
    bool IsSupported { get; }

    bool IsCompositionEnabled { get; }

    int SetSystemBackdrop(nint windowHandle, int backdropValue);
}

public sealed class WindowBackdropService
{
    private readonly IWindowBackdropPlatform _platform;

    public WindowBackdropService(IWindowBackdropPlatform? platform = null)
    {
        _platform = platform ?? new WindowsBackdropPlatform();
    }

    public WindowBackdropResult Apply(
        nint windowHandle,
        WindowBackdropKind kind,
        bool highContrast)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        if (highContrast || !_platform.IsSupported || !_platform.IsCompositionEnabled)
        {
            return new WindowBackdropResult(false, true);
        }

        int backdropValue = kind == WindowBackdropKind.Main ? 2 : 3;
        int result = _platform.SetSystemBackdrop(windowHandle, backdropValue);
        return result == 0
            ? new WindowBackdropResult(true, false)
            : new WindowBackdropResult(false, true, result);
    }

    private sealed class WindowsBackdropPlatform : IWindowBackdropPlatform
    {
        private const int DwmSystemBackdropType = 38;

        public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

        public bool IsCompositionEnabled =>
            DwmIsCompositionEnabled(out int enabled) == 0 && enabled != 0;

        public int SetSystemBackdrop(nint windowHandle, int backdropValue)
        {
            return DwmSetWindowAttribute(
                windowHandle,
                DwmSystemBackdropType,
                ref backdropValue,
                sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out int enabled);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            nint windowHandle,
            int attribute,
            ref int value,
            int valueSize);
    }
}
