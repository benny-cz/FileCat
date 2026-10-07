using System.Text.Json;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class AnalysisCurrentDemandTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("complete")]
    public async Task An_older_analysis_cannot_clear_a_running_replacement(string outcome)
    {
        using var scope = new Scope();
        using var first = new Held();
        using var current = new Held();
        scope.Services.Metadata.Register(Field("old", "Old demand", first.Produce));
        scope.Services.Metadata.Register(Field("current", "Current demand", current.Produce));
        Task? oldWork = null, newWork = null;
        try
        {
            await Wait(() => scope.Tab.Listing.State == ListingState.Complete);
            oldWork = scope.Tab.AnalyzeAsync("old");
            await Wait(() => first.Entered.IsCompleted);
            newWork = scope.Tab.AnalyzeAsync("current");
            await Wait(() => oldWork.IsCompleted && current.Entered.IsCompleted);
            Assert.True(oldWork.IsCompletedSuccessfully);
            Assert.False(newWork.IsCompleted);
            Assert.Equal("owned first", File.ReadAllText(Path.Join(scope.Folder, "first.txt")));
            Assert.Equal("owned second", File.ReadAllText(Path.Join(scope.Folder, "second.txt")));
            output.WriteLine(JsonSerializer.Serialize(new { outcome, OldCompleted = oldWork.IsCompleted, NewPending = !newWork.IsCompleted, scope.Tab.AnalysisStatus, scope.Tab.Banner, OwnedFilesUnchanged = true }));
            Assert.NotNull(scope.Tab.AnalysisStatus);
            Assert.Contains("Current demand", scope.Tab.AnalysisStatus);
            if (outcome == "cancel") Assert.True(scope.Tab.CancelAnalysis(), "Esc must still cancel the current analysis after the old one ends.");
            first.Release.Set();
            current.Release.Set();
            await newWork.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await Wait(() => first.Returned.IsCompleted && current.Returned.IsCompleted);
            Assert.True(newWork.IsCompletedSuccessfully);
            Assert.Null(scope.Tab.AnalysisStatus);
            if (outcome == "complete") Assert.Contains("Sorted by Current demand", scope.Tab.Banner);
            else Assert.StartsWith("Analysis canceled after ", scope.Tab.Banner);
        }
        finally
        {
            first.Release.Set();
            current.Release.Set();
            scope.Tab.CancelAnalysis();
            if (oldWork is not null) await oldWork.WaitAsync(TimeSpan.FromSeconds(3));
            if (newWork is not null) await newWork.WaitAsync(TimeSpan.FromSeconds(3));
            if (first.Entered.IsCompleted) await first.Returned.WaitAsync(TimeSpan.FromSeconds(3));
            if (current.Entered.IsCompleted) await current.Returned.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [AvaloniaFact]
    public async Task A_single_analysis_completes_and_preserves_owned_content()
    {
        using var scope = new Scope();
        scope.Services.Metadata.Register(Field("single", "Single demand", (_, _) => 1));
        await Wait(() => scope.Tab.Listing.State == ListingState.Complete);
        await scope.Tab.AnalyzeAsync("single").WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Null(scope.Tab.AnalysisStatus);
        Assert.Contains("Sorted by Single demand", scope.Tab.Banner);
        Assert.Equal("owned first", File.ReadAllText(Path.Join(scope.Folder, "first.txt")));
        Assert.Equal("owned second", File.ReadAllText(Path.Join(scope.Folder, "second.txt")));
    }

    private static MetadataField Field(string id, string title, Func<string, CancellationToken, object?> produce) =>
        new(id, title, MetadataCost.Analysis, _ => true, produce, v => v?.ToString() ?? "");

    private static async Task Wait(Func<bool> predicate)
    {
        for (int i = 0; i < 300 && !predicate(); i++) await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.True(predicate(), "Owned analysis control did not reach its required checkpoint.");
    }

    private sealed class Held : IDisposable
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public Task Returned => _returned.Task;
        public readonly ManualResetEventSlim Release = new();
        public object Produce(string path, CancellationToken ct)
        {
            _entered.TrySetResult();
            try
            {
                if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned analysis producer was not released.");
                return 1;
            }
            finally { _returned.TrySetResult(); }
        }
        public void Dispose() => Release.Dispose();
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _root = Path.Join(Path.GetTempPath(), "filecat-analysis-current", Guid.NewGuid().ToString("N"));
        public string Folder => Path.Join(_root, "files");
        public AppServices Services { get; }
        public MainViewModel Vm { get; }
        public TabViewModel Tab { get; }
        public Scope()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Join(Folder, "first.txt"), "owned first");
            File.WriteAllText(Path.Join(Folder, "second.txt"), "owned second");
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(_root, "state")));
            Vm = new MainViewModel(Services);
            var panel = new PanelViewModel(Vm.Workspace, Services);
            Vm.Workspace.Panels.Add(panel);
            panel.OpenTab(ResultSetProvider.WorkingSetList);
            Tab = panel.OpenTab(Location.FileSystem(Folder));
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
