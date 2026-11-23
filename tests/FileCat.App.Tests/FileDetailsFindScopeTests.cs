using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.HiddenData;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class FileDetailsFindScopeTests
{
    private sealed class Data : IHiddenData
    {
        public bool IsSupported => true;
        public IReadOnlyList<HiddenItem> List(string path) => [new("fixture", HiddenKind.Attribute, 0)];
        public byte[] Read(string path, HiddenItem item, int max) => [];
        public Stream Open(string path, HiddenItem item) => new MemoryStream();
        public void Delete(string path, HiddenItem item) => throw new NotSupportedException();
    }
    private sealed class TestPlatform : PortablePlatform
    {
        public TestPlatform() => HiddenData = new Data();
    }

    [AvaloniaFact]
    public async Task Find_supplementary_command_opens_captured_source_in_new_workspace_tab_without_replacing_results()
    {
        var previous = PlatformFactory.WindowsFactory;
        (AppServices, MainViewModel, MainWindow, string) fixture;
        try
        {
            if (OperatingSystem.IsWindows()) PlatformFactory.WindowsFactory = () => new TestPlatform();
            fixture = AccessibilityTests.OpenMainWindow();
        }
        finally { PlatformFactory.WindowsFactory = previous; }
        var (services, vm, main, root) = fixture;
        try
        {
            var ct = TestContext.Current.CancellationToken;
            async Task Until(Func<bool> condition)
            {
                for (int i = 0; i < 300 && !condition(); i++) await Task.Delay(10, ct);
                Assert.True(condition());
            }
            await Until(() => vm.ActiveTab?.Listing.State == Core.Listing.ListingState.Complete);
            vm.Execute(CommandIds.FindFiles);
            await Until(() => FindWindow.OpenWindows.Count == 1);
            var find = FindWindow.OpenWindows.Single();
            find.NamesBox.Text = "a.txt";
            find.NamesBox.Focus();
            find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Until(() => find.IsIdle && find.ResultsTab?.Listing.VisibleCount == 1);
            var results = find.ResultsTab!;
            results.Listing.FocusName("a.txt");
            var originalLocation = results.Location;
            string source = results.Listing.GetItemRef(results.Listing.FocusedStoreIndex).FileSystemPath!;
            int tabs = vm.Workspace.Panels.Sum(p => p.Tabs.Count);
            vm.ActiveTab!.Navigate(Location.FileSystem(root));
            find.Run(CommandIds.HiddenData);
            await Until(() => vm.Workspace.Panels.Sum(p => p.Tabs.Count) == tabs + 1);
            Assert.Equal(originalLocation, results.Location);
            Assert.Equal(1, results.Listing.VisibleCount);
            Assert.Equal("a.txt", results.Listing.GetVisible(0).Name);
            Assert.Equal(Schemes.HiddenData, vm.Workspace.ActiveTab!.Location!.Scheme);
            Assert.Equal(source, vm.Workspace.ActiveTab.Location.Container!.Path);
            find.Close();
            Assert.Equal(Schemes.HiddenData, vm.Workspace.ActiveTab.Location.Scheme);
        }
        finally
        {
            foreach (var find in FindWindow.OpenWindows.ToArray()) find.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    private const string NotLocal = "Focus a file or folder on a filesystem; archive, remote and virtual display paths are not local objects.";

    private static string DetailsTitle(AppServices services) => services.Commands.Get(CommandIds.FileRecord)!.Title;

    /// <summary>Find's own result-menu items (the list's context menu is built from the same items).</summary>
    private static MenuItem FindDetailsItem(FindWindow find, string title)
    {
        var items = (List<Control>)typeof(FindWindow).GetMethod("FileItems", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(find, null)!;
        return Assert.Single(items.OfType<MenuItem>(), item => item.Header as string == title);
    }

    private static (bool Enabled, string? Reason) MenuState(MenuItem item) => Assert.IsType<Func<(bool, string?)>>(item.Tag)();

    private static async Task<FindWindow> FindAsync(MainViewModel vm, string names, bool insideArchives = false)
    {
        vm.Execute(CommandIds.FindFiles);
        await FileDetailsWindowTests.Until(() => FindWindow.OpenWindows.Count == 1, "Find opens");
        var find = FindWindow.OpenWindows.Single();
        find.SetInsideArchives(insideArchives);
        find.NamesBox.Text = names;
        find.NamesBox.Focus();
        find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await FileDetailsWindowTests.Until(() => find.IsIdle && find.ResultsTab?.Listing.VisibleCount == 1, "one result is found");
        return find;
    }

    [AvaloniaFact]
    public async Task Details_describe_only_the_focused_item_while_other_rows_are_marked()
    {
        var records = new FileDetailsWindowTests.CountingRecords();
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(records);
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            var tab = vm.ActiveTab!;
            await FileDetailsWindowTests.Until(() => tab.Listing.State == Core.Listing.ListingState.Complete && tab.Listing.FindStoreIndex("b.txt") >= 0, "the folder is listed");
            tab.Listing.MarkNames(["b.txt"], true);
            tab.Listing.FocusName("a.txt");
            vm.Execute(CommandIds.FileRecord);
            await FileDetailsWindowTests.Until(() => opened.Count == 1 && opened[0].Text.Contains("#summary-1#"), "the summary is published");
            string a = Path.Join(root, "files", "a.txt");
            Assert.Equal([a], records.Paths);
            Assert.Contains("Focused item only; marked items are not included.", opened[0].Text);
            Assert.DoesNotContain("b.txt", opened[0].Text);
            Assert.Contains("a.txt", opened[0].Title);
            Assert.Equal(1, tab.Listing.MarkedCount); // marks are neither read nor cleared
            Assert.Equal((1, 0, 0), (records.Summaries, records.Fulls, records.Legacy));
        }
        finally
        {
            foreach (var window in opened.Where(w => w.IsVisible)) window.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task Find_result_and_panel_context_menus_open_details_for_their_own_captured_item()
    {
        var records = new FileDetailsWindowTests.CountingRecords();
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(records);
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            var tab = vm.ActiveTab!;
            await FileDetailsWindowTests.Until(() => tab.Listing.State == Core.Listing.ListingState.Complete && tab.Listing.FindStoreIndex("b.txt") >= 0, "the folder is listed");
            string a = Path.Join(root, "files", "a.txt"), b = Path.Join(root, "files", "b.txt");
            var find = await FindAsync(vm, "a.txt");
            var results = find.ResultsTab!;
            results.Listing.FocusName("a.txt");
            var resultLocation = results.Location;
            // The main panel focuses and marks other rows; Find keeps its own captured result.
            tab.Listing.FocusName("b.txt");
            tab.Listing.MarkNames(["a.txt"], true);

            var fromFind = FindDetailsItem(find, DetailsTitle(services));
            Assert.Equal<(bool, string?)>((true, null), MenuState(fromFind));
            fromFind.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await FileDetailsWindowTests.Until(() => opened.Count == 1 && opened[0].Text.Contains("#summary-1#"), "Find's details are published");
            // Find reports its root in the file system's own spelling; Windows paths compare without case.
            var paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            Assert.Equal([a], records.Paths, paths);
            Assert.Equal(resultLocation, results.Location);
            Assert.Equal(1, results.Listing.VisibleCount);

            // The panel's own context menu describes the panel's focused row, not its marks or Find's result.
            var menu = ContextMenuFactory.Build(vm);
            var fromPanel = Assert.Single(((IEnumerable<Control>)menu.ItemsSource!).OfType<MenuItem>(), item => item.Header as string == DetailsTitle(services));
            fromPanel.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await FileDetailsWindowTests.Until(() => opened.Count == 2 && opened[1].Text.Contains("#summary-2#"), "the panel's details are published");
            Assert.Equal([a, b], records.Paths, paths);
            Assert.Contains("b.txt", opened[1].Title);
            Assert.Equal(1, tab.Listing.MarkedCount);
            Assert.Equal((2, 0, 0), (records.Summaries, records.Fulls, records.Legacy));
        }
        finally
        {
            foreach (var window in opened.Where(w => w.IsVisible)) window.Close();
            foreach (var find in FindWindow.OpenWindows.ToArray()) find.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    /// <summary>A remote-style provider: every listing, content, preparation and retention request is counted.</summary>
    private sealed class RemoteProvider : ResourceProvider
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public override string Scheme => "details-remote-fixture";
        public override string GetDisplayPath(Location location) => "remote://fixture" + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            sink.AddBatch([new EntryData("remote.txt", EntryKind.File, 5, DateTime.UtcNow.Ticks)]);
            return Task.CompletedTask;
        }
        public override IContentSource? OpenContent(ItemRef item) { Interlocked.Increment(ref _calls); return null; }
        public override Task<Location>? PrepareAsync(Location location, CancellationToken ct) { Interlocked.Increment(ref _calls); return null; }
        public override IDisposable? RetainRead(Location location) { Interlocked.Increment(ref _calls); return null; }
    }

    private static (MenuItem Owner, MenuItem Item) MainMenuItem(MainWindow main, string id)
    {
        static IEnumerable<(MenuItem, MenuItem)> Walk(MenuItem owner) => (owner.ItemsSource ?? owner.Items).OfType<MenuItem>()
            .SelectMany(child => (child.Tag is string ? new[] { (owner, child) } : Array.Empty<(MenuItem, MenuItem)>()).Concat(Walk(child)));
        var menu = main.FindControl<Menu>("MainMenu")!;
        return Assert.Single((menu.ItemsSource ?? menu.Items).OfType<MenuItem>().SelectMany(Walk), pair => pair.Item2.Tag as string == id);
    }

    [AvaloniaFact]
    public async Task Archive_members_and_remote_items_show_the_accessible_reason_without_reader_or_provider_calls()
    {
        var records = new FileDetailsWindowTests.CountingRecords();
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(records);
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            var tab = vm.ActiveTab!;
            await FileDetailsWindowTests.Until(() => tab.Listing.State == Core.Listing.ListingState.Complete && tab.Listing.FindStoreIndex("a.txt") >= 0, "the folder is listed");
            string zip = Path.Join(root, "files", "members.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("member.txt").Open())) writer.Write("member");

            // An archive member found by Find: the result menu carries the reason and running it reads nothing.
            var find = await FindAsync(vm, "member.txt", insideArchives: true);
            find.ResultsTab!.Listing.FocusName("member.txt");
            var fromFind = FindDetailsItem(find, DetailsTitle(services));
            Assert.Equal<(bool, string?)>((false, NotLocal), MenuState(fromFind));
            fromFind.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            find.Close();

            // A remote item in the panel: the main menu explains it, the item menu omits the action, and running it reads nothing.
            var remote = new RemoteProvider();
            services.Providers.Register(remote);
            var location = new Location(remote.Scheme, "/home", session: "fixture-profile");
            tab.Navigate(location);
            await FileDetailsWindowTests.Until(() => Equals(tab.Location, location) && tab.Listing.State == Core.Listing.ListingState.Complete &&
                tab.Listing.FindStoreIndex("remote.txt") >= 0, "the remote fixture is listed");
            tab.Listing.FocusName("remote.txt");
            int calls = remote.Calls;
            var availability = vm.GetAvailability(CommandIds.FileRecord);
            Assert.False(availability.Enabled);
            Assert.Equal(NotLocal, availability.Reason);
            var (owner, item) = MainMenuItem(main, CommandIds.FileRecord);
            owner.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
            Assert.False(item.IsEnabled);
            Assert.Equal(NotLocal, AutomationProperties.GetHelpText(item));
            Assert.Equal(NotLocal, ToolTip.GetTip(item));
            Assert.DoesNotContain(((IEnumerable<Control>)ContextMenuFactory.Build(vm).ItemsSource!).OfType<MenuItem>(),
                menuItem => menuItem.Header as string == DetailsTitle(services));
            vm.ClearNotification();
            await vm.ExecuteAsync(CommandIds.FileRecord);
            Assert.Equal(NotLocal, vm.Notification);
            Assert.Equal(calls, remote.Calls);

            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Empty(opened);
            Assert.Empty(records.Paths);
            Assert.Equal(0, records.Legacy);
        }
        finally
        {
            foreach (var window in opened.Where(w => w.IsVisible)) window.Close();
            foreach (var find in FindWindow.OpenWindows.ToArray()) find.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }
}
