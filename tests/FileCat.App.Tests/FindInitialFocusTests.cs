using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;

namespace FileCat.App.Tests;

/// <summary>Window initialization cannot take focus from an already newer Find interaction.</summary>
public sealed class FindInitialFocusTests
{
    [AvaloniaFact]
    public void Queued_initial_focus_preserves_a_newer_deliberate_field_focus()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var find = FindWindow.Open(vm, root, null);
        try
        {
            var containing = find.GetVisualDescendants().OfType<TextBox>()
                .Single(c => AutomationProperties.GetName(c) == "Containing");
            Assert.True(containing.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(containing.IsFocused);
            Assert.False(find.NamesBox.IsFocused);
        }
        finally
        {
            find.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public void Queued_initial_focus_preserves_the_active_other_window()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var find = FindWindow.Open(vm, root, null);
        var elsewhere = new Window { Width = 250, Height = 120, Content = new TextBox() };
        try
        {
            elsewhere.Show();
            elsewhere.Activate();
            var input = (TextBox)elsewhere.Content!;
            Assert.True(input.Focus());
            Assert.False(find.IsActive);
            Dispatcher.UIThread.RunJobs();
            Assert.True(elsewhere.IsActive);
            Assert.True(input.IsFocused);
            Assert.False(find.NamesBox.IsFocused);
        }
        finally
        {
            elsewhere.Close();
            find.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public void Closing_before_initial_focus_does_not_focus_a_retired_find_field()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var find = FindWindow.Open(vm, root, null);
        try
        {
            int gainedFocus = 0;
            find.NamesBox.GotFocus += (_, _) => gainedFocus++;
            find.Close();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, gainedFocus);
            Assert.DoesNotContain(find, FindWindow.OpenWindows);
            Assert.True(window.IsActive);
        }
        finally
        {
            find.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }
}
