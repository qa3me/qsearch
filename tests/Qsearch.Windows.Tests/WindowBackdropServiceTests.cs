using Qsearch.Windows.Shell;

namespace Qsearch.Windows.Tests;

public sealed class WindowBackdropServiceTests
{
    [Theory]
    [InlineData(WindowBackdropKind.Main, 2)]
    [InlineData(WindowBackdropKind.Transient, 3)]
    public void SupportedWindows_AppliesDocumentedBackdropValue(
        WindowBackdropKind kind,
        int expectedValue)
    {
        FakeBackdropPlatform platform = new() { Supported = true, Composition = true };
        WindowBackdropService service = new(platform);

        WindowBackdropResult result = service.Apply((nint)42, kind, highContrast: false);

        Assert.True(result.Applied);
        Assert.False(result.UsedOpaqueFallback);
        Assert.Equal(expectedValue, platform.BackdropValue);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void UnsupportedCompositionOrHighContrast_UsesOpaqueFallback(
        bool supported,
        bool composition,
        bool highContrast)
    {
        FakeBackdropPlatform platform = new()
        {
            Supported = supported,
            Composition = composition,
        };
        WindowBackdropService service = new(platform);

        WindowBackdropResult result = service.Apply((nint)42, WindowBackdropKind.Main, highContrast);

        Assert.False(result.Applied);
        Assert.True(result.UsedOpaqueFallback);
        Assert.Null(platform.BackdropValue);
    }

    [Fact]
    public void NativeFailure_ReturnsFallbackWithoutThrowing()
    {
        FakeBackdropPlatform platform = new()
        {
            Supported = true,
            Composition = true,
            Error = 87,
        };
        WindowBackdropService service = new(platform);

        WindowBackdropResult result = service.Apply(
            (nint)42,
            WindowBackdropKind.Main,
            highContrast: false);

        Assert.False(result.Applied);
        Assert.Equal(87, result.NativeError);
        Assert.True(result.UsedOpaqueFallback);
    }

    private sealed class FakeBackdropPlatform : IWindowBackdropPlatform
    {
        public bool Supported { get; init; }
        public bool Composition { get; init; }
        public int Error { get; init; }
        public int? BackdropValue { get; private set; }

        public bool IsSupported => Supported;
        public bool IsCompositionEnabled => Composition;

        public int SetSystemBackdrop(nint windowHandle, int backdropValue)
        {
            BackdropValue = backdropValue;
            return Error;
        }
    }
}
