using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

/// <summary>Linux and macOS: the attributes dialog edits permissions in boxes and as octal (P9).</summary>
public sealed class PermissionsDialogTests
{
    [AvaloniaFact]
    public async Task Boxes_and_octal_agree_and_only_the_chosen_permissions_change()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("POSIX permissions are a Linux and macOS feature.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string file = Path.Combine(root, "files", "a.txt");
            File.SetUnixFileMode(file, (UnixFileMode)0x1A4); // rw-r--r--
            var other = File.GetUnixFileMode(Path.Combine(root, "files", "b.txt"));
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            listing.SetFocus(1);
            vm.Execute(CommandIds.Attributes);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            await Task.Delay(50, ct);
            CheckBox Box(string name) => window.GetVisualDescendants().OfType<CheckBox>().Single(b => AutomationProperties.GetName(b) == name);
            var octal = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Permissions as octal");

            Assert.Equal("644", octal.Text);
            Assert.True(Box("Group read").IsChecked);
            Assert.False(Box("Group write").IsChecked);
            Box("Group write").IsChecked = true;
            Assert.Equal("664", octal.Text);
            // Typing a mode sets every box (TextChanged is posted, so it arrives a moment later).
            octal.Text = "600";
            for (int i = 0; i < 100 && Box("Group read").IsChecked != false; i++) await Task.Delay(10, ct);
            Assert.False(Box("Group read").IsChecked);
            Assert.False(Box("Group write").IsChecked);
            Assert.True(Box("Owner write").IsChecked);
            Assert.False(window.GetVisualDescendants().OfType<CheckBox>().Single(b => b.Content as string == "Also apply to everything inside the marked folders (links are not followed)").IsEffectivelyVisible);

            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            for (int i = 0; i < 250 && !job.State.IsFinished(); i++) await Task.Delay(20, ct);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(file));
            Assert.Equal(other, File.GetUnixFileMode(Path.Combine(root, "files", "b.txt"))); // not marked, not changed
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [Fact]
    public void Linux_and_macOS_columns_show_permissions_where_Windows_shows_attributes()
    {
        var details = ColumnProfiles.Defaults[0].Columns;
        if (OperatingSystem.IsWindows())
            Assert.Contains(details, c => c.Field == ColumnField.Attributes);
        else
            Assert.Contains(details, c => c.MetadataId == "permissions");
    }
}
