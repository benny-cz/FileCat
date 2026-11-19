using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public void FolderExclusionsKeepNativeChangeEvidenceAtTheSelectedPath()
    {
        byte[] path = "Cache"u8.ToArray();
        var changed = AnalysisEntry.Unavailable([.. path], [.. path], "unix-bytes", AnalysisKind.Directory,
            MeasurementState.Changed, "entry-changed");
        Assert.Same(changed, AnalysisFolderExclusions.Apply(changed, [path]));
    }

    [AvaloniaFact]
    public async Task FolderExclusionsMatchLiteralNativeFolderNames()
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        string input = Directory.CreateDirectory(Path.Combine(fixture.Root, "input")).FullName;
        const string rawName = "trailing. ";
        string exact = Path.Combine(input, rawName);
        if (OperatingSystem.IsWindows()) exact = "\\\\?\\" + exact;
        Directory.CreateDirectory(exact);
        string hidden = Path.Combine(exact, "hidden.txt");
        using var model = new DiskSpaceViewModel(fixture.Services) { Metric = AnalysisMetric.FileSize };
        try
        {
            File.WriteAllText(hidden, "unobserved");
            File.WriteAllText(Path.Combine(input, "visible.txt"), "observed");
            await model.StartAsync([input], [new(0, rawName)]);
            await Until(() => model.Phase == AnalysisPhase.Finished, timeout: TimeSpan.FromSeconds(25));
            await model.RebuildAsync(true, TestContext.Current.CancellationToken);
            var snapshot = model.Tree!.Snapshot;
            Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, snapshot.Coverage);
            Assert.Contains(snapshot.Nodes, node => node.Name == rawName && !node.IncludedByPolicy);
            Assert.DoesNotContain(snapshot.Nodes, node => node.Name == "hidden.txt");
            Assert.Equal((UInt128)8, model.Tree.Projection.Totals.Known);
        }
        finally
        {
            model.Dispose(); await model.CloseCompletion;
            File.Delete(hidden); Directory.Delete(exact);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderExclusionsPreserveNativeCoverageAcrossInventoryRescanAndBranchRefresh(bool indexed)
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        var token = TestContext.Current.CancellationToken;
        string root = Path.Combine(fixture.Root, "input");
        string branch = Path.Combine(root, "branch");
        string disjoint = Path.Combine(fixture.Root, "other");
        AddFile(branch, "Cache", "hidden-first.txt", 100);
        AddFile(branch, "CacheTwo", "hidden-second.txt", 200);
        AddFile(branch, "CacheNearby", "kept-first.txt", 3);
        AddFile(disjoint, "Cache", "kept-other.txt", 4);

        await using var original = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, indexedStorage: indexed);
        var sources = new List<IAnalysisBackend>();
        AnalysisSession scan;
        try
        {
            foreach (string path in new[] { root, branch, disjoint })
            {
                sources.Add(await AnalysisIo.Run(fixture.Services.Io, PathUtil.GetDeviceKey(path), _ =>
                    OperatingSystem.IsWindows() ? (IAnalysisBackend)new WindowsAnalysisBackend(path, null, BeforeChildren, null) :
                        new UnixAnalysisBackend(path, null, BeforeChildren, null), token));
            }
            Assert.Throws<ArgumentException>(() => original.Start(sources, exclusions: [new(0, "branch")]));
            scan = original.Start(sources, exclusions: [new(0, "branch/Cache"), new(1, "CacheTwo")]);
            sources.Clear();
        }
        finally { foreach (var source in sources) await AnalysisIo.ReleaseAsync(fixture.Services.Io, source); }
        await scan.Completion.WaitAsync(TimeSpan.FromSeconds(30), token);
        var first = scan.Snapshot();
        Verify(first, 7);
        Assert.Equal(AnalysisRootDisposition.Covered, first.Roots[1].Disposition);
        Assert.Contains(first.Nodes, node => node.Name == "kept-other.txt");

        using var encoded = new MemoryStream();
        AnalysisInventoryFormat.Write(encoded, first, Guid.NewGuid(), token);
        encoded.Position = 0;
        using var decoded = AnalysisInventoryFormat.Read(encoded, token);
        Verify(decoded.Snapshot, 7);
        Assert.Equal(first.Policy.Fingerprint, decoded.Snapshot.Policy.Fingerprint);

        var (revision, selected) = await FinishedBranchAsync(scan, token);
        await using var refresh = await original.BeginBranchRefreshAsync(revision, selected, cancellation: token);
        await refresh.RuntimeSession.Completion.WaitAsync(TimeSpan.FromSeconds(30), token);
        var refreshed = refresh.RuntimeSession.Snapshot();
        Verify(refreshed, 3);
        Assert.Equal("branch", Assert.Single(refreshed.Roots).Label);
        Assert.DoesNotContain(refreshed.Nodes, node => node.Name == "kept-other.txt");

        await using var next = await original.BeginRescanAsync(cancellation: token);
        await next.RuntimeSession.Completion.WaitAsync(TimeSpan.FromSeconds(30), token);
        var rescanned = next.RuntimeSession.Snapshot();
        Verify(rescanned, 7);
        Assert.Equal(first.Identity.Epoch + 1, rescanned.Identity.Epoch);
        Assert.Equal(first.Policy.Fingerprint, rescanned.Policy.Fingerprint);
        Assert.Equal(100, new FileInfo(Path.Combine(branch, "Cache", "hidden-first.txt")).Length);

        await using var unrestricted = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, indexedStorage: false);
        var full = await StartAcquiredAsync(unrestricted, fixture, root, branch, disjoint);
        var mapping = first.Roots.Where(root => root.Disposition == AnalysisRootDisposition.Active).ToDictionary(root => root.Id, root => root.Id);
        using var comparison = AnalysisComparison.Build(first, full.Snapshot(), AnalysisMetric.FileSize, mapping, token);
        Assert.False(comparison.Compatibility.SamePolicy);
        Assert.Null(comparison.NetChange);
        Assert.DoesNotContain(comparison.Rows, row => row.Change is AnalysisChange.Added or AnalysisChange.Growing);

        static void AddFile(string parent, string name, string file, int bytes) =>
            File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(parent, name)).FullName, file), new byte[bytes]);
        static void BeforeChildren(AnalysisEntry entry, CancellationToken _) =>
            Assert.False(entry.Name == "CacheTwo" || entry.Name == "Cache" &&
                (entry.RelativeName.AsSpan().Contains((byte)'/') || entry.RelativeName.AsSpan().Contains((byte)'\\')),
                "A configured exclusion must keep the scanner from enumerating the folder.");
        static void Verify(AnalysisSnapshot snapshot, ulong bytes)
        {
            Assert.Equal(AnalysisPhase.Finished, snapshot.Phase);
            Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, snapshot.Coverage);
            Assert.Equal(2, AnalysisFolderExclusions.RuleCount(snapshot.Policy));
            Assert.Equal(2UL, snapshot.BoundaryTotals[AnalysisBoundaryKind.FolderExclusion]);
            Assert.Equal(2, snapshot.Nodes.Count(node => !node.IncludedByPolicy && node.MetadataState == MeasurementState.Excluded));
            Assert.DoesNotContain(snapshot.Nodes, node => node.Name.StartsWith("hidden-", StringComparison.Ordinal));
            Assert.Equal((UInt128)bytes, AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize).Totals.Known);
        }
    }

    [AvaloniaFact]
    public async Task FolderExclusionsSetupCapturesTheRightLocationAndRejectsAnEscapingPath()
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        using var styles = new ProductStyles();
        var token = TestContext.Current.CancellationToken;
        string root = Path.Combine(fixture.Root, "input");
        Directory.CreateDirectory(Path.Combine(root, "Cache"));
        File.WriteAllText(Path.Combine(root, "visible.txt"), "visible");
        using var model = new DiskSpaceViewModel(fixture.Services);
        var window = new DiskSpaceWindow();
        window.AddSession(model, [fixture.Root + "-removed", root]);
        window.Show();
        try
        {
            await model.StartFastNtfsAsync([root], 0, (_, _) => throw new InvalidOperationException("A scoped scan must never request elevation."), [new(0, "Cache")]);
            Assert.Equal(DiskSpaceViewModel.FastNtfsFolderExclusionsReason, model.Status);
            window.GetVisualDescendants().OfType<Expander>().Single(control => control.Header as string == "Exclude folders…").IsExpanded = true;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Editor("Excluded folders for location 1").Text = "other";
            Editor("Excluded folders for location 2").Text = "../outside";
            Assert.IsAssignableFrom<Grid>(Editor("Analysis location 1").Parent).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("../outside", Editor("Excluded folders for location 1").Text);

            ClickStart();
            await WaitUntil(() => !model.IsPreparing && model.Status.Contains("relative to its location", StringComparison.Ordinal));
            Assert.Null(model.Tree);
            Assert.Equal(AnalysisPhase.Ready, model.Phase);

            Editor("Excluded folders for location 1").Text = "Cache";
            ClickStart();
            await WaitUntil(() => model.Tree?.Snapshot is { Phase: AnalysisPhase.Finished });
            var snapshot = model.Tree!.Snapshot;
            Assert.Equal(1, AnalysisFolderExclusions.RuleCount(snapshot.Policy));
            Assert.Contains(snapshot.Nodes, node => node.Name == "visible.txt");
            Assert.Contains(snapshot.Nodes, node => node.Name == "Cache" && !node.IncludedByPolicy);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }

        TextBox Editor(string name) => window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == name);
        void ClickStart() => window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Start analysis").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task WaitUntil(Func<bool> ready)
        {
            for (int i = 0; i < 500 && !ready(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20, token); }
            Assert.True(ready(), model.Status);
        }
    }
}
