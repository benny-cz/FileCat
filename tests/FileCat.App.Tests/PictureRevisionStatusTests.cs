using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;
using SkiaSharp;

namespace FileCat.App.Tests;

public sealed class PictureRevisionStatusTests(ITestOutputHelper output)
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaTheory]
    [InlineData("visible", false)]
    [InlineData("visible", true)]
    [InlineData("hidden", false)]
    [InlineData("hidden", true)]
    [InlineData("late", false)]
    [InlineData("late", true)]
    public async Task Cached_picture_status_distinguishes_changed_source_from_current_bytes(string mode, bool changed)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-picture-revision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] original = Encode(12, 8, SKColors.Teal), replacement = Encode(16, 10, SKColors.Orange);
        string path = Path.Join(root, "owned.png");
        File.WriteAllBytes(path, original);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        using var source = new FileContentSource(path);
        var viewer = new ViewerWindow(services, source, path, hex: mode == "late", deviceKey: "owned-picture-revision");
        try
        {
            viewer.Show();
            await WaitFor(() => Field<Avalonia.Controls.Primitives.ToggleButton>(viewer, "_modePicture").IsVisible);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            var reader = Field<PagedReader>(viewer, "_reader");
            var beforeRevision = reader.Revision;
            if (mode != "late")
            {
                Assert.NotNull(viewer.PictureLoad);
                await viewer.PictureLoad.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal((12, 8), (viewer.Picture!.Width, viewer.Picture.Height));
            }
            else Assert.Null(viewer.PictureLoad);
            if (mode == "hidden") typeof(ViewerWindow).GetMethod("SetMode", Fields)!.Invoke(viewer, [true]);
            if (changed)
            {
                File.WriteAllBytes(path, replacement);
                File.SetLastWriteTimeUtc(path, new DateTime(beforeRevision!.Value.ModifiedTicks, DateTimeKind.Utc).AddSeconds(10));
            }
            typeof(ViewerWindow).GetMethod("CheckForChanges", Fields)!.Invoke(viewer, []);
            string refreshStatus = Field<TextBlock>(viewer, "_status").Text ?? "";
            var afterRevision = reader.Revision;
            var bytes = new byte[checked((int)reader.Length)];
            Assert.Equal(bytes.Length, reader.Read(0, bytes));
            Assert.Equal(changed ? replacement : original, bytes);
            if (mode != "visible") viewer.ShowPicture();
            Assert.NotNull(viewer.PictureLoad);
            await viewer.PictureLoad.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var picture = viewer.Picture!;
            Assert.Equal(changed && mode == "late" ? (16, 10) : (12, 8), (picture.Width, picture.Height));
            string pictureStatus = Field<TextBlock>(viewer, "_status").Text ?? "";
            Field<PictureView>(viewer, "_picture").Fit = false;
            string afterZoomStatus = Field<TextBlock>(viewer, "_status").Text ?? "";
            bool oldSnapshot = changed && mode != "late";
            output.WriteLine(JsonSerializer.Serialize(new { mode, changed, oldSnapshot, refreshStatus, pictureStatus, afterZoomStatus,
                beforeRevision, afterRevision, PictureWidth = picture.Width, PictureHeight = picture.Height,
                OriginalPNGBase64 = Convert.ToBase64String(original), ReplacementPNGBase64 = Convert.ToBase64String(replacement),
                ActualContentSHA256 = Convert.ToHexString(SHA256.HashData(bytes)),
                ActualFileContentSourceAndPagedReaderAndViewer = true, HeadlessComponentNotNativeDesktop = true }));
            if (oldSnapshot)
            {
                Assert.Contains("The file changed", pictureStatus, StringComparison.Ordinal);
                Assert.Contains("reopen", pictureStatus, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("showing its current content", pictureStatus, StringComparison.Ordinal);
                Assert.Contains("The file changed", afterZoomStatus, StringComparison.Ordinal);
                Assert.Contains("reopen", afterZoomStatus, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.DoesNotContain("reopen", pictureStatus, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("reopen", afterZoomStatus, StringComparison.OrdinalIgnoreCase);
            }
            if (changed)
            {
                Assert.NotEqual(beforeRevision, afterRevision);
                if (mode != "visible") Assert.Contains("showing its current content", refreshStatus, StringComparison.Ordinal);
            }
            else Assert.Equal(beforeRevision, afterRevision);
        }
        finally
        {
            viewer.Close();
            if (viewer.PictureLoad is { } work) await work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Encode(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned viewer did not complete its initial header.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
