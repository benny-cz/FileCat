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
}
