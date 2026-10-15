using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.App.ViewModels;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class PanelFreeSpaceTests
{
    [AvaloniaFact]
    public async Task Available_space_sits_left_of_the_mask_and_disappears_for_virtual_locations()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 250 && tab.FreeSpaceText.Length == 0; i++) await Task.Delay(20, ct);
            Assert.EndsWith(" free", tab.FreeSpaceText, StringComparison.Ordinal);

            var panel = window.GetVisualDescendants().OfType<PanelView>()
                .First(view => view.DataContext is PanelViewModel model && model.ActiveTab == tab);
            var mask = panel.FindControl<TextBox>("FilterBox")!;
            var space = panel.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == tab.FreeSpaceText && text.IsEffectivelyVisible);
            var spaceRight = Avalonia.VisualExtensions.TranslatePoint(space, new Avalonia.Point(space.Bounds.Width, 0), panel)!.Value.X;
            var maskLeft = Avalonia.VisualExtensions.TranslatePoint(mask, new Avalonia.Point(0, 0), panel)!.Value.X;
            Assert.True(spaceRight <= maskLeft, $"Available space ends at {spaceRight}; mask begins at {maskLeft}.");

            tab.Navigate(new FileCat.Core.Resources.Location(Schemes.Computer, string.Empty));
            for (int i = 0; i < 250 && tab.Location?.Scheme != Schemes.Computer; i++) await Task.Delay(20, ct);
            Assert.Equal(string.Empty, tab.FreeSpaceText);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
