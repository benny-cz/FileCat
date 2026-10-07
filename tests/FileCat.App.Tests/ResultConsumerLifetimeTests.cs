using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>Actual result-tab consumers in a headless composition; no native desktop input.</summary>
public sealed class ResultConsumerLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_live_result_publisher_releases_its_closed_consumer(bool running)
    {
        using var scope = new Scope();
        using var producer = new CancellationTokenSource();
        var set = scope.Services.ResultSets.Create("Owned publisher", "Synthetic members");
        set.IsComplete = !running;
        var closed = OpenAndClose(scope.Vm, set, running ? producer : null);
        try
        {
            // Polling/delayed callbacks have their existing finite intervals to drain.
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                if (!IsAlive(closed)) break;
            }
            Assert.False(IsAlive(closed), "The live result publisher or completion watcher still retains its closed tab.");
            Assert.Same(set, scope.Services.ResultSets.Get(ResultSetProvider.LocationOf(set)));
            Assert.False(producer.IsCancellationRequested); // Closing this consumer does not cancel its producer.
            GC.KeepAlive(scope.Vm);
            GC.KeepAlive(set);
        }
        finally
        {
            producer.Cancel();
            await Task.Delay(550, TestContext.Current.CancellationToken);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TabViewModel> OpenAndClose(MainViewModel vm, ResultSet set, CancellationTokenSource? running)
    {
        vm.OpenResultSet(set, running);
        var tab = vm.ActiveTab!;
        var weak = new WeakReference<TabViewModel>(tab);
        tab.Panel.CloseTab(tab);
        Assert.True(tab.Listing.IsDisposed);
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(WeakReference<TabViewModel> tab) => tab.TryGetTarget(out _);

    [AvaloniaFact]
    public async Task Closing_a_consumer_prevents_late_completion_from_changing_its_banner()
    {
        using var scope = new Scope();
        using var producer = new CancellationTokenSource();
        var set = scope.Services.ResultSets.Create("Owned completion", "Owned late completion");
        scope.Vm.OpenResultSet(set, producer);
        var tab = scope.Vm.ActiveTab!;
        await Until(() => tab.Listing.State == ListingState.Complete, "the result lists");
        tab.Panel.CloseTab(tab);
        string? banner = tab.Banner;
        set.NotifyChanged();
        set.IsComplete = true;
        try
        {
            await Task.Delay(750, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.Listing.IsDisposed);
            Assert.Equal(banner, tab.Banner);
            Assert.False(producer.IsCancellationRequested);
        }
        finally { producer.Cancel(); }
    }

    [AvaloniaFact]
    public async Task A_live_consumer_still_refreshes_and_receives_completion_without_canceling_its_producer()
    {
        using var scope = new Scope();
        using var producer = new CancellationTokenSource();
        var set = scope.Services.ResultSets.Create("Owned positive", "Owned active completion");
        scope.Vm.OpenResultSet(set, producer);
        var tab = scope.Vm.ActiveTab!;
        try
        {
            await Until(() => tab.Listing.State == ListingState.Complete, "the empty set lists");
            var item = new ItemRef(new Location("owned-test", "folder"), "member", EntryKind.File);
            set.Add(item, "relative");
            set.NotifyChanged();
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount == 1, "the live consumer refreshes");
            set.IsComplete = true;
            await Until(() => tab.Banner == set.Provenance + " — complete", "the live consumer gets completion");
            Assert.False(producer.IsCancellationRequested);
            Assert.Same(set, scope.Services.ResultSets.Get(tab.Location!));
        }
        finally
        {
            producer.Cancel();
            await Task.Delay(550, TestContext.Current.CancellationToken);
        }
    }

    private static async Task Until(Func<bool> condition, string message)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), message);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "filecat-result-consumer", Guid.NewGuid().ToString("N"));
        public AppServices Services { get; }
        public MainViewModel Vm { get; }

        public Scope()
        {
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: _root));
            Vm = new MainViewModel(Services);
            var panel = new PanelViewModel(Vm.Workspace, Services);
            Vm.Workspace.Panels.Add(panel);
            panel.OpenTab(ResultSetProvider.WorkingSetList);
            Vm.Workspace.Activate(panel);
        }

        public void Dispose()
        {
            foreach (var tab in Vm.Workspace.Panels.SelectMany(p => p.Tabs).ToArray()) tab.Dispose();
            Services.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }
}
