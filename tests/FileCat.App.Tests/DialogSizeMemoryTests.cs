using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Search;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// Resizable dialogs with a <see cref="DialogBehavior.SizeKey"/> open at the size the user last gave them, within the window,
/// and the grip's double-click (or Home) goes back to the default. Only a finished resize is saved, never each drag step.
/// </summary>
public sealed class DialogSizeMemoryTests
{
    private static readonly Size Default = new(600, 420);

    [AvaloniaFact]
    public async Task A_dragged_size_is_saved_once_the_drag_ends_and_the_dialog_reopens_at_it()
    {
        var settings = new AppSettings();
        int saves = 0;
        var (window, dialogs) = Open(new DialogSizeStore(settings, () => saves++));
        try
        {
            var pending = Show(dialogs, "test.dragged");
            Layout(window);
            var card = Card(window);
            AssertSize(card, Default.Width, Default.Height);
            var start = Center(Grip(card), window);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(40, 30));
            Layout(window);
            window.MouseMove(start + new Vector(80, 60));
            Layout(window);
            Assert.Equal(0, saves);
            window.MouseUp(start + new Vector(80, 60), MouseButton.Left);
            Layout(window);
            Assert.Equal(1, saves);
            double width = card.Bounds.Width, height = card.Bounds.Height;
            Assert.True(width > Default.Width + 40 && height > Default.Height + 30, $"The drag left the dialog {width} by {height}.");
            var saved = Assert.Single(settings.DialogSizes!);
            Assert.Equal("test.dragged", saved.Key);
            Assert.InRange(saved.Width, width - 1, width + 1);
            Assert.InRange(saved.Height, height - 1, height + 1);
            dialogs.CancelAll();
            await pending;

            pending = Show(dialogs, "test.dragged");
            Layout(window);
            AssertSize(Card(window), width, height);
            dialogs.CancelAll();
            await pending;
            Assert.Equal(1, saves);
        }
        finally { Close(window, dialogs); }
    }

    [AvaloniaFact]
    public async Task Arrow_keys_save_once_they_pause_or_leave_the_grip_and_Home_restores_the_default()
    {
        var settings = new AppSettings();
        int saves = 0;
        var (window, dialogs) = Open(new DialogSizeStore(settings, () => saves++));
        try
        {
            var field = new TextBox { Text = "field" };
            var pending = Show(dialogs, "test.keys", field);
            Layout(window);
            var card = Card(window);
            var grip = Grip(card);
            Assert.True(grip.Focus());
            for (int i = 0; i < 3; i++) window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
            Layout(window);
            AssertSize(card, 648, 468);
            Assert.Equal(0, saves);
            await Until(() => saves > 0);
            Assert.Equal((648d, 468d), DialogSizeMemory.Find(settings, "test.keys"));
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, saves);

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.True(field.Focus());
            Assert.Equal(2, saves);
            Assert.Equal((664d, 468d), DialogSizeMemory.Find(settings, "test.keys"));

            Assert.True(grip.Focus());
            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Layout(window);
            AssertSize(card, Default.Width, Default.Height);
            Assert.Null(settings.DialogSizes);
            Assert.Equal(3, saves);
            dialogs.CancelAll();
            await pending;
            Assert.Equal(3, saves);
        }
        finally { Close(window, dialogs); }
    }

    [AvaloniaFact]
    public async Task Double_clicking_the_grip_restores_the_default_size_and_forgets_the_saved_one()
    {
        var settings = new AppSettings();
        Assert.True(DialogSizeMemory.Remember(settings, "test.reset", 900, 650));
        int saves = 0;
        var (window, dialogs) = Open(new DialogSizeStore(settings, () => saves++));
        try
        {
            var pending = Show(dialogs, "test.reset");
            Layout(window);
            var card = Card(window);
            AssertSize(card, 900, 650);
            var grip = Grip(card);
            Assert.Equal("Resize dialog", AutomationProperties.GetName(grip));
            Assert.Contains("Double-click", AutomationProperties.GetHelpText(grip));
            Assert.Contains("Double-click", Assert.IsType<string>(ToolTip.GetTip(grip)));
            var point = Center(grip, window);
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left);
            // A slight movement before the second release must not size the dialog away from the default again.
            window.MouseMove(point + new Vector(3, 2));
            window.MouseUp(point + new Vector(3, 2), MouseButton.Left);
            Layout(window);
            AssertSize(card, Default.Width, Default.Height);
            Assert.Null(settings.DialogSizes);
            Assert.Equal(1, saves);
            dialogs.CancelAll();
            await pending;

            pending = Show(dialogs, "test.reset");
            Layout(window);
            AssertSize(Card(window), Default.Width, Default.Height);
            dialogs.CancelAll();
            await pending;
            Assert.Equal(1, saves);
        }
        finally { Close(window, dialogs); }
    }

    [AvaloniaFact]
    public async Task A_saved_size_is_clamped_to_a_smaller_window_without_being_replaced()
    {
        var settings = new AppSettings();
        Assert.True(DialogSizeMemory.Remember(settings, "test.clamped", 900, 650));
        int saves = 0;
        var (window, dialogs) = Open(new DialogSizeStore(settings, () => saves++), width: 700, height: 500);
        try
        {
            var pending = Show(dialogs, "test.clamped");
            Layout(window);
            var card = Card(window);
            var host = Assert.IsType<Grid>(window.Content);
            AssertSize(card, host.Bounds.Width - 48, host.Bounds.Height - 48);
            window.Width = 1200;
            window.Height = 900;
            Layout(window);
            AssertSize(card, 900, 650);
            dialogs.CancelAll();
            await pending;

            window.Width = 640;
            window.Height = 480;
            Layout(window);
            pending = Show(dialogs, "test.clamped");
            Layout(window);
            AssertSize(Card(window), host.Bounds.Width - 48, host.Bounds.Height - 48);
            dialogs.CancelAll();
            await pending;
            Assert.Equal(0, saves);
            Assert.Equal((900d, 650d), DialogSizeMemory.Find(settings, "test.clamped"));
        }
        finally { Close(window, dialogs); }
    }

    [AvaloniaFact]
    public async Task A_dialog_without_a_key_keeps_no_size()
    {
        var settings = new AppSettings();
        int saves = 0;
        var (window, dialogs) = Open(new DialogSizeStore(settings, () => saves++));
        try
        {
            var pending = Show(dialogs, key: null);
            Layout(window);
            var card = Card(window);
            var grip = Grip(card);
            var start = Center(grip, window);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(60, 40));
            window.MouseUp(start + new Vector(60, 40), MouseButton.Left);
            Assert.True(grip.Focus());
            window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
            Layout(window);
            Assert.True(card.Bounds.Width > Default.Width + 60, $"The dialog stayed {card.Bounds.Width} wide.");
            dialogs.CancelAll();
            await pending;

            pending = Show(dialogs, key: null);
            Layout(window);
            AssertSize(Card(window), Default.Width, Default.Height);
            dialogs.CancelAll();
            await pending;
            Assert.Equal(0, saves);
            Assert.Null(settings.DialogSizes);
        }
        finally { Close(window, dialogs); }
    }

    [Fact]
    public void Remembered_sizes_stay_bounded_and_whole_with_the_most_recent_first()
    {
        var settings = new AppSettings();
        for (int i = 0; i < DialogSizeMemory.MaxEntries + 6; i++)
            Assert.True(DialogSizeMemory.Remember(settings, $"dialog.{i}", 500 + i, 400));
        Assert.Equal(DialogSizeMemory.MaxEntries, settings.DialogSizes!.Count);
        Assert.Equal($"dialog.{DialogSizeMemory.MaxEntries + 5}", settings.DialogSizes[0].Key);
        Assert.Null(DialogSizeMemory.Find(settings, "dialog.5"));
        Assert.Equal((506d, 400d), DialogSizeMemory.Find(settings, "dialog.6"));

        // Resizing an older dialog again makes it the most recent, without a second entry.
        Assert.True(DialogSizeMemory.Remember(settings, "dialog.6", 640.4, 480.6));
        Assert.Equal(("dialog.6", 640d, 481d), (settings.DialogSizes[0].Key, settings.DialogSizes[0].Width, settings.DialogSizes[0].Height));
        Assert.Single(settings.DialogSizes, entry => entry.Key == "dialog.6");
        Assert.False(DialogSizeMemory.Remember(settings, "dialog.6", 640, 481));
        Assert.True(DialogSizeMemory.Remember(settings, "dialog.wide", 20_000, 300));
        Assert.Equal((DialogSizeMemory.MaxExtent, 300d), DialogSizeMemory.Find(settings, "dialog.wide"));
        Assert.Equal(DialogSizeMemory.MaxEntries, settings.DialogSizes.Count);

        foreach (var (width, height) in new[] { (double.NaN, 300d), (300d, double.PositiveInfinity), (0d, 300d), (-5d, 300d) })
            Assert.False(DialogSizeMemory.Remember(settings, "dialog.unusable", width, height));
        foreach (var key in new[] { "", " ", "has space", "tab\tkey", "ünicode", new string('k', DialogSizeMemory.MaxKeyLength + 1) })
            Assert.False(DialogSizeMemory.Remember(settings, key, 500, 400));
        Assert.True(DialogSizeMemory.Forget(settings, "dialog.6"));
        Assert.False(DialogSizeMemory.Forget(settings, "dialog.6"));
        Assert.Null(DialogSizeMemory.Find(settings, "dialog.6"));
    }

    [AvaloniaFact]
    public void Unusable_and_surplus_saved_sizes_are_ignored_while_the_other_settings_load()
    {
        string root = Directory.CreateTempSubdirectory("filecat-dialog-sizes-").FullName;
        var paths = AppPaths.Resolve(overrideRoot: root);
        try
        {
            Directory.CreateDirectory(paths.SettingsDirectory);
            var entries = new List<string>
            {
                Entry("find.advanced", 820, 610),
                Entry("", 700, 500), Entry("has space", 700, 500), Entry("tab\tkey", 700, 500), Entry(new string('k', 65), 700, 500),
                Entry("negative", -700, 500), Entry("zero", 0, 500), Entry("oversized", 1e9, 500), Entry("tall", 700, 8193),
                Entry("find.advanced", 300, 300), "null", "{ \"Width\": 700, \"Height\": 500 }",
            };
            entries.AddRange(Enumerable.Range(0, 70).Select(i => Entry($"bulk.{i}", 700 + i, 500)));
            File.WriteAllText(paths.SettingsFile,
                "{ \"SchemaVersion\": 1, \"HistorySize\": 321, \"DialogSizes\": [" + string.Join(", ", entries) + "] }");

            using var services = AppServices.CreateForPaths(paths);
            Assert.Equal(StateLoadStatus.Loaded, services.SettingsStatus);
            Assert.Equal(321, services.Settings.HistorySize);
            var kept = services.Settings.DialogSizes!;
            Assert.Equal(DialogSizeMemory.MaxEntries, kept.Count);
            Assert.Equal(("find.advanced", 820d, 610d), (kept[0].Key, kept[0].Width, kept[0].Height));
            Assert.Equal("bulk.62", kept[^1].Key);
            Assert.Equal(kept.Count, kept.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count());
            foreach (var key in new[] { "has space", "negative", "zero", "oversized", "tall" })
                Assert.Null(services.DialogSizes.Find(key));
            Assert.Equal(new Size(820, 610), services.DialogSizes.Find("find.advanced"));

            // The next save writes the checked list, with the newest size first and the oldest past the bound dropped.
            services.DialogSizes.Remember("find.ignored-folders", new Size(640, 480));
            var saved = JsonSerializer.Deserialize(File.ReadAllText(paths.SettingsFile), StateJsonContext.Default.AppSettings)!;
            Assert.Equal(321, saved.HistorySize);
            Assert.Equal(DialogSizeMemory.MaxEntries, saved.DialogSizes!.Count);
            Assert.Equal(["find.ignored-folders", "find.advanced"], saved.DialogSizes.Take(2).Select(entry => entry.Key));
            Assert.Equal("bulk.61", saved.DialogSizes[^1].Key);
        }
        finally { Delete(root); }
    }

    [AvaloniaFact]
    public async Task An_older_settings_file_loads_and_a_saved_size_survives_a_restart()
    {
        string root = Directory.CreateTempSubdirectory("filecat-dialog-sizes-").FullName;
        var paths = AppPaths.Resolve(overrideRoot: root);
        try
        {
            Directory.CreateDirectory(paths.SettingsDirectory);
            File.WriteAllText(paths.SettingsFile, "{ \"SchemaVersion\": 1, \"HistorySize\": 321, \"ViewerWidth\": 850 }");
            using (var services = AppServices.CreateForPaths(paths))
            {
                Assert.Equal(StateLoadStatus.Loaded, services.SettingsStatus);
                Assert.Null(services.Settings.DialogSizes);
                Assert.Equal(321, services.Settings.HistorySize);
                var (window, dialogs) = Open(services.DialogSizes);
                try
                {
                    var pending = Show(dialogs, "test.restart");
                    Layout(window);
                    var card = Card(window);
                    Assert.True(Grip(card).Focus());
                    window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
                    window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
                    Layout(window);
                    AssertSize(card, 648, 468);
                    // Closing right away saves the size the keys left.
                    dialogs.CancelAll();
                    await pending;
                }
                finally { Close(window, dialogs); }
            }
            var saved = JsonSerializer.Deserialize(File.ReadAllText(paths.SettingsFile), StateJsonContext.Default.AppSettings)!;
            Assert.Equal(321, saved.HistorySize);
            Assert.Equal(850, saved.ViewerWidth);
            var entry = Assert.Single(saved.DialogSizes!);
            Assert.Equal(("test.restart", 648d, 468d), (entry.Key, entry.Width, entry.Height));

            using var restarted = AppServices.CreateForPaths(paths);
            var (again, reopened) = Open(restarted.DialogSizes);
            try
            {
                var pending = Show(reopened, "test.restart");
                Layout(again);
                AssertSize(Card(again), 648, 468);
                reopened.CancelAll();
                await pending;
            }
            finally { Close(again, reopened); }
        }
        finally { Delete(root); }
    }

    /// <summary>
    /// Settings FileCat may not write, as in an administrator session (here a profile from a newer FileCat): a saved size is
    /// used and a new one is kept until FileCat closes, but nothing is written and nothing fails.
    /// </summary>
    [AvaloniaFact]
    public async Task Read_only_settings_still_size_the_dialog_but_are_never_written()
    {
        string root = Directory.CreateTempSubdirectory("filecat-dialog-sizes-").FullName;
        var paths = AppPaths.Resolve(overrideRoot: root);
        try
        {
            Directory.CreateDirectory(paths.SettingsDirectory);
            File.WriteAllText(paths.SettingsFile,
                "{ \"SchemaVersion\": 999, \"DialogSizes\": [ { \"Key\": \"test.readonly\", \"Width\": 900, \"Height\": 650 } ] }");
            byte[] original = File.ReadAllBytes(paths.SettingsFile);
            using var services = AppServices.CreateForPaths(paths);
            Assert.True(services.SettingsReadOnly);
            var (window, dialogs) = Open(services.DialogSizes);
            try
            {
                var pending = Show(dialogs, "test.readonly");
                Layout(window);
                var card = Card(window);
                AssertSize(card, 900, 650);
                Assert.True(Grip(card).Focus());
                window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null);
                dialogs.CancelAll();
                await pending;
                Assert.Equal((852d, 650d), DialogSizeMemory.Find(services.Settings, "test.readonly"));

                pending = Show(dialogs, "test.readonly");
                Layout(window);
                AssertSize(Card(window), 852, 650);
                dialogs.CancelAll();
                await pending;
            }
            finally { Close(window, dialogs); }
            Assert.Equal(original, File.ReadAllBytes(paths.SettingsFile));
        }
        finally { Delete(root); }
    }

    /// <summary>
    /// An administrator session neither reads the user's settings nor writes them: a size given there lasts until FileCat
    /// closes, and keeping it reports no failure.
    /// </summary>
    [AvaloniaFact]
    public async Task An_administrator_session_keeps_sizes_in_memory_without_writing_or_failing()
    {
        if (!OperatingSystem.IsWindows() || !WindowsTokenAuthority.IsHighIntegrity)
            Assert.Skip("Run in a high-integrity Windows test process.");
        string root = Directory.CreateTempSubdirectory("filecat-dialog-sizes-").FullName;
        var paths = AppPaths.Resolve(overrideRoot: root);
        try
        {
            Directory.CreateDirectory(paths.SettingsDirectory);
            File.WriteAllText(paths.SettingsFile,
                "{ \"SchemaVersion\": 1, \"DialogSizes\": [ { \"Key\": \"test.elevated\", \"Width\": 900, \"Height\": 650 } ] }");
            byte[] original = File.ReadAllBytes(paths.SettingsFile);
            using var services = AppServices.CreateForPaths(paths);
            int failures = 0;
            services.StateSaveFailed += _ => failures++;
            Assert.True(services.SettingsReadOnly);
            var (window, dialogs) = Open(services.DialogSizes);
            try
            {
                var pending = Show(dialogs, "test.elevated");
                Layout(window);
                var card = Card(window);
                AssertSize(card, Default.Width, Default.Height);
                Assert.True(Grip(card).Focus());
                window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
                dialogs.CancelAll();
                await pending;

                pending = Show(dialogs, "test.elevated");
                Layout(window);
                AssertSize(Card(window), Default.Width + 48, Default.Height);
                dialogs.CancelAll();
                await pending;
            }
            finally { Close(window, dialogs); }
            Assert.Equal(0, failures);
            Assert.Equal(original, File.ReadAllBytes(paths.SettingsFile));
        }
        finally { Delete(root); }
    }

    [AvaloniaFact]
    public async Task Main_window_dialogs_keep_their_size_in_the_settings()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1200, height: 900);
        try
        {
            var dialogs = (OverlayDialogService)vm.Dialogs;
            var pending = FindDialogs.IgnoredFoldersAsync(vm.Dialogs, [new IgnoredFolderEntry { Folder = "node_modules", Enabled = true }]);
            Layout(window);
            var card = Card(window);
            Assert.True(Grip(card).Focus());
            window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
            window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
            Layout(window);
            double width = card.Bounds.Width, height = card.Bounds.Height;
            dialogs.CancelAll();
            Assert.Null(await pending);
            Assert.Equal((Math.Round(width), Math.Round(height)), DialogSizeMemory.Find(services.Settings, "find.ignored-folders"));
            Assert.Contains("find.ignored-folders", File.ReadAllText(services.Paths.SettingsFile));

            pending = FindDialogs.IgnoredFoldersAsync(vm.Dialogs, []);
            Layout(window);
            AssertSize(Card(window), width, height);
            dialogs.CancelAll();
            Assert.Null(await pending);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    /// <summary>A key that is not a stable id would silently keep no size: every literal key in the sources must be one.</summary>
    [Fact]
    public void Every_size_key_in_the_sources_is_a_stable_id()
    {
        if (SourceRoot() is not { } app)
        {
            Assert.Skip("The FileCat.App sources are not beside this test build.");
            return;
        }
        var keys = new List<string>();
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
            foreach (Match assignment in Regex.Matches(File.ReadAllText(file), @"\bSizeKey\s*=\s*([^,;}\r\n]+)"))
                foreach (Match literal in Regex.Matches(assignment.Groups[1].Value, "\"([^\"\\\\]*)\""))
                {
                    keys.Add(literal.Groups[1].Value);
                    if (!DialogSizeMemory.IsValidKey(literal.Groups[1].Value)) problems.Add($"{Path.GetFileName(file)}: {literal.Value}");
                }
        Assert.Empty(problems);
        Assert.True(keys.Count >= 20, $"Only {keys.Count} size keys were found in {app}.");
    }

    private static (Window Window, OverlayDialogService Dialogs) Open(DialogSizeStore? sizes, double width = 1200, double height = 900)
    {
        var host = new Grid();
        var window = new Window { Width = width, Height = height, Content = host };
        window.Styles.Add(new StyleInclude(new Uri("avares://FileCat/")) { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
        window.Show();
        return (window, new OverlayDialogService(host, () => null) { Sizes = sizes });
    }

    private static Task<object?> Show(OverlayDialogService dialogs, string? key, Control? content = null) =>
        dialogs.ShowCustomAsync("Sized dialog", content ?? new TextBox { Text = "content" },
            [new DialogButton("Close", "close", IsDefault: true, IsCancel: true)], resizable: true,
            behavior: new DialogBehavior { PreferredSize = Default, SizeKey = key });

    private static void Close(Window window, OverlayDialogService dialogs)
    {
        dialogs.CancelAll();
        Dispatcher.UIThread.RunJobs();
        window.Close();
    }

    private static DialogCard Card(Window window) => Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());

    private static Thumb Grip(DialogCard card) => Assert.Single(card.GetVisualDescendants().OfType<Thumb>(), thumb => thumb.Name == "DialogResizeGrip");

    private static Point Center(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void AssertSize(DialogCard card, double width, double height)
    {
        Assert.InRange(card.Bounds.Width, width - 1, width + 1);
        Assert.InRange(card.Bounds.Height, height - 1, height + 1);
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition());
    }

    private static string Entry(string key, double width, double height) =>
        FormattableString.Invariant($"{{ \"Key\": {JsonSerializer.Serialize(key)}, \"Width\": {width}, \"Height\": {height} }}");

    private static void Delete(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string? SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var app = Path.Combine(directory.FullName, "src", "FileCat.App");
            if (File.Exists(Path.Combine(app, "FileCat.App.csproj"))) return app;
        }
        return null;
    }
}
