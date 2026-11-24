using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// I635: Properties follows the marks, as Windows' item menu always did. Several marked items open nothing and say that
/// Properties shows one item; one marked item shows its own properties wherever the cursor is; with no marks, the item
/// under the cursor does. Alt+Enter, the Properties command, Windows' item menu and FileCat's own item menu decide by
/// that one rule. Windows' property sheet is a recording shell here; Linux and macOS show FileCat's file-system record
/// instead (I589), read through a counting reader. Nothing native opens.
/// </summary>
public sealed class PropertiesMarksTests
{
    private const string OneItem = "Select one item to show its properties.";

    private static readonly FieldInfo OpenMenu = typeof(PanelView).GetField("_openItemMenu",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    [AvaloniaFact]
    public async Task Several_marked_items_open_no_properties_and_say_that_Properties_shows_one()
    {
        using var f = new Fixture();
        await f.ListedAsync();
        f.SetMark("a.txt", true);
        f.SetMark("b.txt", true);

        // Alt+Enter itself, with the cursor on one of the marked items, as the record of I635 found it.
        f.Focus("a.txt");
        f.List.Focus();
        f.Vm.ClearNotification();
        f.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Alt);
        f.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.Alt);
        f.AssertRefused();

        // The command with the cursor elsewhere: on an unmarked item, and on the parent row.
        foreach (string cursor in new[] { "plain.txt", ".." })
        {
            f.Focus(cursor);
            f.Vm.ClearNotification();
            await f.Vm.ExecuteAsync(CommandIds.Properties);
            f.AssertRefused();
        }
        Assert.Equal(2, f.Tab.Listing.MarkedCount); // the marks stay as they were
    }

    [AvaloniaFact]
    public async Task One_marked_item_shows_its_own_properties_wherever_the_cursor_is()
    {
        using var f = new Fixture();
        await f.ListedAsync();
        f.SetMark("b.txt", true);
        string marked = f.PathOf("b.txt");

        f.Focus("a.txt"); // an unmarked item
        await f.Vm.ExecuteAsync(CommandIds.Properties);
        await f.ShownAsync(marked);

        f.Focus(".."); // the parent row, which is never marked
        await f.Vm.ExecuteAsync(CommandIds.Properties);
        await f.ShownAsync(marked, marked);
        Assert.Null(f.Vm.Notification);
        Assert.Equal(1, f.Tab.Listing.MarkedCount);
    }

    [AvaloniaFact]
    public async Task Without_marks_the_item_under_the_cursor_shows_its_properties()
    {
        using var f = new Fixture();
        await f.ListedAsync();

        f.Focus("plain.txt");
        await f.Vm.ExecuteAsync(CommandIds.Properties);
        await f.ShownAsync(f.PathOf("plain.txt"));

        // The parent row stands for the folder itself, as before I635.
        f.Focus("..");
        await f.Vm.ExecuteAsync(CommandIds.Properties);
        await f.ShownAsync(f.PathOf("plain.txt"), f.Tab.Location!.Path);
        Assert.Null(f.Vm.Notification);
    }

    /// <summary>
    /// Windows' own item menu (a stand-in here: no desktop window, no Shell) is opened for the marked items, else the item
    /// under the cursor; choosing its Properties then decides by the command's rule.
    /// </summary>
    [AvaloniaFact]
    public async Task Windows_item_menu_Properties_decides_by_the_same_rule()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' own item menus are Windows-only; elsewhere FileCat's menu runs the Properties command."); return; }
        var menus = new List<IReadOnlyList<string?>>();
        PanelView.NativeItemMenuForTests = (paths, _, _, _) =>
        {
            menus.Add([.. paths]);
            return Task.FromResult(WindowsContextMenu.Result.Properties); // the user chose Properties there
        };
        try
        {
            using var f = new Fixture();
            await f.ListedAsync();
            string a = f.PathOf("a.txt"), b = f.PathOf("b.txt"), plain = f.PathOf("plain.txt");

            // Two marked items, the cursor on neither: the menu is theirs, and its Properties opens nothing.
            f.SetMark("a.txt", true);
            f.SetMark("b.txt", true);
            f.Focus("plain.txt");
            f.Vm.ClearNotification();
            f.View.ShowContextMenu(atFocus: true);
            await FileDetailsWindowTests.Until(() => menus.Count == 1, "Windows' menu opened for the marked items");
            Assert.Equal<string?>([a, b], menus[0]);
            f.AssertRefused();

            // One marked item, the cursor elsewhere: that item's properties.
            f.SetMark("a.txt", false);
            f.Vm.ClearNotification();
            f.View.ShowContextMenu(atFocus: true);
            await FileDetailsWindowTests.Until(() => menus.Count == 2, "Windows' menu opened for the marked item");
            Assert.Equal<string?>([b], menus[1]);
            await f.ShownAsync(b);
            Assert.Null(f.Vm.Notification);

            // No marks: the item under the cursor.
            f.SetMark("b.txt", false);
            f.View.ShowContextMenu(atFocus: true);
            await FileDetailsWindowTests.Until(() => menus.Count == 3, "Windows' menu opened for the cursor item");
            Assert.Equal<string?>([plain], menus[2]);
            await f.ShownAsync(b, plain);
            Assert.Null(f.Vm.Notification);
            Assert.Null(OpenMenu.GetValue(null)); // Windows' menu answered: FileCat's own stays closed
        }
        finally { PanelView.NativeItemMenuForTests = null; }
    }

    /// <summary>FileCat's own item menu (Linux, macOS, and Windows without its menu) runs the Properties command itself.</summary>
    [AvaloniaFact]
    public async Task FileCats_own_item_menu_Properties_decides_by_the_same_rule()
    {
        using var f = new Fixture();
        await f.ListedAsync();
        f.SetMark("a.txt", true);
        f.SetMark("b.txt", true);
        f.Focus("plain.txt");
        f.Vm.ClearNotification();
        f.ChooseFileCatMenuProperties();
        f.AssertRefused();

        f.SetMark("a.txt", false);
        f.Vm.ClearNotification();
        f.ChooseFileCatMenuProperties();
        await f.ShownAsync(f.PathOf("b.txt"));
        Assert.Null(f.Vm.Notification);
    }

    /// <summary>Windows' property sheet, recorded instead of shown.</summary>
    private sealed class RecordingShell : PortableShellServices
    {
        public List<string> Shown { get; } = [];

        public override bool ShowProperties(string path, nint owner = 0)
        {
            Shown.Add(path);
            return true;
        }
    }

    private sealed class ShellPlatform : PortablePlatform
    {
        public ShellPlatform(IShellServices shell) => Shell = shell;
    }

    /// <summary>
    /// The main window on a folder of a.txt, b.txt and plain.txt, with Properties observed: Windows' sheet through the
    /// recording shell, and on Linux and macOS the file-system record shown instead, through the counting reader.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly RecordingShell _shell = new();
        private readonly FileDetailsWindowTests.CountingRecords _records = new();
        private readonly IDisposable _tracking;

        public Fixture()
        {
            var previous = PlatformFactory.WindowsFactory;
            try
            {
                if (OperatingSystem.IsWindows()) PlatformFactory.WindowsFactory = () => new ShellPlatform(_shell);
                (Services, Vm, Window, Root) = AccessibilityTests.OpenMainWindow(
                    prepareFiles: folder => File.WriteAllText(System.IO.Path.Combine(folder, "plain.txt"), "plain"));
            }
            finally { PlatformFactory.WindowsFactory = previous; }
            Vm.FileRecordsForTests = _records;
            _tracking = FileDetailsWindowTests.TrackReportWindows(Reports);
        }

        public AppServices Services { get; }
        public MainViewModel Vm { get; }
        public MainWindow Window { get; }
        public string Root { get; }
        public List<ReportWindow> Reports { get; } = [];
        public TabViewModel Tab => Vm.ActiveTab!;
        public PanelView View => Window.GetVisualDescendants().OfType<PanelView>()
            .Single(view => ReferenceEquals(view.DataContext, Vm.Workspace.ActivePanel));
        public FileListControl List => View.FindControl<FileListControl>("List")!;

        /// <summary>The paths whose properties were shown, in order.</summary>
        public IReadOnlyList<string> Shown => OperatingSystem.IsWindows() ? [.. _shell.Shown] : [.. _records.Paths];

        public string PathOf(string name) => System.IO.Path.Join(Tab.Location!.Path, name);

        public Task ListedAsync() => FileDetailsWindowTests.Until(() => Tab.Listing.State == ListingState.Complete &&
            Tab.Listing.FindStoreIndex("plain.txt") >= 0 && Tab.Listing.VisibleCount == 4, "the folder is listed");

        public void SetMark(string name, bool marked)
        {
            int index = Tab.Listing.FindStoreIndex(name);
            Assert.True(index >= 0, name);
            Tab.Listing.SetStoreMark(index, marked);
            Assert.Equal(marked, Tab.Listing.IsMarked(index));
        }

        /// <summary>Puts the cursor on a named item, or on the parent row for "..".</summary>
        public void Focus(string name)
        {
            var listing = Tab.Listing;
            if (name == "..") listing.SetFocus(0);
            else Assert.True(listing.FocusName(name), name);
            Assert.True(listing.TryGetFocused(out var focused));
            Assert.Equal(name == ".." ? EntryKind.Parent : EntryKind.File, focused.Kind);
        }

        /// <summary>Properties said it shows one item, and nothing was shown.</summary>
        public void AssertRefused()
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(OneItem, Vm.Notification);
            Assert.Empty(Shown);
            Assert.Empty(Reports);
        }

        /// <summary>Waits until properties were shown for exactly these paths, in order.</summary>
        public async Task ShownAsync(params string[] expected)
        {
            await FileDetailsWindowTests.Until(() => Shown.Count >= expected.Length, $"properties shown for {expected.Length} item(s)");
            Assert.Equal(expected, Shown);
        }

        /// <summary>FileCat's own item menu, as a window without Windows' menu opens it, and its Properties chosen.</summary>
        public void ChooseFileCatMenuProperties()
        {
            View.ShowContextMenu(atFocus: true);
            var menu = Assert.IsType<ContextMenu>(OpenMenu.GetValue(null));
            try
            {
                var properties = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>()
                    .Single(item => item.Header as string == "Properties");
                properties.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            finally { menu.Close(); }
        }

        public void Dispose()
        {
            _tracking.Dispose();
            foreach (var report in Reports.Where(window => window.IsVisible)) report.Close();
            AccessibilityTests.Close(Services, Window, Root);
        }
    }
}
