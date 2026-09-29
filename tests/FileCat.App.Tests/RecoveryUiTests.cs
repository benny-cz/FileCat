using System.IO.Compression;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.App.Controls;
using FileCat.Core.Commands;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>Recover deleted files (P10): a disk image's deleted items in an ordinary panel, read-only, in a tab of their own.</summary>
public sealed class RecoveryUiTests
{
    [AvaloniaFact]
    public async Task A_disk_image_opens_as_its_deleted_items_with_state_and_evidence()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files");
            string image = Path.Combine(folder, "stick.img");
            var fixture = Path.Combine(AppContext.BaseDirectory, "TestData", "fat16.img.gz");
            using (var input = new GZipStream(File.OpenRead(fixture), CompressionMode.Decompress))
            using (var output = File.Create(image))
                await input.CopyToAsync(output, ct);
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            int Index(string name)
            {
                for (int i = 0; i < listing.VisibleCount; i++)
                    if (listing.GetVisible(i).Name == name) return i;
                return -1;
            }
            for (int i = 0; i < 250 && Index("stick.img") < 0; i++) await Task.Delay(20, ct);
            listing.SetFocus(Index("stick.img"));
            // The context menu offers the image's deleted files directly.
            Assert.Equal("Recover deleted files from this disk image…", vm.RecoveryOfferForFocus());
            listing.SetFocus(Index("a.txt"));
            Assert.Null(vm.RecoveryOfferForFocus());
            listing.SetFocus(Index("stick.img"));

            // The command asks what to scan, with the image under the cursor chosen; Enter scans it in a new tab, and the
            // folder's tab stays where it was.
            var dialogs = (Views.OverlayDialogService)vm.Dialogs;
            vm.Execute(CommandIds.FindDeleted);
            for (int i = 0; i < 750 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            await Task.Delay(50, ct);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 250 && ReferenceEquals(vm.ActiveTab, tab); i++) await Task.Delay(20, ct);
            Assert.Equal(folder, tab.Location!.Path);
            tab = vm.ActiveTab!;
            listing = tab.Listing;
            for (int i = 0; i < 250 && !(tab.Location?.Scheme == Schemes.Recovery && listing.State == Core.Listing.ListingState.Complete && Index("photos") >= 0); i++)
                await Task.Delay(20, ct);
            Assert.Equal(Schemes.Recovery, tab.Location!.Scheme);
            Assert.Equal("1", tab.Location.Session); // one volume: straight to its deleted items
            Assert.True(Index("docs") >= 0 && Index("frag-a.bin") >= 0 && Index("old") >= 0);
            var frag = listing.GetVisible(Index("frag-a.bin"));
            Assert.Equal("Partly lost", ((IDisplayDetails)frag.Tag!).KindText);
            Assert.Same(ColumnProfiles.Recovery, services.Columns.Get(0, Schemes.Recovery));

            // Nothing here can be changed: the panel explains instead.
            listing.SetFocus(Index("frag-a.bin"));
            vm.Execute(CommandIds.DeletePermanent);
            await Task.Delay(50, ct);
            Assert.False(((Views.OverlayDialogService)vm.Dialogs).IsOpen);
            Assert.Contains("only reads", vm.Notification ?? "", StringComparison.Ordinal);
            Assert.True(File.Exists(image));
            // FileCat's own item menu there: every item with its icon, and nothing that would change the source.
            var menuItems = Views.ContextMenuFactory.Build(vm).Items.OfType<Avalonia.Controls.MenuItem>().ToList();
            Assert.NotEmpty(menuItems);
            Assert.All(menuItems, item => Assert.NotNull(item.Icon));
            foreach (var id in new[] { CommandIds.CutToClipboard, CommandIds.Edit, CommandIds.Move, CommandIds.Delete })
                Assert.DoesNotContain(menuItems, item => item.Header as string == services.Commands.Get(id)!.Title);

