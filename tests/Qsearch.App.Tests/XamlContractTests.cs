using System.Xml.Linq;

namespace Qsearch.App.Tests;

public sealed class XamlContractTests
{
    [Fact]
    public void Theme_UsesApprovedQa3Tokens()
    {
        string theme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Theme.xaml"));
        string dark = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Palette.Dark.xaml"));
        string light = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Palette.Light.xaml"));

        Assert.Contains("#06090D", dark, StringComparison.Ordinal);
        Assert.Contains("#1DB954", dark, StringComparison.Ordinal);
        Assert.Contains("Qsearch.PrimaryFillBrush", dark, StringComparison.Ordinal);
        Assert.Contains("Qsearch.PrimaryFillBrush", light, StringComparison.Ordinal);
        Assert.Contains("#EDF5F2", dark, StringComparison.Ordinal);
        Assert.Contains("#FF8B7E", dark, StringComparison.Ordinal);
        Assert.Contains("#F4F7F6", light, StringComparison.Ordinal);
        Assert.Contains("#087732", light, StringComparison.Ordinal);
        Assert.Contains("RectangleGeometry Rect=\"0,0,32,32\"", dark, StringComparison.Ordinal);
        Assert.Contains("RectangleGeometry Rect=\"0,0,32,32\"", light, StringComparison.Ordinal);
        Assert.Contains("Qsearch.OnAccentBrush", light, StringComparison.Ordinal);
        Assert.Contains("DynamicResource Qsearch.OnAccentBrush", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("<Style TargetType=\"TextBlock\">", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void MainResultList_IsVirtualizedAndPathTextIsDirectionIsolated()
    {
        string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));

        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizationMode=\"Recycling\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.ScrollUnit=\"Pixel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FlowDirection=\"LeftToRight\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_EnablesSmoothWheelInputAndNestedScrollHandoff()
    {
        string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "MainWindow.xaml.cs"));

        Assert.Contains("PanningMode=\"VerticalOnly\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SmoothScrollBehavior.Attach", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_IsResponsiveSortableAndExplainsOfflineIndex()
    {
        string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        string english = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Strings.en.xaml"));
        string persian = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Strings.fa.xaml"));

        Assert.Contains("MinWidth=\"720\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WideState\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MediumState\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GridViewColumnHeader", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Modified\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HotkeyDisplayText", xaml, StringComparison.Ordinal);
        Assert.Contains("IndexPulse", xaml, StringComparison.Ordinal);
        Assert.Contains("no telemetry", english, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local search index", english, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("کاملاً آفلاین", persian, StringComparison.Ordinal);
        Assert.Contains("فهرست جست", persian, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusCopy_IsLocalizedInsteadOfEmbeddedInBindings()
    {
        string main = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        string pulse = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Controls", "IndexPulse.xaml"));
        string english = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Strings.en.xaml"));
        string persian = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Strings.fa.xaml"));

        Assert.DoesNotContain(" indexed}", main, StringComparison.Ordinal);
        Assert.DoesNotContain(" visible}", main, StringComparison.Ordinal);
        Assert.DoesNotContain(" volumes fresh}", main, StringComparison.Ordinal);
        Assert.DoesNotContain(" volumes ready}", pulse, StringComparison.Ordinal);
        Assert.Contains("Status.IndexedLabel", english, StringComparison.Ordinal);
        Assert.Contains("Freshness.Fresh", english, StringComparison.Ordinal);
        Assert.Contains("Status.IndexedLabel", persian, StringComparison.Ordinal);
        Assert.Contains("Freshness.Fresh", persian, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexProgressAndPulse_UseEasedTransitionsInsteadOfSnapping()
    {
        string pulse = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Controls", "IndexPulse.xaml"));
        string code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "IndexPulse.xaml.cs"));

        Assert.Contains("AnimatedProgress", pulse, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", code, StringComparison.Ordinal);
        Assert.Contains("QuadraticEase", code, StringComparison.Ordinal);
        Assert.Contains("PulseTransitionProperty", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ExposesAReadOnlyMetadataAndContentInspector()
    {
        string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));

        Assert.Contains("x:Name=\"FileInspector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedResult.DisplayName", xaml, StringComparison.Ordinal);
        Assert.Contains("FilePreview.ImagePreview", xaml, StringComparison.Ordinal);
        Assert.Contains("FilePreview.TextPreview", xaml, StringComparison.Ordinal);
        Assert.Contains("FilePreview.CreatedText", xaml, StringComparison.Ordinal);
        Assert.Contains("FilePreview.ModifiedText", xaml, StringComparison.Ordinal);
        Assert.Contains("FilePreview.AttributesText", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FileActionStatus\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ReviewResult.Evidence", xaml, StringComparison.Ordinal);
        Assert.Contains("Explanation", xaml, StringComparison.Ordinal);
        Assert.Contains("Guidance", xaml, StringComparison.Ordinal);
        Assert.Contains("IncompleteReason", xaml, StringComparison.Ordinal);
        Assert.Contains("FileAssociation.HandlerName", xaml, StringComparison.Ordinal);
        Assert.Contains("FilePurpose.Summary", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FileTypeAndMissingAssociationLabels_AreLocalizedResources()
    {
        string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        string english = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Strings.en.xaml"));
        string persian = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Strings.fa.xaml"));

        Assert.DoesNotContain("DisplayMemberBinding=\"{Binding Type}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding SelectedResult.Type}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemType.File", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemType.Folder", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemType.Application", xaml, StringComparison.Ordinal);
        Assert.Contains("Preview.NoDefaultApp", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ItemType.File\"", english, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ItemType.File\"", persian, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ItemType.Application\"", english, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ItemType.Application\"", persian, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Preview.NoDefaultApp\"", english, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Preview.NoDefaultApp\"", persian, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UsesNonBlockingPermissionProgressAndThreeResponsiveStates()
    {
        string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        string settings = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Controls", "SettingsPanel.xaml"));

        Assert.Contains("x:Name=\"MachineIndexCallout\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NarrowState\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ProgressBar", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAboutClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnStartupFullClick\"", settings, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnStartupCompactClick\"", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_DefinesTracklessBrandScrollbarAndQuietGlassSurfaces()
    {
        string theme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Theme.xaml"));

        Assert.Contains("x:Key=\"Qsearch.ScrollThumb\"", theme, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"ScrollBar\"", theme, StringComparison.Ordinal);
        Assert.Contains("Qsearch.ProgressBar", theme, StringComparison.Ordinal);
        Assert.Contains("PART_HorizontalScrollBar", theme, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\" Grid.Column=\"1\"", theme, StringComparison.Ordinal);
        Assert.Contains("Property=\"CornerRadius\" Value=\"18\"", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultAndActionIcons_UseSharedCenteredVectorPrimitives()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XDocument[] surfaces =
        [
            XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml")),
            XDocument.Load(Path.Combine(AppContext.BaseDirectory, "WidgetWindow.xaml")),
            XDocument.Load(Path.Combine(AppContext.BaseDirectory, "LauncherWindow.xaml")),
        ];

        List<XElement> resultIconHosts = surfaces
            .SelectMany(document => document.Descendants(presentation + "ContentControl"))
            .Where(element => ((string?)element.Attribute("ContentTemplate"))?.Contains(
                "Qsearch.ResultGlyph",
                StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(5, resultIconHosts.Count);
        Assert.All(
            resultIconHosts,
            element => Assert.Equal(
                "{DynamicResource Qsearch.ResultIconHost}",
                (string?)element.Attribute("Style")));

        foreach (XDocument surface in surfaces)
        {
            Assert.DoesNotContain(
                surface.Descendants().Attributes("Content"),
                attribute => string.Equals(attribute.Value, "×", StringComparison.Ordinal));
            Assert.DoesNotContain(
                surface.Descendants().Attributes("Text"),
                attribute => string.Equals(attribute.Value, "↗", StringComparison.Ordinal));
        }

        foreach (string fileName in new[] { "WidgetWindow.xaml", "LauncherWindow.xaml" })
        {
            XDocument surface = XDocument.Load(Path.Combine(AppContext.BaseDirectory, fileName));
            XElement statusDot = Assert.Single(surface.Descendants(presentation + "Ellipse"));
            Assert.Equal("{DynamicResource Qsearch.StatusDot}", (string?)statusDot.Attribute("Style"));
        }
    }

    [Fact]
    public void AboutAndSettings_KeepIconAndSelectionStatesVisuallyCoherent()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XDocument about = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AboutWindow.xaml"));
        XElement githubButton = Assert.Single(
            about.Descendants(presentation + "Button"),
            element => string.Equals(
                (string?)element.Attribute("AutomationProperties.Name"),
                "GitHub",
                StringComparison.Ordinal));
        XElement githubIcon = Assert.Single(githubButton.Descendants(presentation + "ContentControl"));
        Assert.Equal("{DynamicResource Qsearch.GitHubGlyph}", (string?)githubIcon.Attribute("Template"));
        Assert.Contains("Foreground", (string?)githubIcon.Attribute("Foreground"), StringComparison.Ordinal);
        Assert.Contains("RelativeSource", (string?)githubIcon.Attribute("Foreground"), StringComparison.Ordinal);

        XDocument settings = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Controls", "SettingsPanel.xaml"));
        List<XElement> selectedStateTriggers = settings
            .Descendants(presentation + "DataTrigger")
            .Where(trigger => trigger.Elements(presentation + "Setter").Any(setter =>
                string.Equals((string?)setter.Attribute("Property"), "Background", StringComparison.Ordinal) &&
                ((string?)setter.Attribute("Value"))?.Contains("Qsearch.PrimaryFillBrush", StringComparison.Ordinal) == true))
            .ToList();

        Assert.NotEmpty(selectedStateTriggers);
        Assert.All(selectedStateTriggers, trigger => Assert.Contains(
            trigger.Elements(presentation + "Setter"),
            setter => string.Equals((string?)setter.Attribute("Property"), "BorderBrush", StringComparison.Ordinal) &&
                ((string?)setter.Attribute("Value"))?.Contains("Qsearch.PrimaryFillBrush", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void Settings_ExposeThemeShortcutAndReservedWindowsCombination()
    {
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "Controls", "SettingsPanel.xaml");
        string windowPath = Path.Combine(AppContext.BaseDirectory, "SettingsWindow.xaml");
        Assert.True(File.Exists(settingsPath));
        Assert.True(File.Exists(windowPath));
        string xaml = File.ReadAllText(settingsPath);
        string window = File.ReadAllText(windowPath);

        Assert.Contains("OnThemeSystemClick", xaml, StringComparison.Ordinal);
        Assert.Contains("OnThemeDarkClick", xaml, StringComparison.Ordinal);
        Assert.Contains("OnThemeLightClick", xaml, StringComparison.Ordinal);
        Assert.Contains("OnHotkeyControlAltSpaceClick", xaml, StringComparison.Ordinal);
        Assert.Contains("OnHotkeyControlShiftSpaceClick", xaml, StringComparison.Ordinal);
        Assert.Contains("OnHotkeyAltQClick", xaml, StringComparison.Ordinal);
        Assert.Contains("OnHotkeyWindowsSClick", xaml, StringComparison.Ordinal);
        Assert.Contains("⊞ S", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.HotkeyProcessOnly", xaml, StringComparison.Ordinal);
        Assert.Contains("SettingsPanel", window, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultItemTemplate_PreservesGridViewCellPresentation()
    {
        string theme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Theme.xaml"));

        Assert.Contains("GridViewRowPresenter", theme, StringComparison.Ordinal);
        Assert.Contains("Path=View.Columns", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void Typography_UsesChosenPersianFamiliesWithoutPublishingPrivateBinaries()
    {
        string fonts = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts.xaml"));
        string project = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "Qsearch.App.csproj"));

        Assert.Contains("Farhang2", fonts, StringComparison.Ordinal);
        Assert.Contains("Tahrir", fonts, StringComparison.Ordinal);
        Assert.DoesNotContain("Modam", fonts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assets\\private\\fonts", project, StringComparison.Ordinal);
        Assert.Contains("Exists('..\\..\\assets\\private\\fonts", project, StringComparison.Ordinal);
        Assert.Contains("'$(Configuration)' != 'Release'", project, StringComparison.Ordinal);
    }
}
