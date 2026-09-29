using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Platform;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

// Renders FileCat's main window off-screen to PNG files, one per theme, for visual checks of the whole app.
// Usage: Screenshots <out-folder> <left> [<right>] [--themes Classic,Cyberpunk] [--size 1400x900] [--wait 3]
//        [--frames 3 --every 700]   (several frames per theme, for animated themes)
//        [--focus name] [--focus-right name] [--mark a;b] [--commands id,id]   (a scene: focus and mark items in the
//        left panel (the right one's focus too), run commands; every window a command opens is captured as well)
if (args.Length < 2)
{
    Console.Error.WriteLine("Screenshots <out-folder> <left-folder> [<right-folder>] [--themes a,b] [--size WxH] [--wait seconds] [--frames n --every ms]");
    return 2;
}
string output = Directory.CreateDirectory(args[0]).FullName;
// "computer" shows the drives (This PC); "reg:HKEY_CURRENT_USER\Software" a Registry key; "recovery:<image>[|<folder>]" a
// disk image's deleted items (its first volume);
// anything else is a folder.
Location Place(string arg)
{
    if (arg == "computer") return new Location(FileCat.Core.Resources.Schemes.Computer, string.Empty);
    if (arg == "phones") return FileCat.Platform.Windows.Mtp.MtpProvider.Devices;
    if (arg.StartsWith("reg:", StringComparison.Ordinal))
        return new FileCat.Platform.Windows.WindowsRegistryProvider().TryParse(arg, null, out var key) && key is not null ? key : throw new ArgumentException("Not a Registry key: " + arg);
    if (arg.StartsWith("recovery:", StringComparison.Ordinal))
    {
        var parts = arg["recovery:".Length..].Split('|');
        var volume = FileCat.Recovery.RecoveryProvider.ForImage(Path.GetFullPath(parts[0]), 1);
        return parts.Length > 1 ? volume.WithPath(parts[1]) : volume;
    }
    return Location.FileSystem(Path.GetFullPath(arg));
}
var left = Place(args[1]);
var right = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? Place(args[2]) : left;
string Option(string name, string fallback) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
var themes = Option("--themes", string.Join(',', ThemePalette.All.Select(p => p.Name))).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var size = Option("--size", "1400x900").Split('x');
double wait = double.Parse(Option("--wait", "3"), System.Globalization.CultureInfo.InvariantCulture);
int frames = int.Parse(Option("--frames", "1"), System.Globalization.CultureInfo.InvariantCulture);
int every = int.Parse(Option("--every", "700"), System.Globalization.CultureInfo.InvariantCulture);
string? focusLeft = Option("--focus", "") is { Length: > 0 } fl ? fl : null;
string? focusRight = Option("--focus-right", "") is { Length: > 0 } fr ? fr : null;
var marks = Option("--mark", "").Split(';', StringSplitOptions.RemoveEmptyEntries);
var commands = Option("--commands", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var presses = Option("--press", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
int tabIndex = int.Parse(Option("--tab", "-1"), System.Globalization.CultureInfo.InvariantCulture);

AppBuilder.Configure<ShotApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (OperatingSystem.IsWindows()) PlatformFactory.WindowsFactory = () => new FileCat.Platform.Windows.WindowsPlatform();
string state = Path.Combine(Path.GetTempPath(), "filecat-shots", Guid.NewGuid().ToString("N"));
var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: state));
services.Icons.Native = NativeIconSource.TryCreate(services.Shell, () => services.AllowedShellPictures) ?? MacIconSource.TryCreate() ?? FreedesktopIconSource.TryCreate();

// The dispatcher's own loop runs (so timers fire: animations, glitches), with a render tick every 16 ms.
void Pump(TimeSpan duration)
{
    using var stop = new CancellationTokenSource(duration);
    var render = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => AvaloniaHeadlessPlatform.ForceRenderTimerTick());
    render.Start();
    Dispatcher.UIThread.MainLoop(stop.Token);
    render.Stop();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
}

// Windows a command opens (viewer, compare, hex editor, …) are captured next to the main window.
var opened = new List<Avalonia.Controls.Window>();
Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<Avalonia.Controls.Window>((w, _) => opened.Add(w));

static void Focus(FileCat.Core.Listing.ListingModel listing, string name, bool mark)
{
    for (int i = 0; i < listing.VisibleCount; i++)
    {
        if (!string.Equals(listing.GetVisible(i).Name, name, StringComparison.OrdinalIgnoreCase)) continue;
        listing.SetFocus(i);
        if (mark) listing.ToggleMark(i);
        return;
    }
    Console.Error.WriteLine($"\"{name}\" is not in the panel.");
}

