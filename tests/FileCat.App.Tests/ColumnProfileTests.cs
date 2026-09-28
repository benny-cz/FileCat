using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class ColumnProfileTests
{
    [Fact]
    public void Built_in_profiles_round_trip_through_settings_with_fill_columns_as_zero()
    {
        var set = new ColumnProfileSet(null);
        Assert.Equal(ColumnProfiles.Defaults.Count, set.Count);
        var saved = set.ToSettings();
        Assert.Equal(0, saved[0].Columns[0].Width); // Name fills
        // Full: file versions on Windows; permissions and ownership on Linux and macOS.
        Assert.Equal(OperatingSystem.IsWindows() ? ["meta:version"] : ["meta:permissions", "meta:owner", "meta:group"],
            saved[2].Columns.Where(c => c.Field.StartsWith("meta:", StringComparison.Ordinal)).Select(c => c.Field));
        var reloaded = new ColumnProfileSet(saved);
        Assert.Equal(set.Profiles.Select(p => p.Name), reloaded.Profiles.Select(p => p.Name));
        for (int i = 0; i < set.Count; i++)
        {
            Assert.Equal(set.Profiles[i].Columns.Select(ColumnProfileSet.KeyOf), reloaded.Profiles[i].Columns.Select(ColumnProfileSet.KeyOf));
            Assert.Equal(set.Profiles[i].Columns.Select(c => c.Star), reloaded.Profiles[i].Columns.Select(c => c.Star));
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

    [Fact]
    public void Dragged_widths_persist_for_fixed_columns_only()
    {
        var set = new ColumnProfileSet(null);
        int changes = 0;
        set.Changed += () => changes++;
        set.SetWidth(0, 2, 140.4);
        Assert.Equal(140, set.Profiles[0].Columns[2].Width);
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

    /// <summary>The x position of the right border of <paramref name="column"/> (public layout via the test hook).</summary>
    private static double FindBorder(FileListControl list, int column) => list.ColumnRightEdge(column);
}
