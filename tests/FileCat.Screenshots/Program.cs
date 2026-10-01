using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

// Pictures of FileCat's windows, drawn offscreen with its own styles (UI reviews; nothing shows on the desktop).
//   FileCat.Screenshots <out> record <path> [pages] [theme ...]      the file-system record of <path>, page by page
//   FileCat.Screenshots <out> window <left> [right] [theme ...]      the main window with these locations in its panels
//   FileCat.Screenshots <out> command <left>[|right] <id>[@tab] [theme ...]  the main window after a command (its dialog
//                                                                    open), on the dialog's tab of that number (from 0) if given;
//                                                                    menu:<name> (menu:View) pictures that menu open instead;
//                                                                    tip:toolbar:<id> or tip:place:<title> a button's tooltip
// A location is a folder or file path, journal:<drive root> for a drive's change journal, or what the path box reads
// ("This PC", "HKEY_CURRENT_USER\Software").
if (args.Length < 3 || args[1] is not ("record" or "window" or "command") || args[1] == "command" && args.Length < 4)
{
    Console.WriteLine("usage: FileCat.Screenshots <out> record <path> [pages] [theme ...] | window <left> [right] [theme ...] | command <left> <id>[@tab] [theme ...]");
    return 2;
}
string output = Directory.CreateDirectory(args[0]).FullName;
AppBuilder.Configure<ShotApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
FileCat.Core.Platform.PlatformFactory.WindowsFactory = () => new FileCat.Platform.Windows.WindowsPlatform();
// Windows a command opens (viewer, Find, comparison) are pictured too.
var opened = new List<Window>();
Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
return args[1] switch
{
    "record" => Record(args),
    // "left|right" puts a second location in the right panel (a copy's destination, say).
    "command" => MainWindowShot([args[0], args[1], .. args[2].Split('|'), .. args[4..]], args[3]),
    _ => MainWindowShot(args),
};

int Record(string[] a)
{
    string target = Path.GetFullPath(a[2]);
    int pages = a.Length > 3 && int.TryParse(a[3], out int n) ? n : 3;
    string[] themes = a.Length > 4 ? a[4..] : ["ClassicDark"];
    using var platform = FileCat.Core.Platform.PlatformFactory.Create();
    // FILECAT_SHOT_UNPRIVILEGED=1: the record as FileCat shows it without administrator rights (most people's view).
    var records = OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FILECAT_SHOT_UNPRIVILEGED") == "1"
        ? new FileCat.Platform.Windows.WindowsFileRecords { AssumeNotPrivileged = true }
        : platform.FileRecords;
    foreach (string theme in themes)
    {
        ThemeManager.Apply(theme);
        var window = new ReportWindow("File-system record", target, ct => Task.FromResult(records.Read(target, ct).ToText())) { Width = 1100, Height = 820 };
        window.Show();
        Pump(() => window.Reading is { IsCompleted: true });
        for (int page = 1; page <= pages; page++)
        {
            Save(window, $"record-{theme}-{page}.png");
            window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
        }
        window.Close();
    }
    return 0;
}