            // Recover deleted files again, here: the volume's free space is offered first, and searched after a confirmation.
            var location = tab.Location;
            vm.Execute(CommandIds.FindDeleted);
            for (int i = 0; i < 750 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            await Task.Delay(50, ct);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Avalonia.Controls.Button? search = null;
            for (int i = 0; i < 250 && search is null; i++)
            {
                search = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Button>()
                    .FirstOrDefault(b => b.Content as string == "Search" && b.IsEffectivelyVisible);
                if (search is null) await Task.Delay(20, ct);
            }
            Assert.NotNull(search);
            search.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            for (int i = 0; i < 500 && services.Recovery.DescribeFreeSpaceSearch(location) is not { Searched: true }; i++) await Task.Delay(20, ct);
            Assert.True(services.Recovery.DescribeFreeSpaceSearch(location)!.Searched);
            for (int i = 0; i < 250 && (listing.State != Core.Listing.ListingState.Complete || listing.IsRefreshing); i++) await Task.Delay(20, ct);
            Assert.True(Index("frag-a.bin") >= 0);
            // Searched once: the command still opens (to scan something else), without that choice.
            vm.Execute(CommandIds.FindDeleted);
            for (int i = 0; i < 750 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            await Task.Delay(50, ct);
            Assert.DoesNotContain(Texts(window), s => s.StartsWith("Search this volume's free space", StringComparison.Ordinal));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    private static List<string> Texts(Avalonia.Controls.Window window) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Text is not null).Select(t => t.Text!).ToList();

    [AvaloniaFact]
    public async Task A_disk_whose_table_was_erased_shows_its_lost_partition_and_searches_the_rest()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files");
            // A 32 MiB disk whose partition table is empty: the FAT16 volume that was its partition is still at 1 MiB.
            var volume = new MemoryStream();
            using (var input = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "fat16.img.gz")), CompressionMode.Decompress))
                await input.CopyToAsync(volume, ct);
            var disk = new byte[32 * 1024 * 1024];
            disk[510] = 0x55;
            disk[511] = 0xAA;
            volume.ToArray().CopyTo(disk, 1024 * 1024);
            string image = Path.Combine(folder, "disk.img");
            await File.WriteAllBytesAsync(image, disk, ct);
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            int Index(string name)
            {
                for (int i = 0; i < listing.VisibleCount; i++)
                    if (listing.GetVisible(i).Name == name) return i;
                return -1;
            }
            for (int i = 0; i < 250 && Index("disk.img") < 0; i++) await Task.Delay(20, ct);
            listing.SetFocus(Index("disk.img"));
            var dialogs = (Views.OverlayDialogService)vm.Dialogs;
            vm.Execute(CommandIds.FindDeleted);
            for (int i = 0; i < 750 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            await Task.Delay(50, ct);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 250 && ReferenceEquals(vm.ActiveTab, tab); i++) await Task.Delay(20, ct);
            tab = vm.ActiveTab!;
            listing = tab.Listing;
            // The lost partition's whole file system: existing files too, recoverable.
            for (int i = 0; i < 250 && !(tab.Location?.Scheme == Schemes.Recovery && listing.State == Core.Listing.ListingState.Complete && Index("keep.txt") >= 0); i++)
                await Task.Delay(20, ct);
            Assert.Equal("1", tab.Location!.Session);
            Assert.Equal("Recoverable", ((IDisplayDetails)listing.GetVisible(Index("keep.txt")).Tag!).KindText);
            Assert.True(Index("docs") >= 0 && Index("photos") >= 0);

            // Recover deleted files, here: the disk's space in no partition can be searched too (after this volume's free
            // space, which comes first inside a FAT volume).
            vm.Execute(CommandIds.FindDeleted);
            for (int i = 0; i < 750 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            await Task.Delay(50, ct);
            Assert.Contains(Texts(window), s => s == "Search this disk for deleted partitions");
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Avalonia.Controls.Button? search = null;
            for (int i = 0; i < 250 && search is null; i++)
            {
                search = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Button>()
                    .FirstOrDefault(b => b.Content as string == "Search" && b.IsEffectivelyVisible);
                if (search is null) await Task.Delay(20, ct);
            }
            Assert.NotNull(search);
            search.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            // The search runs where the disk's partitions are listed, and says what it read.
            for (int i = 0; i < 500 && services.Recovery.DescribeDiskSearch(tab.Location!) is not { Searched: true }; i++) await Task.Delay(20, ct);
            Assert.Null(tab.Location!.Session);
            Assert.True(services.Recovery.DescribeDiskSearch(tab.Location)!.Searched);
            for (int i = 0; i < 250 && (listing.State != Core.Listing.ListingState.Complete || listing.IsRefreshing); i++) await Task.Delay(20, ct);
            var partition = listing.GetVisible(Index("Volume 1"));
            Assert.StartsWith("Lost partition at 1 MiB", ((IDisplayDetails)partition.Tag!).DetailsText);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
