using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// Settings' OK leaves each list where it is scrolled, with the widths dragged in it, unless the columns it shows changed;
/// icons made anew (Shell pictures turned on or off) are drawn again in place.
/// </summary>
public sealed class SettingsApplyListStateTests
{
    private static readonly FieldInfo ShownColumns = typeof(FileListControl).GetField("_columns",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    [AvaloniaFact]
    public async Task Settings_OK_that_changes_no_columns_keeps_every_lists_scroll_and_dragged_widths()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 760, height: 600);
        try
        {
            var lists = await TwoScrolledLists(services, vm, window, root);
            int iconsMadeAnew = 0;
            services.Icons.Cleared += () => iconsMadeAnew++;

            var showing = SettingsDialog.ShowAsync(vm);
            Dispatcher.UIThread.RunJobs();
            ClickFooter(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            window.CaptureRenderedFrame();

            Assert.Equal(lists.FolderOffset, lists.Folder.HorizontalOffset);
            Assert.Equal(lists.ResultOffset, lists.Result.HorizontalOffset);
            Assert.Equal(lists.DraggedWidth, Width(lists.Result, lists.DraggedColumn), 1);
            // The icons were still made anew, and every list showing them draws them again (see the test below).
            Assert.True(iconsMadeAnew > 0);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_column_edit_reaches_only_the_lists_on_that_profile()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 760, height: 600);
        try
        {
            var lists = await TwoScrolledLists(services, vm, window, root);
            var folderTab = lists.Folder.Tab!;
            int before = folderTab.Columns.Length;
            int unshown = folderTab.ColumnProfile == 1 ? 0 : 1;
            int unshownBefore = services.Columns.Profiles[unshown].Columns.Length;

            // A column removed from a profile no panel shows: the folder list stays where it is scrolled.
            await RemoveSecondColumn(vm, window, unshown);
            Assert.Equal(unshownBefore - 1, services.Columns.Profiles[unshown].Columns.Length);
            Assert.Equal(lists.FolderOffset, lists.Folder.HorizontalOffset);
            Assert.Equal(before, folderTab.Columns.Length);

            // One removed from the folder's own profile reaches it, and other columns start from the first.
            await RemoveSecondColumn(vm, window, folderTab.ColumnProfile);
            Assert.Equal(before - 1, folderTab.Columns.Length);
            Assert.Same(folderTab.Columns, ShownColumns.GetValue(lists.Folder));
            Assert.Equal(0, lists.Folder.HorizontalOffset);
            // The results keep their own columns throughout, with the width dragged in them and where they are scrolled.
            Assert.Equal(lists.ResultOffset, lists.Result.HorizontalOffset);
            Assert.Equal(lists.DraggedWidth, Width(lists.Result, lists.DraggedColumn), 1);

            // A border dragged in a panel on a profile (stored in it) leaves the results as they are, too.
            int fixedColumn = Array.FindIndex(folderTab.Columns, c => !c.Star);
            services.Columns.SetWidth(folderTab.ColumnProfile, fixedColumn, folderTab.Columns[fixedColumn].Width + 20);
            window.CaptureRenderedFrame();
            Assert.Equal(lists.ResultOffset, lists.Result.HorizontalOffset);
            Assert.Equal(lists.DraggedWidth, Width(lists.Result, lists.DraggedColumn), 1);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_width_dragged_in_a_view_with_its_own_columns_follows_the_font_size()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        string folder = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
        File.WriteAllText(Path.Combine(folder, "a.txt"), "alpha");
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var workspace = new WorkspaceViewModel(services);
        var panel = new PanelViewModel(workspace, services);
        workspace.Panels.Add(panel);
        var set = services.ResultSets.Create("Font size", "local fixture");
        set.Add(ItemRef.ForFileSystemPath(Path.Combine(folder, "a.txt"), EntryKind.File), "");
        var tab = panel.OpenTab(ResultSetProvider.LocationOf(set));
        var list = new FileListControl { Tab = tab, FontSize = 13 };
        var window = new Window { Width = 1400, Height = 300, Content = list };
        window.Show();
        try
        {
            await Until(() => tab.Listing.State == ListingState.Complete);
            window.CaptureRenderedFrame();
            var shown = (ColumnSpec[])ShownColumns.GetValue(list)!;
            int kind = Array.FindIndex(shown, c => c.Field == ColumnField.Kind);
            int size = Array.FindIndex(shown, c => c.Field == ColumnField.Size);
            Assert.InRange(list.ColumnRightEdge(kind), 10, list.HorizontalViewport - 10);
            double undragged = Width(list, kind);
            var start = new Point(list.ColumnRightEdge(kind), 8);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Point(40, 0));
            window.MouseUp(start + new Point(40, 0), MouseButton.Left);
            window.CaptureRenderedFrame();
            double dragged = Width(list, kind), reference = Width(list, size);
            Assert.True(dragged > undragged + 25, $"the drag made Kind {dragged} wide, from {undragged}");

            list.FontSize = 20;
            window.CaptureRenderedFrame();
            // The dragged column grows with the font as an undragged fixed column does.
            double grown = Width(list, size) / reference;
            Assert.True(grown > 1.2, $"the font made Size {grown:0.00} times as wide");
            Assert.Equal(grown, Width(list, kind) / dragged, 2);
        }
        finally
        {
            window.Close();
            tab.Dispose();
            services.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task Icons_made_anew_are_drawn_again_where_the_list_is_scrolled()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        string folder = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
        File.WriteAllText(Path.Combine(folder, "a.txt"), "alpha");
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var workspace = new WorkspaceViewModel(services);
        var panel = new PanelViewModel(workspace, services);
        workspace.Panels.Add(panel);
        var tab = panel.OpenTab(Location.FileSystem(folder));
        tab.ColumnProfile = 2; // Full: wider than the window
        var list = new FileListControl { Tab = tab };
        var window = new Window { Width = 400, Height = 300, Content = list };
        int renders = 0;
        Action<double, long> counted = (_, _) => renders++;
        window.Show();
        try
        {
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 1);
            window.CaptureRenderedFrame();
            Assert.True(list.HorizontalExtent > list.HorizontalViewport);
            list.ScrollToHorizontalOffset(50);
            window.CaptureRenderedFrame();
            double offset = list.HorizontalOffset;
            Assert.True(offset > 0);

            FileListControl.RenderTimed += counted;
            // Settled first: a frame with nothing new draws nothing again (otherwise the count below would prove nothing).
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            bool settled = false;
            for (int i = 0; i < 10 && !settled; i++)
            {
                renders = 0;
                window.CaptureRenderedFrame();
                settled = renders == 0;
            }
            Assert.True(settled, "The list kept drawing with nothing changed.");

            services.Icons.ClearCache();
            renders = 0;
            window.CaptureRenderedFrame();
            Assert.True(renders > 0, "The list was not drawn again with the icons made anew.");
            Assert.Equal(offset, list.HorizontalOffset);
        }
        finally
        {
            FileListControl.RenderTimed -= counted;
            window.Close();
            tab.Dispose();
            services.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private sealed record Lists(FileListControl Folder, double FolderOffset, FileListControl Result, double ResultOffset,
        int DraggedColumn, double DraggedWidth);

    /// <summary>
    /// The first panel on the Full profile, scrolled right; the second on a result list (columns of its own) with a
    /// border dragged in it, scrolled right as well.
    /// </summary>
    private static async Task<Lists> TwoScrolledLists(AppServices services, MainViewModel vm, Window window, string root)
    {
        var folderTab = vm.Workspace.Panels[0].ActiveTab!;
        var resultTab = vm.Workspace.Panels[1].ActiveTab!;
        folderTab.ColumnProfile = 2;
        var set = services.ResultSets.Create("Settings apply", "local fixture");
        set.Add(ItemRef.ForFileSystemPath(Path.Combine(root, "files", "a.txt"), EntryKind.File), "");
        resultTab.Navigate(ResultSetProvider.LocationOf(set));
        await Until(() => folderTab.Listing.State == ListingState.Complete && resultTab.Location?.Scheme == Schemes.ResultSet &&
                          resultTab.Listing.State == ListingState.Complete);
        var folder = ListOf(window, vm.Workspace.Panels[0]);
        var result = ListOf(window, vm.Workspace.Panels[1]);
        window.CaptureRenderedFrame();
        Assert.True(folder.HorizontalExtent > folder.HorizontalViewport, "the folder list should scroll sideways");
        Assert.True(result.HorizontalExtent > result.HorizontalViewport, "the result list should scroll sideways");

        // A fixed column (a width dragged in it is kept for this view only): its border scrolled into sight, then dragged
        // 30 pixels wider.
        var shown = (ColumnSpec[])ShownColumns.GetValue(result)!;
        int dragged = Array.FindIndex(shown, c => !c.Star);
        result.ScrollToHorizontalOffset(result.HorizontalOffset + result.ColumnRightEdge(dragged) - result.HorizontalViewport / 2);
        window.CaptureRenderedFrame();
        Assert.InRange(result.ColumnRightEdge(dragged), 10, result.HorizontalViewport - 10);
        double width = Width(result, dragged);
        var start = result.TranslatePoint(new Point(result.ColumnRightEdge(dragged), 8), window)!.Value;
        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(30, 0));
        window.MouseUp(start + new Point(30, 0), MouseButton.Left);
        window.CaptureRenderedFrame();
        Assert.True(Width(result, dragged) > width + 20, $"the drag made the column {Width(result, dragged)} wide, from {width}");

        folder.ScrollToHorizontalOffset(50);
        window.CaptureRenderedFrame();
        Assert.True(folder.HorizontalOffset > 0);
        Assert.True(result.HorizontalOffset > 0);
        return new Lists(folder, folder.HorizontalOffset, result, result.HorizontalOffset, dragged, Width(result, dragged));
    }

    /// <summary>Settings → Columns on a profile: its second column removed, then OK.</summary>
    private static async Task RemoveSecondColumn(MainViewModel vm, Window window, int profile)
    {
        var showing = vm.ShowSettingsAsync(SettingsDialog.ColumnsPage, profile);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var card = window.GetVisualDescendants().OfType<DialogCard>().Single();
        Assert.Equal(profile, card.GetVisualDescendants().OfType<ListBox>()
            .Single(box => AutomationProperties.GetName(box) == "Column profiles").SelectedIndex);
        card.GetVisualDescendants().OfType<ListBox>()
            .Single(box => AutomationProperties.GetName(box) == "Columns of the selected profile").SelectedIndex = 1;
        card.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Remove column")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ClickFooter(window, "OK");
        await showing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        window.CaptureRenderedFrame();
    }

    private static double Width(FileListControl list, int column) => list.ColumnRightEdge(column) - list.ColumnLeftEdge(column);

    private static FileListControl ListOf(Window window, PanelViewModel panel) => window.GetVisualDescendants().OfType<PanelView>()
        .Single(view => ReferenceEquals(view.DataContext, panel)).FindControl<FileListControl>("List")!;

    private static void ClickFooter(Window window, string text) => window.GetVisualDescendants().OfType<Button>()
        .Single(b => b.Content as string == text && b.IsEffectivelyEnabled &&
            b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("dialogFooter")))
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
