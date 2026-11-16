using System.Windows.Media;
using System.Windows.Media.Imaging;
using Qsearch.App.Services;

namespace Qsearch.App.Tests;

public sealed class FileAssociationServiceTests
{
    [Fact]
    public async Task ResolveAsync_PublishesOnlyTheLocalHandlerNameAndFrozenIcon()
    {
        BitmapSource icon = CreateIcon();
        StubAssociationApi api = new(new FileAssociationInfo("Local Viewer", icon, true));
        WindowsFileAssociationService service = new(api);

        FileAssociationInfo result = await service.ResolveAsync(
            @"C:\scope\report.pdf",
            TestContext.Current.CancellationToken);

        Assert.Equal("Local Viewer", result.HandlerName);
        Assert.True(result.IsAvailable);
        Assert.Same(icon, result.HandlerIcon);
        Assert.True(result.HandlerIcon!.IsFrozen);
        Assert.Equal(@"C:\scope\report.pdf", api.LastPath);
    }

    [Fact]
    public async Task ResolveAsync_WhenNoAssociationExists_ReturnsAGenericLocalFallback()
    {
        StubAssociationApi api = new(FileAssociationInfo.Unavailable);
        WindowsFileAssociationService service = new(api);

        FileAssociationInfo result = await service.ResolveAsync(
            @"C:\scope\unknown.qz",
            TestContext.Current.CancellationToken);

        Assert.False(result.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(result.HandlerName));
    }

    private static BitmapSource CreateIcon()
    {
        byte[] pixels = [0x60, 0xD7, 0x1E, 0xFF];
        BitmapSource icon = BitmapSource.Create(
            1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        icon.Freeze();
        return icon;
    }

    private sealed class StubAssociationApi(FileAssociationInfo result) : IWindowsFileAssociationApi
    {
        public string? LastPath { get; private set; }

        public FileAssociationInfo Resolve(string fullPath)
        {
            LastPath = fullPath;
            return result;
        }
    }
}
