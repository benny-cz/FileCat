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
    public async Task FileCats_own_shift_drag_is_a_move_but_not_of_copies_made_to_drag()
    {
        // A Low of the review of d1757bca: FileCat's own Shift-drag was refused with a message blaming another app. Its
        // source deletes nothing on the drop's result, so the drop is a Move job, reviewed as F6's is; copies made to drag
        // items out of an archive, a server or a device are not moved, which would leave the originals.
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[1];
            await Listed(panel.ActiveTab!.Listing);
            var (move, refusal) = DropTargetAdmission.Check(panel, true, move: true, ownDrag: true);
            Assert.Null(refusal);
            Assert.Equal(DragDropEffects.Move, move!.Effect);
            Assert.Equal(DragDropEffects.Copy, DropTargetAdmission.Check(panel, true, move: false, ownDrag: true).Accepted!.Effect);

            string staged = Path.Combine(services.Paths.TempDirectory, "drag", "owned-" + Guid.NewGuid().ToString("N"), "member.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.WriteAllText(staged, "copied out to be dragged");
            await vm.DropFilesAsync(panel, move, [staged]);
            Assert.Equal(DropTargetAdmission.StagedMoveRefusal, vm.Notification);
            Assert.True(File.Exists(staged));
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Local_target_offers_copy_but_refuses_native_shift_move_before_review()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[1];
            await Listed(panel.ActiveTab!.Listing);
            var (copy, _) = DropTargetAdmission.Check(panel, true, false);
            var (move, moveRefusal) = DropTargetAdmission.Check(panel, true, true);
            Assert.Equal(DragDropEffects.Copy, copy!.Effect);
            Assert.Null(move);
            Assert.Equal(DropTargetAdmission.ExternalMoveRefusal, moveRefusal);
            var drag = new DropTargetAdmission.DragIntent();
            Assert.Null(drag.Hover(move, moveRefusal).Accepted);
            Assert.True(drag.MoveRefused);
            // A source may release Shift after mouse-up, before Drop reads the modifiers.
            // Only a later actual Copy hover clears the refused Move intent.
            Assert.Equal(copy, drag.Hover(copy, null).Accepted);
            Assert.False(drag.MoveRefused);
            drag.Hover(move, moveRefusal);
            drag.Reset();
            Assert.False(drag.MoveRefused);
            Assert.Empty(services.Jobs.Jobs);
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
    public async Task Native_drag_intent_retains_first_folder_when_panel_navigates_during_hover()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[1];
            await Listed(panel.ActiveTab!.Listing);
            var drag = new DropTargetAdmission.DragIntent();
            var (first, _) = DropTargetAdmission.Check(panel, true, false);
            Assert.NotNull(first);
            Assert.Equal(first, drag.Hover(first, null).Accepted);

            // A transient unavailable state must not erase the original offer.
            Assert.Null(drag.Hover(null, "Folder unavailable").Accepted);
            Assert.Same(first, drag.Original);

            string parent = Directory.CreateDirectory(Path.Combine(root, "replacement-target")).FullName;
            panel.ActiveTab.Navigate(Location.FileSystem(parent));
            await Listed(panel.ActiveTab.Listing);
            var (changed, _) = DropTargetAdmission.Check(panel, true, false);
            Assert.NotNull(changed);
            var refused = drag.Hover(changed, null);
            Assert.Null(refused.Accepted);
            Assert.Equal(DropTargetAdmission.ChangedTargetMessage, refused.Refusal);
            Assert.Same(first, drag.Original);
            Assert.True(drag.TargetChanged);
            Assert.Null(drag.Hover(first, null).Accepted); // cannot silently restore the old offer

            drag.Reset(); // a later, distinct native drag may use the new folder
            Assert.Equal(changed, drag.Hover(changed, null).Accepted);
            Assert.False(drag.TargetChanged);
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