foreach (var theme in themes)
{
    ThemeManager.Apply(theme);
    var vm = new MainViewModel(services);
    var window = new MainWindow(vm, null) { Width = double.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture), Height = double.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture) };
    vm.Initialize(null);
    var panels = vm.Workspace.Panels;
    panels[0].ActiveTab?.Navigate(left);
    if (panels.Count > 1) panels[1].ActiveTab?.Navigate(right);
    opened.Clear();
    window.Show();
    Pump(TimeSpan.FromSeconds(wait));
    if (panels.Count > 1 && focusRight is not null && panels[1].ActiveTab is { } rightTab) Focus(rightTab.Listing, focusRight, mark: false);
    if (panels[0].ActiveTab is { } leftTab)
    {
        foreach (var name in marks) Focus(leftTab.Listing, name, mark: true);
        if (focusLeft is not null) Focus(leftTab.Listing, focusLeft, mark: false);
    }
    foreach (var command in commands)
    {
        vm.Execute(command);
        Pump(TimeSpan.FromSeconds(Math.Max(1, wait / 2)));
    }
    // --tab N: the Nth tab of the first tab control on screen (a dialog's pages).
    if (tabIndex >= 0 && Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TabControl>().FirstOrDefault() is { } tabs)
    {
        tabs.SelectedIndex = tabIndex;
        Pump(TimeSpan.FromSeconds(1));
    }
    // --in-window: --type and --press go to the last window a command opened (Find, say) instead of the main window.
    Avalonia.Controls.Window keys = args.Contains("--in-window") && opened.LastOrDefault(w => w != window && w.IsVisible) is { } last ? last : window;
    // --type <text>: typed into the text box that has the keyboard (the command search after app.palette), or else into
    // the active panel's location box (its folder suggestions show).
    if (Option("--type", "") is { Length: > 0 } typed)
    {
        Avalonia.Controls.TextBox? FocusedBox() =>
            Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(keys).OfType<Avalonia.Controls.TextBox>().FirstOrDefault(t => t.IsFocused);
        if (FocusedBox() is null && keys == window)
        {
            vm.View.FocusPathBox();
            Pump(TimeSpan.FromMilliseconds(300));
        }
        if (FocusedBox() is { } box)
        {
            box.Text = typed;
            box.CaretIndex = typed.Length;
        }
        Pump(TimeSpan.FromSeconds(1));
    }
    // --drag-panel 0.5,0.9: drags panel 1 by its number over panel 2 to that point (fractions of panel 2's size) and
    // keeps the button down, so the docking preview shows.
    if (Option("--drag-panel", "") is { Length: > 0 } drag)
    {
        var at = drag.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var views = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<PanelView>().ToList();
        var firstView = views.First(v => ReferenceEquals(v.DataContext, panels[0]));
        var secondView = views.First(v => ReferenceEquals(v.DataContext, panels[1]));
        var badge = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(firstView).OfType<Avalonia.Controls.Border>().First(b => b.Name == "NumberBadge");
        var from = badge.TranslatePoint(new Avalonia.Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), window)!.Value;
        var to = secondView.TranslatePoint(new Avalonia.Point(secondView.Bounds.Width * at[0], secondView.Bounds.Height * at[1]), window)!.Value;
        window.MouseDown(from, Avalonia.Input.MouseButton.Left);
        window.MouseMove(new Avalonia.Point(from.X + 20, from.Y + 20));
        window.MouseMove(to);
        Pump(TimeSpan.FromMilliseconds(500));
    }
    // --press Enter,Tab,Ctrl+D: keys after the commands (confirm a dialog, then see what follows).
    foreach (var key in presses)
    {
        // "Ctrl+D", "Shift+F8": modifiers before the key.
        var parts = key.Split('+');
        var modifiers = Avalonia.Input.RawInputModifiers.None;
        foreach (var m in parts[..^1])
            modifiers |= m.ToLowerInvariant() switch
            {
                "ctrl" => Avalonia.Input.RawInputModifiers.Control,
                "shift" => Avalonia.Input.RawInputModifiers.Shift,
                "alt" => Avalonia.Input.RawInputModifiers.Alt,
                _ => Avalonia.Input.RawInputModifiers.None,
            };
        keys.KeyPressQwerty(Enum.Parse<Avalonia.Input.PhysicalKey>(parts[^1], ignoreCase: true), modifiers);
        Pump(TimeSpan.FromSeconds(Math.Max(1, wait / 2)));
    }
    for (int frame = 0; frame < frames; frame++)
    {
        if (frame > 0) Pump(TimeSpan.FromMilliseconds(every));
        // --glitch: the psychedelic theme's glitch starts just before each frame.
        if (args.Contains("--glitch"))
        {
            ThemeAnimation.TriggerGlitch();
            Pump(TimeSpan.FromMilliseconds(120));
        }
        using var bitmap = window.CaptureRenderedFrame();
        string file = Path.Combine(output, frames == 1 ? $"{theme}.png" : $"{theme}-{frame + 1}.png");
        bitmap?.Save(file);
        Console.WriteLine(bitmap is null ? $"{theme}: nothing rendered" : file);
        foreach (var other in opened.Where(w => w != window && w.IsVisible).ToList())
        {
            using var shot = other.CaptureRenderedFrame();
            string name = string.Concat((other.Title ?? "window").Split(Path.GetInvalidFileNameChars())).Trim();
            string otherFile = Path.Combine(output, $"{theme}-{name}{(frames == 1 ? "" : $"-{frame + 1}")}.png");
            shot?.Save(otherFile);
            Console.WriteLine(shot is null ? $"{theme} / {name}: nothing rendered" : otherFile);
        }
    }
    foreach (var other in opened.Where(w => w != window).ToList()) other.Close();
    window.Close();
    foreach (var tab in panels.SelectMany(p => p.Tabs).ToList()) tab.Dispose();
}
services.Dispose();
try { Directory.Delete(state, recursive: true); } catch (IOException) { }
return 0;

internal sealed class ShotApp : Application
{
    public override void Initialize()
    {
        Name = "FileCat";
        Styles.Add(new FluentTheme { DensityStyle = DensityStyle.Compact });
        Styles.Add(new StyleInclude(new Uri("avares://FileCat/")) { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
    }
}
