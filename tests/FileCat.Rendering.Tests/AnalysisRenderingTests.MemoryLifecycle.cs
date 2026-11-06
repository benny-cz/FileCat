using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopOrCloseAtTheInitialScanningNotificationNeverStartsTheAcquisition(bool close)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); var source = new PreparedSource(fixture.Root);
        using var model = new DiskSpaceViewModel(fixture.Services, _ => source); bool interrupted = false;
        model.Changed += () =>
        {
            if (interrupted || model.Phase != AnalysisPhase.Scanning) return;
            interrupted = true; if (close) model.Dispose(); else model.Stop();
        };
        await model.StartAsync([fixture.Root]); Assert.True(interrupted); Assert.False(model.IsPreparing); Assert.False(model.Running);
        Assert.Null(model.Tree); Assert.Equal(0, source.Enumerations); Assert.Equal(1, source.CloseCalls);
        Assert.NotEqual(fixture.UiThread, source.CloseThread); model.Dispose(); await model.CloseCompletion;
    }

    [AvaloniaFact]
    public async Task NormalStartExcludesItsOwnArtifactsWithoutClaimingScratchFailure()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        string file = Path.Combine(fixture.Root, "keep.bin"); File.WriteAllText(file, "source unchanged");
        using var model = new DiskSpaceViewModel(fixture.Services);
        try
        {
            await model.StartAsync([fixture.Root]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true);
            var snapshot = model.Tree!.Snapshot; AnalysisValidation.Validate(snapshot, TestContext.Current.CancellationToken);
            Assert.Contains(snapshot.Nodes, n => n.Name == "keep.bin");
            Assert.DoesNotContain(snapshot.Nodes, n => n.Name == Path.GetFileName(fixture.Services.Paths.AnalysisPrivateStateFile));
            Assert.Contains(snapshot.Nodes, n => !n.IncludedByPolicy && n.MetadataState == MeasurementState.Excluded);
            Assert.Contains("bounded memory", model.AcquisitionNotice); Assert.DoesNotContain("unavailable", model.AcquisitionNotice);
            Assert.DoesNotContain("scratch is unavailable", model.Status, StringComparison.OrdinalIgnoreCase); Assert.True(model.HasPrivateStorage);
            Assert.NotNull(snapshot.Identity.HostProfileIdentity); Assert.Equal("source unchanged", File.ReadAllText(file));
            using var retained = model.CaptureDisplayedRevision(); model.Dispose(); await model.CloseCompletion;
            Assert.Contains(retained.Snapshot.Nodes, n => n.Name == "keep.bin");
        }
        finally { model.Dispose(); await model.CloseCompletion; }
    }

    [AvaloniaFact]
    public async Task NormalStartPreservesUnsupportedStateAndReportsOnlyTheUnreadRoot()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); await CorruptProfileForMemoryAsync(fixture);
        string parent = Path.GetDirectoryName(fixture.Services.Paths.AnalysisScratchDirectory)!;
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input); File.WriteAllText(Path.Combine(input, "safe.bin"), "safe");
        byte[] state = File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile);
        using var model = new DiskSpaceViewModel(fixture.Services); var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(model); window.Show(); await model.StartAsync([parent, input]);
            await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true);
            var snapshot = model.Tree!.Snapshot; Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage);
            Assert.Equal(AnalysisRootDisposition.UnavailablePrivateBoundary, snapshot.Roots[0].Disposition);
            Assert.Contains(snapshot.Nodes, n => n.Name == "safe.bin"); Assert.Contains("Not read because a private-state boundary", model.AcquisitionNotice);
            Assert.Contains("Private profile continuity could not be loaded or created", model.ContinuityNotice); Assert.False(model.HasPrivateStorage);
            await Assert.ThrowsAsync<InvalidOperationException>(() => model.SaveNewAsync("refused.fcdsa", TestContext.Current.CancellationToken));
        }
        finally { window.ForceClose(); model.Dispose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
        Assert.Equal(state, File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile)); Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisScratchDirectory));
        Assert.Equal("safe", File.ReadAllText(Path.Combine(input, "safe.bin")));
    }

    [AvaloniaFact]
    public async Task ResolvedTargetReviewRetainsTheSameNativePreparationUntilSecondStart()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input); File.WriteAllText(Path.Combine(input, "captured.bin"), "captured");
        int calls = 0; using var model = new DiskSpaceViewModel(fixture.Services, _ => { calls++; return NativeRoot(input); });
        try
        {
            await model.StartAsync([fixture.Root]); Assert.Equal(AnalysisPhase.Ready, model.Phase); Assert.False(model.IsPreparing);
            Assert.Contains("Review the resolved locations", model.Status); Assert.Contains("input", model.ResolvedTargets);
            byte[] state = File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile);
            await model.StartAsync([fixture.Root]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true);
            Assert.Equal(1, calls); Assert.Contains(model.Tree!.Snapshot.Nodes, n => n.Name == "captured.bin");
            Assert.Equal(state, File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile));
        }
        finally { model.Dispose(); await model.CloseCompletion; }
    }
}
