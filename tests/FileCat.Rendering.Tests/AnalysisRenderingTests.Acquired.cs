using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static async Task CorruptProfileForMemoryAsync(PreparationFixture fixture)
    {
        await using (var initial = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, TestContext.Current.CancellationToken)) { }
        File.WriteAllText(fixture.Services.Paths.AnalysisPrivateStateFile, "{\"schema\":\"future\",\"private-canary\":true}");
        Directory.Delete(fixture.Services.Paths.AnalysisScratchDirectory, recursive: false);
    }
    private static async Task<AnalysisSession> StartAcquiredAsync(AnalysisIndexedAcquisition acquisition, PreparationFixture fixture, params string[] paths)
    {
        var sources = new List<IAnalysisBackend>();
        try
        {
            foreach (string path in paths) sources.Add(await AnalysisIo.Run(fixture.Services.Io, PathUtil.GetDeviceKey(path), _ => NativeRoot(path), TestContext.Current.CancellationToken));
            var session = acquisition.Start(sources); sources.Clear();
            await session.Completion.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken); return session;
        }
        finally { foreach (var source in sources) await AnalysisIo.ReleaseAsync(fixture.Services.Io, source); }
    }

    [AvaloniaFact]
    public async Task MemoryHandoffKeepsItsStoreAfterAcquisitionClosesAndShowsTheFallbackNotice()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        await CorruptProfileForMemoryAsync(fixture); string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "keep.bin"), "untouched"); byte[] state = File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile);
        await using var acquisition = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, TestContext.Current.CancellationToken);
        var session = await StartAcquiredAsync(acquisition, fixture, input); Assert.True(session.UsesMemoryFallback);
        var window = new DiskSpaceWindow(); AnalysisSnapshotLease? displayed = null;
        try
        {
            var view = await window.AddAcquiredSessionAsync(session, cancellation: TestContext.Current.CancellationToken);
            Assert.Null(view.Indexed); Assert.NotNull(view.Memory); Assert.False(view.Memory.HasPrivateStorage);
            await acquisition.DisposeAsync();
            await view.Memory.RebuildAsync(true); Assert.Equal(2, view.Memory.Tree!.Snapshot.Nodes.Length);
            displayed = view.Memory.CaptureDisplayedRevision(); Assert.Contains("bounded memory", view.Memory.AcquisitionNotice);
            Assert.Contains("continuity is unknown", view.Memory.ContinuityNotice);
            window.Show(); Frame(window);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("This analysis uses bounded memory", StringComparison.Ordinal) == true);
            window.ForceClose(); await window.WaitForIndexedCloseAsync();
            using var retained = displayed.Retain(); displayed.Dispose(); displayed = null;
            Assert.Equal(2, retained.Snapshot.Nodes.Length); Assert.Equal(AnalysisOrigin.LiveSession, retained.Snapshot.Origin);
            Assert.Equal(state, File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile)); Assert.Equal("untouched", File.ReadAllText(Path.Combine(input, "keep.bin")));
            Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisScratchDirectory));
        }
        finally { displayed?.Dispose(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task MissingPrivateBoundaryRefusesOnlyOverlappingSourcesAndExplainsTheScope()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture(); await CorruptProfileForMemoryAsync(fixture);
        string parent = Path.GetDirectoryName(fixture.Services.Paths.AnalysisScratchDirectory)!;
        string hidden = Path.Combine(parent, "PRIVATE_PARENT_CANARY.bin"); File.WriteAllText(hidden, "untouched private neighbor");
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input); File.WriteAllText(Path.Combine(input, "safe.bin"), "safe source");
        await using var acquisition = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, TestContext.Current.CancellationToken);
        var session = await StartAcquiredAsync(acquisition, fixture, parent, input); var snapshot = session.Snapshot(); AnalysisValidation.Validate(snapshot, TestContext.Current.CancellationToken);
        Assert.True(session.UsesMemoryFallback); Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage);
        Assert.Equal(AnalysisRootDisposition.UnavailablePrivateBoundary, snapshot.Roots[0].Disposition);
        Assert.Null(snapshot.Roots[0].ObservedObjectIdentity); Assert.Null(snapshot.Roots[0].SourceIdentity);
        Assert.Contains(snapshot.Nodes, n => n.Name == "safe.bin"); Assert.DoesNotContain(snapshot.Nodes, n => n.Name.Contains("PRIVATE_", StringComparison.Ordinal));
        var window = new DiskSpaceWindow();
        try
        {
            var view = await window.AddAcquiredSessionAsync(session, cancellation: TestContext.Current.CancellationToken); Assert.NotNull(view.Memory);
            await acquisition.DisposeAsync(); await view.Memory.RebuildAsync(true); window.Show(); Frame(window);
            Assert.Contains("Not read because a private-state boundary", view.Memory.AcquisitionNotice);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Not read because a private-state boundary", StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Recorded partial results; unread scopes remain unknown", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Known results so far; table ranking can change", StringComparison.Ordinal) == true);
            Assert.Equal("untouched private neighbor", File.ReadAllText(hidden)); Assert.Equal("safe source", File.ReadAllText(Path.Combine(input, "safe.bin")));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedHandoffRedactsASelectedPrivateFileAlreadyCoveredByItsParent()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        await using var acquisition = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, TestContext.Current.CancellationToken);
        var session = await StartAcquiredAsync(acquisition, fixture, fixture.Root, fixture.Services.Paths.AnalysisPrivateStateFile);
        Assert.False(session.UsesMemoryFallback); Assert.NotNull(session.DatasetDescriptor);
        var root = session.DatasetDescriptor.Roots[1]; Assert.Equal(AnalysisRootDisposition.ExcludedPrivateState, root.Disposition);
        Assert.Null(root.SourceIdentity); Assert.Null(root.ObservedObjectIdentity); Assert.Equal(IdentityAssurance.Unknown, root.Assurance);
        var window = new DiskSpaceWindow();
        try
        {
            var view = await window.AddAcquiredSessionAsync(session, cancellation: TestContext.Current.CancellationToken);
            Assert.NotNull(view.Indexed); Assert.Null(view.Memory); await acquisition.DisposeAsync(); window.Show(); Frame(window);
            Assert.Equal(1, window.SessionCount);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task UnstartedMemoryPreparationClosesAfterSchedulerShutdownAndPreservesState()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); await CorruptProfileForMemoryAsync(fixture);
        var acquisition = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, TestContext.Current.CancellationToken);
        Assert.NotNull(acquisition.MemoryPreparationNotice); byte[] state = File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile);
        fixture.Services.Io.Dispose(); await acquisition.DisposeAsync(); await acquisition.DisposeAsync();
        Assert.Equal(state, File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile));
        Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisScratchDirectory));
        Assert.Throws<ObjectDisposedException>(() => acquisition.Start([]));
    }
}
