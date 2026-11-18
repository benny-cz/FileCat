using System.Collections.Immutable;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDuplicateReviewEstimatesMarkedObjectsAndLeavesSourceAndSelectionsIntact(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "duplicate-review");
        Directory.CreateDirectory(input);
        string first = Path.Combine(input, "primary.bin");
        string alias = Path.Combine(input, "additional-name.bin");
        string second = Path.Combine(input, "same-size-different-contents.bin");
        string unique = Path.Combine(input, "unique-length.bin");
        File.WriteAllBytes(first, [1, 2, 3, 4]);
        CreateGroupedNativeAlias(first, alias);
        File.WriteAllBytes(second, [4, 3, 2, 1]);
        File.WriteAllBytes(unique, [1]);

        var pool = new AnalysisSessionAdmission();
        var window = new DiskSpaceWindow(null, null, pool);
        try
        {
            window.Show();
            var opened = await window.AddOwnedAcquisitionAsync(fixture.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, fixture, input); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            });
            Frame(window);
            var review = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Review duplicate candidates…"));
            Assert.False(review.IsEnabled);
            Click(window, "Mark matching files in this scope");
            await Until(() => review.IsEnabled && (indexed ? IndexedSelectionCount(window, 4, 0) : opened.Memory!.Selection.Marks.Count == 4),
                () => ComparisonText(window));
            if (indexed)
            {
                Click(window, "Add to review basket");
                var query = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationLabel(text) == "Filter indexed observations");
                query.Text = "name:unique-length.bin";
                Click(window, "Apply filter");
                await Until(() => IndexedSelectionSummary(window).Contains("3 marked items hidden by filters", StringComparison.Ordinal),
                    () => ComparisonText(window));
            }
            else opened.Memory!.Selection.StageCaptured(opened.Memory.Selection.Marks);
            Click(window, "Review duplicate candidates…");
            await Until(() => ComparisonControl<ListBox>(window, "Captured duplicate candidates and exclusions")?.ItemCount == 4,
                () => ComparisonText(window), TimeSpan.FromSeconds(20));
            var rows = ComparisonControl<ListBox>(window, "Captured duplicate candidates and exclusions")!.Items.Cast<string>().ToArray();
            Assert.Equal(2, rows.Count(row => row.Contains("Same-size candidate", StringComparison.Ordinal)));
            Assert.Single(rows, row => row.Contains("Additional name for the same recorded file", StringComparison.Ordinal));
            Assert.Single(rows, row => row.Contains("No separate marked object with this size", StringComparison.Ordinal));
            var description = ComparisonText(window);
            Assert.Contains("8 B for 2 distinct candidate objects", description);
            Assert.Contains("0 marked file paths have unknown", description);
            Assert.Contains("streams, forks and other metadata would remain unexamined", description);
            Assert.Contains("A bound stable-content reader has not been qualified", description);
            Click(window, "Close review");
            if (indexed) Assert.True(IndexedSelectionCount(window, 4, 4));
            else
            {
                Assert.Equal(4, opened.Memory!.Selection.Marks.Count);
                Assert.Equal(4, opened.Memory.Selection.Basket.Count);
            }
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(first));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(alias));
            Assert.Equal(new byte[] { 4, 3, 2, 1 }, File.ReadAllBytes(second));
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(unique));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);
    }

    [Fact]
    public void DuplicateReviewCountsUnknownSizesAndExcludesCloudAndUnverifiedObjects()
    {
        var identity = DuplicateEstimateIdentity();
        var estimate = new AnalysisDuplicateEstimates([DuplicateEstimateRoot], identity, DuplicateSelection(identity, [1, 2, 3, 4, 5, 6]));
        var file = new AnalysisNode(1, DuplicateEstimateRoot.Id, 0, "observed.bin", AnalysisKind.File, "one") { ObservedIdentity = "native-one" };
        var observed = new AnalysisObject("one", "native-one", AnalysisMeasurement.Known(64), AnalysisMeasurement.Known(1), VersionEvidence: "v1");
        estimate.Add(1, file, observed);
        estimate.Add(2, file with { Id = 2, Cloud = true, ObjectId = "cloud" }, observed with { Id = "cloud" });
        estimate.Add(3, file with { Id = 3, ObjectId = "changed" }, observed with { Id = "changed", LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Changed) });
        estimate.Add(4, file with { Id = 4, ObjectId = "unverified", ObservedIdentity = null }, observed with { Id = "unverified", VerifiedIdentity = null });
        estimate.Add(5, file with { Id = 5, Kind = AnalysisKind.Link, ObjectId = null }, null);
        estimate.Add(6, null, null);
        var result = estimate.Finish(identity, AnalysisOrigin.ImportedInventory, NamespaceCoverage.Partial);
        Assert.Equal(0, result.CandidateObjects);
        Assert.Equal(1, result.UnknownBytePaths);
        Assert.Equal(BigInteger.Zero, result.KnownBytes);
        Assert.Equal(AnalysisOrigin.ImportedInventory, result.Origin);
        Assert.Equal(NamespaceCoverage.Partial, result.Coverage);
        Assert.Equal([AnalysisDuplicateCandidateState.NoSizePeer, AnalysisDuplicateCandidateState.Cloud, AnalysisDuplicateCandidateState.UnknownSize,
            AnalysisDuplicateCandidateState.UnverifiedObject, AnalysisDuplicateCandidateState.NotRegularFile, AnalysisDuplicateCandidateState.Absent],
            result.Rows.Select(row => row.State));
    }

    [Fact]
    public void DuplicateReviewRejectsStaleOrOversizedScopeAndKeepsExactLargeCost()
    {
        var identity = DuplicateEstimateIdentity();
        var selection = DuplicateSelection(identity, [1, 2]);
        Assert.Throws<InvalidOperationException>(() => new AnalysisDuplicateEstimates([DuplicateEstimateRoot], identity, selection with { Revision = identity.Revision + 1 }));
        Assert.Throws<InvalidOperationException>(() => new AnalysisDuplicateEstimates([DuplicateEstimateRoot], identity, selection with { Epoch = identity.Epoch + 1 }));
        Assert.Throws<ArgumentException>(() => new AnalysisDuplicateEstimates([DuplicateEstimateRoot], identity, selection with { NodeIds = [1, 1] }));
        Assert.Throws<InvalidOperationException>(() => new AnalysisDuplicateEstimates([DuplicateEstimateRoot], identity,
            selection with { NodeIds = [.. Enumerable.Range(1, AnalysisDuplicateEstimates.MaximumMarks + 1).Select(id => (ulong)id)] }));

        var estimate = new AnalysisDuplicateEstimates([DuplicateEstimateRoot], identity, selection);
        foreach (ulong id in selection.NodeIds)
        {
            string objectId = $"file-{id}";
            var path = new AnalysisNode(id, DuplicateEstimateRoot.Id, 0, objectId, AnalysisKind.File, objectId) { ObservedIdentity = objectId };
            estimate.Add(id, path, new(objectId, objectId, AnalysisMeasurement.Known(UInt128.MaxValue), AnalysisMeasurement.Known(1), VersionEvidence: "v1"));
        }
        var result = estimate.Finish(identity, AnalysisOrigin.LiveSession, NamespaceCoverage.CompleteUnderPolicy);
        Assert.Equal(2, result.CandidateObjects);
        Assert.Equal((BigInteger)UInt128.MaxValue * 2, result.KnownBytes);
    }

    private static readonly RootBinding DuplicateEstimateRoot = new("r", "file", "Recorded", "Recorded", "root", IdentityAssurance.Verified, "Recorded");
    private static SessionIdentity DuplicateEstimateIdentity() => new(Guid.NewGuid(), 1, 2, "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private static AnalysisSelection DuplicateSelection(SessionIdentity identity, ImmutableArray<ulong> ids) =>
        new(identity.SessionId, identity.Epoch, identity.Revision, ids);
}
