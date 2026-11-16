using Qsearch.Core.Settings;
using Qsearch.Windows.Shell;

namespace Qsearch.Windows.Tests;

public sealed class GlobalHotkeyServiceTests
{
    [Theory]
    [InlineData(HotkeyChoice.ControlAltSpace, 0x0003u, 0x20u, "Ctrl + Alt + Space")]
    [InlineData(HotkeyChoice.ControlShiftSpace, 0x0006u, 0x20u, "Ctrl + Shift + Space")]
    [InlineData(HotkeyChoice.AltQ, 0x0001u, 0x51u, "Alt + Q")]
    [InlineData(HotkeyChoice.WindowsS, 0x0008u, 0x53u, "Win + S")]
    public void GestureChoice_MapsToARegisterableCombination(
        HotkeyChoice choice,
        uint modifiers,
        uint virtualKey,
        string display)
    {
        HotkeyGesture gesture = HotkeyGesture.FromChoice(choice);

        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(virtualKey, gesture.VirtualKey);
        Assert.Equal(display, gesture.DisplayText);
    }

    [Fact]
    public void HotkeyConflict_IsVisibleAndStartupContinues()
    {
        FakeHotkeyNative native = new(registerResult: false);
        using GlobalHotkeyService service = new(native, new IntPtr(42));

        HotkeyRegistrationResult result = service.Register(HotkeyGesture.Default);

        Assert.Equal(HotkeyRegistrationState.Conflict, result.State);
        Assert.Equal(1, native.RegisterCalls);
    }

    [Fact]
    public void Dispose_UnregistersSuccessfulHotkeyExactlyOnce()
    {
        FakeHotkeyNative native = new(registerResult: true);
        GlobalHotkeyService service = new(native, new IntPtr(42));
        Assert.Equal(HotkeyRegistrationState.Registered, service.Register(HotkeyGesture.Default).State);

        service.Dispose();
        service.Dispose();

        Assert.Equal(1, native.UnregisterCalls);
    }

    [Fact]
    public void RegisteringANewChoice_ReplacesThePreviousRegistration()
    {
        FakeHotkeyNative native = new(registerResult: true);
        using GlobalHotkeyService service = new(native, new IntPtr(42));
        service.Register(HotkeyGesture.FromChoice(HotkeyChoice.ControlAltSpace));

        service.Register(HotkeyGesture.FromChoice(HotkeyChoice.AltQ));

        Assert.Equal(2, native.RegisterCalls);
        Assert.Equal(1, native.UnregisterCalls);
    }

    [Fact]
    public void FailedReplacement_KeepsThePreviousRegistrationActive()
    {
        SequenceHotkeyNative native = new(true, false);
        using GlobalHotkeyService service = new(native, new IntPtr(42));
        int presses = 0;
        service.Pressed += (_, _) => presses++;
        Assert.Equal(HotkeyRegistrationState.Registered, service.Register(HotkeyGesture.FromChoice(HotkeyChoice.AltQ)).State);

        HotkeyRegistrationResult failed = service.Register(HotkeyGesture.FromChoice(HotkeyChoice.ControlShiftSpace));
        bool handled = service.HandleMessage(GlobalHotkeyService.MessageId, new IntPtr(GlobalHotkeyService.RegistrationId));

        Assert.Equal(HotkeyRegistrationState.Conflict, failed.State);
        Assert.True(handled);
        Assert.Equal(1, presses);
        Assert.Equal(0, native.UnregisterCalls);
    }