int MainWindowShot(string[] a, string? command = null)
{
    string left = a[2];
    string? right = a.Length > 3 && !IsTheme(a[3]) ? a[3] : null;
    string[] themes = a.Skip(right is null ? 3 : 4).ToArray() is { Length: > 0 } t ? t : ["ClassicDark"];
    foreach (string theme in themes)
    {
        string state = Path.Combine(Path.GetTempPath(), "filecat-screenshots", Guid.NewGuid().ToString("N"));
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: state));
        try
        {
            ThemeManager.Apply(theme);
            // Keys to trust for signatures beside files (D-57), as if the user had put them in the profile's keys folder.
            foreach (string pub in (Environment.GetEnvironmentVariable("FILECAT_SHOT_KEYS") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                File.Copy(pub, Path.Combine(services.KeyDirectory, Path.GetFileName(pub)), overwrite: true);
            services.Icons.Native = NativeIconSource.TryCreate(services.Shell, () => services.AllowedShellPictures);
            var vm = new MainViewModel(services);
            var window = new MainWindow(vm, null) { Width = 1400, Height = 900 };
            vm.Initialize(null);
            var panels = vm.Workspace.Panels;
            Open(panels[0].ActiveTab!, left);
            if (right is not null && panels.Count > 1) Open(panels[1].ActiveTab!, right);
            window.Show();
            // The first frame, as a slow start shows it (before the panels are built).
            if (Environment.GetEnvironmentVariable("FILECAT_SHOT_STARTUP") == "1")
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                window.CaptureRenderedFrame()?.Save(Path.Combine(output, $"startup-{theme}.png"));
            }
            vm.Workspace.Activate(panels[0]);
            Pump(() => panels.All(p => p.ActiveTab?.Listing.State is ListingState.Complete or ListingState.Failed));
            // Drawn rows ask for their metadata (versions, checksums beside files); the values come from the background.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.CaptureRenderedFrame();
            Pump(() => false, 1500);
            // FILECAT_SHOT_OPERATION=<folder or file>: a copy of it under way, verified and at a limited speed
            // (FILECAT_SHOT_RATE bytes per second), pictured after FILECAT_SHOT_OPERATION_SECONDS with the Operations strip
            // and again with its details open (UI reviews of how a running operation shows, release issue I30).
            if (Environment.GetEnvironmentVariable("FILECAT_SHOT_OPERATION") is { Length: > 0 } copyFrom)
            {
                string copyTo = Directory.CreateDirectory(Path.Combine(state, "copy-target")).FullName;
                long rate = long.TryParse(Environment.GetEnvironmentVariable("FILECAT_SHOT_RATE"), out long r) ? r : 60_000_000;
                int seconds = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_SHOT_OPERATION_SECONDS"), out int s) ? s : 6;
                var job = services.Jobs.Submit(new FileCat.Core.Jobs.JobRequest
                {
                    Kind = FileCat.Core.Jobs.JobKind.Copy,
                    Sources = [ItemRef.ForFileSystemPath(copyFrom, Directory.Exists(copyFrom) ? EntryKind.Directory : EntryKind.File)],
                    Destination = Location.FileSystem(copyTo),
                    Options = new FileCat.Core.Jobs.TransferOptions { Verify = FileCat.Core.Jobs.VerifyMode.ReadBack, RateLimit = rate },
                });
                // The Operations view refreshes every quarter second in the app; offscreen its timer runs only with frames.
                var waiting = System.Diagnostics.Stopwatch.StartNew();
                while (waiting.Elapsed < TimeSpan.FromSeconds(seconds))
                {
                    Pump(() => false, 250);
                    foreach (var running in vm.Operations.Jobs) running.Refresh();
                }
                Save(window, $"operation-{theme}.png");
                vm.Operations.IsOpen = true;
                Pump(() => false, 800);
                Save(window, $"operation-details-{theme}.png");
                job.Cancel();
                Pump(() => !services.Jobs.HasActiveWork, 5000);
                vm.Operations.IsOpen = false;
            }
            if (command is not null)
            {
                string[] parts = command.Split('@');
                // A command on ".." pictures little: the first item instead.
                var active = panels[0].ActiveTab!;
                if (active.Listing.TryGetFocused(out var focusedRow) && focusedRow.Kind == EntryKind.Parent && active.Listing.VisibleCount > 1)
                    active.Listing.SetFocus(1);
                // A dialog that reads the clipboard (Calculate checksums) finds this there.
                if (Environment.GetEnvironmentVariable("FILECAT_SHOT_CLIPBOARD") is { Length: > 0 } copied && window.Clipboard is { } clipboard)
                    Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, copied).GetAwaiter().GetResult();
                if (parts[0].StartsWith("tip:", StringComparison.Ordinal))
                {
                    ShowTip(window, parts[0]["tip:".Length..], theme);
                    window.Close();
                    foreach (var open in panels.SelectMany(p => p.Tabs).ToList()) open.Dispose();
                    continue;
                }
                if (parts[0].StartsWith("menu:", StringComparison.Ordinal))
                {
                    ShowMenu(window, parts[0]["menu:".Length..], theme);
                    window.Close();
                    foreach (var open in panels.SelectMany(p => p.Tabs).ToList()) open.Dispose();
                    continue;
                }
                vm.Execute(parts[0]);
                Pump(() => false, 1500);
                if (parts.Length > 1 && int.TryParse(parts[1], out int tab))
                {
                    foreach (var tabs in window.GetVisualDescendants().OfType<TabControl>()) tabs.SelectedIndex = tab;
                    Pump(() => false, 500);
                }
                Save(window, $"{parts[0].Replace('.', '-')}{(parts.Length > 1 ? "-" + parts[1] : "")}-{theme}.png");
                int n = 0;
                foreach (var other in opened.Where(w => !ReferenceEquals(w, window) && w.IsVisible).ToList())
                {
                    Pump(() => false, 1000);
                    Save(other, $"{parts[0].Replace('.', '-')}-window{++n}-{theme}.png");
                    other.Close();
                }
                opened.Clear();
            }
            else Save(window, $"window-{theme}.png");
            window.Close();
            foreach (var tab in panels.SelectMany(p => p.Tabs).ToList()) tab.Dispose();
        }
        finally
        {
            services.Dispose();
            try { Directory.Delete(state, recursive: true); } catch (IOException) { }
        }
    }
    return 0;
}

