using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.Rendering.Tests;

public sealed class PictureGridRenderingTests
{
    [AvaloniaFact]
    public async Task Grid_renders_mixed_items_and_decoded_thumbnails_in_a_bounded_viewport()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-picture-render", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string folder = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
        Window? window = null;
        TabViewModel? tab = null;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "picture-a.bmp"), Bmp(35));
            File.WriteAllBytes(Path.Combine(folder, "picture-b.bmp"), Bmp(180));
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "kept in the grid");
            Directory.CreateDirectory(Path.Combine(folder, "folder"));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            workspace.ActivePanel = panel;
            tab = panel.OpenTab(Location.FileSystem(folder));
            var list = new FileListControl { Tab = tab, PictureMode = true, Width = 640, Height = 420 };
            window = new Window { Width = 640, Height = 420, Content = list };
            window.Show();
            for (int i = 0; i < 300 && (tab.Listing.State != ListingState.Complete || list.CachedPictureThumbnailCount < 2); i++)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                using var warm = new RenderTargetBitmap(new PixelSize(640, 420));
                warm.Render(list);
            }
            Assert.Equal(2, list.CachedPictureThumbnailCount);
            Assert.Contains(Enumerable.Range(0, tab.Listing.VisibleCount),
                row => tab.Listing.GetVisible(row).Name == "notes.txt");
            Assert.Contains(Enumerable.Range(0, tab.Listing.VisibleCount),
                row => tab.Listing.GetVisible(row).Name == "folder");
            Assert.InRange(list.VisibleRowCapacity, 1, 24);
            using var target = new RenderTargetBitmap(new PixelSize(640, 420));
            target.Render(list);
            if (Environment.GetEnvironmentVariable("FILECAT_PICTURE_GRID_SHOT") is { Length: > 0 } shot)
                target.Save(shot, PngBitmapEncoderOptions.Default);
        }
        finally
        {
            window?.Close();
            tab?.Dispose();
            services.Dispose();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task Large_picture_grid_keeps_visible_thumbnails_across_redraw_and_unchanged_refresh()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-picture-stable", Guid.NewGuid().ToString("N"));
        string folder = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
        for (int i = 0; i < 65; i++)
            File.WriteAllBytes(Path.Combine(folder, $"picture-{i:00}.bmp"), Bmp((byte)(20 + i)));
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
        Window? window = null;
        TabViewModel? tab = null;
        try
        {
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            workspace.ActivePanel = panel;
            tab = panel.OpenTab(Location.FileSystem(folder));
            var list = new FileListControl { Tab = tab, PictureMode = true, Width = 1900, Height = 950 };
            window = new Window { Width = 1900, Height = 950, Content = list };
            window.Show();
            using var frame = new RenderTargetBitmap(new PixelSize(1900, 950));

            async Task WaitForPictures(long loads, int cached)
            {
                for (int i = 0; i < 2000; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    if (i % 5 == 0) frame.Render(list);
                    if (list.PictureThumbnailLoadCount >= loads && list.CachedPictureThumbnailCount == cached
                        && list.PendingPictureThumbnailCount == 0) return;
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                }
                Assert.Fail($"Only {list.CachedPictureThumbnailCount}/{cached} thumbnails settled; " +
                    $"loads={list.PictureThumbnailLoadCount}, pending={list.PendingPictureThumbnailCount}.");
            }

            await WaitForPictures(65, 65);
            Assert.Equal(65, list.PictureThumbnailLoadCount);
            for (int i = 0; i < 10; i++)
            {
                frame.Render(list);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(65, list.CachedPictureThumbnailCount);
            Assert.Equal(65, list.PictureThumbnailLoadCount);

            async Task RefreshListing()
            {
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnChanged(object? sender, ListingChange change)
                {
                    if ((change & ListingChange.RefreshComplete) != 0) completed.TrySetResult();
                }
                tab.Listing.Changed += OnChanged;
                try
                {
                    tab.Refresh();
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                }
                finally { tab.Listing.Changed -= OnChanged; }
                frame.Render(list);
                Dispatcher.UIThread.RunJobs();
            }

            await RefreshListing();
            Assert.Equal(65, list.CachedPictureThumbnailCount);
            Assert.Equal(65, list.PictureThumbnailLoadCount);

            string changed = Path.Combine(folder, "picture-00.bmp");
            File.WriteAllBytes(changed, Bmp(230));
            File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddMinutes(1));
            await RefreshListing();
            await WaitForPictures(66, 65);
            Assert.Equal(66, list.PictureThumbnailLoadCount);
        }
        finally
        {
            window?.Close();
            tab?.Dispose();
            services.Dispose();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static byte[] Bmp(byte blue)
    {
        var bytes = new byte[58];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        foreach (var (at, value) in new[] { (2, bytes.Length), (10, 54), (14, 40), (18, 1), (22, -1) })
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 32);
        bytes[54] = blue; bytes[57] = 255;
        return bytes;
    }
}
