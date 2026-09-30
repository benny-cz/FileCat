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
//                                                                    open), on the dialog's tab of that number (from 0) if given
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
    var records = platform.FileRecords;
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
            services.Icons.Native = NativeIconSource.TryCreate(services.Shell, () => services.AllowedShellPictures);
            var vm = new MainViewModel(services);
            var window = new MainWindow(vm, null) { Width = 1400, Height = 900 };
            vm.Initialize(null);
            var panels = vm.Workspace.Panels;
            Open(panels[0].ActiveTab!, left);
            if (right is not null && panels.Count > 1) Open(panels[1].ActiveTab!, right);
            window.Show();
            vm.Workspace.Activate(panels[0]);
            Pump(() => panels.All(p => p.ActiveTab?.Listing.State is ListingState.Complete or ListingState.Failed));
            // Drawn rows ask for their metadata (versions, checksums beside files); the values come from the background.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.CaptureRenderedFrame();
            Pump(() => false, 1500);
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

void Save(Window window, string name)
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
