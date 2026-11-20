using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeRescanPreparationKeepsTheOldRevisionPrivateProfileAndExplicitMemoryFallback(bool indexed, bool fallback)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        if (fallback) await CorruptProfileForMemoryAsync(fixture);
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "first.bin"), "one");
        await using var original = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexed);
        var first = await StartAcquiredAsync(original, fixture, input); var old = first.Snapshot();
        using var retained = first.AcquireSnapshot();
        await using var oldDataset = await first.AcquireDatasetAsync(token);
        string profile = original.ProfileIdentity; byte[] state = File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile);
        File.WriteAllText(Path.Combine(input, "second.bin"), "two");
        await using var next = await AnalysisIndexedAcquisition.PrepareRescanAsync(original, token);
        Assert.Equal(profile, next.ProfileIdentity); Assert.Equal(fallback, next.MemoryPreparationNotice is not null);
        var second = await StartAcquiredAsync(next, fixture, input); var current = second.Snapshot();
        Assert.Equal(old.Identity.SessionId, current.Identity.SessionId); Assert.Equal(old.Identity.Epoch + 1, current.Identity.Epoch);
        Assert.True(current.Identity.Revision > old.Identity.Revision); Assert.Equal(old, retained.Snapshot);
        Assert.DoesNotContain(old.Nodes, node => node.Name == "second.bin"); Assert.Contains(current.Nodes, node => node.Name == "second.bin");
        await using var newDataset = await second.AcquireDatasetAsync(token);
        if (indexed && !fallback)
        {
            Assert.NotNull(oldDataset); Assert.NotNull(newDataset);
            Assert.Equal(old.Identity.Epoch, oldDataset.Descriptor.Identity.Epoch); Assert.Equal(current.Identity.Epoch, newDataset.Descriptor.Identity.Epoch);
            Assert.Equal(3U, (await oldDataset.SummarizeAsync(AnalysisMetric.FileSize, token)).Totals.Known);
            Assert.Equal(6U, (await newDataset.SummarizeAsync(AnalysisMetric.FileSize, token)).Totals.Known);
        }
        else { Assert.Null(oldDataset); Assert.Null(newDataset); }
        Assert.Equal(state, File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRescanPreparationRefusesAnUnusedOrClosedPreparation(bool closed)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await using var original = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token);
        if (closed) await original.DisposeAsync();
        if (closed) await Assert.ThrowsAsync<ObjectDisposedException>(() => AnalysisIndexedAcquisition.PrepareRescanAsync(original, token));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisIndexedAcquisition.PrepareRescanAsync(original, token));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRescanPreparationKeepsPrivateNamesExcludedAcrossBothEpochs(bool indexed)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await using (var initial = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token)) { }
        string canary = Path.Combine(fixture.Services.Paths.AnalysisSnapshotsDirectory, "RESCAN_PRIVATE_CANARY.txt");
        File.WriteAllText(canary, "private content must remain untouched");
        File.WriteAllText(Path.Combine(fixture.Root, "first-visible.bin"), "one");
        await using var original = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexed);
        var first = await StartAcquiredAsync(original, fixture, fixture.Root); var old = first.Snapshot();
        File.WriteAllText(Path.Combine(fixture.Root, "second-visible.bin"), "two");
        await using var next = await AnalysisIndexedAcquisition.PrepareRescanAsync(original, token);
        var second = await StartAcquiredAsync(next, fixture, fixture.Root); var current = second.Snapshot();
        foreach (var snapshot in new[] { old, current })
        {
            Assert.DoesNotContain(snapshot.Nodes, row => row.Name == "RESCAN_PRIVATE_CANARY.txt");
            Assert.True(snapshot.Nodes.Count(row => !row.IncludedByPolicy && row.MetadataState == MeasurementState.Excluded) >= 2);
            foreach (var boundary in snapshot.Nodes.Where(row => !row.IncludedByPolicy && row.MetadataState == MeasurementState.Excluded))
            {
                Assert.Null(boundary.Times?.CreatedUtc); Assert.Null(boundary.Times?.CreatedPrecisionTicks);
                Assert.Null(boundary.Times?.ChangedUtc); Assert.Null(boundary.Times?.ChangedPrecisionTicks);
            }
        }
        Assert.DoesNotContain(old.Nodes, row => row.Name == "second-visible.bin");
        Assert.Contains(current.Nodes, row => row.Name == "second-visible.bin");
        Assert.Equal("private content must remain untouched", File.ReadAllText(canary));
    }

    [AvaloniaTheory]
    [InlineData("snapshots")]
    [InlineData("scratch")]
    [InlineData("history")]
    public async Task NativeRescanPreparationRefusesPrivateBoundaryReplacementEvenWithACopiedProfileMarker(string boundary)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "keep.bin"), "untouched");
        await using var original = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: false);
        var session = await StartAcquiredAsync(original, fixture, input); var old = session.Snapshot();
        byte[] profile = File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile);
        string selected = boundary switch
        {
            "snapshots" => fixture.Services.Paths.AnalysisSnapshotsDirectory,
            "scratch" => fixture.Services.Paths.AnalysisScratchDirectory,
            _ => fixture.Services.Paths.AnalysisHistoryDirectory
        };
        string moved = Path.GetFullPath(Path.Combine(fixture.Root, "owned-original-" + boundary));
        string scope = Path.GetFullPath(fixture.Root) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Assert.StartsWith(scope, Path.GetFullPath(selected), comparison); Assert.StartsWith(scope, moved, comparison);
        Directory.Move(selected, moved);
        await using (var replacement = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: false)) { }
        // The profile marker correlates data. Copying it cannot prove that the held private root
        // is the same object; each physical boundary must independently match its runtime witness.
        File.WriteAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile, profile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisIndexedAcquisition.PrepareRescanAsync(original, token));
        Assert.Equal(old, session.Snapshot()); Assert.Equal("untouched", File.ReadAllText(Path.Combine(input, "keep.bin")));
        Assert.True(Directory.Exists(moved));
    }
}
