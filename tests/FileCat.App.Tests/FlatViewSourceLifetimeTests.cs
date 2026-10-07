using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class FlatViewSourceLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_flat_views_release_their_cancel_sources(bool closeConsumer)
    {
        using var scope = new Scope();
        var (set, source) = Start(scope, closeConsumer);
        for (int i = 0; i < 200 && !set.IsComplete; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(set.IsComplete);
        Assert.Equal(2, set.Count);
        Assert.Equal("owned first", File.ReadAllText(Path.Join(scope.Folder, "first.txt")));
        Assert.Equal("owned second", File.ReadAllText(Path.Join(scope.Folder, "second.txt")));
        // Drain the real completion watcher and UI posts; the workspace/provider stay alive.
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(30, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!Alive(source)) break;
        }
        Assert.False(Alive(source), "A completed flat-view producer still retains its cancellation source.");
        Assert.Same(set, scope.Services.ResultSets.Get(ResultSetProvider.LocationOf(set)));
        GC.KeepAlive(scope.Vm);
        GC.KeepAlive(set);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ResultSet Set, WeakReference<CancellationTokenSource> Source) Start(Scope scope, bool closeConsumer)
    {
        scope.Vm.ExecuteAsync(CommandIds.FlatView).GetAwaiter().GetResult();
        var tab = scope.Vm.ActiveTab!;
        var set = scope.Services.ResultSets.Get(tab.Location!)!;
        // Observe the actual producer's source, then assert reachability rather than a private registry count.
        var field = typeof(MainViewModel).GetField("_flatViews", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var producers = (Dictionary<string, CancellationTokenSource>)field.GetValue(scope.Vm)!;
        var weak = new WeakReference<CancellationTokenSource>(producers[set.Id]);
        if (closeConsumer) tab.Panel.CloseTab(tab);
        return (set, weak);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Alive(WeakReference<CancellationTokenSource> source) => source.TryGetTarget(out _);

    private sealed class Scope : IDisposable
    {
        private readonly string _root = Path.Join(Path.GetTempPath(), "filecat-flat-lifetime", Guid.NewGuid().ToString("N"));
        internal string Folder => Path.Join(_root, "files");
        internal AppServices Services { get; }
        internal MainViewModel Vm { get; }

        internal Scope()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Join(Folder, "first.txt"), "owned first");
            File.WriteAllText(Path.Join(Folder, "second.txt"), "owned second");
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(_root, "state")));
            Vm = new MainViewModel(Services);
            var panel = new PanelViewModel(Vm.Workspace, Services);
            Vm.Workspace.Panels.Add(panel);
            panel.OpenTab(ResultSetProvider.WorkingSetList);
            panel.OpenTab(Location.FileSystem(Folder));
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
