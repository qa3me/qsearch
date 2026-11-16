using Qsearch.App.Controls;
using Qsearch.App.Services;
using Qsearch.App.ViewModels;
using Qsearch.Core.Indexing;
using Qsearch.Core.Settings;
using Qsearch.Safety.Models;
using Qsearch.Windows.Shell;

namespace Qsearch.App.Tests;

public sealed class XamlRuntimeTests
{
    [Fact]
    public void MainWindow_ConstructsWithMergedThemeResources()
    {
        Exception? captured = null;
        bool widgetWasVisible = false;
        Thread thread = new(() =>
        {
            try
            {
                App app = new();
                app.InitializeComponent();
                AssertPrimaryButtonContrast(app);
                AssertMarkRendersAsFiveSeparateParts(app);
                AssertUiPolishPrimitives(app);
                QsearchMark mark = new();
                System.Reflection.PropertyInfo? isAnimated = mark.GetType().GetProperty("IsAnimated");
                System.Reflection.PropertyInfo? orbitAngle = mark.GetType().GetProperty("OrbitAngle");
                Assert.NotNull(isAnimated);
                Assert.NotNull(orbitAngle);
                Assert.False(Assert.IsType<bool>(isAnimated.GetValue(mark)));
                Assert.NotNull(mark.FindName("Orbit"));
                Assert.Equal(0, Assert.IsType<double>(orbitAngle.GetValue(mark)));
                MainViewModel viewModel = new(
                    new EmptyIndexStore(),
                    new EmptyShell(),
                    new EmptyReview());
                viewModel.SetSearchTextAsync(string.Empty).AsTask().GetAwaiter().GetResult();
                Assert.Single(viewModel.Results);
                ThemeService theme = new();
                LocalizationService localization = new();
                localization.Apply("fa-IR");
                MainWindow window = new(
                    viewModel,
                    localization,
                    themeService: theme);
                Assert.Equal(System.Windows.FlowDirection.RightToLeft, window.FlowDirection);
                AboutWindow about = new(new AboutViewModel(), themeService: theme);
                using WidgetViewModel widgetViewModel = new(viewModel);
                WidgetWindow widget = new(widgetViewModel, themeService: theme);
                LauncherWindow launcher = new(widgetViewModel, themeService: theme);
                theme.Apply(app.Resources, AppThemePreference.Light);
                AssertPrimaryButtonContrast(app);
                window.RefreshTheme();
                about.RefreshTheme();
                widget.RefreshTheme();
                launcher.RefreshTheme();
                AssertLightSurface(window);
                AssertLightSurface(about);
                AssertLightSurface(widget);
                AssertLightSurface(launcher);
                widget.ShowNear(global::System.Windows.Forms.Screen.PrimaryScreen ??
                    global::System.Windows.Forms.Screen.AllScreens[0]);
                widgetWasVisible = widget.IsVisible;
                widget.Hide();
                launcher.ShowCentered(global::System.Windows.Forms.Screen.PrimaryScreen ??
                    global::System.Windows.Forms.Screen.AllScreens[0]);
                Assert.True(launcher.IsVisible);
                launcher.Hide();
                launcher.Close();
                widget.Close();
                about.Close();
                window.Close();
                app.Shutdown();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(30)),
            "The WPF runtime probe did not complete within 30 seconds.");
        Assert.Null(captured);
        Assert.True(widgetWasVisible);
    }

    private static void AssertLightSurface(System.Windows.Window window)
    {
        System.Windows.Media.SolidColorBrush windowBrush = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            window.Background);
        Assert.Equal(
            System.Windows.Media.Color.FromArgb(1, 0xF4, 0xF7, 0xF6),
            windowBrush.Color);

        System.Windows.FrameworkElement layout = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(
            window.FindName("LayoutRoot"));
        System.Windows.Media.DrawingBrush gridBrush = Assert.IsType<System.Windows.Media.DrawingBrush>(
            layout.GetValue(System.Windows.Controls.Panel.BackgroundProperty));
        System.Windows.Media.GeometryDrawing drawing = Assert.IsType<System.Windows.Media.GeometryDrawing>(
            gridBrush.Drawing);
        System.Windows.Media.SolidColorBrush fill = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            drawing.Brush);
        Assert.Equal(
            System.Windows.Media.Color.FromArgb(0xF0, 0xF4, 0xF7, 0xF6),
            fill.Color);
    }

    private static void AssertPrimaryButtonContrast(App app)
    {
        System.Windows.Controls.TextBlock label = new()
        {
            Text = "Open",
        };
        System.Windows.Controls.Button button = new()
        {
            Content = label,
            Style = Assert.IsType<System.Windows.Style>(app.FindResource("Qsearch.PrimaryButton")),
        };
        button.Measure(new System.Windows.Size(160, 48));
        button.Arrange(new System.Windows.Rect(0, 0, 160, 48));
        button.UpdateLayout();
        _ = button.ApplyTemplate();

        System.Windows.Media.SolidColorBrush foreground = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            label.Foreground);
        System.Windows.Media.SolidColorBrush background = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            button.Background);
        AssertContrast("normal", foreground.Color, background.Color);

        System.Windows.Trigger hover = Assert.Single(
            button.Template.Triggers.OfType<System.Windows.Trigger>(),
            trigger => trigger.Property == System.Windows.UIElement.IsMouseOverProperty &&
                Equals(trigger.Value, true));
        System.Windows.Setter opacitySetter = Assert.Single(
            hover.Setters.OfType<System.Windows.Setter>(),
            setter => setter.Property == System.Windows.UIElement.OpacityProperty &&
                string.Equals(setter.TargetName, "Border", StringComparison.Ordinal));
        double hoverOpacity = Assert.IsType<double>(opacitySetter.Value);
        System.Windows.Media.Color panel = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            app.FindResource("Qsearch.PanelBrush")).Color;
        AssertContrast(
            "hover",
            Composite(foreground.Color, panel, hoverOpacity),
            Composite(background.Color, panel, hoverOpacity));

        button.IsEnabled = false;
        button.UpdateLayout();
        System.Windows.Controls.Border border = Assert.IsType<System.Windows.Controls.Border>(
            button.Template.FindName("Border", button));
        System.Windows.Media.SolidColorBrush disabledBackground = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            border.Background);
        System.Windows.Media.SolidColorBrush disabledForeground = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            label.Foreground);
        AssertContrast("disabled", disabledForeground.Color, disabledBackground.Color);
    }

    private static void AssertContrast(
        string state,
        System.Windows.Media.Color foreground,
        System.Windows.Media.Color background)
    {
        double foregroundLuminance = RelativeLuminance(foreground);
        double backgroundLuminance = RelativeLuminance(background);
        double ratio = (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05) /
            (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);
        Assert.True(
            ratio >= 4.5,
            $"Primary button {state} contrast was {ratio:F3}:1 for {foreground} on {background}.");
    }

    private static double RelativeLuminance(System.Windows.Media.Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(byte channel)
    {
        double value = channel / 255d;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static System.Windows.Media.Color Composite(
        System.Windows.Media.Color foreground,
        System.Windows.Media.Color background,
        double opacity) =>
        System.Windows.Media.Color.FromRgb(
            (byte)Math.Round((foreground.R * opacity) + (background.R * (1 - opacity))),
            (byte)Math.Round((foreground.G * opacity) + (background.G * (1 - opacity))),
            (byte)Math.Round((foreground.B * opacity) + (background.B * (1 - opacity))));

    private static void AssertMarkRendersAsFiveSeparateParts(App app)
    {
        byte[] leftToRight = RenderMark(app, System.Windows.FlowDirection.LeftToRight);
        byte[] rightToLeft = RenderMark(app, System.Windows.FlowDirection.RightToLeft);

        Assert.Equal(leftToRight, rightToLeft);
        Assert.Equal(5, CountSubstantialComponents(leftToRight, 16, alphaThreshold: 128));

        QsearchMark mark = new();
        System.Windows.FrameworkElement orbit = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(
            mark.FindName("Orbit"));
        Assert.Null(orbit.CacheMode);
    }

    private static void AssertUiPolishPrimitives(App app)
    {
        System.Windows.Style iconHostStyle = Assert.IsType<System.Windows.Style>(
            app.FindResource("Qsearch.ResultIconHost"));
        System.Windows.Controls.Border marker = new()
        {
            Width = 10,
            Height = 6,
        };
        System.Windows.Controls.ContentControl iconHost = new()
        {
            Width = 24,
            Height = 24,
            Content = marker,
            Style = iconHostStyle,
        };
        System.Windows.Controls.Grid iconSurface = new()
        {
            Width = 24,
            Height = 24,
            FlowDirection = System.Windows.FlowDirection.RightToLeft,
            Resources = app.Resources,
        };
        iconSurface.Children.Add(iconHost);
        iconSurface.Measure(new System.Windows.Size(24, 24));
        iconSurface.Arrange(new System.Windows.Rect(0, 0, 24, 24));
        iconSurface.UpdateLayout();

        Assert.Equal(System.Windows.HorizontalAlignment.Center, iconHost.HorizontalContentAlignment);
        Assert.Equal(System.Windows.VerticalAlignment.Center, iconHost.VerticalContentAlignment);
        System.Windows.Point markerPosition = marker.TranslatePoint(new System.Windows.Point(), iconHost);
        Assert.InRange(markerPosition.X, 6.9, 7.1);
        Assert.InRange(markerPosition.Y, 8.9, 9.1);

        System.Windows.Controls.Button iconButton = new()
        {
            Style = Assert.IsType<System.Windows.Style>(app.FindResource("Qsearch.IconButton")),
        };
        Assert.Equal(System.Windows.HorizontalAlignment.Center, iconButton.HorizontalContentAlignment);
        Assert.Equal(System.Windows.VerticalAlignment.Center, iconButton.VerticalContentAlignment);

        System.Windows.Controls.Viewbox iconViewbox = new()
        {
            Style = Assert.IsType<System.Windows.Style>(app.FindResource("Qsearch.IconViewbox")),
        };
        Assert.Equal(System.Windows.HorizontalAlignment.Center, iconViewbox.HorizontalAlignment);
        Assert.Equal(System.Windows.VerticalAlignment.Center, iconViewbox.VerticalAlignment);
        Assert.Equal(System.Windows.FlowDirection.LeftToRight, iconViewbox.FlowDirection);
        Assert.Equal(System.Windows.Media.Stretch.Uniform, iconViewbox.Stretch);

        System.Windows.Shapes.Ellipse statusDot = new()
        {
            Style = Assert.IsType<System.Windows.Style>(app.FindResource("Qsearch.StatusDot")),
        };
        Assert.Equal(System.Windows.VerticalAlignment.Center, statusDot.VerticalAlignment);

        System.Windows.Controls.ScrollViewer scrollViewer = new()
        {
            Width = 80,
            Height = 80,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Visible,
            Style = Assert.IsType<System.Windows.Style>(app.FindResource(typeof(System.Windows.Controls.ScrollViewer))),
            Content = new System.Windows.Controls.Border
            {
                Width = 160,
                Height = 160,
            },
        };
        scrollViewer.Measure(new System.Windows.Size(80, 80));
        scrollViewer.Arrange(new System.Windows.Rect(0, 0, 80, 80));
        _ = scrollViewer.ApplyTemplate();
        scrollViewer.UpdateLayout();
        System.Windows.Controls.Border corner = Assert.IsType<System.Windows.Controls.Border>(
            scrollViewer.Template.FindName("PART_ScrollBarCorner", scrollViewer));
        System.Windows.Media.SolidColorBrush cornerBrush = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            corner.Background);
        Assert.Equal(0, cornerBrush.Color.A);
        Assert.True(corner.SnapsToDevicePixels);

        System.Windows.Controls.ScrollViewer gridViewScrollViewer = new()
        {
            Width = 80,
            Height = 80,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Visible,
            Style = Assert.IsType<System.Windows.Style>(app.FindResource(System.Windows.Controls.GridView.GridViewScrollViewerStyleKey)),
            Content = new System.Windows.Controls.Border
            {
                Width = 160,
                Height = 160,
            },
        };
        gridViewScrollViewer.Measure(new System.Windows.Size(80, 80));
        gridViewScrollViewer.Arrange(new System.Windows.Rect(0, 0, 80, 80));
        _ = gridViewScrollViewer.ApplyTemplate();
        gridViewScrollViewer.UpdateLayout();
        System.Windows.Controls.Border gridViewCorner = Assert.IsType<System.Windows.Controls.Border>(
            gridViewScrollViewer.Template.FindName("PART_ScrollBarCorner", gridViewScrollViewer));
        System.Windows.Media.SolidColorBrush gridViewCornerBrush = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            gridViewCorner.Background);
        Assert.Equal(0, gridViewCornerBrush.Color.A);

        System.Windows.Controls.Primitives.ScrollBar verticalScrollBar = new()
        {
            Orientation = System.Windows.Controls.Orientation.Vertical,
            Style = Assert.IsType<System.Windows.Style>(app.FindResource(typeof(System.Windows.Controls.Primitives.ScrollBar))),
        };
        _ = verticalScrollBar.ApplyTemplate();
        System.Windows.Controls.Primitives.Track verticalTrack = Assert.IsType<System.Windows.Controls.Primitives.Track>(
            verticalScrollBar.Template.FindName("PART_Track", verticalScrollBar));
        Assert.Equal(0, verticalTrack.Thumb.MinWidth);
        Assert.Equal(28, verticalTrack.Thumb.MinHeight);

        System.Windows.Controls.Primitives.ScrollBar horizontalScrollBar = new()
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Style = Assert.IsType<System.Windows.Style>(app.FindResource(typeof(System.Windows.Controls.Primitives.ScrollBar))),
        };
        _ = horizontalScrollBar.ApplyTemplate();
        System.Windows.Controls.Primitives.Track horizontalTrack = Assert.IsType<System.Windows.Controls.Primitives.Track>(
            horizontalScrollBar.Template.FindName("PART_Track", horizontalScrollBar));
        Assert.Equal(28, horizontalTrack.Thumb.MinWidth);
        Assert.Equal(0, horizontalTrack.Thumb.MinHeight);

        Assert.Equal(
            RenderResultGlyph(app, "sample.txt", IndexItemType.File, System.Windows.FlowDirection.LeftToRight),
            RenderResultGlyph(app, "sample.txt", IndexItemType.File, System.Windows.FlowDirection.RightToLeft));

        AssertGlyphIsOpticallyCentered(
            "File",
            RenderResultGlyph(app, "sample.txt", IndexItemType.File, System.Windows.FlowDirection.LeftToRight),
            24);
        AssertGlyphIsOpticallyCentered(
            "Folder",
            RenderResultGlyph(app, "sample", IndexItemType.Folder, System.Windows.FlowDirection.LeftToRight),
            24);
        AssertGlyphIsOpticallyCentered(
            "Application",
            RenderResultGlyph(app, "sample.exe", IndexItemType.File, System.Windows.FlowDirection.LeftToRight),
            24);
        AssertGlyphIsOpticallyCentered(
            "Close",
            RenderTemplateGlyph(app, "Qsearch.CloseGlyph", hostSize: 34, glyphSize: 14),
            34);
        AssertGlyphIsOpticallyCentered(
            "External link",
            RenderTemplateGlyph(app, "Qsearch.ExternalLinkGlyph", hostSize: 34, glyphSize: 14),
            34);
        AssertGlyphIsOpticallyCentered(
            "GitHub",
            RenderTemplateGlyph(app, "Qsearch.GitHubGlyph", hostSize: 36, glyphSize: 18),
            36);
    }

    private static byte[] RenderResultGlyph(
        App app,
        string name,
        IndexItemType type,
        System.Windows.FlowDirection inheritedFlowDirection)
    {
        System.Windows.Controls.ContentControl icon = new()
        {
            Width = 24,
            Height = 24,
            Content = new ResultItemViewModel(new SearchResult(
                "icon",
                "scope",
                name,
                $@"C:\scope\{name}",
                type,
                1,
                DateTimeOffset.UnixEpoch)),
            ContentTemplate = Assert.IsType<System.Windows.DataTemplate>(app.FindResource("Qsearch.ResultGlyph")),
            Style = Assert.IsType<System.Windows.Style>(app.FindResource("Qsearch.ResultIconHost")),
        };
        System.Windows.Controls.Grid surface = new()
        {
            Width = 24,
            Height = 24,
            FlowDirection = inheritedFlowDirection,
            Resources = app.Resources,
        };
        surface.Children.Add(icon);
        surface.Measure(new System.Windows.Size(24, 24));
        surface.Arrange(new System.Windows.Rect(0, 0, 24, 24));
        surface.UpdateLayout();

        System.Windows.Media.Imaging.RenderTargetBitmap bitmap = new(
            24,
            24,
            96,
            96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(surface);
        byte[] pixels = new byte[24 * 24 * 4];
        bitmap.CopyPixels(pixels, 24 * 4, 0);
        return pixels;
    }

    private static byte[] RenderTemplateGlyph(App app, string templateKey, int hostSize, int glyphSize)
    {
        System.Windows.Controls.ContentControl icon = new()
        {
            Width = glyphSize,
            Height = glyphSize,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Foreground = System.Windows.Media.Brushes.White,
            Template = Assert.IsType<System.Windows.Controls.ControlTemplate>(app.FindResource(templateKey)),
        };
        System.Windows.Controls.Grid surface = new()
        {
            Width = hostSize,
            Height = hostSize,
            Resources = app.Resources,
        };
        surface.Children.Add(icon);
        surface.Measure(new System.Windows.Size(hostSize, hostSize));
        surface.Arrange(new System.Windows.Rect(0, 0, hostSize, hostSize));
        surface.UpdateLayout();

        System.Windows.Media.Imaging.RenderTargetBitmap bitmap = new(
            hostSize,
            hostSize,
            96,
            96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(surface);
        byte[] pixels = new byte[hostSize * hostSize * 4];
        bitmap.CopyPixels(pixels, hostSize * 4, 0);
        return pixels;
    }

    private static void AssertGlyphIsOpticallyCentered(string name, byte[] pixels, int size)
    {
        int minX = size;
        int minY = size;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (pixels[((y * size) + x) * 4 + 3] == 0)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        Assert.True(maxX >= minX && maxY >= minY, $"{name} rendered no visible pixels.");
        double target = (size - 1) / 2d;
        double centerX = (minX + maxX) / 2d;
        double centerY = (minY + maxY) / 2d;
        Assert.InRange(
            Math.Abs(centerX - target),
            0,
            0.5);
        Assert.InRange(
            Math.Abs(centerY - target),
            0,
            0.5);
    }

    private static byte[] RenderMark(App app, System.Windows.FlowDirection inheritedFlowDirection)
    {
        System.Windows.Controls.Grid host = new()
        {
            Width = 16,
            Height = 16,
            FlowDirection = inheritedFlowDirection,
            Resources = app.Resources,
        };
        host.Children.Add(new QsearchMark
        {
            Width = 16,
            Height = 16,
        });
        host.Measure(new System.Windows.Size(16, 16));
        host.Arrange(new System.Windows.Rect(0, 0, 16, 16));
        host.UpdateLayout();

        System.Windows.Media.Imaging.RenderTargetBitmap bitmap = new(
            16,
            16,
            96,
            96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(host);
        byte[] pixels = new byte[16 * 16 * 4];
        bitmap.CopyPixels(pixels, 16 * 4, 0);
        return pixels;
    }

    private static int CountSubstantialComponents(byte[] pixels, int size, byte alphaThreshold)
    {
        bool[] visited = new bool[size * size];
        int components = 0;
        int[] offsets = [-1, 0, 1];
        for (int index = 0; index < visited.Length; index++)
        {
            if (visited[index] || pixels[(index * 4) + 3] < alphaThreshold)
            {
                continue;
            }

            Queue<int> pending = new();
            pending.Enqueue(index);
            visited[index] = true;
            int count = 0;
            while (pending.TryDequeue(out int current))
            {
                count++;
                int x = current % size;
                int y = current / size;
                foreach (int deltaY in offsets)
                {
                    foreach (int deltaX in offsets)
                    {
                        int nextX = x + deltaX;
                        int nextY = y + deltaY;
                        if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1 ||
                            nextX < 0 || nextX >= size || nextY < 0 || nextY >= size)
                        {
                            continue;
                        }

                        int next = (nextY * size) + nextX;
                        if (!visited[next] && pixels[(next * 4) + 3] >= alphaThreshold)
                        {
                            visited[next] = true;
                            pending.Enqueue(next);
                        }
                    }
                }
            }

            if (count >= 2)
            {
                components++;
            }
        }

        return components;
    }

    private sealed class EmptyIndexStore : IIndexStore
    {
        public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ApplyBatchAsync(IndexBatch batch, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<SearchPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SearchPage(
                [new SearchResult("test", "scope", "test.txt", @"C:\scope\test.txt", IndexItemType.File, 1, DateTimeOffset.UnixEpoch)],
                IndexFreshness.Fresh));
        public ValueTask<long> CountAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0L);
        public ValueTask ResetScopeAsync(string scopeId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IndexCheckpoint?> GetCheckpointAsync(string scopeId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IndexCheckpoint?>(null);
        public ValueTask SaveCheckpointAsync(IndexCheckpoint checkpoint, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<ReviewCacheRecord?> GetReviewAsync(string itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ReviewCacheRecord?>(null);
        public ValueTask SaveReviewAsync(ReviewCacheRecord review, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class EmptyShell : IShellActionService
    {
        public ValueTask OpenAsync(string path, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevealAsync(string path, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class EmptyReview : IFileReviewService
    {
        public ValueTask<FileReviewResult> ReviewAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new FileReviewResult(ReviewState.NotReviewed, 0, FileKind.Unknown, [], false));
    }
}
