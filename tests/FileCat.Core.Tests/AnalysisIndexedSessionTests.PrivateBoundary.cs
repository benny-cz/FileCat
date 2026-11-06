using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisIndexedSessionTests
{
    [Fact]
    public async Task UnavailableBoundaryReactivatesSafeChildrenAndRecomputesTheirCoverage()
    {
        Ordinary(); using var io = new DeviceIoScheduler();
        var parent = new Backend(1, "parent") { Locator = "/selected" };
        var child = new Backend(1, "child") { Locator = "/selected/child" };
        var grand = new Backend(1, "grand") { Locator = "/selected/child/grand" };
        var guard = new Backend(0, "guard") { Locator = "/selected/private-parent" };
        parent.Relation = other => ReferenceEquals(other, child) || ReferenceEquals(other, grand) || ReferenceEquals(other, guard) ? AnalysisRootContainment.Covered : AnalysisRootContainment.Disjoint;
        child.Relation = other => ReferenceEquals(other, grand) ? AnalysisRootContainment.Covered : AnalysisRootContainment.Disjoint;
        var storage = new AnalysisStorageOptions("scratch", () => throw new InvalidOperationException("Memory-only acquisition must not create scratch."),
            [new("private", () => guard) { RequiresDisjointSource = true }]) { AllowMemoryFallback = true, MemoryOnly = true };
        using var session = new AnalysisCoordinator(io).Start([parent, child, grand], storage: storage);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        var snapshot = session.Snapshot(); AnalysisValidation.Validate(snapshot, Cancellation);
        Assert.True(session.UsesMemoryFallback); Assert.False(parent.Enumerated); Assert.True(child.Enumerated); Assert.False(grand.Enumerated);
        Assert.Equal(AnalysisRootDisposition.UnavailablePrivateBoundary, snapshot.Roots[0].Disposition);
        Assert.Null(snapshot.Roots[0].SourceIdentity); Assert.Null(snapshot.Roots[0].ObservedObjectIdentity);
        Assert.Equal(AnalysisRootDisposition.Active, snapshot.Roots[1].Disposition);
        Assert.Equal(AnalysisRootDisposition.Covered, snapshot.Roots[2].Disposition); Assert.Equal("2", snapshot.Roots[2].CoveredByRootId);
        Assert.Single(snapshot.Objects); Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage);
        Assert.Contains(snapshot.Issues, i => i.Code == "private-analysis-boundary-unavailable" && i.RootId == "1" && i.State == MeasurementState.Unsupported);
        using var bytes = new MemoryStream(); AnalysisInventoryFormat.Write(bytes, snapshot, Guid.NewGuid(), Cancellation);
        bytes.Position = 0; using var imported = AnalysisInventoryFormat.Read(bytes, Cancellation);
        Assert.Equal(AnalysisRootDisposition.UnavailablePrivateBoundary, imported.Snapshot.Roots[0].Disposition);
        Assert.Equal(AnalysisOrigin.ImportedInventory, imported.Snapshot.Origin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASelectedPrivateRootCoveredByAnotherSelectionDoesNotPublishItsNativeIdentity(bool memoryOnly)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var parent = new Backend(1, "parent") { Locator = "/selected", IncludePrivate = true };
        var selectedPrivate = new Backend(3, "private") { Locator = "/selected/private" };
        var protection = new Backend(0, "private") { Locator = "/selected/private" };
        parent.Relation = other => ReferenceEquals(other, selectedPrivate) ? AnalysisRootContainment.Covered : AnalysisRootContainment.Disjoint;
        var storage = Storage(new MemoryDirectory(), protection) with { MemoryOnly = memoryOnly, AllowMemoryFallback = true };
        using var session = new AnalysisCoordinator(io).Start([parent, selectedPrivate], storage: storage);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
        var snapshot = session.Snapshot(); AnalysisValidation.Validate(snapshot, Cancellation);
        Assert.Equal(AnalysisRootDisposition.ExcludedPrivateState, snapshot.Roots[1].Disposition);
        Assert.Equal(IdentityAssurance.Unknown, snapshot.Roots[1].Assurance);
        Assert.Null(snapshot.Roots[1].SourceIdentity); Assert.Null(snapshot.Roots[1].ObservedObjectIdentity); Assert.Null(snapshot.Roots[1].CoveredByRootId);
        Assert.False(selectedPrivate.Enumerated); Assert.False(parent.PrivateEnumerated); Assert.DoesNotContain(snapshot.Nodes, n => n.Name == "must-not-enumerate");
        Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, snapshot.Coverage);
        var membership = new AnalysisRootScopeIndex(snapshot, Cancellation);
        Assert.All(snapshot.Nodes, n => Assert.Equal(AnalysisTruth.False, membership.IsMember("2", n)));
        if (!memoryOnly)
        {
            await using var reader = await session.AcquireDatasetAsync(Cancellation); Assert.NotNull(reader);
            await using var filtered = await reader.BuildHierarchyAsync(AnalysisMetric.Files, new("", UnknownOnly: true, RootId: "2"), Cancellation);
            Assert.Equal(0UL, filtered.LogicalTotals.Files); Assert.NotNull(filtered.Filter); Assert.Equal(0UL, filtered.Filter.UnknownPaths);
        }
        session.Dispose(); await session.DatasetCloseCompletion;
    }
}
