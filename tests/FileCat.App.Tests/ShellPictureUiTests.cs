using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Platform.Windows.Shell;

namespace FileCat.App.Tests;

public sealed class ShellPictureUiTests(ITestOutputHelper output)
{
    private static void WriteBitmap(string path, int width, int height)
    {
        int stride = (width * 3 + 3) & ~3;
        var bytes = new byte[54 + stride * height];
        void U32(int at, int v) => BitConverter.GetBytes(v).CopyTo(bytes, at);
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        U32(2, bytes.Length);
        U32(10, 54);
        U32(14, 40);
        U32(18, width);
        U32(22, height);
        bytes[26] = 1;
        bytes[28] = 24;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                bytes[54 + y * stride + x * 3] = (byte)(x * 255 / width);
        File.WriteAllBytes(path, bytes);
    }

    [AvaloniaFact]
    public async Task Quick_view_shows_the_Shells_thumbnail_of_a_picture_through_the_restricted_helper()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Shell thumbnails exist only on Windows.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            if (services.ShellPictures is null) Assert.Skip("This build has no Shell helper beside the tests.");
            var picture = Path.Combine(root, "files", "c.bmp");
            WriteBitmap(picture, 300, 200);
            var left = vm.Workspace.Panels[0];
            var listing = left.ActiveTab!.Listing;
            left.ActiveTab.Refresh();
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 4); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            vm.Workspace.Activate(left);
            Assert.True(listing.FocusName("c.bmp"));
            vm.Execute(CommandIds.QuickView);

            // Every panel has a quick view pane; the target panel's is the one in use.
            Image? Thumbnail() => window.GetVisualDescendants().OfType<Image>()
                .Where(i => AutomationProperties.GetName(i) == "Thumbnail").OrderByDescending(i => i.IsVisible).FirstOrDefault();
            for (int i = 0; i < 1500 && Thumbnail() is not { IsVisible: true, Source: not null }; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            if (Thumbnail() is not { IsVisible: true })
            {
                // What quick view was answered (its request: 256 pixels at the headless scale of 1), before asking again.
                bool answered = services.ShellPictures.TryGetCached(ShellImageKind.Thumbnail, picture, File.GetLastWriteTimeUtc(picture).Ticks, 256, out var first);
                string quickView = !answered ? "quick view's request has no answer yet" : first is null ? "quick view's request was answered with no picture" : "quick view's request was answered with a picture";
                // Distinguish "no handler on this machine" from a broken quick view.
                var direct = services.ShellPictures.Client.Get(ShellImageKind.Thumbnail, picture, 64, TimeSpan.FromSeconds(30));
                if (direct is null) Assert.Skip("This Windows installation has no thumbnail handler for .bmp files.");
                Assert.Fail($"The helper made a thumbnail, but quick view did not show it: {quickView}; helpers started: {services.ShellPictures.Client.Starts}.");
            }
            // Other Shell requests and contained handler failures may replace a helper. Verify this picture's
            // actual answer and UI binding; helper reuse/containment has separate native client tests.
            Assert.True(services.ShellPictures.TryGetCached(ShellImageKind.Thumbnail, picture,
                File.GetLastWriteTimeUtc(picture).Ticks, 256, out var cached));
            Assert.NotNull(cached);
            var shown = Assert.IsType<WriteableBitmap>(Thumbnail()!.Source);
            Assert.Equal(cached.Width, shown.PixelSize.Width);
            Assert.Equal(cached.Height, shown.PixelSize.Height);
            Assert.Equal(cached.Width * cached.Height * 4, cached.Bgra.Length);
            Assert.True(cached.Bgra.Where((_, i) => i % 4 == 0).Distinct().Count() > 8, "The helper answered with the gradient picture.");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text?.Contains("Shell thumbnail", StringComparison.Ordinal) == true);
            output.WriteLine($"The visible Shell thumbnail binds the {cached.Width} x {cached.Height} helper answer; helpers started: {services.ShellPictures.Client.Starts}.");
            Assert.True(services.ShellPictures.Client.RunsAtLowIntegrity);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>Where no Shell thumbnail comes (here: a member of a ZIP), FileCat's own decoder worker shows the picture.</summary>
    [AvaloniaFact]
    public async Task Quick_view_decodes_a_picture_itself_where_the_Shell_gives_none()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            using (var bitmap = new SkiaSharp.SKBitmap(300, 200))
            {
                using (var canvas = new SkiaSharp.SKCanvas(bitmap)) canvas.Clear(SkiaSharp.SKColors.Teal);
                using var png = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                using var zip = System.IO.Compression.ZipFile.Open(Path.Combine(root, "files", "pictures.zip"), System.IO.Compression.ZipArchiveMode.Create);
                using var entry = zip.CreateEntry("pic.png").Open();
                png.SaveTo(entry);
            }
            var left = vm.Workspace.Panels[0];
            var listing = left.ActiveTab!.Listing;
            left.ActiveTab.Refresh();
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 4); i++) await Task.Delay(20, ct);
            vm.Workspace.Activate(left);
            Assert.True(listing.FocusName("pictures.zip"));
            vm.Execute(CommandIds.Enter);
            for (int i = 0; i < 250 && !listing.FocusName("pic.png"); i++) await Task.Delay(20, ct);
            vm.Execute(CommandIds.QuickView);

            Image? Thumbnail() => window.GetVisualDescendants().OfType<Image>()
                .Where(i => AutomationProperties.GetName(i) == "Thumbnail").OrderByDescending(i => i.IsVisible).FirstOrDefault();
            for (int i = 0; i < 1000 && Thumbnail() is not { IsVisible: true, Source: not null }; i++) await Task.Delay(20, ct);
            Assert.True(Thumbnail() is { IsVisible: true, Source: not null });
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("PNG, 300 × 200 (F3 shows the picture)", StringComparison.Ordinal) == true);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
