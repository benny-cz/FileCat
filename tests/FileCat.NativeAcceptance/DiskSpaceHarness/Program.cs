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
using Avalonia.VisualTree;
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
    private TabControl? _tabs;
    private object? _lastState;
    private object? _lastScreen;
    private string _run = "", _fixture = "";
    private bool _deadlineClosed;
    private bool _profileProbeFinished;
    private string? _profileProbeError, _profileProbeCaptureError;
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
        if (Program.IndexedDeferredProbe) for (int i = 0; i < DeferredNativePaths; i++) Add($"Deferred/future-{i:D5}.bin", 1);
        if (Program.IndexedInventoryProbe) for (int i = 0; i < 4200; i++) Add($"Streamed/row-{i:D4}.bin", 1);
        PlatformFactory.WindowsFactory = () => new FileCat.Platform.Windows.WindowsPlatform();
        string profileRoot = Program.SharedProfile ? Path.Combine(AppContext.BaseDirectory, "native-validation-shared-profile") : Path.Combine(_run, "data");
        _services = AppServices.CreateForPaths(AppPaths.Resolve(dataRoot: profileRoot));
        ThemeManager.Apply("ClassicDark");
        _window = new DiskSpaceWindow(AddLiveSession, () => _services.Keymap);
        if (Program.LargeText) _window.FontSize = 20;
        if (Program.Compact) { _window.Width = 380; _window.Height = 430; }
        _window.Opened += (_, _) =>
        {
            _tabs = _window.GetVisualDescendants().OfType<TabControl>().Single();
            _tabs.SelectionChanged += (_, _) => WriteReport("open");
        };
        _window.SizeChanged += (_, _) => { if (_window.IsVisible) WriteReport("open"); };
        desktop.MainWindow = _window;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!Program.IndexedProbe) AddLiveSession();
        _window.Closed += async (_, _) =>
        {
            _deadline.Stop();
            _indexedLifetime.Cancel();
            try
            {
                await _window.WaitForIndexedCloseAsync();
                await _indexedProbeTask;
                if (_indexedAcquisition is { } indexed) await indexed.DisposeAsync();
            }
            catch (Exception error) { _indexedProbeError ??= error.GetType().Name + ": " + error.Message; }
            var unchanged = _originals.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
            WriteReport("closed", unchanged);
            try { _services.Dispose(); }
            finally { _indexedLifetime.Dispose(); desktop.Shutdown(); }
        };
        _deadline.Tick += (_, _) =>
        {
            bool requested = File.Exists(Path.Combine(_run, "close.request"));
            if (requested || DateTimeOffset.UtcNow >= _deadlineAt) { _deadlineClosed = !requested; _window.ForceClose(); }
        };
        _deadline.Start(); _window.Show(); WriteReport("ready");
        if (Program.WindowsInput) BeginWindowsInput();
        if (Program.ProfileProbe) _ = ProfileProbeAsync();
        if (Program.IndexedProbe) _indexedProbeTask = Program.MemoryFallbackProbe ? MemoryFallbackProbeAsync() : IndexedProbeAsync();
        if (Program.ReplacementUiProbe) _indexedProbeTask = ReplacementUiProbeAsync();
        if (Program.IndexedDeferredProbe) _indexedProbeTask = IndexedDeferredProbeAsync();
    }

    private async Task ProfileProbeAsync()
    {
        try
        {
            await _models[0].StartAsync([_fixture]);
            var until = DateTimeOffset.UtcNow.AddMinutes(2);
            while (_window!.IsVisible && DateTimeOffset.UtcNow < until)
            {
                await _models[0].RebuildAsync(true);
                if (_models[0].Phase == AnalysisPhase.Finished && _models[0].Tree?.Snapshot.Phase == AnalysisPhase.Finished)
                {
                    if (Program.SavedOrderingProbe) await SavedOrderingProbeAsync();
                    if (Program.AcquisitionProbe) await AcquisitionProbeAsync();
                    await Task.Delay(300);
                    if (OperatingSystem.IsWindows())
                    {
                        try
                        {
                            WindowsGuestInput.ActivateFromDesktop(_window.TryGetPlatformHandle()?.Handle ?? 0);
                            await Task.Delay(600); WindowsCapture("profile-probe");
                        }
                        catch (Exception captureError) { _profileProbeCaptureError = captureError.GetType().Name; }
                    }
                    _profileProbeFinished = true; WriteReport("profile-probe-finished"); return;
                }
                if (_models[0].Phase is AnalysisPhase.Failed or AnalysisPhase.Stopped) throw new InvalidOperationException("The generated profile probe scan did not finish.");
                await Task.Delay(100);
            }
            throw new TimeoutException("The generated profile probe scan did not publish its finished revision.");
        }
        catch (Exception error) { _profileProbeError = error.GetType().Name + ": " + error.Message; WriteReport("profile-probe-failed"); }
    }

    private void AddLiveSession()
    {
        if (_window is null || _services is null) return;
        if (!_window.CanAddSession) { _window.ExplainSessionLimit(); return; }
        var model = new DiskSpaceViewModel(_services, indexedStorage: Program.IndexedDeferredProbe);
        if (Program.IndexedDeferredProbe) { model.Query = "name:future-"; model.Chart = "Table"; }
        _models.Add(model);
        model.Changed += () =>
        {
            if (model.Tree is { } tree)
            {
                _lastState = new
                {
                    model.Phase, model.Status, model.FilterError, model.Query, model.Metric,
                    session = tree.Snapshot.Identity.SessionId, tree.Snapshot.Coverage, tree.Snapshot.Freshness,
                    hostProfile = tree.Snapshot.Identity.HostProfile, model.ContinuityNotice,
                    paths = tree.Snapshot.Nodes.Length, objects = tree.Snapshot.Objects.Length,
                    issues = tree.Snapshot.Issues.Select(i => new { i.Code, i.State }).ToArray(),
                    known = tree.Projection.Totals.Known.ToString(), unverified = tree.Projection.Totals.IdentityUnverified.ToString(),
                    matchingPaths = tree.Projection.Totals.MatchingPaths, rows = model.Rows.Length,
                    focus = model.Selection.Focus, marked = model.Selection.Marks.Count, basket = model.Selection.Basket.Count,
                    tableSort = model.Sort, tableDescending = model.SortDescending,
                    tableRows = model.Rows.Take(12).Select(row => new { row.Key, row.Name, value = row.Value.ToString(),
                        activeText = DiskSpaceViewModel.ValueText(row, tree.Projection.Metric), fileSizeText = DiskSpaceViewModel.LogicalText(row), state = DiskSpaceViewModel.StatusText(row) }).ToArray(),
                    breadcrumbs = model.Breadcrumbs
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
            lastVisibleScale = _lastScale, lastScreen = _lastScreen, largeText = Program.LargeText, compact = Program.Compact, sharedProfile = Program.SharedProfile,
            sessions = _window?.SessionCount, createdSessions = _models.Count, lastObservation = _lastState,
            selectedTab = _tabs?.SelectedIndex, tabHeaders = _tabs?.Items.OfType<TabItem>().Select(tab => tab.Header as string).ToArray(),
            appSha256 = _appHash, harnessSha256 = _harnessHash,
            profileProbe = Program.ProfileProbe, profileProbeFinished = _profileProbeFinished, profileProbeError = _profileProbeError, profileProbeCaptureError = _profileProbeCaptureError,
            savedOrderingProbe = _savedOrderingProbe, acquisitionProbe = _acquisitionProbe,
            indexedProbeRequested = Program.IndexedProbe, indexedProbeFinished = _indexedProbeFinished, indexedProbeError = _indexedProbeError, indexedProbe = _indexedProbe,
            indexedInventoryProbeRequested = Program.IndexedInventoryProbe, inventoryProbe = _inventoryProbe, memoryFallbackProbeRequested = Program.MemoryFallbackProbe, memoryLifecycleProbeRequested = Program.MemoryLifecycleProbe,
            memoryProbeStage = _memoryProbeStage, memoryProbeView = _memoryProbeView,
            replacementUiProbe = _replacementUiProbe, indexedDeferredProbe = _indexedDeferredProbe
        };
        File.WriteAllText(Path.Combine(_run, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool HardLink(string link, string existing, nint attributes);
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8)] private static partial int UnixLink(string existing, string link);
}

internal static class Program
{
    internal static bool LargeText { get; private set; }
    internal static bool Compact { get; private set; }
    internal static bool WindowsInput { get; private set; }
    internal static bool SharedProfile { get; private set; }
    internal static bool ProfileProbe { get; private set; }
    internal static bool SavedOrderingProbe { get; private set; }
    internal static bool AcquisitionProbe { get; private set; }
    internal static bool IndexedProbe { get; private set; }
    internal static bool MemoryFallbackProbe { get; private set; }
    internal static bool MemoryLifecycleProbe { get; private set; }
    internal static bool IndexedInventoryProbe { get; private set; }
    internal static bool ReplacementUiProbe { get; private set; }
    internal static bool ReplacementCompositorCapture { get; private set; }
    internal static bool IndexedDeferredProbe { get; private set; }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(arg => arg is not ("--large-text" or "--compact" or "--windows-input" or "--shared-profile" or "--profile-probe" or "--saved-ordering-probe" or "--acquisition-probe" or "--indexed-probe" or "--memory-fallback-probe" or "--memory-lifecycle-probe" or "--indexed-inventory-probe" or "--replacement-ui-probe" or "--replacement-compositor-capture" or "--indexed-deferred-probe"))) return 64;
        LargeText = args.Contains("--large-text", StringComparer.Ordinal);
        Compact = args.Contains("--compact", StringComparer.Ordinal);
        WindowsInput = args.Contains("--windows-input", StringComparer.Ordinal);
        SharedProfile = args.Contains("--shared-profile", StringComparer.Ordinal);
        SavedOrderingProbe = args.Contains("--saved-ordering-probe", StringComparer.Ordinal);
        AcquisitionProbe = args.Contains("--acquisition-probe", StringComparer.Ordinal);
        ProfileProbe = args.Contains("--profile-probe", StringComparer.Ordinal) || SavedOrderingProbe || AcquisitionProbe;
        MemoryLifecycleProbe = args.Contains("--memory-lifecycle-probe", StringComparer.Ordinal);
        if (MemoryLifecycleProbe && args.Contains("--memory-fallback-probe", StringComparer.Ordinal)) return 64;
        MemoryFallbackProbe = args.Contains("--memory-fallback-probe", StringComparer.Ordinal) || MemoryLifecycleProbe;
        IndexedInventoryProbe = args.Contains("--indexed-inventory-probe", StringComparer.Ordinal);
        if (IndexedInventoryProbe && (MemoryFallbackProbe || args.Contains("--indexed-probe", StringComparer.Ordinal))) return 64;
        IndexedProbe = args.Contains("--indexed-probe", StringComparer.Ordinal) || MemoryFallbackProbe || IndexedInventoryProbe;
        ReplacementUiProbe = args.Contains("--replacement-ui-probe", StringComparer.Ordinal);
        IndexedDeferredProbe = args.Contains("--indexed-deferred-probe", StringComparer.Ordinal);
        if (IndexedDeferredProbe && (ProfileProbe || IndexedProbe || WindowsInput || SharedProfile || ReplacementUiProbe)) return 64;
        ReplacementCompositorCapture = args.Contains("--replacement-compositor-capture", StringComparer.Ordinal);
        if (ReplacementCompositorCapture && (!(ReplacementUiProbe || IndexedDeferredProbe) || !OperatingSystem.IsMacOS())) return 64;
        if (ReplacementUiProbe && (ProfileProbe || IndexedProbe || WindowsInput || SharedProfile)) return 64;
        if (MemoryFallbackProbe && args.Contains("--indexed-probe", StringComparer.Ordinal)) return 64;
        if (ProfileProbe && WindowsInput) return 64;
        if (IndexedProbe && (ProfileProbe || WindowsInput)) return 64;
        if ((WindowsInput || (ProfileProbe || IndexedProbe || ReplacementUiProbe || IndexedDeferredProbe) && OperatingSystem.IsWindows()) && !HarnessApp.WindowsInputVmAllowed()) return 77;
        if (AnalysisAdmission.Refusal() is not null) return 77;
        foreach (var process in Process.GetProcessesByName("FileCat"))
            using (process) if (process.Id != Environment.ProcessId) return 75;
        return AppBuilder.Configure<HarnessApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
