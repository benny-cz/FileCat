using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class ChooserTests
{
    [AvaloniaFact]
    public async Task History_chooser_pins_with_insert_and_removes_with_ctrl_delete()
    {
        var host = new Grid();
        var window = new Window { Width = 1000, Height = 750, Content = host };
        window.Show();
        try
        {
            var dialogs = new OverlayDialogService(host, () => null);
            var task = dialogs.ChooseAsync(new ChoiceOptions("Folder history", [new ChoiceItem(@"C:\a"), new ChoiceItem(@"C:\b"), new ChoiceItem(@"C:\c") { Pinned = true }])
            {
                AllowDelete = true,
                AllowPin = true,
            });
            await Task.Delay(20, TestContext.Current.CancellationToken);
            var filter = host.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.True(filter.Focus());

            window.KeyPress(Key.Insert, RawInputModifiers.None, PhysicalKey.Insert, null); // pin C:\a
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == @"📌 C:\a");
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyPress(Key.Delete, RawInputModifiers.Control, PhysicalKey.Delete, null); // remove C:\b
            await Task.Delay(20, TestContext.Current.CancellationToken);
            window.KeyPress(Key.Insert, RawInputModifiers.None, PhysicalKey.Insert, null); // unpin C:\c (now selected)
            window.KeyPress(Key.Insert, RawInputModifiers.None, PhysicalKey.Insert, null); // and pin it again: no change
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            var r = await task;
            Assert.Equal(2, r.Index);
            Assert.Equal([1], r.Deleted);
            Assert.Equal([0], r.PinToggled);
            Assert.False(dialogs.IsOpen);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void Drives_in_the_location_menu_say_their_name_and_free_space_or_why_not()
    {
        const long gb = 1024L * 1024 * 1024;
        Assert.Equal("SYSTEM · 214 GB free", MainViewModel.DriveDetail(new(@"C:\", "SYSTEM", "Fixed", "NTFS", 214 * gb, 900 * gb, true)));
        // Linux and macOS name a volume by its mount point: the file system says more.
        Assert.Equal("ext4 · 214 GB free", MainViewModel.DriveDetail(new("/mnt/data", "/mnt/data", "Fixed", "ext4", 214 * gb, 900 * gb, true)));
        Assert.Equal("No disc", MainViewModel.DriveDetail(new(@"D:\", null, "CDRom", null, -1, -1, false)));
        Assert.Equal("Not responding", MainViewModel.DriveDetail(new(@"Z:\", null, "Not responding", null, -1, -1, false)));
    }

    /// <summary>Alt+F1: drives, This PC, phones, working sets, the Registry, and folders each show their icon.</summary>
    [AvaloniaFact]
    public async Task The_location_menu_shows_an_icon_for_every_place()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            vm.Execute(Core.Commands.CommandIds.LocationMenuLeft);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            List<ListBoxItem> Rows() => window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            for (int i = 0; i < 250 && Rows().Count == 0; i++) await Task.Delay(20, ct);
            var rows = Rows();
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.NotNull(row.GetVisualDescendants().OfType<Image>().FirstOrDefault()?.Source));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.False(dialogs.IsOpen);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
