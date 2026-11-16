using Qsearch.App.ViewModels;

namespace Qsearch.App.Tests;

public sealed class AboutContractTests
{
    [Fact]
    public void AboutViewModel_IdentifiesCreatorBrandProductAndWebsite()
    {
        AboutViewModel viewModel = new();

        Assert.Equal("Qsearch", viewModel.ProductName);
        Assert.Equal("Behzad Qasemi", viewModel.CreatorName);
        Assert.Equal("QA3", viewModel.BrandName);
        Assert.Equal(new Uri("https://qa3.me/"), viewModel.DeveloperUri);
        Assert.Equal(new Uri("https://qsearch.qa3.me/"), viewModel.ProductUri);
        Assert.Equal(new Uri("https://github.com/qa3me/qsearch"), viewModel.GitHubUri);
        Assert.StartsWith("0.1.", viewModel.Version, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutWindow_UsesLocalizedPrivacyCopyAndExplicitWebsiteButton()
    {
        string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AboutWindow.xaml"));

        Assert.Contains("About.Privacy", xaml, StringComparison.Ordinal);
        Assert.Contains("About.Creator", xaml, StringComparison.Ordinal);
        Assert.Contains("OnDeveloperClick", xaml, StringComparison.Ordinal);
        Assert.Contains("OnProductClick", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnGitHubClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GitHub", xaml, StringComparison.Ordinal);
        Assert.Contains("Qsearch.GitHubGlyph", xaml, StringComparison.Ordinal);
        Assert.Contains("Qsearch.VectorIconHost", xaml, StringComparison.Ordinal);
        Assert.Contains("QsearchMark", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"690\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"False\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("About.GitHubPending", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WebBrowser", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Icon_ContainsEveryRequiredResolutionFromCheckedInSvg()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "qsearch.ico");
        string svg = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "qsearch-icon.svg"));
        using BinaryReader reader = new(File.OpenRead(iconPath));

        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        int count = reader.ReadUInt16();
        List<int> sizes = [];
        for (int index = 0; index < count; index++)
        {
            byte width = reader.ReadByte();
            byte height = reader.ReadByte();
            sizes.Add(width == 0 ? 256 : width);
            Assert.Equal(width, height);
            _ = reader.ReadBytes(14);
        }

        Assert.Equal([16, 20, 24, 32, 48, 64, 128, 256], sizes);
        Assert.Contains("viewBox=\"150 150 700 700\"", svg, StringComparison.Ordinal);
        Assert.Contains("#1db954", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("M 316.45 256.42", svg, StringComparison.Ordinal);
        Assert.Contains("r=\"151\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<image", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("font-family", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fill=\"#06090d\"", svg, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "qsearch-dark.ico")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "qsearch-light.ico")));
    }

    [Fact]
    public void ApplicationAndTray_UseTheGeneratedIcon()
    {
        string project = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Contracts",
            "Qsearch.App.csproj"));
        string tray = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Contracts",
            "TrayIconService.cs"));

        Assert.Contains("assets\\brand\\qsearch.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("Link=\"qsearch.ico\"", project, StringComparison.Ordinal);
        Assert.Contains("Link=\"qsearch-dark.ico\"", project, StringComparison.Ordinal);
        Assert.Contains("Link=\"qsearch-light.ico\"", project, StringComparison.Ordinal);
        Assert.Contains("new Icon(iconPath)", tray, StringComparison.Ordinal);
    }

    [Fact]
    public void InAppMark_IsTransparentAndUsesThemeAwareBrushes()
    {
        string mark = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Controls", "QsearchMark.xaml"));
        string dark = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Palette.Dark.xaml"));
        string light = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Palette.Light.xaml"));

        Assert.Contains("x:Name=\"Orbit\"", mark, StringComparison.Ordinal);
        Assert.Contains("M 316.45 256.42", mark, StringComparison.Ordinal);
        Assert.Contains("RadiusX=\"151\"", mark, StringComparison.Ordinal);
        Assert.Contains("DynamicResource Qsearch.MarkBrush", mark, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Qsearch.MarkBrush\" Color=\"#1DB954\"", dark, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Qsearch.MarkBrush\" Color=\"#087732\"", light, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=", mark, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeneratedIcons_AreTransparentAndUseTheSuppliedThemeColors()
    {
        byte[] brandPixels = ReadFirstFramePixels(Path.Combine(AppContext.BaseDirectory, "Assets", "qsearch.ico"));
        byte[] darkPixels = ReadFirstFramePixels(Path.Combine(AppContext.BaseDirectory, "Assets", "qsearch-dark.ico"));
        byte[] lightPixels = ReadFirstFramePixels(Path.Combine(AppContext.BaseDirectory, "Assets", "qsearch-light.ico"));

        Assert.True(ContainsPixel(brandPixels, blue: 0x54, green: 0xB9, red: 0x1D, alpha: 0xFF));
        Assert.True(ContainsPixel(darkPixels, blue: 0x54, green: 0xB9, red: 0x1D, alpha: 0xFF));
        Assert.True(ContainsPixel(lightPixels, blue: 0x32, green: 0x77, red: 0x08, alpha: 0xFF));
        Assert.True(ContainsTransparentPixel(brandPixels));
        Assert.True(ContainsTransparentPixel(darkPixels));
        Assert.True(ContainsTransparentPixel(lightPixels));
    }

    private static byte[] ReadFirstFramePixels(string iconPath)
    {
        using BinaryReader reader = new(File.OpenRead(iconPath));
        reader.BaseStream.Position = 6;
        int width = reader.ReadByte();
        _ = reader.ReadByte();
        _ = reader.ReadBytes(6);
        int dataLength = reader.ReadInt32();
        int dataOffset = reader.ReadInt32();
        width = width == 0 ? 256 : width;
        reader.BaseStream.Position = dataOffset + 40;
        int pixelBytes = checked(width * width * 4);
        Assert.True(dataLength >= 40 + pixelBytes);
        return reader.ReadBytes(pixelBytes);
    }

    private static bool ContainsPixel(byte[] pixels, byte blue, byte green, byte red, byte alpha)
    {
        for (int offset = 0; offset <= pixels.Length - 4; offset += 4)
        {
            if (pixels[offset] == blue && pixels[offset + 1] == green &&
                pixels[offset + 2] == red && pixels[offset + 3] == alpha)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsTransparentPixel(byte[] pixels)
    {
        for (int offset = 3; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] == 0)
            {
                return true;
            }
        }

        return false;
    }
}