    [Theory]
    [InlineData(0x5Bu, false)]
    [InlineData(0x5Bu, true)]
    [InlineData(0x5Cu, false)]
    [InlineData(0x5Cu, true)]
    public void WindowsSFilter_PairsSuppressionAcrossReleaseOrders(uint windows, bool releaseWindowsFirst)
    {
        WindowsSChordFilter filter = new();
        AssertKey(filter, WindowsSChordFilter.KeyDown, windows);
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x53, suppress: true, activate: true);
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x53, suppress: true);
        if (releaseWindowsFirst)
        {
            AssertKey(filter, WindowsSChordFilter.KeyUp, windows);
            AssertKey(filter, WindowsSChordFilter.KeyUp, 0x53, suppress: true);
        }
        else
        {
            AssertKey(filter, WindowsSChordFilter.KeyUp, 0x53, suppress: true);
            AssertKey(filter, WindowsSChordFilter.KeyUp, windows);
        }
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x53);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0x53);
    }

    [Theory]
    [InlineData(0xA0u, false)]
    [InlineData(0xA0u, true)]
    [InlineData(0xA1u, false)]
    [InlineData(0xA1u, true)]
    [InlineData(0xA2u, false)]
    [InlineData(0xA2u, true)]
    [InlineData(0xA3u, false)]
    [InlineData(0xA3u, true)]
    [InlineData(0xA4u, false)]
    [InlineData(0xA4u, true)]
    [InlineData(0xA5u, false)]
    [InlineData(0xA5u, true)]
    public void WindowsSFilter_PassesExtraModifiersInEitherOrder(uint modifier, bool modifierFirst)
    {
        WindowsSChordFilter filter = new();
        uint first = modifierFirst ? modifier : 0x5Bu;
        uint second = modifierFirst ? 0x5Bu : modifier;
        AssertKey(filter, WindowsSChordFilter.KeyDown, first);
        AssertKey(filter, WindowsSChordFilter.KeyDown, second);
        AssertKey(filter, WindowsSChordFilter.SystemKeyDown, 0x53);
        AssertKey(filter, WindowsSChordFilter.SystemKeyUp, 0x53);
        AssertKey(filter, WindowsSChordFilter.KeyUp, modifier);
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x53, suppress: true, activate: true);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0x53, suppress: true);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0x5B);
    }

    [Fact]
    public void WindowsSFilter_LetsAnAlreadyHeldSPassThrough()
    {
        WindowsSChordFilter filter = new();
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x53);
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x5B);
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x53);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0x53);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0x5B);
    }

    [Fact]
    public void WindowsSFilter_PreservesCaptureAfterAnotherModifierIsPressed()
    {
        WindowsSChordFilter filter = new();
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x5B);
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x53, suppress: true, activate: true);
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0xA2);
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x53, suppress: true);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0x53, suppress: true);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0xA2);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0x5B);
    }

    [Fact]
    public void WindowsSFilter_TracksBothWindowsKeysIndependently()
    {
        WindowsSChordFilter filter = new();
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x5B);
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x5C);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0x5B);
        AssertKey(filter, WindowsSChordFilter.KeyDown, 0x53, suppress: true, activate: true);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0x53, suppress: true);
        AssertKey(filter, WindowsSChordFilter.KeyUp, 0x5C);
    }

    private static void AssertKey(
        WindowsSChordFilter filter,
        int message,
        uint key,
        bool suppress = false,
        bool activate = false)
    {
        bool suppressed = filter.Process(message, key, out bool activated);
        Assert.Equal(suppress, suppressed);
        Assert.Equal(activate, activated);
    }

    private sealed class FakeHotkeyNative(bool registerResult) : IHotkeyNative
    {
        public int RegisterCalls { get; private set; }

        public int UnregisterCalls { get; private set; }

        public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey)
        {
            RegisterCalls++;
            return registerResult;
        }

        public bool Unregister(IntPtr windowHandle, int id)
        {
            UnregisterCalls++;
            return true;
        }
    }

    private sealed class SequenceHotkeyNative(params bool[] results) : IHotkeyNative
    {
        private int _position;
        public int UnregisterCalls { get; private set; }

        public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey) =>
            results[_position++];

        public bool Unregister(IntPtr windowHandle, int id)
        {
            UnregisterCalls++;
            return true;
        }
    }
}
