using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class ColumnProfileTests
{
    /// <summary>
    /// Settings keep a filling column as 0 (it fills); read back, every built-in profile is as it was, headers and
    /// alignment too, and the Links profile's link target still takes more of the space than the name beside it.
    /// </summary>
    [Fact]
    public void Built_in_profiles_come_back_from_settings_as_they_were()
    {
        var set = new ColumnProfileSet(null);
        Assert.Equal(ColumnProfiles.Defaults.Count, set.Count);
        var saved = set.ToSettings();
        Assert.Equal(0, saved[0].Columns[0].Width); // Name fills
        Assert.All(saved.Single(p => p.Name == "Links").Columns.Take(2), c => Assert.Equal(0, c.Width));
        // Full: file versions on Windows; permissions and ownership on Linux and macOS; and everywhere how many streams
        // or attributes a file carries (D-55).
        Assert.Equal(OperatingSystem.IsWindows() ? ["meta:version", "meta:hidden"] : ["meta:permissions", "meta:owner", "meta:group", "meta:hidden"],
            saved[2].Columns.Where(c => c.Field.StartsWith("meta:", StringComparison.Ordinal)).Select(c => c.Field));
        var reloaded = new ColumnProfileSet(saved);
        Assert.Equal(set.Profiles.Select(p => p.Name), reloaded.Profiles.Select(p => p.Name));
        for (int i = 0; i < set.Count; i++)
            Assert.Equal(set.Profiles[i].Columns, reloaded.Profiles[i].Columns);
        var links = reloaded.Profiles.Single(p => p.Name == "Links").Columns;
        Assert.True(links[1].Width > links[0].Width, "Links' link target should take more of the space than the name");
    }

    /// <summary>
    /// How filling columns share the space counts in whether columns are the same: a profile given other shares takes
    /// them, and a tab on it tells its list as for any other width (the list stays where it is scrolled); the same
    /// shares again tell it nothing.
    /// </summary>
    [AvaloniaFact]
    public void A_list_is_told_when_its_profiles_filling_columns_share_the_space_anew()
    {
        ColumnSpec[] even =
        [
            new(ColumnField.Name, "Name", 200, Star: true),
            new(ColumnField.Extension, "Ext", 200, Star: true),
            new(ColumnField.Size, "Size", 86, RightAlign: true),
        ];
        ColumnSpec[] uneven = [even[0], even[1] with { Width = 300 }, even[2]];
        Assert.True(ColumnProfileSet.SameFields(even, uneven));
        Assert.False(ColumnProfileSet.SameColumns(even, uneven));

        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "files");
        Directory.CreateDirectory(folder);
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            var tab = panel.OpenTab(Location.FileSystem(folder));
            services.Columns.Replace([("Two fill", even)]);
            Assert.Same(even, tab.Columns);
            var told = new List<bool>();
            tab.ColumnsChanged += (_, e) => told.Add(e.WidthsOnly);
            services.Columns.Replace([("Two fill", [.. even])]); // the same columns again
            Assert.Empty(told);
            services.Columns.Replace([("Two fill", uneven)]);
            Assert.Same(uneven, tab.Columns);
            Assert.Equal([true], told);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Saved_profiles_are_validated()
    {
        var saved = new List<ColumnProfile>
        {
            new() { Name = "Sizes", Columns = [new() { Field = "size", Width = 90 }, new() { Field = "bogus", Width = 50 }] },
            new() { Name = "Empty", Columns = [new() { Field = "bogus" }] },
        };
        saved.AddRange(Enumerable.Range(0, 20).Select(i => new ColumnProfile { Name = "P" + i, Columns = [new() { Field = "name" }] }));
        var set = new ColumnProfileSet(saved);
        Assert.Equal(ColumnProfileSet.MaxProfiles, set.Count);
        Assert.Equal("Sizes", set.NameOf(0));
        Assert.Equal(["name", "size"], set.Profiles[0].Columns.Select(ColumnProfileSet.KeyOf)); // Name is always kept
        Assert.Equal("P0", set.NameOf(1)); // the profile without valid columns is dropped
        set.Replace([]);
        Assert.Equal(ColumnProfiles.Defaults.Count, set.Count);
    }

    /// <summary>
    /// Profiles replaced by the same ones (Settings' OK with nothing edited, or a name typed with a space after it, which
    /// is trimmed) tell no list its columns changed; a real edit does.
    /// </summary>
    [AvaloniaFact]
    public void Replacing_profiles_with_the_same_ones_tells_no_one()
    {
        var set = new ColumnProfileSet(null);
        int changes = 0;
        set.Changed += () => changes++;
        // As the Settings editor hands them back: every column made again from its key and width.
        var again = set.Profiles.Select(p => (p.Name, p.Columns
            .Select(c => ColumnProfileSet.Create(ColumnProfileSet.KeyOf(c), c.Star ? 0 : c.Width)!).ToArray())).ToList();
        set.Replace(again);
        Assert.Equal(0, changes);

        string first = set.NameOf(0);
        var editor = new ColumnProfilesEditor(set);
        var window = new Window { Width = 900, Height = 700, Content = editor.View };
        window.Show();
        try
        {
            var name = editor.View.GetLogicalDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "Profile name");
            name.Text = first + " ";
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.HasChanges()); // a draft change until applied, where it is trimmed away
            editor.Apply();
            Assert.Equal(0, changes);
            name.Text = first + " 2";
            Dispatcher.UIThread.RunJobs();
            editor.Apply();
            Assert.Equal(1, changes);
            Assert.Equal(first + " 2", set.NameOf(0));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// An edit to one profile leaves the others' column lists as they were (no list showing one is told its columns
    /// changed), the built-in Links profile's unevenly shared space included.
    /// </summary>
    [Fact]
    public void An_edit_leaves_the_profiles_it_did_not_touch_as_they_were()
    {
        var set = new ColumnProfileSet(null);
        int links = set.Profiles.ToList().FindIndex(p => p.Name == "Links");
        var linkColumns = set.Profiles[links].Columns;
        Assert.True(linkColumns.Where(c => c.Star).Select(c => c.Width).Distinct().Count() > 1, "Links should share its space unevenly");
        int changes = 0;
        set.Changed += () => changes++;
        // As the Settings editor hands them back after the first profile was renamed.
        var edited = set.Profiles.Select((p, i) => (i == 0 ? p.Name + " 2" : p.Name, p.Columns
            .Select(c => ColumnProfileSet.Create(ColumnProfileSet.KeyOf(c), c.Star ? 0 : c.Width)!).ToArray())).ToList();
        set.Replace(edited);
        Assert.Equal(1, changes);
        Assert.Same(linkColumns, set.Profiles[links].Columns);
    }

    /// <summary>
    /// A width changed in the Links profile itself (Settings shows its filling columns only as "fills") leaves its link
    /// target taking more of the space than its name, as before.
    /// </summary>
    [AvaloniaFact]
    public void A_width_edited_in_Links_leaves_its_filling_columns_sharing_the_space_as_before()
    {
        var set = new ColumnProfileSet(null);
        int links = set.Profiles.ToList().FindIndex(p => p.Name == "Links");
        var before = set.Profiles[links].Columns;
        var editor = new ColumnProfilesEditor(set);
        var window = new Window { Width = 900, Height = 700, Content = editor.View };
        window.Show();
        try
        {
            editor.SelectProfile(links);
            var columns = editor.View.GetLogicalDescendants().OfType<ListBox>()
                .Single(l => AutomationProperties.GetName(l) == "Columns of the selected profile");
            columns.SelectedIndex = Array.FindIndex(before, c => c.Field == ColumnField.Modified);
            var width = editor.View.GetLogicalDescendants().OfType<NumericUpDown>().Single();
            width.Value = 150;
            Dispatcher.UIThread.RunJobs();
            editor.Apply();
            var after = set.Profiles[links].Columns;
            Assert.NotSame(before, after);
            Assert.Equal(150, after.Single(c => c.Field == ColumnField.Modified).Width);
            Assert.Equal(before.Where(c => c.Star), after.Where(c => c.Star));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void Dragged_widths_persist_for_fixed_columns_only()
    {
        var set = new ColumnProfileSet(null);
        int changes = 0;
        set.Changed += () => changes++;
        set.SetWidth(0, 2, 140.4);
        Assert.Equal(140, set.Profiles[0].Columns[2].Width);
        set.SetWidth(0, 2, 139.6); // stored as the same 140: nothing changed, no one told
        Assert.Equal(1, changes);
        set.SetWidth(0, 0, 500); // Name fills: never turned into a fixed width
        Assert.True(set.Profiles[0].Columns[0].Star);
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void Dragging_a_border_stores_the_width_in_the_profile_for_every_tab()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "files");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "x");
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            var tab = panel.OpenTab(Location.FileSystem(folder));
            var other = panel.OpenTab(Location.FileSystem(folder));
            var list = new FileListControl { Tab = tab };
            var window = new Window { Width = 800, Height = 400, Content = list };
            window.Show();
            try
            {
                window.CaptureRenderedFrame();
                // Details: Name (fills) | Ext 56 | Size 86 | ... The border right of Ext resizes Ext.
                var columns = tab.Columns;
                Assert.Equal(ColumnField.Extension, columns[1].Field);
                double y = 8;
                double x = FindBorder(list, 1);
                window.MouseMove(new Point(x, y));
                window.MouseDown(new Point(x, y), MouseButton.Left);
                window.MouseMove(new Point(x + 40, y));
                window.MouseUp(new Point(x + 40, y), MouseButton.Left);
                // Stored at the default font's scale: the 40 pixels dragged, in the list's current font.
                double stored = (56 * list.WidthScale + 40) / list.WidthScale;
                Assert.Equal(stored, services.Columns.Profiles[0].Columns[1].Width, 0);
                Assert.Equal(stored, other.Columns[1].Width, 0);
                Assert.Equal(stored, services.Settings.ColumnProfiles[0].Columns[1].Width, 0); // saved
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// A border dragged in one profile saves them all; at the next start every profile is as it was, and the Links
    /// profile's link target still takes more of the space than the name beside it (it came back sharing it evenly).
    /// </summary>
    [AvaloniaFact]
    public void Profiles_saved_after_a_drag_are_as_they_were_at_the_next_start()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: root);
            List<(string Name, ColumnSpec[] Columns)> before;
            using (var first = AppServices.CreateForPaths(paths))
            {
                Assert.Empty(first.Settings.ColumnProfiles); // the built-in profiles, not saved yet
                first.Columns.SetWidth(0, 1, 90); // a border dragged in a list on the first profile
                Assert.NotEmpty(first.Settings.ColumnProfiles);
                before = [.. first.Columns.Profiles];
            }
            using var services = AppServices.CreateForPaths(paths);
            Assert.Equal(before.Select(p => p.Name), services.Columns.Profiles.Select(p => p.Name));
            for (int i = 0; i < before.Count; i++)
                Assert.Equal(before[i].Columns, services.Columns.Profiles[i].Columns);
            Assert.Equal(90, services.Columns.Profiles[0].Columns[1].Width);
            var links = services.Columns.Profiles.Single(p => p.Name == "Links").Columns;
            Assert.True(links[1].Width > links[0].Width, "Links' link target should take more of the space than the name");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// A tab restored on a profile removed since it was saved (or on a damaged number) is on the profile it shows: the
    /// last there is, or the first; its number says so and a dragged width is stored there rather than dropped.
    /// </summary>
    [AvaloniaFact]
    public void A_tab_restored_on_a_profile_that_is_gone_is_on_the_profile_it_shows()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "files");
        Directory.CreateDirectory(folder);
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            var tab = panel.OpenTab(Location.FileSystem(folder));
            int last = services.Columns.Count - 1;

            tab.ApplyState(new TabState { ColumnProfile = last + 3 });
            Assert.Equal(last, tab.ColumnProfile);
            int column = Array.FindIndex(tab.Columns, c => !c.Star);
            Assert.True(tab.SetColumnWidth(column, 123));
            Assert.Equal(123, services.Columns.Profiles[last].Columns[column].Width);

            tab.ApplyState(new TabState { ColumnProfile = -2 });
            Assert.Equal(0, tab.ColumnProfile);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Dates fit whatever the date format: a longer format widens the date columns, and only them.</summary>
    [AvaloniaFact]
    public void Date_columns_widen_for_longer_date_formats()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "files");
        Directory.CreateDirectory(folder);
        string format = Formatters.DateFormat;
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            var tab = panel.OpenTab(Location.FileSystem(folder));
            var list = new FileListControl { Tab = tab };
            var window = new Window { Width = 1400, Height = 400, Content = list };
            window.Show();
            try
            {
                int modified = Array.FindIndex(tab.Columns, c => c.Field == ColumnField.Modified);
                double Width(int column) => list.ColumnRightEdge(column) - list.ColumnRightEdge(column - 1);
                Formatters.DateFormat = "dd.MM.yyyy HH:mm"; // what the widths were set for
                window.CaptureRenderedFrame();
                double set = Width(modified), extension = Width(1);
                Assert.Equal(list.WidthScale, list.DateScale, 3);

                Formatters.DateFormat = "yyyy-MM-dd HH:mm:ss"; // "2026-12-28 22:58:58": three characters more
                window.CaptureRenderedFrame();
                Assert.True(Width(modified) > set * 1.1, $"{Width(modified)} is not wider than {set}.");
                Assert.Equal(extension, Width(1), 1);

                Formatters.DateFormat = "MM/dd/yyyy h:mm tt"; // "12/28/2026 10:58 AM"
                window.CaptureRenderedFrame();
                Assert.True(Width(modified) > set * 1.1, $"{Width(modified)} is not wider than {set}.");
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Formatters.DateFormat = format;
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The x position of the right border of <paramref name="column"/> (public layout via the test hook).</summary>
    private static double FindBorder(FileListControl list, int column) => list.ColumnRightEdge(column);
}
