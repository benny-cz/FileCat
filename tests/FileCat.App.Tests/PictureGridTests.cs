using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Archives;

namespace FileCat.App.Tests;

public sealed class PictureGridTests
{
    [AvaloniaFact]
    public async Task Grid_loads_a_picture_through_the_archive_provider_without_hiding_other_members()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string archivePath = Path.Combine(root, "files", "pictures.zip");
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                using (var image = zip.CreateEntry("image.bmp").Open()) image.Write(Bmp(95));
                using (var text = new StreamWriter(zip.CreateEntry("readme.txt").Open())) text.Write("keep this file");
                zip.CreateEntry("folder/");
            }
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.ActiveTab!;
            tab.Navigate(ZipProvider.ForFile(archivePath));
            for (int i = 0; i < 250 && (tab.Listing.State != ListingState.Complete || tab.Listing.FindStoreIndex("image.bmp") < 0); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(item => ReferenceEquals(item.DataContext, panel));
            var list = view.FindControl<FileListControl>("List")!;
            tab.PictureView = true;
            await RenderUntilThumbnailAsync(window, list);
            Assert.Equal(1, list.CachedPictureThumbnailCount);
            Assert.Contains(Enumerable.Range(0, tab.Listing.VisibleCount),
                row => tab.Listing.GetVisible(row).Name == "readme.txt");
            Assert.Contains(Enumerable.Range(0, tab.Listing.VisibleCount),
                row => tab.Listing.GetVisible(row).Name == "folder");
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Grid_keeps_folders_and_other_files_with_selection_keyboard_and_bounded_thumbnails()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1100, height: 720);
        try
        {
            string folder = Path.Combine(root, "files");
            Directory.CreateDirectory(Path.Combine(folder, "folder"));
            for (int i = 0; i < 8; i++) File.WriteAllBytes(Path.Combine(folder, $"picture-{i}.bmp"), Bmp((byte)(20 + i)));
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.ActiveTab!;
            tab.Refresh();
            for (int i = 0; i < 250 && (tab.Listing.State != ListingState.Complete || tab.Listing.FindStoreIndex("picture-7.bmp") < 0); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(item => ReferenceEquals(item.DataContext, panel));
            var list = view.FindControl<FileListControl>("List")!;
            tab.PictureView = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(list.PictureMode);
            Assert.True(list.VisibleRowCapacity > 1);
            Assert.False(list.HorizontalScrollBarVisible);
            var saved = tab.ToState();
            Assert.True(saved.PictureView);
            tab.PictureView = false;
            tab.ApplyState(saved);
            Dispatcher.UIThread.RunJobs();
            Assert.True(list.PictureMode);
            Assert.Contains(Enumerable.Range(0, tab.Listing.VisibleCount),
                index => tab.Listing.GetVisible(index).Name == "folder");
            Assert.Contains(Enumerable.Range(0, tab.Listing.VisibleCount),
                index => tab.Listing.GetVisible(index).Name == "a.txt");

            int row = tab.Listing.GetVisibleIndex(tab.Listing.FindStoreIndex("picture-0.bmp"));
            list.ScrollToTopRow(row);
            var tile = list.GetRowBounds(row);
            Assert.True(tile.Width < list.Bounds.Width);
            var point = list.TranslatePoint(tile.Center, window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(row, tab.Listing.FocusedIndex);
            Assert.True(list.Focus());
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.Equal(row + 1, tab.Listing.FocusedIndex);
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Assert.Equal(row, tab.Listing.FocusedIndex);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, tab.Listing.MarkedCount);

            window.Width = 650;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.False(list.HorizontalScrollBarVisible);
            Assert.NotNull(list.FocusedRowBounds());
            Assert.InRange(list.FocusedRowBounds()!.Value.Right, 0, list.Bounds.Width + 1);

            await RenderUntilThumbnailAsync(window, list);
            Assert.InRange(list.CachedPictureThumbnailCount, 1, 64);
            Assert.InRange(list.PendingPictureThumbnailCount, 0, list.VisibleRowCapacity + 8);
            tab.PictureView = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(list.PictureMode);
            Assert.Equal(0, list.CachedPictureThumbnailCount);
            Assert.Equal(row, tab.Listing.FocusedIndex);
            Assert.Equal(1, tab.Listing.MarkedCount);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task RenderUntilThumbnailAsync(Window window, FileListControl list)
    {
        // Visible thumbnail requests are posted by RenderPictureGrid. Layout and dispatcher jobs alone
        // do not guarantee the headless renderer draws a frame after a mode change or resize.
        var ct = TestContext.Current.CancellationToken;
        var renderDeadline = Stopwatch.StartNew();
        while (list.CachedPictureThumbnailCount == 0 && list.PendingPictureThumbnailCount == 0 && renderDeadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            window.UpdateLayout();
            window.CaptureRenderedFrame()?.Dispose();
            Dispatcher.UIThread.RunJobs();
            if (list.CachedPictureThumbnailCount == 0 && list.PendingPictureThumbnailCount == 0) await Task.Delay(20, ct);
        }

        // Once the renderer has requested a thumbnail, let the dispatcher publish its result while the
        // real provider and decoder finish. Include startup and publication in the decoder's time budget.
        var loadDeadline = Stopwatch.StartNew();
        var loadBudget = PictureDecoder.Timeout + TimeSpan.FromSeconds(10);
        while (list.CachedPictureThumbnailCount == 0 && list.PendingPictureThumbnailCount > 0 && loadDeadline.Elapsed < loadBudget)
            await Task.Delay(20, ct);
        Assert.True(list.CachedPictureThumbnailCount > 0,
            $"The rendered picture grid has no thumbnails. Started: {list.PictureThumbnailLoadCount}. Pending: {list.PendingPictureThumbnailCount}.");
        window.CaptureRenderedFrame()?.Dispose();
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
