using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Search;

namespace FileCat.App.Tests;

public sealed class WorkingSetUiTests
{
    private static async Task Until(Func<bool> condition, string what)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), what);
    }

    private static async Task ConfirmDialog(MainWindow window, OverlayDialogService dialogs)
    {
        await Until(() => dialogs.IsOpen, "a dialog opens");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Until(() => !dialogs.IsOpen, "the dialog closes");
        await Task.Delay(100, TestContext.Current.CancellationToken); // focus returns to the panel asynchronously
    }

    [AvaloniaFact]
    public async Task F5_toward_a_working_set_adds_references_and_deleting_the_set_keeps_the_files()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var dialogs = (OverlayDialogService)vm.Dialogs;
        try
        {
            var left = vm.Workspace.Panels[0];
            var right = vm.Workspace.Panels[1];
            var folder = Path.Combine(root, "files");
            var files = left.ActiveTab!.Listing;
            await Until(() => files.State == ListingState.Complete && files.VisibleCount == 3, "the folder lists");

            var set = services.WorkingSets.Create("Collected");
            right.ActiveTab!.Navigate(ResultSetProvider.LocationOf(set));
            vm.Workspace.Activate(left);
            files.SetMark(1, true);
            files.SetMark(2, true);
            vm.Execute(CommandIds.Copy);
            await ConfirmDialog(window, dialogs);
            Assert.Equal(2, set.Count);
            Assert.Equal(["a.txt", "b.txt"], Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => f.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).Order());

            // Inside the set: Remove from set forgets a reference and keeps the file.
            var members = right.ActiveTab.Listing;
            await Until(() => members.State == ListingState.Complete && members.VisibleCount == 3, "the set lists its members");
            Assert.Contains("Working set", right.ActiveTab.Banner);
            vm.Workspace.Activate(right);
            members.SetFocus(1);
            vm.Execute(CommandIds.RemoveFromSet);
            await Until(() => set.Count == 1, "one reference is removed");
            Assert.True(File.Exists(Path.Combine(folder, "a.txt")) && File.Exists(Path.Combine(folder, "b.txt")));

            // Deleting through the set deletes the original and drops its reference.
            await Until(() => members.State == ListingState.Complete && members.VisibleCount == 2, "the set relists");
            members.SetFocus(1);
            string remaining = members.GetItemRef(members.FocusedStoreIndex).Name;
            vm.Execute(CommandIds.DeletePermanent);
            await ConfirmDialog(window, dialogs);
            await Until(() => !File.Exists(Path.Combine(folder, remaining)), "the original is deleted");
            await Until(() => set.Count == 0, "the deleted item leaves the set");

            // The list of sets: F8 forgets the set itself.
            right.ActiveTab.Navigate(ResultSetProvider.WorkingSetList);
            var list = right.ActiveTab.Listing;
            await Until(() => list.State == ListingState.Complete && list.VisibleCount == 1, "the list shows the set");
            vm.Workspace.Activate(right);
            list.SetFocus(0);
            vm.Execute(CommandIds.Delete);
            await ConfirmDialog(window, dialogs);
            Assert.Empty(services.WorkingSets.All);
            Assert.True(File.Exists(Path.Combine(folder, remaining == "a.txt" ? "b.txt" : "a.txt")));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
