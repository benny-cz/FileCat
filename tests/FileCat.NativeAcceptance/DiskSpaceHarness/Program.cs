using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.Platform;
using FileCat.Core.State;

namespace DiskSpaceHarness;

/// <summary>Real desktop window, live ordinary metadata backend, isolated generated sources and profile.</summary>
internal sealed partial class HarnessApp : Application
{
    private readonly Dictionary<string, string> _originals = new(StringComparer.Ordinal);
    private readonly List<string> _fixtureLimitations = [];
    private readonly List<DiskSpaceViewModel> _models = [];
    private readonly DispatcherTimer _deadline = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DateTimeOffset _deadlineAt = DateTimeOffset.UtcNow.AddMinutes(8);
    private AppServices? _services;
    private DiskSpaceWindow? _window;
    private object? _lastState;
    private object? _lastScreen;
    private string _run = "", _fixture = "";
    private bool _deadlineClosed;
    private double _lastWidth, _lastHeight, _lastScale;
    private readonly string _appHash = Hash(Path.Combine(AppContext.BaseDirectory, "FileCat.dll"));
    private readonly string _harnessHash = Hash(Path.Combine(AppContext.BaseDirectory, "DiskSpaceHarness.dll"));

    // Match App.axaml, without invoking the unrelated production main-window/profile composition.
    public override void Initialize()
    {
        Name = "FileCat";
        Styles.Add(new FluentTheme { DensityStyle = DensityStyle.Compact });
        Styles.Add(new StyleInclude(new Uri("avares://FileCat/")) { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        _run = Path.Combine(AppContext.BaseDirectory, "native-validation", "dsa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_run);
        _fixture = Path.Combine(_run, "fixture"); Directory.CreateDirectory(_fixture);
        CreateFixture();
        PlatformFactory.WindowsFactory = () => new FileCat.Platform.Windows.WindowsPlatform();
        _services = AppServices.CreateForPaths(AppPaths.Resolve(dataRoot: Path.Combine(_run, "data")));
        ThemeManager.Apply("ClassicDark");
        _window = new DiskSpaceWindow(AddLiveSession, () => _services.Keymap);
        _window.SizeChanged += (_, _) => { if (_window.IsVisible) WriteReport("open"); };
        desktop.MainWindow = _window;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        AddLiveSession();
        _window.Closed += (_, _) =>
        {
            _deadline.Stop();
            var unchanged = _originals.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
            WriteReport("closed", unchanged);
            _services.Dispose();
        };
        _deadline.Tick += (_, _) =>
        {
            bool requested = File.Exists(Path.Combine(_run, "close.request"));
            if (requested || DateTimeOffset.UtcNow >= _deadlineAt) { _deadlineClosed = !requested; _window.ForceClose(); }
        };
        _deadline.Start(); _window.Show(); WriteReport("ready");
    }

    private void AddLiveSession()
    {
        if (_window is null || _services is null) return;
        if (_window.SessionCount >= AnalysisLimits.Sessions) { _window.ExplainSessionLimit(); return; }
        var model = new DiskSpaceViewModel(_services); _models.Add(model);
        model.Changed += () =>
        {
            if (model.Tree is { } tree)
            {
                _lastState = new
                {
                    model.Phase, model.Status, model.FilterError, model.Query, model.Metric,
                    session = tree.Snapshot.Identity.SessionId, tree.Snapshot.Coverage, tree.Snapshot.Freshness,
                    paths = tree.Snapshot.Nodes.Length, objects = tree.Snapshot.Objects.Length,
                    issues = tree.Snapshot.Issues.Select(i => new { i.Code, i.State }).ToArray(),
                    known = tree.Projection.Totals.Known.ToString(), unverified = tree.Projection.Totals.IdentityUnverified.ToString(),
                    matchingPaths = tree.Projection.Totals.MatchingPaths, rows = model.Rows.Length,
                    focus = model.Selection.Focus, marked = model.Selection.Marks.Count, basket = model.Selection.Basket.Count
                };
            }
            WriteReport("open");
        };
        _window.AddSession(model, [_fixture]);
    }

    private void CreateFixture()
    {
        Add("large.bin", 1024 * 1024); Add("Photos/photo.png", 240 * 1024); Add("Archives/archive.zip", 80 * 1024);
        Add("zero.bin", 0); Add("report.csv", 60); Add("Package.app/Contents/readme.md", 11 * 1024);
        Add("Unicode/space and café 😀.txt", 123); Add(".hidden.bin", 321);
        for (int i = 0; i < 500; i++) Add($"Small files/item-{i:D4}.txt", i % 97);
        string aliases = Path.Combine(_fixture, "Aliases"); Directory.CreateDirectory(aliases);
        string original = Path.Combine(_fixture, "large.bin"), alias = Path.Combine(aliases, "large-link.bin");
        bool linked = OperatingSystem.IsWindows() ? HardLink(alias, original, 0) : UnixLink(original, alias) == 0;
        if (linked) _originals.Add(alias, _originals[original]); else _fixtureLimitations.Add("Hard-link fixture unavailable.");
        string outside = Path.Combine(_run, "outside"); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "must-not-appear.txt"), "outside the selected source");
        try { Directory.CreateSymbolicLink(Path.Combine(_fixture, "link-outside"), outside); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _fixtureLimitations.Add("Directory link fixture unavailable: " + ex.GetType().Name); }
    }

    private void Add(string relative, int length)
    {
        string path = Path.Combine(_fixture, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = new byte[length]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 251);
        File.WriteAllBytes(path, bytes); _originals.Add(path, Hash(path));
    }
    private static string Hash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private void WriteReport(string state, bool? sourceUnchanged = null)
    {
        if (_run.Length == 0) return;
        if (_window is { IsVisible: true })
        {
            _lastWidth = _window.ClientSize.Width; _lastHeight = _window.ClientSize.Height; _lastScale = _window.RenderScaling;
            var screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
            if (screen is not null) _lastScreen = new
            {
                bounds = screen.Bounds.ToString(), workingArea = screen.WorkingArea.ToString(), screen.Scaling,
                position = _window.Position.ToString(), frameSize = _window.FrameSize?.ToString()
            };
        }
        var report = new
        {
            schemaVersion = 1, purpose = "Native desktop development validation; no capability qualification",
            state, sourceUnchanged, deadlineClosed = _deadlineClosed, processId = Environment.ProcessId,
            platform = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            run = _run, fixture = _fixture, fixtureRegularPaths = _originals.Count, fixtureLimitations = _fixtureLimitations,
            nativeWindowVisible = _window?.IsVisible, lastVisibleWidth = _lastWidth, lastVisibleHeight = _lastHeight,
            lastVisibleScale = _lastScale, lastScreen = _lastScreen,
            sessions = _window?.SessionCount, createdSessions = _models.Count, lastObservation = _lastState,
            appSha256 = _appHash, harnessSha256 = _harnessHash
        };
        File.WriteAllText(Path.Combine(_run, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool HardLink(string link, string existing, nint attributes);
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8)] private static partial int UnixLink(string existing, string link);
}

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (AnalysisAdmission.Refusal() is not null) return 77;
        foreach (var process in Process.GetProcessesByName("FileCat"))
            using (process) if (process.Id != Environment.ProcessId) return 75;
        return AppBuilder.Configure<HarnessApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
    }
}
