using System.IO.Compression;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.Core.Commands;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>Find deleted files (P10): a disk image's deleted items in an ordinary panel, read-only.</summary>
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
            vm.Execute(CommandIds.FindDeleted);
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
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
