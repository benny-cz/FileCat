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

// Renders FileCat's main window off-screen to PNG files, one per theme, for visual checks of icons and themes.
// Usage: Screenshots <out-folder> <left-folder> [<right-folder>] [--themes Classic,Cyberpunk] [--size 1400x900] [--wait 3]
//        [--frames 3 --every 700]   (several frames per theme, for animated themes)
if (args.Length < 2)
{
    Console.Error.WriteLine("Screenshots <out-folder> <left-folder> [<right-folder>] [--themes a,b] [--size WxH] [--wait seconds] [--frames n --every ms]");
    return 2;
}
string output = Directory.CreateDirectory(args[0]).FullName;
// "computer" shows the drives (This PC); anything else is a folder.
Location Place(string arg) => arg == "computer" ? new Location(FileCat.Core.Resources.Schemes.Computer, string.Empty) : Location.FileSystem(Path.GetFullPath(arg));
var left = Place(args[1]);
var right = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? Place(args[2]) : left;
string Option(string name, string fallback) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
var themes = Option("--themes", string.Join(',', ThemePalette.All.Select(p => p.Name))).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var size = Option("--size", "1400x900").Split('x');
double wait = double.Parse(Option("--wait", "3"), System.Globalization.CultureInfo.InvariantCulture);
int frames = int.Parse(Option("--frames", "1"), System.Globalization.CultureInfo.InvariantCulture);
int every = int.Parse(Option("--every", "700"), System.Globalization.CultureInfo.InvariantCulture);

AppBuilder.Configure<ShotApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (OperatingSystem.IsWindows()) PlatformFactory.WindowsFactory = () => new FileCat.Platform.Windows.WindowsPlatform();
string state = Path.Combine(Path.GetTempPath(), "filecat-shots", Guid.NewGuid().ToString("N"));
var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: state));
services.Icons.Native = NativeIconSource.TryCreate(services.Shell, () => services.AllowedShellPictures);

void Pump(TimeSpan duration)
{
    var until = DateTime.UtcNow + duration;
    while (DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(15);
    }
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
    window.Show();
    Pump(TimeSpan.FromSeconds(wait));
    for (int frame = 0; frame < frames; frame++)
    {
        if (frame > 0) Pump(TimeSpan.FromMilliseconds(every));
        using var bitmap = window.CaptureRenderedFrame();
        string file = Path.Combine(output, frames == 1 ? $"{theme}.png" : $"{theme}-{frame + 1}.png");
        bitmap?.Save(file);
        Console.WriteLine(bitmap is null ? $"{theme}: nothing rendered" : file);
    }
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
