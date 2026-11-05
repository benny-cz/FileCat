using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task TreemapFontAndSizeChangesDeferHitTargetLayoutUntilPointerRelease()
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture());
        await model.RebuildAsync(true); ThemeManager.Apply("ClassicDark");
        var chart = new TreemapControl(); chart.SetView(model.Tree!, AnalysisTree.RootKey, null, []);
        var window = new Window { Width = 820, Height = 580, FontSize = 13, Content = chart };
        string? activated = null; chart.Activated += (key, _, _) => activated = key;
        window.Show();
        try
        {
            void Frame() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); using var bitmap = new RenderTargetBitmap(new PixelSize(900, 600)); bitmap.Render(window); }
            Frame(); var point = new Point(24, 60);
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.Equal("n:2", activated);
            activated = null; window.MouseDown(point, MouseButton.Left);
            window.FontSize = 32; window.Width = 900;
            chart.SetView(model.Tree!, AnalysisTree.RootKey, "n:1", []); Frame();
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal("n:2", activated);
            Frame(); activated = null;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.Equal("n:1", activated);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TreemapFolderHeadersRemainSelectableAboveTheirChildrenAtBothTextSizes()
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture());
        await model.RebuildAsync(true);
        var chart = new TreemapControl(); chart.SetView(model.Tree!, AnalysisTree.RootKey, null, []);
        var window = new Window { Width = 820, Height = 580, Content = chart };
        (string Key, bool Mark, bool Open)? activated = null;
        chart.Activated += (key, mark, open) => activated = (key, mark, open);
        window.Show();
        try
        {
            foreach (string theme in new[] { "Classic", "ClassicDark", "HighContrast", "Cyberpunk", "Psychedelic", "Steampunk", "DosCommander" })
            foreach (int font in new[] { 13, 20 })
            {
                ThemeManager.Apply(theme); window.FontSize = font;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                using var image = new RenderTargetBitmap(new PixelSize(820, 580)); image.Render(window);
                string output = Path.Combine(AppContext.BaseDirectory, "analysis-renders"); Directory.CreateDirectory(output);
                image.Save(Path.Combine(output, $"{theme}-treemap-folder-headers-{font}.png"), new PngBitmapEncoderOptions());

                // A parent header must be an actual target, rather than text covered by a child tile.
                // The old inset-only layout resolves this point to source.cs instead of Fixture.
                var header = new Point(24, 24); activated = null;
                window.MouseMove(header); window.MouseDown(header, MouseButton.Left); window.MouseUp(header, MouseButton.Left);
                Assert.Equal("n:1", activated?.Key); Assert.False(activated?.Mark);
                activated = null;
                window.MouseDown(header, MouseButton.Left, RawInputModifiers.Control); window.MouseUp(header, MouseButton.Left, RawInputModifiers.Control);
                Assert.Equal("n:1", activated?.Key); Assert.True(activated?.Mark);

                // Reserving a header must leave the measured descendants reachable as well.
                var child = new Point(24, chart.Bounds.Height - 24); activated = null;
                window.MouseMove(child); window.MouseDown(child, MouseButton.Left); window.MouseUp(child, MouseButton.Left);
                Assert.NotNull(activated); Assert.NotEqual("n:1", activated?.Key);
                Assert.Equal((UInt128)98304, model.Tree!.ScopeValue(AnalysisTree.RootKey));
            }
        }
        finally { window.Close(); }
    }
}
