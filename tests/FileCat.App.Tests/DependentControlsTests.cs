using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class DependentControlsTests
{
    [AvaloniaFact]
    public void Dependent_options_follow_parent_without_erasing_their_draft_choice()
    {
        var parent = new CheckBox { Content = "Parent", IsChecked = false };
        var child = new CheckBox { Content = "Child", IsChecked = true };
        SettingsDialog.BindDependentOption(parent, child, true, "Enable Parent first.");
        Assert.False(child.IsEnabled);
        Assert.True(child.IsChecked);
        Assert.Equal(20, child.Margin.Left);
        Assert.True(ToolTip.GetShowOnDisabled(child));
        Assert.Equal("Enable Parent first.", AutomationProperties.GetHelpText(child));

        parent.IsChecked = true;
        Assert.True(child.IsEnabled);
        Assert.True(child.IsChecked);
        child.IsChecked = false;
        parent.IsChecked = false;
        parent.IsChecked = true;
        Assert.False(child.IsChecked);
        Assert.True(child.IsEnabled);

        var unsupported = new CheckBox { Content = "Unavailable child", IsChecked = true };
        SettingsDialog.BindDependentOption(parent, unsupported, false, "Enable Parent first.", "Helper unavailable.");
        Assert.False(unsupported.IsEnabled);
        Assert.True(unsupported.IsChecked);
        Assert.Equal("Helper unavailable.", ToolTip.GetTip(unsupported));
    }

    [AvaloniaFact]
    public async Task Behavior_page_disables_its_child_immediately_and_Cancel_discards_the_draft()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.SizeFolderOnSpace = false;
            services.Settings.SizeFolderOnSlowLocations = true;
            var showing = SettingsDialog.ShowAsync(vm);
            Dispatcher.UIThread.RunJobs();
            var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList().FindIndex(t => t.Header as string == "Behavior");
            window.UpdateLayout();
            var parent = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => c.Content as string == "Space computes the size of a marked folder");
            var child = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => c.Content as string == "…also on network, removable, and cloud locations");
            Assert.False(child.IsEnabled);
            Assert.True(child.IsChecked);
            Assert.Contains("Turn on folder sizing", AutomationProperties.GetHelpText(child));
            parent.IsChecked = true;
            Assert.True(child.IsEnabled);
            Assert.True(child.IsChecked);
            parent.IsChecked = false;
            Assert.False(child.IsEnabled);
            Assert.True(child.IsChecked);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(services.Settings.SizeFolderOnSpace);
            Assert.True(services.Settings.SizeFolderOnSlowLocations);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Column_editor_buttons_follow_profile_column_and_choice_state_without_applying_the_draft()
    {
        var set = new ColumnProfileSet(null);
        int savedCount = set.Count;
        var editor = new ColumnProfilesEditor(set);
        var window = new Window { Width = 900, Height = 700, Content = editor.View };
        window.Show();
        try
        {
            Button Action(string name) => editor.View.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetName(b) == name);
            var profileList = editor.View.GetVisualDescendants().OfType<ListBox>()
                .Single(l => AutomationProperties.GetName(l) == "Column profiles");
            var columnList = editor.View.GetVisualDescendants().OfType<ListBox>()
                .Single(l => AutomationProperties.GetName(l) == "Columns of the selected profile");
            var choice = editor.View.GetVisualDescendants().OfType<ComboBox>()
                .Single(c => AutomationProperties.GetName(c) == "Column to add");
            var firstUp = Action("Up profile");
            Assert.False(firstUp.IsEnabled);
            Assert.Contains("first profile", AutomationProperties.GetHelpText(firstUp));
            Assert.True(ToolTip.GetShowOnDisabled(firstUp));
            Assert.True(Action("Down profile").IsEnabled);

            profileList.SelectedIndex = set.Count - 1;
            Assert.False(Action("Down profile").IsEnabled);
            Assert.True(Action("Up profile").IsEnabled);
            for (int count = set.Count; count < ColumnProfileSet.MaxProfiles; count++)
                Action("New profile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(Action("New profile").IsEnabled);
            Assert.False(Action("Duplicate profile").IsEnabled);
            Assert.Contains("Maximum profiles", AutomationProperties.GetHelpText(Action("New profile")));
            Action("Remove profile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Action("New profile").IsEnabled);

            profileList.SelectedIndex = 0;
            columnList.SelectedIndex = 0;
            Assert.False(Action("Remove column").IsEnabled);
            Assert.Contains("Name column is required", AutomationProperties.GetHelpText(Action("Remove column")));
            Assert.False(Action("Up column").IsEnabled);
            Assert.True(Action("Down column").IsEnabled);
            choice.SelectedIndex = 0; // Name is already present.
            Assert.False(Action("Add column").IsEnabled);
            Assert.Contains("already", AutomationProperties.GetHelpText(Action("Add column")));
            choice.SelectedIndex = ColumnProfileSet.Choices.ToList().FindIndex(c => c.Key == "meta:dimensions");
            Assert.True(Action("Add column").IsEnabled);
            Action("Add column").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(Action("Add column").IsEnabled);
            Assert.False(Action("Down column").IsEnabled);
            Assert.True(Action("Up column").IsEnabled);
            Assert.True(Action("Remove column").IsEnabled);
            Action("Remove column").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Action("Add column").IsEnabled);
            Assert.Equal(savedCount, set.Count); // Discarding this editor never mutates saved profiles.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_only_profile_cannot_be_removed_until_another_is_added()
    {
        var set = new ColumnProfileSet(null);
        set.Replace([("Only", [new ColumnSpec(ColumnField.Name, "Name", 200, Star: true)])]);
        var editor = new ColumnProfilesEditor(set);
        var window = new Window { Width = 700, Height = 500, Content = editor.View };
        window.Show();
        try
        {
            Button Action(string name) => editor.View.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetName(b) == name);
            Assert.False(Action("Remove profile").IsEnabled);
            Assert.Contains("At least one profile", AutomationProperties.GetHelpText(Action("Remove profile")));
            Assert.False(Action("Up profile").IsEnabled);
            Assert.False(Action("Down profile").IsEnabled);
            Action("New profile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Action("Remove profile").IsEnabled);
            Assert.Equal(1, set.Count);
        }
        finally { window.Close(); }
    }
}
