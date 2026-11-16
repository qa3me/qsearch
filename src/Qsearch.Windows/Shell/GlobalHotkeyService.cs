using System.Runtime.InteropServices;
using Qsearch.Core.Settings;

namespace Qsearch.Windows.Shell;

public enum HotkeyRegistrationState
{
    Registered,
    Conflict,
}

public readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey, string DisplayText)
{
    private const uint Alt = 0x0001;
    private const uint Control = 0x0002;
    private const uint Shift = 0x0004;
    private const uint Windows = 0x0008;
    private const uint Space = 0x20;
    private const uint Q = 0x51;
    private const uint S = 0x53;

    public static HotkeyGesture Default { get; } = FromChoice(HotkeyChoice.WindowsS);

    public static HotkeyGesture FromChoice(HotkeyChoice choice) => choice switch
    {
        HotkeyChoice.ControlAltSpace => new(Control | Alt, Space, "Ctrl + Alt + Space"),
        HotkeyChoice.ControlShiftSpace => new(Control | Shift, Space, "Ctrl + Shift + Space"),
        HotkeyChoice.AltQ => new(Alt, Q, "Alt + Q"),
        HotkeyChoice.WindowsS => new(Windows, S, "Win + S"),
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };
}

public sealed record HotkeyRegistrationResult(HotkeyRegistrationState State);

public interface IHotkeyNative
{
    bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    bool Unregister(IntPtr windowHandle, int id);
}

public sealed class GlobalHotkeyService : IDisposable
{
    public const int MessageId = 0x0312;
    public const int RegistrationId = 0x5133;
    public const int AlternativeRegistrationId = RegistrationId + 1;
    private readonly IHotkeyNative _native;
    private readonly IntPtr _windowHandle;
    private bool _registered;
    private int _activeRegistrationId;
    private bool _disposed;

    public GlobalHotkeyService(IHotkeyNative native, IntPtr windowHandle)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
        _windowHandle = windowHandle;
    }

    public event EventHandler? Pressed;

    public HotkeyRegistrationResult Register(HotkeyGesture gesture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int candidateId = !_registered || _activeRegistrationId == AlternativeRegistrationId
            ? RegistrationId
            : AlternativeRegistrationId;
        bool candidateRegistered = _native.Register(
            _windowHandle,
            candidateId,
            gesture.Modifiers,
            gesture.VirtualKey);
        if (!candidateRegistered)
        {
            return new HotkeyRegistrationResult(HotkeyRegistrationState.Conflict);
        }

        if (_registered)
        {
            _native.Unregister(_windowHandle, _activeRegistrationId);
        }

        _registered = true;
        _activeRegistrationId = candidateId;
        return new HotkeyRegistrationResult(
            HotkeyRegistrationState.Registered);
    }

    public bool HandleMessage(int message, IntPtr wParam)
    {
        if (message != MessageId || !_registered || wParam.ToInt32() != _activeRegistrationId)
        {
            return false;
        }

        Pressed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Unregister()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_registered)
        {
            _native.Unregister(_windowHandle, _activeRegistrationId);
            _registered = false;
            _activeRegistrationId = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterCore();

        GC.SuppressFinalize(this);
    }

    private void UnregisterCore()
    {
        if (_registered)
        {
            _native.Unregister(_windowHandle, _activeRegistrationId);
            _registered = false;
            _activeRegistrationId = 0;
        }
    }
}

public sealed class WindowsHotkeyNative : IHotkeyNative
{
    public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey)
    {
        return NativeMethods.RegisterHotKey(windowHandle, id, modifiers, virtualKey);
    }

    public bool Unregister(IntPtr windowHandle, int id)
    {
        return NativeMethods.UnregisterHotKey(windowHandle, id);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(
            IntPtr windowHandle,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
    }
}
