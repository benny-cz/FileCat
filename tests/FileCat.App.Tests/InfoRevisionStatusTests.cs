using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;
using SkiaSharp;

namespace FileCat.App.Tests;

public sealed class InfoRevisionStatusTests(ITestOutputHelper output)
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaTheory]
    [InlineData("visible", false)]
    [InlineData("visible", true)]
    [InlineData("hidden", false)]
    [InlineData("hidden", true)]
    [InlineData("late", false)]
    [InlineData("late", true)]
    public async Task Cached_structure_status_distinguishes_changed_source_from_current_bytes(string mode, bool changed)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-info-revision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] original = Encode(12, 8, SKColors.Teal), replacement = Encode(16, 10, SKColors.Orange);
        string path = Path.Join(root, "owned.png");
        File.WriteAllBytes(path, original);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        using var source = new FileContentSource(path);
        var viewer = new ViewerWindow(services, source, path, hex: true, deviceKey: "owned-info-revision");
        try
        {
            viewer.Show();
            await WaitFor(() => Field<Avalonia.Controls.Primitives.ToggleButton>(viewer, "_modePicture").IsVisible);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            var reader = Field<PagedReader>(viewer, "_reader");
            var beforeRevision = reader.Revision;
            if (mode != "late")
            {
                await viewer.ShowInfoAsync();
                Assert.Contains("12 × 8 pixels", viewer.InfoText, StringComparison.Ordinal);
            }
            else Assert.Equal("", viewer.InfoText);
            string beforeReport = viewer.InfoText;
            if (mode == "hidden") typeof(ViewerWindow).GetMethod("SetMode", Fields)!.Invoke(viewer, [true]);
            if (changed)
            {
                File.WriteAllBytes(path, replacement);
                File.SetLastWriteTimeUtc(path, new DateTime(beforeRevision!.Value.ModifiedTicks, DateTimeKind.Utc).AddSeconds(10));
            }
            typeof(ViewerWindow).GetMethod("CheckForChanges", Fields)!.Invoke(viewer, []);
            string refreshStatus = viewer.StatusText;
            var afterRevision = reader.Revision;
            var bytes = new byte[checked((int)reader.Length)];
            Assert.Equal(bytes.Length, reader.Read(0, bytes));
            Assert.Equal(changed ? replacement : original, bytes);
            if (mode != "visible") await viewer.ShowInfoAsync();
            string report = viewer.InfoText;
            string infoStatus = viewer.StatusText;
            // Search/navigation and other component events call this same status method.
            typeof(ViewerWindow).GetMethod("UpdateStatus", Fields)!.Invoke(viewer, []);
            string afterUpdateStatus = viewer.StatusText;
            bool oldSnapshot = changed && mode != "late";
            Assert.Contains(changed && mode == "late" ? "16 × 10 pixels" : "12 × 8 pixels", report, StringComparison.Ordinal);
            Assert.Null(viewer.PictureLoad);
            output.WriteLine(JsonSerializer.Serialize(new { mode, changed, oldSnapshot, refreshStatus, infoStatus, afterUpdateStatus,
                beforeRevision, afterRevision, BeforeReport = beforeReport, Report = report,
                OriginalPNGBase64 = Convert.ToBase64String(original), ReplacementPNGBase64 = Convert.ToBase64String(replacement),
                ActualContentSHA256 = Convert.ToHexString(SHA256.HashData(bytes)),
                ActualFileContentSourceAndPagedReaderAndViewerAndInspector = true, NoPictureWorkerStarted = viewer.PictureLoad is null,
                HeadlessComponentNotNativeDesktop = true }));
            if (oldSnapshot)
            {
                Assert.Contains("The file changed", infoStatus, StringComparison.Ordinal);
                Assert.Contains("reopen", infoStatus, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("showing its current content", infoStatus, StringComparison.Ordinal);
                Assert.Contains("The file changed", afterUpdateStatus, StringComparison.Ordinal);
                Assert.Contains("reopen", afterUpdateStatus, StringComparison.OrdinalIgnoreCase);
                if (mode == "visible") Assert.DoesNotContain("showing its current content", refreshStatus, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("reopen", infoStatus, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("reopen", afterUpdateStatus, StringComparison.OrdinalIgnoreCase);
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
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Encode(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;

    private static async Task WaitFor(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "Owned Info fixture checkpoint timed out");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
