using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.Views;

// Pictures of FileCat's windows, drawn offscreen with its own styles (UI reviews; nothing shows on the desktop).
//   FileCat.Screenshots <output folder> record <path> [pages] [theme ...]
// writes record-<theme>-<page>.png: the file-system record of <path>, page by page (Page Down between them).
if (args.Length < 3 || args[1] != "record")
{
    Console.WriteLine("usage: FileCat.Screenshots <output folder> record <path> [pages] [theme ...]");
    return 2;
}
string output = Directory.CreateDirectory(args[0]).FullName;
string target = Path.GetFullPath(args[2]);
int pages = args.Length > 3 && int.TryParse(args[3], out int n) ? n : 3;
string[] themes = args.Length > 4 ? args[4..] : ["ClassicDark"];

AppBuilder.Configure<ShotApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
FileCat.Core.Platform.PlatformFactory.WindowsFactory = () => new FileCat.Platform.Windows.WindowsPlatform();
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
        Pump(() => false, 200);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame();
        string file = Path.Combine(output, $"record-{theme}-{page}.png");
        frame?.Save(file);
        Console.WriteLine(frame is null ? $"no frame for {file}" : "saved " + file);
        window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
    }
    window.Close();
}
return 0;

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
