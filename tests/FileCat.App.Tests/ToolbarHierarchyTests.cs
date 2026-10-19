using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.State;
using System.Text.Json;

namespace FileCat.App.Tests;

public sealed class ToolbarHierarchyTests
{
    [Fact]
    public void Focused_model_accounts_for_every_classic_action_once_and_keeps_stable_primary_order()
    {
        Assert.Equal(10, MainMenuModel.PrimaryToolbar.Length);
        Assert.Equal(CommandIds.Back, MainMenuModel.PrimaryToolbar[0]);
        Assert.Equal(CommandIds.Operations, MainMenuModel.PrimaryToolbar[^1]);
        var focused = MainMenuModel.PrimaryToolbar.Concat(MainMenuModel.SecondaryToolbar.SelectMany(s => s.Commands)).ToArray();
        Assert.Equal(focused.Length, focused.Distinct(StringComparer.Ordinal).Count());
        var classic = MainMenuModel.Toolbar.Where(id => id != "-").Append(CommandIds.Operations);
        Assert.Equal(classic.OrderBy(id => id, StringComparer.Ordinal), focused.OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(MainMenuModel.SecondaryToolbar, section => Assert.False(string.IsNullOrWhiteSpace(section.Header)));
        Assert.Equal("File › Archives", MainMenuModel.PathOf(CommandIds.Pack));
        Assert.Equal("File › Inspect", MainMenuModel.PathOf(CommandIds.HiddenData));
        Assert.Equal("File › Working sets", MainMenuModel.PathOf(CommandIds.AddToWorkingSet));
    }

    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(1280)]
    public void Primary_actions_keep_one_order_and_more_is_anchored_at_both_widths(int width)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: width);
        try
        {
            window.UpdateLayout();
            var toolbar = window.GetVisualDescendants().OfType<WrapPanel>().Single(p => p.Name == "Toolbar");
            var buttons = toolbar.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is string).ToArray();
            Assert.Equal(MainMenuModel.PrimaryToolbar, buttons.Select(b => (string)b.Tag!));
            var more = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ToolbarMore");
            Assert.True(more.IsVisible);
            Assert.Equal("More toolbar commands", AutomationProperties.GetName(more));
            Assert.True(more.Bounds.Width > 20);
            Assert.Single(buttons.Select(b => Math.Round(b.TranslatePoint(default, toolbar)!.Value.Y)).Distinct());
            var groups = Assert.IsType<ContextMenu>(more.ContextMenu).Items.OfType<MenuItem>().ToArray();
            Assert.Equal(MainMenuModel.SecondaryToolbar.Select(s => s.Header), groups.Select(g => g.Header));
            var secondary = groups.SelectMany(g => g.Items.OfType<MenuItem>()).ToArray();
            Assert.Contains(secondary, item => Equals(item.Tag, CommandIds.CompareFiles));
            Assert.Contains(secondary, item => Equals(item.Tag, CommandIds.Settings));
            Assert.Contains(buttons, b => Equals(b.Tag, CommandIds.Copy));
            Assert.Contains(buttons, b => Equals(b.Tag, CommandIds.MakeDirectory));
            Assert.Contains(buttons, b => Equals(b.Tag, CommandIds.FindFiles));
            var point = more.TranslatePoint(new Point(more.Bounds.Width / 2, more.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(more.ContextMenu?.IsOpen);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Appearance_preference_restores_classic_toolbar_and_can_return_to_focused_layout()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            await ChooseClassic(true);
            Assert.True(services.Settings.ClassicToolbar);
            var saved = JsonSerializer.Deserialize(File.ReadAllText(services.Paths.SettingsFile), StateJsonContext.Default.AppSettings)!;
            Assert.True(saved.ClassicToolbar);
            Assert.False(More().IsVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("toolbar") && Equals(b.Tag, CommandIds.Settings));
            await ChooseClassic(false);
            Assert.False(services.Settings.ClassicToolbar);
            Assert.True(More().IsVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("toolbar") && Equals(b.Tag, CommandIds.Settings));
        }
        finally { AccessibilityTests.Close(services, window, root); }

        Button More() => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ToolbarMore");
        async Task ChooseClassic(bool value)
        {
            vm.Execute(CommandIds.Settings);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(dialogs.IsOpen);
            window.UpdateLayout();
            var choice = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => c.Content as string == "Classic full toolbar (all commands)");
            choice.IsChecked = value;
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "OK")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && dialogs.IsOpen; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.False(dialogs.IsOpen);
            window.UpdateLayout();
        }
    }
}
