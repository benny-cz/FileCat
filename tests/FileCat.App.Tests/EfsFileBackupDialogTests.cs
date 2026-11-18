using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class EfsFileBackupDialogTests
{
    [AvaloniaFact]
    public void Efs_commands_are_hidden_when_the_platform_has_no_qualified_adapter()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            foreach (string id in new[] { CommandIds.EfsProtection, CommandIds.EfsFileBackup })
            {
                Assert.False(vm.GetAvailability(id).Enabled);
                Assert.DoesNotContain(vm.CommandSearchEntries(), item => item.Id == id);
            }
            var file = window.MainMenu.Items.OfType<MenuItem>().Single(menu => menu.Header as string == "_File");
            var inspect = file.Items.OfType<MenuItem>().Single(menu => menu.Header as string == "_Inspect");
            Assert.Contains(inspect.Items.OfType<MenuItem>(), menu => menu.Tag as string == CommandIds.StorageAttributes);
            Assert.DoesNotContain(inspect.Items.OfType<MenuItem>(), menu => menu.Tag as string is CommandIds.EfsFileBackup or CommandIds.EfsProtection);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Key_selection_reports_only_the_files_that_matched_that_export_and_requires_an_owned_wizard(bool hasOwner)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        string first = new('A', 40), second = new('B', 40), unavailable = new('C', 40);
        const string parent = @"C:\fixtures";
        var identities = new[] { new StorageObjectIdentity(1, "one"), new StorageObjectIdentity(1, "two") };
        StorageTarget Target(string name) => new(new(Location.FileSystem(parent), name, EntryKind.File), parent + "\\" + name);
        var review = new StorageEfsFileBackupReview(null!, "S-1-5-21-1-2-3-1000",
            [new(Target("one.bin"), identities[0], [first], null), new(Target("two.bin"), identities[1], [second, unavailable], null)],
            [new(first, "First", StorageEfsKeyState.MatchedAvailable, null), new(second, "Second", StorageEfsKeyState.MatchedAvailable, null),
             new(unavailable, "Unavailable", StorageEfsKeyState.InteractionRequired,
                 new(StorageReasonKind.PermissionOrPolicy, "needs-interaction", "This key requires provider interaction.", true))]);
        var outcomes = new Dictionary<string, StorageEfsFileBackupResult> { [first] = new(true, false, [0], null) };
        var shown = EfsFileBackupDialog.ChooseKeyAsync(vm.Dialogs, review, [false, true], outcomes, first, hasOwner);
        try
        {
            Dispatcher.UIThread.RunJobs();
            var controls = window.GetVisualDescendants().ToArray();
            var picker = Assert.Single(controls.OfType<ComboBox>(), control => AutomationProperties.GetName(control) == "Selected file EFS certificate");
            var rows = Assert.Single(controls.OfType<TextBox>(), control => AutomationProperties.GetName(control) == "Selected items, original locations and EFS certificate associations");
            var status = Assert.Single(controls.OfType<TextBlock>(), control => AutomationProperties.GetAutomationId(control) == "EFS key status");
            var backup = Assert.Single(controls.OfType<Button>(), button => button.Content as string == "Back up selected key…");
            var done = Assert.Single(controls.OfType<Button>(), button => button.Content as string == "Done");
            Assert.True(done.IsDefault);
            Assert.Equal(hasOwner, backup.IsEnabled);
            Assert.Contains("Matched the exported key when checked", rows.Text);
            Assert.Contains("Uses a different key", rows.Text);
            Assert.Contains("two.bin", rows.Text);
            Assert.Contains("Hidden mark", rows.Text);
            Assert.Contains("1 of the 2 selected items still matched", status.Text);

            picker.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            Assert.False(backup.IsEnabled);
            Assert.Equal("This key requires provider interaction.", status.Text);
            picker.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(hasOwner, backup.IsEnabled);
            (hasOwner ? backup : done).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(hasOwner ? second : null, await shown.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await shown;
            AccessibilityTests.Close(services, window, root);
        }
    }
}
