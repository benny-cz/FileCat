using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.State;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task SourceCloseFailureIsVisibleInTheAnalysisStatus()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        var source = new PreparedSource(fixture.Root) { CloseAction = () => throw new IOException("injected cleanup failure") };
        using var model = new DiskSpaceViewModel(fixture.Services, _ => source);
        await model.StartAsync([fixture.Root]); await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (model.SourceCloseFailures == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.Equal(1ul, model.SourceCloseFailures); Assert.Contains("Source cleanup reported 1 failure", model.Status);
        Assert.Equal(AnalysisPhase.Finished, model.Phase); Assert.Equal(1, source.CloseCalls);
        await model.RebuildAsync(true); Assert.Equal(NamespaceCoverage.Partial, model.Tree!.Snapshot.Coverage);
    }

    [AvaloniaFact]
    public async Task StopBindingIsEnabledAndLateNativeSourceClosesWithoutStartingTheNextRoot()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var gate = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new PreparedSource(fixture.Root); int calls = 0;
        using var model = new DiskSpaceViewModel(fixture.Services, _ =>
        { Interlocked.Increment(ref calls); entered.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(15)); return source; });
        var window = new DiskSpaceWindow(); var run = model.StartAsync([fixture.Root, fixture.Root + "-second"]);
        try
        {
            window.AddSession(model); window.Show(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var stop = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Stop binding");
            Assert.True(stop.IsEnabled); stop.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(model.IsPreparing); Assert.False(run.IsCompleted); Assert.Contains("Stopping location binding", model.Status);
        }
        finally { gate.Set(); await run.WaitAsync(TimeSpan.FromSeconds(10)); window.ForceClose(); }
        await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls); Assert.Equal(1, source.CloseCalls); Assert.False(model.Running);
        Assert.NotEqual(fixture.UiThread, source.CloseThread);
    }

    [AvaloniaFact]
    public async Task QueuedBindingCanStopBeforeAnyNativeSourceIsCreated()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var gate = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
        var blocker = AnalysisIo.Run(fixture.Services.Io, PathUtil.GetDeviceKey(fixture.Root), _ =>
        { entered.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(15)); return 0; });
        using var model = new DiskSpaceViewModel(fixture.Services, path => { Interlocked.Increment(ref calls); return new PreparedSource(path); });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var run = model.StartAsync([fixture.Root]);
            Assert.True(model.IsPreparing); model.Stop(); await run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, calls); Assert.False(model.IsPreparing); Assert.False(model.Running);
            Assert.Contains("No new scan was started", model.Status);
        }
        finally { gate.Set(); await blocker.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [AvaloniaFact]
    public async Task DisposingWhileBindingRetainsNativeWorkThenClosesItsLateResultOnce()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var gate = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var source = new PreparedSource(fixture.Root);
        using var model = new DiskSpaceViewModel(fixture.Services, _ =>
        { entered.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(15)); return source; });
        var run = model.StartAsync([fixture.Root]);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); model.Dispose();
            Assert.False(run.IsCompleted); Assert.Equal(0, source.CloseCalls);
        }
        finally { gate.Set(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.CloseCalls); Assert.NotEqual(fixture.UiThread, source.CloseThread);
    }

    [AvaloniaFact]
    public async Task NonrespondingPreparationFreezesBindingAndNeverStartsAScanAfterReturning()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var gate = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var source = new PreparedSource(fixture.Root);
        using var model = new DiskSpaceViewModel(fixture.Services, _ =>
        { entered.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(25)); return source; });
        var run = model.StartAsync([fixture.Root]);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!model.Status.Contains("not responding", StringComparison.Ordinal) && DateTime.UtcNow < deadline) await Task.Delay(50);
            Assert.Contains("not responding", model.Status); Assert.True(model.IsPreparing); Assert.False(run.IsCompleted);
        }
        finally { gate.Set(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(model.Running); Assert.Equal(0, source.Enumerations);
    }

    private static void OrdinaryPreparation() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Requires an ordinary process.");
    private sealed class PreparationFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-preparation-" + Guid.NewGuid().ToString("N"));
        public int UiThread { get; } = Environment.CurrentManagedThreadId;
        public AppServices Services { get; }
        public PreparationFixture() { Directory.CreateDirectory(Root); Services = AppServices.CreateForPaths(AppPaths.Resolve(dataRoot: Root)); }
        public void Dispose() { Services.Dispose(); Directory.Delete(Root, recursive: true); }
    }
    private sealed class PreparedSource(string path) : IAnalysisBackend
    {
        public int CloseCalls, CloseThread, Enumerations;
        public Action? CloseAction;
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RootBinding Root { get; } = new("1", "file", path, path, "fixture", IdentityAssurance.Verified, "fixture");
        public string Basis => "fixture-v1";
        public AnalysisEntry RootEntry => new([], "root", [], AnalysisKind.Directory, "fixture", AnalysisMeasurement.Known(0),
            AnalysisMeasurement.Known(0), AnalysisMeasurement.Known(1), null, "v1");
        public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation) { Interlocked.Increment(ref Enumerations); return []; }
        public void ValidateReadScope(AnalysisEntry directory) { }
        public void Dispose()
        {
            CloseThread = Environment.CurrentManagedThreadId; Interlocked.Increment(ref CloseCalls);
            try { CloseAction?.Invoke(); } finally { Closed.TrySetResult(); }
        }
    }
}
