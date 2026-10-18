using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class FindOriginScopeTests
{
    [AvaloniaFact]
    public async Task Find_command_from_this_pc_does_not_switch_to_local_home()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            vm.ActiveTab!.Navigate(new Location(Schemes.Computer, string.Empty));
            await WaitFor(() => vm.ActiveTab?.Location?.Scheme == Schemes.Computer);
            vm.Execute(CommandIds.FindFiles);
            await WaitFor(() => FindWindow.OpenWindows.Count > 0);
            var find = Assert.Single(FindWindow.OpenWindows);
            find.UpdateLayout();
            Assert.Equal(string.Empty, find.Criteria.LookIn);
            Assert.False(FindButton(find).IsEnabled);
            Assert.Contains("This PC", ScopeGuidance(find).Text);
        }
        finally
        {
            foreach (var find in FindWindow.OpenWindows.ToArray()) find.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(Schemes.Sftp)]
    [InlineData(Schemes.Ftp)]
    [InlineData(Schemes.Zip)]
    [InlineData(Schemes.Archive)]
    [InlineData(Schemes.Computer)]
    [InlineData(Schemes.Network)]
    public void Unsupported_origin_starts_without_a_local_scope_or_search(string scheme)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        FindWindow? find = null;
        try
        {
            var origin = new Location(scheme, "origin", scheme is Schemes.Zip or Schemes.Archive
                ? Location.FileSystem(Path.Combine(root, "archive.zip")) : null);
            Assert.Equal(string.Empty, MainViewModel.FindInitialRoot(origin));
            find = FindWindow.Open(vm, root, null, origin);
            find.UpdateLayout();
            Assert.Equal(string.Empty, find.Criteria.LookIn);
            Assert.Contains("cannot search", ScopeGuidance(find).Text, StringComparison.OrdinalIgnoreCase);
            Assert.False(FindButton(find).IsEnabled);
            find.StartSearch(RefineMode.Replace); // Menu and shortcut paths keep the same admission guard.
            Assert.Null(find.ResultsTab);
            Assert.Contains("Choose local folders", find.GetVisualDescendants().OfType<TextBlock>()
                .Single(b => b.Classes.Contains("error") && b.IsVisible).Text);
        }
        finally { find?.Close(); AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Explicit_local_choice_searches_only_the_chosen_folder()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        FindWindow? find = null;
        try
        {
            find = FindWindow.Open(vm, string.Empty, null, new Location(Schemes.Sftp, "/remote", session: "profile"));
            find.UpdateLayout();
            var home = find.GetVisualDescendants().OfType<Button>().Single(b =>
                AutomationProperties.GetName(b) == "Choose local home as Find scope");
            home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), find.Criteria.LookIn);
            Assert.Null(find.ResultsTab);
            Assert.True(FindButton(find).IsEnabled);
            var lookIn = find.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "Look in");
            lookIn.Text = Path.Combine(root, "files");
            find.NamesBox.Text = "a.txt";
            find.StartSearch(RefineMode.Replace);
            await WaitFor(() => find.IsIdle);
            Assert.Equal(["a.txt"], find.Found);
            Assert.Contains("local folders shown", ScopeGuidance(find).Text);
        }
        finally { find?.Close(); AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Load_on_open_preset_names_its_scope_or_still_requires_a_choice(bool hasFolders)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        FindWindow? find = null;
        try
        {
            string local = Path.Combine(root, "files");
            services.Settings.SavedSearches.Add(new SavedSearch
            {
                Name = "Owned preset", LoadOnOpen = true,
                Criteria = new SearchCriteria { Names = "*.txt", LookIn = hasFolders ? local : string.Empty }
            });
            find = FindWindow.Open(vm, string.Empty, null, new Location(Schemes.Sftp, "/remote"));
            Assert.Equal(hasFolders ? local : string.Empty, find.Criteria.LookIn);
            Assert.Equal(hasFolders, FindButton(find).IsEnabled);
            Assert.Contains("Owned preset", ScopeGuidance(find).Text);
            Assert.Contains(hasFolders ? "its local folders" : "has no local folders", ScopeGuidance(find).Text);
            Assert.Null(find.ResultsTab); // Loading saved criteria never launches a search.
        }
        finally { find?.Close(); AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Saved_criteria_without_folders_keep_an_already_chosen_local_scope()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        FindWindow? find = null;
        try
        {
            find = FindWindow.Open(vm, string.Empty, null, new Location(Schemes.Sftp, "/remote"));
            string local = Path.Combine(root, "files");
            find.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "Look in").Text = local;
            find.Apply(new SearchCriteria { Names = "*.txt" }, "No folders");
            Assert.Equal(local, find.Criteria.LookIn);
            Assert.True(FindButton(find).IsEnabled);
            Assert.Contains("keeps your previously chosen local scope", ScopeGuidance(find).Text);
        }
        finally { find?.Close(); AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Result_snapshot_keeps_its_scope_when_a_saved_search_loads()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        FindWindow? find = null;
        try
        {
            var set = services.ResultSets.Create("Owned results", "local fixture");
            set.Add(ItemRef.ForFileSystemPath(Path.Combine(root, "files", "a.txt"), EntryKind.File), "");
            services.Settings.SavedSearches.Add(new SavedSearch
            {
                Name = "Local preset", LoadOnOpen = true,
                Criteria = new SearchCriteria { Names = "*.txt", LookIn = Path.Combine(root, "elsewhere") }
            });
            find = FindWindow.Open(vm, string.Empty, set, ResultSetProvider.LocationOf(set));
            Assert.Contains("Owned results", find.GetVisualDescendants().OfType<TextBox>()
                .Single(b => AutomationProperties.GetName(b) == "Search within").Text);
            Assert.Contains("remains within this result-set snapshot", ScopeGuidance(find).Text);
            Assert.True(FindButton(find).IsEnabled);
            Assert.Null(find.ResultsTab);
        }
        finally { find?.Close(); AccessibilityTests.Close(services, window, root); }
    }

    private static TextBlock ScopeGuidance(FindWindow find) => find.GetVisualDescendants().OfType<TextBlock>()
        .Single(b => AutomationProperties.GetName(b) == "Find scope guidance");

    private static Button FindButton(FindWindow find) => find.GetVisualDescendants().OfType<Button>()
        .Single(b => b.Content as string == "Find");

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), "Find did not reach the expected state.");
    }
}
