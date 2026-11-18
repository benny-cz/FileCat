using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task DiskSpaceEntryToolsAndSearchUseTheActiveFolderAndRestoreHiddenSessions()
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var profile = new PreparationFixture();
        string source = Path.Combine(profile.Root, "source");
        string other = Path.Combine(profile.Root, "other");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(other);
        string file = Path.Combine(source, "scan-this.bin");
        File.WriteAllText(file, "original");
        var vm = new MainViewModel(profile.Services);
        var main = new MainWindow(vm, null);
        vm.Initialize(null);
        main.Show();
        try
        {
            var tab = vm.ActiveTab!;
            tab.Navigate(Location.FileSystem(source));
            Assert.True(tab.SetFilter("*.does-not-match"));
            Frame(main);
            var search = vm.CommandSearchEntries();
            foreach (string term in new[] { "disk space", "storage", "largest files", "folder usage", "disk map" })
                Assert.Contains(CommandSearch.Rank(term, search, []), entry => entry.Id == CommandIds.DiskSpace && entry.Enabled);
            Assert.False(vm.GetAvailability(CommandIds.DiskSpaceReview).Enabled);
            Assert.Empty(profile.Services.Keymap.GetChords(CommandIds.DiskSpace));
            var tools = main.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().Single(menu => Equals(menu.Header, "_Tools"));
            var command = tools.Items.OfType<MenuItem>().Single(menu => Equals(menu.Tag, CommandIds.DiskSpace));
            Click(command);
            await Until(() => profile.Services.DiskSpace.Window is { IsVisible: true });
            var analyzer = profile.Services.DiskSpace.Window!;
            Assert.Equal(new[] { source }, DiskSpaceEntryRoots(analyzer));

            Click(AnalyzerButton(analyzer, "Continue in FileCat"));
            Assert.False(analyzer.IsVisible);
            tab.Navigate(Location.FileSystem(other));
            vm.RunFromSearch(CommandIds.DiskSpace);
            await Until(() => analyzer.IsVisible);
            Assert.Same(analyzer, profile.Services.DiskSpace.Window);
            Assert.Equal(new[] { source }, DiskSpaceEntryRoots(analyzer));

            Click(AnalyzerButton(analyzer, "Start analysis"));
            await Until(() => analyzer.HasTemporaryWork && analyzer.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text?.Contains("Finished", StringComparison.Ordinal) == true), timeout: TimeSpan.FromSeconds(20));
            var filter = analyzer.GetVisualDescendants().OfType<TextBox>()
                .Single(box => box.PlaceholderText == "Search within observations…");
            Assert.True(string.IsNullOrEmpty(filter.Text));
            Assert.Equal(other, tab.Location!.Path);
            Assert.Equal("original", File.ReadAllText(file));

            Assert.False(vm.CanCloseImmediately());
            var confirm = vm.ConfirmExitWithActiveWorkAsync();
            var dialogs = Assert.IsType<OverlayDialogService>(vm.Dialogs);
            await Until(() => dialogs.IsOpen);
            var keep = AnalyzerButton(main, "Keep FileCat open");
            Assert.True(keep.IsDefault);
            Click(keep);
            Assert.False(await confirm.WaitAsync(TimeSpan.FromSeconds(10)));

            await vm.ExecuteAsync(CommandIds.DiskSpace);
            Assert.Same(analyzer, profile.Services.DiskSpace.Window);
            Assert.Equal(new[] { other }, DiskSpaceEntryRoots(analyzer));
        }
        finally { await CloseDiskSpaceEntryAsync(profile.Services, vm, main); }
    }

    [AvaloniaFact]
    public async Task DiskSpaceEntryContextUsesAnInvokedFolderOrDriveAndOnlyExplicitMarks()
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var profile = new PreparationFixture();
        string source = Path.Combine(profile.Root, "source");
        string first = Path.Combine(source, "first");
        string second = Path.Combine(source, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(source, "file.txt"), "original");
        var vm = new MainViewModel(profile.Services);
        var main = new MainWindow(vm, null);
        vm.Initialize(null);
        main.Show();
        try
        {
            var tab = vm.ActiveTab!;
            tab.Navigate(Location.FileSystem(source));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 3);
            int firstIndex = StoreIndex("first"), secondIndex = StoreIndex("second"), fileIndex = StoreIndex("file.txt");
            Assert.Null(MainViewModel.MarkedDiskSpaceRoots(tab));
            tab.Listing.SetStoreMark(firstIndex, true);
            tab.Listing.SetStoreMark(secondIndex, true);
            tab.Listing.SetFocus(tab.Listing.GetVisibleIndex(firstIndex));
            var invoked = MainViewModel.DiskSpaceRoot(tab, secondIndex);
            var marked = MainViewModel.MarkedDiskSpaceRoots(tab);
            Assert.Equal(second, invoked);
            Assert.Equal(new[] { first, second }, marked!.Order(StringComparer.Ordinal));
            var menu = ContextMenuFactory.Build(vm, invoked, marked);

            tab.Listing.SetStoreMark(fileIndex, true);
            Assert.Null(MainViewModel.DiskSpaceRoot(tab, fileIndex));
            Assert.Null(MainViewModel.MarkedDiskSpaceRoots(tab));
            tab.Navigate(new Location(Schemes.Computer, ""));
            Click(ContextItem(menu, MainViewModel.AnalyzeDiskSpaceTitle));
            var analyzer = profile.Services.DiskSpace.Window!;
            Assert.Equal(new[] { second }, DiskSpaceEntryRoots(analyzer));
            Click(ContextItem(menu, MainViewModel.AnalyzeSelectedFoldersTitle));
            Assert.Equal(new[] { first, second }, DiskSpaceEntryRoots(analyzer).Order(StringComparer.Ordinal));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount > 0, timeout: TimeSpan.FromSeconds(20));
            string? drive = Enumerable.Range(0, tab.Listing.Store.Count)
                .Select(index => MainViewModel.DiskSpaceRoot(tab, index)).FirstOrDefault(path => path is not null);
            Assert.NotNull(drive);
            Click(ContextItem(ContextMenuFactory.Build(vm, drive), MainViewModel.AnalyzeDiskSpaceTitle));
            Assert.Equal(new[] { drive! }, DiskSpaceEntryRoots(analyzer));
            Assert.False(analyzer.HasTemporaryWork);
            await vm.ExecuteAsync(CommandIds.DiskSpace);
            Assert.Equal(new[] { "" }, DiskSpaceEntryRoots(analyzer));

            int StoreIndex(string name) => Enumerable.Range(0, tab.Listing.Store.Count).Single(index => tab.Listing.Store[index].Name == name);
        }
        finally { await CloseDiskSpaceEntryAsync(profile.Services, vm, main); }
    }

    private static string[] DiskSpaceEntryRoots(DiskSpaceWindow window)
    {
        Frame(window);
        return window.GetVisualDescendants().OfType<TextBox>()
            .Where(box => AutomationLabel(box)?.StartsWith("Analysis location ", StringComparison.Ordinal) == true)
            .Select(box => box.Text ?? "").ToArray();
    }

    private static Button AnalyzerButton(Window window, string label)
    {
        Frame(window);
        return window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == label);
    }

    private static MenuItem ContextItem(ContextMenu menu, string label) =>
        menu.Items.OfType<MenuItem>().Single(item => item.Header as string == label);

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task CloseDiskSpaceEntryAsync(AppServices services, MainViewModel vm, MainWindow main)
    {
        if (vm.Dialogs is OverlayDialogService dialogs) dialogs.CancelAll();
        if (services.DiskSpace.Window is { } analyzer)
        {
            analyzer.ForceClose();
            await analyzer.WaitForIndexedCloseAsync();
        }
        main.Close();
        foreach (var tab in vm.Workspace.Panels.SelectMany(panel => panel.Tabs).ToList()) tab.Dispose();
    }
}
