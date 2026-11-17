using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed partial class OwnerColumnTests
{
    [Fact]
    public void Owner_is_offered_without_changing_builtins_and_imported_profiles_keep_width_and_order()
    {
        var owner = Assert.Single(ColumnProfileSet.Choices, c => c.Key == "meta:owner");
        Assert.Equal(OperatingSystem.IsWindows() ? 160 : 110, owner.DefaultWidth);
        Assert.Equal(ColumnDescriptions.For("owner"), owner.Description);
        if (OperatingSystem.IsWindows())
        {
            Assert.DoesNotContain(ColumnProfileSet.Choices, c => c.Key is "meta:group" or "meta:permissions");
            Assert.DoesNotContain(ColumnProfiles.Defaults.SelectMany(p => p.Columns), c => c.MetadataId == "owner");
        }
        else
        {
            Assert.Contains(ColumnProfileSet.Choices, c => c.Key == "meta:group");
            Assert.Contains(ColumnProfileSet.Choices, c => c.Key == "meta:permissions");
        }
        var raw = new List<ColumnProfile> { new() { Name = "Imported", Columns =
        [
            new() { Field = "name", Width = 0 },
            new() { Field = "meta:owner", Width = 187 },
            new() { Field = "size", Width = 91 },
            new() { Field = "unknown-on-this-version", Width = 99 },
        ] } };
        var parsed = new ColumnProfileSet(raw);
        var saved = parsed.ToSettings();
        Assert.Equal(new[] { "name", "meta:owner", "size" }, saved[0].Columns.Select(c => c.Field));
        Assert.Equal(187, saved[0].Columns[1].Width);
        Assert.Equal(4, raw[0].Columns.Count); // parsing alone leaves raw settings intact
        Assert.Equal(saved[0].Columns.Select(c => (c.Field, c.Width, c.Visible)),
            new ColumnProfileSet(saved).ToSettings()[0].Columns.Select(c => (c.Field, c.Width, c.Visible)));
        Assert.Equal(1, AppSettings.CurrentSchema);
    }

    [AvaloniaFact]
    public void Add_box_and_analyze_share_one_description_and_fixed_columns_hide_the_note()
    {
        var editor = new ColumnProfilesEditor(new ColumnProfileSet(null));
        var window = new Window { Width = 900, Height = 700, Content = editor.View };
        window.Show();
        try
        {
            var add = editor.View.GetLogicalDescendants().OfType<ComboBox>().Single();
            var note = editor.View.GetLogicalDescendants().OfType<TextBlock>().Single(c => AutomationProperties.GetAutomationId(c) == "Column description");
            add.SelectedIndex = ColumnProfileSet.Choices.ToList().FindIndex(c => c.Key == "meta:owner");
            Assert.True(note.IsVisible);
            Assert.Equal(ColumnDescriptions.For("owner"), note.Text);
            Assert.Equal(note.Text, AutomationProperties.GetHelpText(add));
            add.SelectedIndex = ColumnProfileSet.Choices.ToList().FindIndex(c => c.Key == "name");
            Assert.False(note.IsVisible);
            Assert.Equal("", AutomationProperties.GetHelpText(add));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Windows_owner_analysis_is_guarded_for_mixed_results_before_any_sort_or_read()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The newly enabled Owner scope guard is Windows-specific.");
        using var f = new OwnerFixture();
        await f.Load();
        var vm = new MainViewModel(f.Services);
        Assert.Contains(vm.AnalysisFields(f.Tab), field => field.Id == "owner");
        string local = Path.Combine(f.Root, "local.bin");
        File.WriteAllText(local, "owned local fixture");
        var set = f.Services.ResultSets.Create("Mixed ownership scope", "Owned local and archive references");
        set.Add(ItemRef.ForFileSystemPath(local, EntryKind.File), "");
        set.Add(new ItemRef(new Location("zip", Path.Combine(f.Root, "owned.zip")), "member.txt", EntryKind.File), "");
        set.IsComplete = true;
        f.Tab.Navigate(ResultSetProvider.LocationOf(set));
        await FileListFixture.Wait(() => f.Tab.Listing.State == ListingState.Complete && f.Tab.Listing.VisibleCount == 2);
        Assert.DoesNotContain(vm.AnalysisFields(f.Tab), field => field.Id == "owner");
        Assert.Contains(vm.AnalysisFields(f.Tab), field => field.Id == "dimensions");
        var before = f.Tab.Listing.Sort;
        f.Tab.SortByMetadata("owner", analyzing: true);
        await f.Tab.AnalyzeAsync("owner");
        Assert.Equal(before, f.Tab.Listing.Sort);
        Assert.Null(f.Tab.AnalysisStatus);
        Assert.Empty(f.Source.ReadPaths);
        Assert.Equal(0, f.Source.NameCalls);
        Assert.Equal("owned local fixture", File.ReadAllText(local));
    }

    [AvaloniaFact]
    public async Task Explicit_filesystem_owner_analysis_reads_the_listing_without_enabling_the_column()
    {
        using var f = new OwnerFixture();
        await f.Load();
        Assert.DoesNotContain(f.Tab.Columns, column => column.MetadataId == "owner");
        f.Tab.SortByMetadata("owner", analyzing: true);
        await f.Tab.AnalyzeAsync("owner");
        Assert.Equal(120, f.Source.ReadPaths.Count);
        Assert.Equal(SortField.Metadata, f.Tab.Listing.Sort.Field);
        Assert.Equal("owner", f.Tab.Listing.Sort.MetadataId);
    }

    [AvaloniaFact]
    public async Task Owner_is_available_from_both_header_and_sort_menu()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1600, height: 600);
        try
        {
            var tab = vm.ActiveTab!;
            await FileListFixture.Wait(() => tab.Listing.State == ListingState.Complete);
            var profiles = services.Columns.Profiles.ToList();
            profiles[0] = (profiles[0].Name, [.. profiles[0].Columns, ColumnProfileSet.Create("meta:owner", 160)!]);
            services.Columns.Replace(profiles);
            tab.ColumnProfile = 0;
            window.CaptureRenderedFrame()?.Dispose();
            var view = window.GetVisualDescendants().OfType<PanelView>().Single(v => ReferenceEquals(v.List.Tab, tab));
            var button = view.FindControl<Button>("SortButton")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = Assert.IsType<ContextMenu>(view.SortMenu);
            var owner = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource).Single(item => item.Header as string == "Owner");
            owner.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("owner", tab.Listing.Sort.MetadataId);
            bool descending = tab.Listing.Sort.Descending;
            menu.Close();
            window.CaptureRenderedFrame()?.Dispose();
            int column = Array.FindIndex(tab.Columns, c => c.MetadataId == "owner");
            view.List.ScrollToHorizontalOffset(view.List.ColumnLeftEdge(column));
            window.CaptureRenderedFrame()?.Dispose();
            var point = view.List.TranslatePoint(new Point(view.List.ColumnLeftEdge(column) + 12, 10), window)!.Value;
            window.MouseDown(point, Avalonia.Input.MouseButton.Left);
            window.MouseUp(point, Avalonia.Input.MouseButton.Left);
            Assert.Equal("owner", tab.Listing.Sort.MetadataId);
            Assert.Equal(!descending, tab.Listing.Sort.Descending);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
