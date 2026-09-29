using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

public sealed class SettingsDialogTests
{
    /// <summary>Settings names the default verification as the copy dialog does, and stores what was chosen.</summary>
    [AvaloniaFact]
    public async Task Default_copy_verification_is_chosen_by_the_names_the_copy_dialog_uses()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            vm.Execute(CommandIds.Settings);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList().FindIndex(t => t.Header as string == "Behavior");
            ComboBox? Verify() => window.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(c => AutomationProperties.GetName(c) == "Default copy verification");
            for (int i = 0; i < 100 && Verify() is null; i++) await Task.Delay(20, ct);
            Assert.Equal(["Size and metadata (fast)", "Read back and compare content"], Verify()!.Items.OfType<string>());
            Assert.Equal("Size and metadata (fast)", Verify()!.SelectedItem);
            Verify()!.SelectedIndex = 1;
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "OK").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.Equal("ReadBack", services.Settings.DefaultVerify);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
