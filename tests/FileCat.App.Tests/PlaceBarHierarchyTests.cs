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
    [AvaloniaFact]
    public async Task Compact_place_bar_fills_a_wide_panel_and_restores_places_after_resize()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 2000);
        try
        {
            var row = window.GetVisualDescendants().OfType<PlaceBarPanel>().First();
            for (int i = 0; i < 250 && row.Children.OfType<Button>().All(b => b.Tag is not Place); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            window.UpdateLayout();
            Assert.True(services.Settings.CompactPlaces);
            Assert.Equal(vm.BarPlaces().Count, row.Children.OfType<Button>().Count(b => b.Tag is Place));
            int wide = row.ShownCount;
            Assert.True(wide > 4, $"A wide panel showed only {wide} places despite having room");

            window.Width = 640;
            for (int i = 0; i < 100 && row.ShownCount >= wide; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            int narrow = row.ShownCount;
            Assert.True(narrow < wide, $"Narrow panel still showed {narrow} of {wide} places");
            Assert.Contains(row.Hidden.OfType<Button>(), b => b.Tag is Place);
            Assert.True(row.Children[^1].Bounds.Right <= row.Bounds.Width,
                        $"Overflow button {row.Children[^1].Bounds} is outside {row.Bounds}");

            window.Width = 2000;
            for (int i = 0; i < 100 && row.ShownCount <= narrow; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(wide, row.ShownCount);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [Fact]
    public void Compact_policy_prioritizes_pins_and_common_places_but_keeps_every_place_in_the_row()
    {
        var all = new[]
        {
            Place("Drive A", PlaceGroup.Drives), Place("Drive B", PlaceGroup.Drives),
            Place("This PC", PlaceGroup.Collections), Place("Working sets", PlaceGroup.Collections),
            Place("Home", PlaceGroup.Folders), Place("Documents", PlaceGroup.Folders),
            Place("Bookmark", PlaceGroup.Bookmarks), Place("Server", PlaceGroup.Servers),
        };
        var defaults = PlaceBarPolicy.Select(all, [], compact: true);
        Assert.Equal(all.Length, defaults.Count);
        Assert.Equal(new[] { all[0], all[2], all[4], all[6] }, defaults.Take(4));
        Assert.Equal(all, defaults.OrderBy(place => Array.IndexOf(all, place)));
        var pinned = PlaceBarPolicy.Select(all, [PlaceBarPolicy.Key(all[3]), PlaceBarPolicy.Key(all[7])], compact: true);
        Assert.Equal(all.Length, pinned.Count);
        Assert.Equal(new[] { all[0], all[2], all[3], all[7] }, pinned.Take(4));
        Assert.Equal(all, pinned.OrderBy(place => Array.IndexOf(all, place)));
        Assert.Equal(all, PlaceBarPolicy.Select(all, [], compact: false));
        Assert.NotEqual(PlaceBarPolicy.Key(new Place("A|B", null, new Location(Schemes.Sftp, "/same", session: "one"), () => null)),
            PlaceBarPolicy.Key(new Place("A|B", null, new Location(Schemes.Sftp, "/same", session: "two"), () => null)));

        static Place Place(string title, PlaceGroup group) =>
            new(title, null, Location.FileSystem("/" + title), () => null) { Group = group };
    }

    [AvaloniaFact]
    public async Task Compact_panel_header_uses_available_width_and_keeps_overflow_places_reachable()
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
            Assert.Equal(all.Length, Buttons().Length);
            var omittedButton = row.Hidden.OfType<Button>().First(b => b.Tag is Place);
            var omitted = (Place)omittedButton.Tag!;
            var more = row.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "More places");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = Assert.IsType<ContextMenu>(more.ContextMenu);
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => item.Header as string == omitted.Title);
            var pinMenu = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Pin places to bar");
            var pin = pinMenu.Items.OfType<MenuItem>().Single(item => item.Header as string == omitted.Title);
            pin.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for (int i = 0; i < 250 && row.Hidden.Contains(Buttons().Single(b => PlaceBarPolicy.Key((Place)b.Tag!) == PlaceBarPolicy.Key(omitted))); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Contains(PlaceBarPolicy.Key(omitted), services.Settings.PinnedPlaces);
            var saved = JsonSerializer.Deserialize(File.ReadAllText(services.Paths.SettingsFile), StateJsonContext.Default.AppSettings)!;
            Assert.Contains(PlaceBarPolicy.Key(omitted), saved.PinnedPlaces);
            var pinnedButton = Buttons().Single(b => PlaceBarPolicy.Key((Place)b.Tag!) == PlaceBarPolicy.Key(omitted));
            Assert.DoesNotContain(pinnedButton, row.Hidden);
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
            Assert.Equal(vm.BarPlaces().Count, Buttons().Length);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
