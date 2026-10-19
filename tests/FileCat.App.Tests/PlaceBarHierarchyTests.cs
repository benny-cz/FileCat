using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Text.Json;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class PlaceBarHierarchyTests
{
    [Fact]
    public void Compact_policy_keeps_pins_in_original_order_and_exposes_a_short_default_row()
    {
        var all = new[]
        {
            Place("Drive A", PlaceGroup.Drives), Place("Drive B", PlaceGroup.Drives),
            Place("This PC", PlaceGroup.Collections), Place("Working sets", PlaceGroup.Collections),
            Place("Home", PlaceGroup.Folders), Place("Documents", PlaceGroup.Folders),
            Place("Bookmark", PlaceGroup.Bookmarks), Place("Server", PlaceGroup.Servers),
        };
        var defaults = PlaceBarPolicy.Select(all, [], compact: true);
        Assert.Equal(4, defaults.Count);
        Assert.Contains(all[0], defaults);
        Assert.Contains(all[2], defaults);
        Assert.Contains(all[4], defaults);
        Assert.Contains(all[6], defaults);
        var pinned = PlaceBarPolicy.Select(all, [PlaceBarPolicy.Key(all[3]), PlaceBarPolicy.Key(all[7])], compact: true);
        Assert.Equal(4, pinned.Count);
        Assert.Contains(all[3], pinned);
        Assert.Contains(all[7], pinned);
        Assert.Equal(all.Where(pinned.Contains), pinned);
        Assert.Equal(all, PlaceBarPolicy.Select(all, [], compact: false));
        Assert.NotEqual(PlaceBarPolicy.Key(new Place("A|B", null, new Location(Schemes.Sftp, "/same", session: "one"), () => null)),
            PlaceBarPolicy.Key(new Place("A|B", null, new Location(Schemes.Sftp, "/same", session: "two"), () => null)));

        static Place Place(string title, PlaceGroup group) =>
            new(title, null, Location.FileSystem("/" + title), () => null) { Group = group };
    }

    [AvaloniaFact]
    public async Task Compact_panel_header_keeps_all_destinations_in_more_and_pins_a_chosen_place()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1280);
        try
        {
            var panel = window.GetVisualDescendants().OfType<PanelView>().First();
            var row = panel.GetVisualDescendants().OfType<PlaceBarPanel>().Single();
            for (int i = 0; i < 250 && row.Children.OfType<Button>().All(b => b.Tag is not Place); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(services.Settings.CompactPlaces);
            Place[] all = [.. vm.BarPlaces()];
            Assert.True(all.Length > 4);
            Button[] Buttons() => [.. row.Children.OfType<Button>().Where(b => b.Tag is Place)];
            Assert.InRange(Buttons().Length, 1, 4);
            var omitted = all.First(p => !Buttons().Any(b => PlaceBarPolicy.Key((Place)b.Tag!) == PlaceBarPolicy.Key(p)));
            Assert.DoesNotContain(Buttons(), b => PlaceBarPolicy.Key((Place)b.Tag!) == PlaceBarPolicy.Key(omitted));
            var more = row.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "More places");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = Assert.IsType<ContextMenu>(more.ContextMenu);
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => item.Header as string == omitted.Title);
            var pinMenu = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Pin places to bar");
            var pin = pinMenu.Items.OfType<MenuItem>().Single(item => item.Header as string == omitted.Title);
            pin.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for (int i = 0; i < 250 && !Buttons().Any(b => PlaceBarPolicy.Key((Place)b.Tag!) == PlaceBarPolicy.Key(omitted)); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Contains(PlaceBarPolicy.Key(omitted), services.Settings.PinnedPlaces);
            var saved = JsonSerializer.Deserialize(File.ReadAllText(services.Paths.SettingsFile), StateJsonContext.Default.AppSettings)!;
            Assert.Contains(PlaceBarPolicy.Key(omitted), saved.PinnedPlaces);
            var pinnedButton = Buttons().Single(b => PlaceBarPolicy.Key((Place)b.Tag!) == PlaceBarPolicy.Key(omitted));
            var placeMenu = panel.PlaceMenu((Place)pinnedButton.Tag!);
            Assert.Contains(placeMenu.Items.OfType<MenuItem>(), item => item.Header as string == "Unpin from Places bar");
            more = row.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "More places");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsType<ContextMenu>(more.ContextMenu).Items.OfType<MenuItem>()
                .Single(item => item.Header as string == "Clear pinned Places")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Empty(services.Settings.PinnedPlaces);
            services.Settings.CompactPlaces = false;
            vm.ApplySettings();
            for (int i = 0; i < 250 && Buttons().Length != vm.BarPlaces().Count; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(vm.BarPlaces().Count, Buttons().Length);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
