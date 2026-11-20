using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
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
            var panelFilter = analyzer.GetVisualDescendants().OfType<CheckBox>().Single(box =>
                AutomationLabel(box) == "Use panel filter for display after scan");
            Assert.False(panelFilter.IsChecked == true);
            Assert.False(PrivateIndexChoice(analyzer).IsChecked == true);

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
            Assert.DoesNotContain(analyzer.GetVisualDescendants().OfType<Button>(),
                button => button.IsEffectivelyVisible && button.Content as string == "Open recorded result");
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
            var tabs = analyzer.GetVisualDescendants().OfType<TabControl>().Single();
            Assert.Equal(1, tabs.SelectedIndex);
            Click(AnalyzerButton(analyzer, "Continue in FileCat"));
            Assert.False(analyzer.IsVisible);
            confirm = vm.ConfirmExitWithActiveWorkAsync();
            await Until(() => dialogs.IsOpen);
            Click(AnalyzerButton(main, "Review and save"));
            Assert.False(await confirm.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(analyzer.IsVisible);
            Assert.Equal(0, tabs.SelectedIndex);
            Assert.True(analyzer.HasTemporaryWork);
        }
        finally { await CloseDiskSpaceEntryAsync(profile.Services, vm, main); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiskSpaceEntryStandardIndexChoiceUsesVerifiedScratchOrExplainsMemoryFallback(bool unavailable)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        string source = Path.Combine(profile.Root, "source"); Directory.CreateDirectory(source);
        string file = Path.Combine(source, "captured.bin"); File.WriteAllText(file, "keep");
        if (unavailable) await CorruptProfileForMemoryAsync(profile);
        var originalState = unavailable ? File.ReadAllBytes(profile.Services.Paths.AnalysisPrivateStateFile) : null;
        profile.Services.DiskSpace.OpenFromTools([source]);
        var window = profile.Services.DiskSpace.Window!;
        try
        {
            var index = PrivateIndexChoice(window);
            Assert.False(index.IsChecked == true);
            index.IsChecked = true;
            Click(AnalyzerButton(window, "Start analysis"));
            Button? RecordedResult() => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
                button.IsEffectivelyVisible && button.Content as string == "Open recorded result");
            if (unavailable)
            {
                await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
                    text.Text?.Contains("Secure private storage is unavailable. Using bounded memory", StringComparison.Ordinal) == true));
                Assert.Null(RecordedResult());
                Assert.Equal(originalState, File.ReadAllBytes(profile.Services.Paths.AnalysisPrivateStateFile));
            }
            else
            {
                await Until(() => RecordedResult() is { IsEnabled: true }, timeout: TimeSpan.FromSeconds(30));
                Click(RecordedResult()!);
                await Until(() => window.SessionCount == 2 && window.GetVisualDescendants().OfType<ListBox>().Any(list =>
                    list.IsEffectivelyVisible && AutomationLabel(list) == "Indexed analysis entries" && list.ItemCount == 1), timeout: TimeSpan.FromSeconds(30));
            }
            Assert.Equal("keep", File.ReadAllText(file));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task DiskSpaceEntryChangingIndexChoiceRequiresReviewOfNewResolvedBinding()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Requires Unix symbolic-link creation.");
        OrdinaryPreparation(); using var profile = new PreparationFixture();
        string first = Path.Combine(profile.Root, "first"), second = Path.Combine(profile.Root, "second"), alias = Path.Combine(profile.Root, "alias");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "first.bin"), "first");
        File.WriteAllText(Path.Combine(second, "second.bin"), "second");
        Directory.CreateSymbolicLink(alias, first);
        using var model = new DiskSpaceViewModel(profile.Services);
        try
        {
            await model.StartAsync([alias]);
            Assert.Equal(AnalysisPhase.Ready, model.Phase);
            Assert.EndsWith("/first", model.ResolvedTargets, StringComparison.Ordinal);
            File.Delete(alias); Directory.CreateSymbolicLink(alias, second);
            await model.StartAsync([alias], [], false, usePrivateIndex: true);
            Assert.Equal(AnalysisPhase.Ready, model.Phase);
            Assert.EndsWith("/second", model.ResolvedTargets, StringComparison.Ordinal);
            File.Delete(alias); Directory.CreateSymbolicLink(alias, first);
            await model.StartAsync([alias], [], false, usePrivateIndex: true);
            await Until(() => model.Phase == AnalysisPhase.Finished);
            await model.RebuildAsync(true);
            await Until(() => model.CanOpenRecordedResult);
            await using var captured = await model.AcquireRecordedDatasetAsync(CancellationToken.None);
            var rows = await captured.ReadPathsAsync(0);
            Assert.Contains(rows.Rows, row => row.Name == "second.bin");
            Assert.DoesNotContain(rows.Rows, row => row.Name == "first.bin");
        }
        finally { model.Dispose(); await model.CloseCompletion; }
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

    private static CheckBox PrivateIndexChoice(Window window) => window.GetVisualDescendants().OfType<CheckBox>()
        .Single(box => AutomationLabel(box) == "Use a private index for standard analysis");

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
