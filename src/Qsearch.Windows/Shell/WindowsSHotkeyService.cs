using System.Runtime.InteropServices;

namespace Qsearch.Windows.Shell;

public sealed class WindowsSChordFilter
{
    public const int KeyDown = 0x0100;
    public const int KeyUp = 0x0101;
    public const int SystemKeyDown = 0x0104;
    public const int SystemKeyUp = 0x0105;

    private const uint S = 0x53;
    private const uint LeftWindows = 0x5B;
    private const uint RightWindows = 0x5C;

    private readonly HashSet<uint> _heldKeys = new(16);
    private bool _capturedS;

    public bool Process(int message, uint virtualKey, out bool activate)
    {
        activate = false;
        if (message is KeyDown or SystemKeyDown)
        {
            bool firstPress = _heldKeys.Add(virtualKey);
            if (virtualKey != S)
            {
                return false;
            }

            if (firstPress)
            {
                _capturedS = _heldKeys.Count == 2 &&
                    (_heldKeys.Contains(LeftWindows) || _heldKeys.Contains(RightWindows));
                activate = _capturedS;
            }

            return _capturedS;
        }

        if (message is KeyUp or SystemKeyUp)
        {
            _heldKeys.Remove(virtualKey);
            if (virtualKey == S)
            {
                bool suppress = _capturedS;
                _capturedS = false;
                return suppress;
            }
        }

        return false;
    }
}

public sealed class WindowsSHotkeyService : IDisposable
{
    private const int LowLevelKeyboardHook = 13;
    private const uint QuitMessage = 0x0012;
    private const ushort DummyKey = 0x00FF;
    private const uint KeyboardInputType = 1;
    private const uint KeyUpFlag = 0x0002;
    private static readonly nuint DummyInputTag = 0x51535753;
    private static readonly NativeInput[] StartMenuMask = [new(0), new(KeyUpFlag)];
    private readonly WindowsSChordFilter _filter = new();
    private readonly object _sync = new();
    private Thread? _messageThread;
    private HookProcedure? _hookProcedure;
    private uint _messageThreadId;
    private IntPtr _hookHandle;
    private bool _started;
    private bool _disposed;

    public event EventHandler? Pressed;

    public HotkeyRegistrationResult Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return new HotkeyRegistrationResult(HotkeyRegistrationState.Registered);
            }

            TaskCompletionSource<HotkeyRegistrationState> ready = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _messageThread = new Thread(() => RunMessageLoop(ready))
            {
                IsBackground = true,
                Name = "Qsearch Win+S shortcut",
            };
            _messageThread.Start();
            HotkeyRegistrationState state = ready.Task.GetAwaiter().GetResult();
            _started = state == HotkeyRegistrationState.Registered;
            return new HotkeyRegistrationResult(state);
        }
    }

    public void Dispose()
    {
        Thread? thread;
        uint threadId;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _messageThread;
            threadId = _messageThreadId;
        }

        if (threadId != 0)
        {
            _ = NativeMethods.PostThreadMessage(threadId, QuitMessage, IntPtr.Zero, IntPtr.Zero);
        }

        if (thread is not null && thread != Thread.CurrentThread)
        {
            _ = thread.Join(TimeSpan.FromSeconds(2));
        }

        GC.SuppressFinalize(this);
    }

    private void RunMessageLoop(TaskCompletionSource<HotkeyRegistrationState> ready)
    {
        _messageThreadId = NativeMethods.GetCurrentThreadId();
        _ = NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _hookProcedure = HandleKeyboardMessage;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            LowLevelKeyboardHook,
            _hookProcedure,
            IntPtr.Zero,
            0);
        if (_hookHandle == IntPtr.Zero)
        {
            _messageThreadId = 0;
            ready.TrySetResult(HotkeyRegistrationState.Conflict);
            return;
        }

        ready.TrySetResult(HotkeyRegistrationState.Registered);
        try
        {
            while (NativeMethods.GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
            {
                _ = NativeMethods.TranslateMessage(in message);
                _ = NativeMethods.DispatchMessage(in message);
            }
        }
        finally
        {
            _ = NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _messageThreadId = 0;
        }
    }

    private IntPtr HandleKeyboardMessage(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            KeyboardHookData keyboard = Marshal.PtrToStructure<KeyboardHookData>(data);
            if (keyboard.VirtualKey == DummyKey && keyboard.ExtraInfo == DummyInputTag)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, code, message, data);
            }

            bool suppress = _filter.Process(message.ToInt32(), keyboard.VirtualKey, out bool activate);
            if (activate)
            {
                _ = NativeMethods.SendInput(
                    (uint)StartMenuMask.Length,
                    StartMenuMask,
                    Marshal.SizeOf<NativeInput>());
                Pressed?.Invoke(this, EventArgs.Empty);
            }

            if (suppress)
            {
                return new IntPtr(1);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, code, message, data);
    }

    private delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KeyboardHookData
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeMessage
    {
        public readonly IntPtr Window;
        public readonly uint Message;
        public readonly nuint WParam;
        public readonly nint LParam;
        public readonly uint Time;
        public readonly int PointX;
        public readonly int PointY;
        public readonly uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeInput
    {
        public readonly uint Type;
        public readonly InputUnion Input;

        public NativeInput(uint flags)
        {
            Type = KeyboardInputType;
            Input = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = DummyKey,
                    Flags = flags,
                    ExtraInfo = DummyInputTag,
                },
            };
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr SetWindowsHookEx(
            int hookId,
            HookProcedure callback,
            IntPtr module,
            uint threadId);

        [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr CallNextHookEx(
            IntPtr hook,
            int code,
            IntPtr message,
            IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int GetMessage(
            out NativeMessage message,
            IntPtr window,
            uint minimumMessage,
            uint maximumMessage);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PeekMessage(
            out NativeMessage message,
            IntPtr window,
            uint minimumMessage,
            uint maximumMessage,
            uint removeMessage);

        [DllImport("user32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TranslateMessage(in NativeMessage message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr DispatchMessage(in NativeMessage message);

        [DllImport("user32.dll", SetLastError = true, ExactSpelling = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostThreadMessage(
            uint threadId,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint SendInput(
            uint count,
            [In] NativeInput[] inputs,
            int inputSize);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint GetCurrentThreadId();
    }
}