static void Open(TabViewModel tab, string where)
{
    if (where.StartsWith("journal:", StringComparison.OrdinalIgnoreCase))
    {
        tab.Navigate(FileCat.Platform.Windows.UsnJournalProvider.ForPath(where["journal:".Length..]));
        return;
    }
    string full = Path.GetFullPath(where);
    if (File.Exists(full)) tab.Navigate(Location.FileSystem(Path.GetDirectoryName(full)!), Path.GetFileName(full));
    else if (Directory.Exists(full)) tab.Navigate(Location.FileSystem(full));
    // Anything else FileCat's path box reads: "This PC", "HKEY_CURRENT_USER\Software", "Network", …
    else if (tab.Services.Providers.TryParse(where, null, out var parsed) && parsed is not null) tab.Navigate(parsed);
    else tab.Navigate(Location.FileSystem(full));
}

static bool IsTheme(string name) => ThemePalette.Find(name) is not null;

// A main-menu menu open: the window, and the menu's own popup (drawn apart from the window, as on the desktop).
void ShowMenu(Window window, string name, string theme)
{
    var top = window.GetVisualDescendants().OfType<MenuItem>().FirstOrDefault(m => (m.Header as string)?.Replace("_", "") == name);
    if (top is null)
    {
        Console.WriteLine($"no menu {name}");
        return;
    }
    top.Open();
    Pump(() => false, 800);
    Save(window, $"menu-{name}-{theme}.png");
    // The popup's content lives in the popup's own top level.
    if (top.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().FirstOrDefault()?.Child is { } content && TopLevel.GetTopLevel(content) is { } popup)
        Save(popup, $"menu-{name}-popup-{theme}.png");
    top.Close();
}

// A button's tooltip open (I80): tip:toolbar:<command id> or tip:place:<title>; the tooltip's popup is pictured.
void ShowTip(Window window, string which, string theme)
{
    string[] what = which.Split(':', 2);
    var button = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => what[0] switch
    {
        "toolbar" => b.Classes.Contains("toolbar") && (string?)b.Tag == what[1],
        "place" => b.Classes.Contains("drive") && b.Tag is Place place && place.Title == what[1],
        _ => false,
    });
    if (button is null)
    {
        Console.WriteLine($"no button for {which}");
        return;
    }
    ToolTip.SetIsOpen(button, true);
    Pump(() => false, 800);
    string name = string.Concat(which.Split(Path.GetInvalidFileNameChars().Append(':').Append(' ').ToArray()));
    if (ToolTip.GetTip(button) is Control content && TopLevel.GetTopLevel(content) is { } popup) Save(popup, $"tip-{name}-{theme}.png");
    else Console.WriteLine($"no tooltip popup for {which}");
    ToolTip.SetIsOpen(button, false);
}

void Save(TopLevel window, string name)
{
    Pump(() => false, 200);
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    var frame = window.CaptureRenderedFrame();
    string file = Path.Combine(output, name);
    frame?.Save(file);
    Console.WriteLine(frame is null ? $"no frame for {file}" : "saved " + file);
}

// Runs the UI thread's work until the condition holds or the time is up.
static void Pump(Func<bool> done, int milliseconds = 30_000)
{
    var clock = System.Diagnostics.Stopwatch.StartNew();
    while (!done() && clock.ElapsedMilliseconds < milliseconds)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    Dispatcher.UIThread.RunJobs();
}

/// <summary>An application with FileCat's own look: Fluent (compact) and FileCat's styles; the theme comes from ThemeManager.</summary>
internal sealed class ShotApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme { DensityStyle = DensityStyle.Compact });
        Styles.Add(new StyleInclude(new Uri("avares://FileCat.Screenshots/")) { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
    }
}
