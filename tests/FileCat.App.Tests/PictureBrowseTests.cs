using System.Buffers.Binary;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class PictureBrowseTests
{
    [AvaloniaFact]
    public async Task Damaged_next_picture_keeps_the_last_good_image_and_can_be_skipped()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        ViewerWindow? viewer = null;
        try
        {
            string folder = Path.Join(root, "files");
            string first = Path.Join(folder, "a.bmp"), last = Path.Join(folder, "c.bmp");
            File.WriteAllBytes(first, Bmp(20));
            File.WriteAllBytes(Path.Join(folder, "b.bmp"), "not a bitmap"u8.ToArray());
            File.WriteAllBytes(last, Bmp(80));
            var tab = vm.Workspace.ActiveTab!;
            tab.Navigate(Location.FileSystem(folder));
            for (int i = 0; i < 250 && (tab.Listing.State != ListingState.Complete || tab.Listing.FindStoreIndex("c.bmp") < 0); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            var item = tab.Listing.GetItemRef(tab.Listing.FindStoreIndex("a.bmp"));
            viewer = new ViewerWindow(services, new FileContentSource(first), first, hex: false)
            {
                BrowseContext = new PictureBrowseContext(tab, item),
            };
            viewer.Show();
            for (int i = 0; i < 250 && viewer.PictureLoad is null; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            await viewer.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(40), TestContext.Current.CancellationToken);
            for (int i = 0; i < 250 && !viewer.ReadyForRefresh; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(viewer.ReadyForRefresh);
            Assert.NotNull(viewer.Picture);
            viewer.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            for (int i = 0; i < 2000 && viewer.PictureRefreshBusy; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(first, viewer.DisplayName);
            Assert.NotNull(viewer.Picture);
            Assert.Contains("Could not show", viewer.StatusLine);
            viewer.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            for (int i = 0; i < 250 && viewer.DisplayName != last; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(last, viewer.DisplayName);
            Assert.Single(ViewerWindow.OpenWindows);
        }
        finally
        {
            viewer?.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F3_from_a_panel_keeps_folder_browsing_in_one_window(bool pictureGrid)
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        ViewerWindow? viewer = null;
        try
        {
            string folder = Path.Join(root, "files");
            File.WriteAllBytes(Path.Join(folder, "a.bmp"), Bmp(20));
            File.WriteAllBytes(Path.Join(folder, "b.bmp"), Bmp(80));
            var tab = vm.Workspace.ActiveTab!;
            tab.PictureView = pictureGrid;
            tab.Navigate(Location.FileSystem(folder));
            for (int i = 0; i < 250 && (tab.Listing.State != ListingState.Complete || tab.Listing.FindStoreIndex("b.bmp") < 0); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            tab.Listing.SetFocus(tab.Listing.GetVisibleIndex(tab.Listing.FindStoreIndex("a.bmp")));
            var list = main.GetVisualDescendants().OfType<FileListControl>()
                .First(control => ReferenceEquals(control.Tab, tab));
            list.Focus();
            main.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
            for (int i = 0; i < 250 && ViewerWindow.OpenWindows.Count == 0; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            viewer = Assert.Single(ViewerWindow.OpenWindows);
            for (int i = 0; i < 250 && (!viewer.ReadyForRefresh || viewer.Picture is null); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(viewer.ReadyForRefresh);
            Assert.NotNull(viewer.Picture);
            Assert.Contains("Space next", viewer.StatusLine);
            viewer.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            for (int i = 0; i < 250 && !viewer.DisplayName.EndsWith("b.bmp", StringComparison.Ordinal); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.EndsWith("b.bmp", viewer.DisplayName, StringComparison.Ordinal);
            Assert.Single(ViewerWindow.OpenWindows);
        }
        finally
        {
            viewer?.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task Space_and_Backspace_browse_pictures_in_the_same_viewer_and_stop_at_the_ends()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        ViewerWindow? viewer = null;
        try
        {
            var folder = Path.Join(root, "files");
            var first = Path.Join(folder, "a.bmp");
            var second = Path.Join(folder, "c.bmp");
            File.WriteAllBytes(first, Bmp(20));
            File.WriteAllText(Path.Join(folder, "b.txt"), "not a picture");
            File.WriteAllBytes(second, Bmp(80));
            var tab = vm.Workspace.ActiveTab!;
            tab.Navigate(Location.FileSystem(folder));
            for (int i = 0; i < 250 && (tab.Listing.State != ListingState.Complete || tab.Listing.FindStoreIndex("c.bmp") < 0); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(tab.Listing.FindStoreIndex("c.bmp") >= 0);

            var item = tab.Listing.GetItemRef(tab.Listing.FindStoreIndex("a.bmp"));
            viewer = new ViewerWindow(services, new FileContentSource(first), first, hex: false)
            {
                BrowseContext = new PictureBrowseContext(tab, item),
            };
            viewer.Show();
            for (int i = 0; i < 250 && viewer.PictureLoad is null; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.NotNull(viewer.PictureLoad);
            await viewer.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(40), TestContext.Current.CancellationToken);
            Assert.NotNull(viewer.Picture);

            viewer.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            for (int i = 0; i < 250 && viewer.DisplayName != second; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(second, viewer.DisplayName);
            Assert.Equal("c.bmp — FileCat Viewer", viewer.Title);
            Assert.NotNull(viewer.Picture);
            Assert.Single(ViewerWindow.OpenWindows, w => ReferenceEquals(w, viewer));

            viewer.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.Equal(second, viewer.DisplayName);
            Assert.Contains("Last supported picture", viewer.StatusLine);

            viewer.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            for (int i = 0; i < 250 && viewer.DisplayName != first; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(first, viewer.DisplayName);
            viewer.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.Equal(first, viewer.DisplayName);
            Assert.Contains("First supported picture", viewer.StatusLine);
            tab.Navigate(Location.FileSystem(root));
            viewer.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.Equal(first, viewer.DisplayName);
            Assert.Contains("source panel left this folder", viewer.StatusLine);
        }
        finally
        {
            viewer?.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    private static byte[] Bmp(byte blue)
    {
        var bytes = new byte[54 + 4];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        foreach (var (at, value) in new[] { (2, bytes.Length), (10, 54), (14, 40), (18, 1), (22, -1) })
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 32);
        bytes[54] = blue; bytes[55] = 0; bytes[56] = 0; bytes[57] = 255;
        return bytes;
    }
}
