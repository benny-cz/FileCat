using System.IO.Compression;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Platform.Windows;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>FCUX-075: the same admission used for drag feedback and the actual drop review.</summary>
public sealed class DropTargetAdmissionTests
{
    [AvaloniaFact]
    public void File_payload_must_convert_every_advertised_identity_to_an_absolute_path()
    {
        string first = Path.GetFullPath("first.txt"), second = Path.GetFullPath("second.txt");
        var (complete, refusal) = DropTargetAdmission.CompletePaths(2, [first, second]);
        Assert.Null(refusal);
        Assert.Equal([first, second], complete);
        foreach (string?[] incomplete in new[] { new string?[] { first }, [first, null], [first, "relative.txt"] })
        {
            (complete, refusal) = DropTargetAdmission.CompletePaths(2, incomplete);
            Assert.Null(complete);
            Assert.Contains("No items were accepted from the 2 dropped", refusal);
        }
    }

    [AvaloniaFact]
    public async Task Local_target_offers_exact_copy_or_move_while_nonfilesystem_targets_offer_no_effect()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[1];
            await Listed(panel.ActiveTab!.Listing);
            var (copy, _) = DropTargetAdmission.Check(panel, true, false);
            var (move, _) = DropTargetAdmission.Check(panel, true, true);
            Assert.Equal(DragDropEffects.Copy, copy!.Effect);
            Assert.Equal(DragDropEffects.Move, move!.Effect);
            Assert.True(DropTargetAdmission.SameTarget(copy, move));
            Assert.Null(DropTargetAdmission.Check(panel, false, false).Accepted);

            string zipPath = Path.Combine(root, "drop-target.zip");
            using (ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }
            services.Providers.Register(new WindowsRegistryProvider());
            var set = services.ResultSets.Create("Drop target", "Working set");
            set.IsComplete = true;
            foreach (var location in new[] { ZipProvider.ForFile(zipPath), new Location(Schemes.Registry, ""),
                         ResultSetProvider.LocationOf(set) })
            {
                panel.ActiveTab.Navigate(location);
                var (offer, refusal) = DropTargetAdmission.Check(panel, true, false);
                Assert.Null(offer);
                Assert.Contains("local folder", refusal);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Failed_or_loading_local_listing_advertises_no_drop_effect()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[1];
            string missing = Path.Combine(root, "missing-folder");
            panel.ActiveTab!.Navigate(Location.FileSystem(missing));
            Assert.Null(DropTargetAdmission.Check(panel, true, false).Accepted);
            await Until(() => panel.ActiveTab!.Listing.State == ListingState.Failed);
            var (offer, refusal) = DropTargetAdmission.Check(panel, true, false);
            Assert.Null(offer);
            Assert.Contains("not available", refusal);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Changed_or_unavailable_target_refuses_without_activating_panel_or_opening_review()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var sourcePanel = vm.Workspace.Panels[0];
            var targetPanel = vm.Workspace.Panels[1];
            await Listed(targetPanel.ActiveTab!.Listing);
            var (offered, _) = DropTargetAdmission.Check(targetPanel, true, false);
            Assert.NotNull(offered);
            vm.Workspace.Activate(sourcePanel);
            string source = Path.Combine(root, "files", "a.txt");

            string otherFolder = Directory.CreateDirectory(Path.Combine(root, "other-target")).FullName;
            targetPanel.ActiveTab.Navigate(Location.FileSystem(otherFolder));
            await Listed(targetPanel.ActiveTab.Listing);
            var (currentLocal, _) = DropTargetAdmission.Check(targetPanel, true, false);
            Assert.NotNull(currentLocal);
            Assert.False(DropTargetAdmission.SameTarget(offered, currentLocal));
            await vm.DropFilesAsync(targetPanel, offered, [source]);
            Assert.Same(sourcePanel, vm.Workspace.ActivePanel);
            Assert.Empty(services.Jobs.Jobs);
            Assert.Contains("changed during the drag", vm.Notification);

            targetPanel.ActiveTab.Navigate(new Location(Schemes.ResultSet, "working-set"));
            Assert.Null(DropTargetAdmission.Check(targetPanel, true, false).Accepted);
            await vm.DropFilesAsync(targetPanel, offered, [source]);
            Assert.Same(sourcePanel, vm.Workspace.ActivePanel);
            Assert.False(((OverlayDialogService)vm.Dialogs).IsOpen);
            Assert.Empty(services.Jobs.Jobs);
            Assert.Contains("local folder", vm.Notification);

            string vanished = Directory.CreateDirectory(Path.Combine(root, "vanished")).FullName;
            targetPanel.ActiveTab.Navigate(Location.FileSystem(vanished));
            await Listed(targetPanel.ActiveTab.Listing);
            (offered, _) = DropTargetAdmission.Check(targetPanel, true, false);
            Assert.NotNull(offered);
            Directory.Delete(vanished);
            await vm.DropFilesAsync(targetPanel, offered, [source]);
            Assert.Same(sourcePanel, vm.Workspace.ActivePanel);
            Assert.False(((OverlayDialogService)vm.Dialogs).IsOpen);
            Assert.Empty(services.Jobs.Jobs);
            Assert.Contains("no longer available", vm.Notification);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Valid_drop_reviews_captured_folder_and_commits_there_after_panel_navigates()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string source = Path.Combine(root, "files", "a.txt");
            string destination = Directory.CreateDirectory(Path.Combine(root, "drop-A")).FullName;
            string later = Directory.CreateDirectory(Path.Combine(root, "drop-B")).FullName;
            var panel = vm.Workspace.Panels[1];
            panel.ActiveTab!.Navigate(Location.FileSystem(destination));
            await Listed(panel.ActiveTab!.Listing);
            var (offered, _) = DropTargetAdmission.Check(panel, true, false);
            Assert.NotNull(offered);
            var pending = vm.DropFilesAsync(panel, offered, [source]);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen);
            window.UpdateLayout();
            var destBox = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Destination");
            Assert.Equal(destination + Path.DirectorySeparatorChar, destBox.Text);
            Assert.Empty(services.Jobs.Jobs); // review happens before a job is submitted
            // Destination validation is asynchronous. Under a busy full-suite host, Enter before it finishes
            // correctly leaves the dialog open; wait for the reviewed action to become available.
            await Until(() => window.GetVisualDescendants().OfType<Button>()
                .Any(button => button.Content as string == "Copy" && button.IsDefault && button.IsEnabled));
            panel.ActiveTab.Navigate(Location.FileSystem(later));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await Until(() => services.Jobs.Jobs.Count > 0 && services.Jobs.Jobs.All(job => job.State.IsFinished()));
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(destination, "a.txt")));
            Assert.False(File.Exists(Path.Combine(later, "a.txt")));
            Assert.Equal("alpha", File.ReadAllText(source));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static Task Listed(ListingModel listing) => Until(() => listing.State == ListingState.Complete);

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
