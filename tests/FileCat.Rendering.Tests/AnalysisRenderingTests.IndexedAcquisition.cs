using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task PreparedPrivateProfileFeedsTheFullIndexedWindowAfterAcquisitionOwnerCloses()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        var paths = fixture.Services.Paths;
        await using (var initial = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, TestContext.Current.CancellationToken)) { }
        byte[] state = File.ReadAllBytes(paths.AnalysisPrivateStateFile);
        string canary = Path.Combine(paths.AnalysisSnapshotsDirectory, "PRIVATE_PROFILE_NAME_CANARY.txt"); File.WriteAllText(canary, "private payload");
        string scratchCanary = Path.Combine(paths.AnalysisScratchDirectory, "PRIVATE_SCRATCH_NAME_CANARY.txt"); File.WriteAllText(scratchCanary, "preserve this neighbor");
        string sourcePath = Path.Combine(fixture.Root, "source"); Directory.CreateDirectory(sourcePath);
        for (int at = 0; at < 4_100; at++) File.WriteAllBytes(Path.Combine(sourcePath, $"row-{at:D4}.bin"), [17]);
        await using var acquisition = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, TestContext.Current.CancellationToken);
        string device = PathUtil.GetDeviceKey(fixture.Root); IAnalysisBackend? source = await AnalysisIo.Run(fixture.Services.Io, device, _ => NativeRoot(fixture.Root), TestContext.Current.CancellationToken);
        AnalysisDatasetLease? lease = null; var window = new DiskSpaceWindow();
        try
        {
            var session = acquisition.Start([source]); source = null;
            await session.Completion.WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);
            Assert.Equal(AnalysisPhase.Finished, session.Snapshot().Phase); Assert.True(session.DatasetDescriptor!.Paths > 4_096);
            Assert.True(session.Snapshot().Nodes.Length <= 4_096); Assert.Equal(0UL, session.SourceCloseFailures);
            Assert.Equal(acquisition.Profile.ProfileId, session.DatasetDescriptor.Identity.HostProfileIdentity!.ProfileId);
            Assert.Throws<InvalidOperationException>(() => acquisition.Start([]));
            lease = await session.AcquireDatasetAsync(TestContext.Current.CancellationToken); Assert.NotNull(lease);
            ulong offset = 0; int excluded = 0;
            do
            {
                var page = await lease.ReadPathsAsync(offset, maximum: 128, cancellation: TestContext.Current.CancellationToken);
                Assert.DoesNotContain(page.Rows, node => node.Name.Contains("PRIVATE_", StringComparison.Ordinal));
                excluded += page.Rows.Count(node => !node.IncludedByPolicy && node.MetadataState == MeasurementState.Excluded && node.Kind == AnalysisKind.Directory);
                offset = page.NextOffset;
            } while (offset < lease.Descriptor.Paths);
            Assert.True(excluded >= 2);
            // The acquisition closes first; the displayed dataset keeps its native store and quota.
            await acquisition.DisposeAsync();
            var explorer = await window.AddIndexedSessionAsync(lease, AnalysisMetric.Files, new("name:row-"), cancellation: TestContext.Current.CancellationToken);
            await lease.DisposeAsync(); lease = null; window.Show(); Frame(window);
            Assert.Equal(4_100UL, explorer.LogicalTotals.Files);
            Assert.Equal(state, File.ReadAllBytes(paths.AnalysisPrivateStateFile)); Assert.Equal("private payload", File.ReadAllText(canary));
            Assert.Equal("preserve this neighbor", File.ReadAllText(scratchCanary));
        }
        finally
        {
            window.ForceClose(); await window.WaitForIndexedCloseAsync();
            if (lease is not null) await lease.DisposeAsync();
            if (source is not null) await AnalysisIo.ReleaseAsync(fixture.Services.Io, source);
            await acquisition.DisposeAsync();
        }
        Assert.Equal(new[] { scratchCanary }, Directory.GetFiles(paths.AnalysisScratchDirectory));
    }

    [AvaloniaFact]
    public async Task UnstartedIndexedPreparationClosesNativeCapabilitiesAfterSchedulerShutdown()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        var acquisition = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, TestContext.Current.CancellationToken);
        byte[] state = File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile);
        fixture.Services.Io.Dispose(); await acquisition.DisposeAsync(); await acquisition.DisposeAsync();
        Assert.Equal(state, File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile));
        Assert.Empty(Directory.GetFiles(fixture.Services.Paths.AnalysisScratchDirectory));
        Assert.Throws<ObjectDisposedException>(() => acquisition.Start([]));
    }

    [AvaloniaFact]
    public async Task CorruptPrivateIdentityPreservesBytesAndRefusesIndexedScratchPreparation()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        await using (var initial = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, TestContext.Current.CancellationToken)) { }
        string path = fixture.Services.Paths.AnalysisPrivateStateFile; const string corrupt = "{\"schema\":\"future\",\"profile\":\"private-canary\"}";
        File.WriteAllText(path, corrupt); Directory.Delete(fixture.Services.Paths.AnalysisScratchDirectory, recursive: false);
        await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, TestContext.Current.CancellationToken));
        Assert.Equal(corrupt, File.ReadAllText(path)); Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisScratchDirectory));
    }
    private static IAnalysisBackend NativeRoot(string path) => OperatingSystem.IsWindows() ? new WindowsAnalysisBackend(path) : new UnixAnalysisBackend(path);
}
