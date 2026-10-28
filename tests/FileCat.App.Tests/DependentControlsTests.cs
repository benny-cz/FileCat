using Avalonia;
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

    /// <summary>
    /// The reason a disabled option gives is in its tooltip, so the tooltip's pop-up must say it to a screen reader too:
    /// NVDA, reporting tooltips, said only "tool tip" over the disabled folder-sizing option on Windows.
    /// </summary>
    [AvaloniaFact]
    public void A_tooltip_names_itself_with_its_text_for_screen_readers()
    {
        ToolTipNames.Install();
        var tip = new ToolTip { Content = "Turn on folder sizing with Space to use this option." };
        Assert.Equal("Turn on folder sizing with Space to use this option.",
            Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(tip).GetName());
        tip.Content = new RichTip("Copy", "F5", "Copies the marked items.", ["Shift+F5 copies in place."]);
        Assert.Equal("Copy (F5)\nCopies the marked items.\nShift+F5 copies in place.", AutomationProperties.GetName(tip));
        tip.Content = new TextBlock { Text = "Maximum profiles reached (10)." };
        Assert.Equal("Maximum profiles reached (10).", AutomationProperties.GetName(tip));
        tip.Content = new Border();
        Assert.Null(AutomationProperties.GetName(tip));
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
            // An available action carries no note (a screen reader read "Available" after each one).
            Assert.Null(AutomationProperties.GetHelpText(Action("New profile")));
            Assert.Null(ToolTip.GetTip(Action("New profile")));

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

    /// <summary>
    /// An action used by keyboard until it stops being available keeps the keyboard in the editor and says why: with
    /// NVDA on Windows, New profile pressed up to the limit dropped focus to the main window without a word.
    /// </summary>
    [AvaloniaFact]
    public void An_action_that_becomes_unavailable_under_the_keyboard_hands_focus_to_its_list_and_says_why()
    {
        var set = new ColumnProfileSet(null);
        var editor = new ColumnProfilesEditor(set);
        var window = new Window { Width = 900, Height = 700, Content = editor.View };
        window.Show();
        try
        {
            Button Action(string name) => editor.View.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetName(b) == name);
            TextBlock Status() => editor.View.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => AutomationProperties.GetAutomationId(t) == "Column editor action status");
            var profileList = editor.View.GetVisualDescendants().OfType<ListBox>()
                .Single(l => AutomationProperties.GetName(l) == "Column profiles");
            var newProfile = Action("New profile");
            Assert.True(newProfile.Focus());
            while (newProfile.IsEnabled)
            {
                Assert.True(newProfile.IsFocused);
                newProfile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            var focused = window.FocusManager?.GetFocusedElement() as Visual;
            Assert.NotNull(focused);
            Assert.True(ReferenceEquals(focused, profileList) || profileList.IsVisualAncestorOf(focused!),
                $"Focus went to {focused}");
            Assert.True(Status().IsVisible);
            Assert.Equal("Maximum profiles reached (10).", Status().Text);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Status()));

            // Once it is available again the note goes.
            Action("Remove profile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(newProfile.IsEnabled);
            Assert.False(Status().IsVisible);

            // Add, once its column is in the profile, hands the keyboard to the column choice.
            profileList.SelectedIndex = 0;
            var choice = editor.View.GetVisualDescendants().OfType<ComboBox>()
                .Single(c => AutomationProperties.GetName(c) == "Column to add");
            choice.SelectedIndex = ColumnProfileSet.Choices.ToList().FindIndex(c => c.Key == "meta:dimensions");
            var add = Action("Add column");
            Assert.True(add.IsEnabled);
            Assert.True(add.Focus());
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(add.IsEnabled);
            Assert.True(choice.IsFocused || choice.IsKeyboardFocusWithin);
            Assert.Equal("This column is already in the profile.", Status().Text);
        }
        finally { window.Close(); }
    }
}
