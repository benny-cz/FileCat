using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// Command-line arguments open locations, named workspaces and list files (plan §19.1), at start and when a second
/// launch forwards them. --workspace and --list were read and then ignored (found reviewing V23 B12).
/// </summary>
public sealed class StartupArgumentsTests
{
    private static async Task Until(Func<bool> condition, string what)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), what);
    }

    [AvaloniaFact]
    public async Task A_list_file_opens_as_a_result_set_without_contacting_the_servers_it_names()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string list = Path.Combine(root, "picked.lst");
            File.WriteAllLines(list, [Path.Combine(root, "files", "a.txt"), Path.Combine("files", "b.txt"), @"\\filecat-test-server.invalid\share\x.txt", Path.Combine(root, "gone.txt")]);
            var panel = vm.Workspace.ActivePanel!;
            int tabs = panel.Tabs.Count;
            vm.OpenArguments(StartupOptions.Parse(["--list", list]));
            await Until(() => panel.Tabs.Count == tabs + 1, "the list opens in a new tab");
            var tab = panel.ActiveTab!;
            Assert.Equal(Schemes.ResultSet, tab.Location!.Scheme);
            Assert.StartsWith("Paths listed in " + list, tab.Banner);
            int Files() => Enumerable.Range(0, tab.Listing.VisibleCount).Count(i => tab.Listing.GetVisible(i).Kind == EntryKind.File);
            await Until(() => tab.Listing.State == ListingState.Complete && Files() == 2,
                $"the list's two files are listed (state {tab.Listing.State}, {tab.Listing.VisibleCount} rows, {Files()} files; banner {tab.Banner})");
            Assert.Equal(["a.txt", "b.txt"], Enumerable.Range(0, tab.Listing.VisibleCount).Select(i => tab.Listing.GetVisible(i))
                .Where(e => e.Kind == EntryKind.File).Select(e => e.Name).Order(StringComparer.Ordinal));

            // Forwarded by a second launch from another folder: the list's path is made full there.
            var forwarded = StartupOptions.Parse(StartupOptions.Parse(["--list", Path.GetRelativePath(Environment.CurrentDirectory, list)]).ToForwardArgs());
            Assert.Equal(list, forwarded.ListFile);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_named_workspace_opens_and_then_the_locations_given_with_it()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string files = Path.Combine(root, "files");
            string elsewhere = Directory.CreateDirectory(Path.Combine(root, "elsewhere")).FullName;
            var left = vm.Workspace.Panels[0];
            left.ActiveTab!.Navigate(Location.FileSystem(elsewhere));
            await Until(() => left.ActiveTab.Location?.Path == elsewhere, "the left panel shows the other folder");
            var state = vm.Workspace.ToState(null);
            state.Name = "Review";
            Directory.CreateDirectory(services.Paths.WorkspacesDirectory);
            JsonFileStore.Save(Path.Combine(services.Paths.WorkspacesDirectory, "Review.json"), state, StateJsonContext.Default.WorkspaceState);
            left.ActiveTab.Navigate(Location.FileSystem(files));
            await Until(() => left.ActiveTab.Location?.Path == files, "the left panel is back");

            vm.OpenArguments(StartupOptions.Parse(["--workspace", "Review", Path.Combine(files, "b.txt")]));
            await Until(() => vm.Workspace.Panels[0].Tabs.Any(t => t.Location?.Path == elsewhere), "the workspace's tabs are back");
            var active = vm.Workspace.ActivePanel!;
            await Until(() => active.ActiveTab?.Location?.Path == files, "the file's folder opens in a new tab of the workspace");
            var listing = active.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusedStoreIndex >= 0, "it lists, an item focused");
            Assert.Equal("b.txt", listing.GetItemRef(listing.FocusedStoreIndex).Name);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
