using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisReplacementBoundaryTests
{
    [Fact]
    public void GeneratedReplacementNamesAreClosedCanonicalAndSeparateFromUserInventoryNames()
    {
        Guid catalog = Guid.NewGuid(), previous = Guid.NewGuid(), incoming = Guid.NewGuid(), operation = Guid.NewGuid();
        string marker = AnalysisPrivateNames.PendingReplacement(catalog, previous, incoming, operation);
        Assert.Equal(144, marker.Length); Assert.True(AnalysisPrivateNames.TryPendingReplacement(marker, out var c, out var p, out var n, out var o));
        Assert.Equal((catalog, previous, incoming, operation), (c, p, n, o)); Assert.True(AnalysisPrivateNames.IsMetadata(marker)); Assert.True(AnalysisPrivateNames.NeedsReadLease(marker));
        foreach (string name in new[] { AnalysisPrivateNames.ReplacementIncoming(incoming, operation), AnalysisPrivateNames.ReplacementPrevious(previous, operation) })
        {
            Assert.Equal(82, name.Length); Assert.True(AnalysisPrivateNames.IsReplacementPayload(name)); Assert.False(AnalysisPrivateNames.IsMetadata(name)); Assert.True(AnalysisPrivateNames.NeedsReadLease(name));
            AnalysisPrivateNames.Entry(name); AnalysisPrivateNames.Maintenance(name); AnalysisPrivateNames.ReplacementData(name); AnalysisPrivateNames.ReplacementTarget(name, "named.fcdsa");
            Assert.Throws<ArgumentException>(() => AnalysisPrivateInventoryStore.ValidateName(name)); Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.Metadata(name));
            Assert.False(AnalysisPrivateNames.IsReplacementPayload(name.ToUpperInvariant()));
        }
        Assert.False(AnalysisPrivateNames.IsPendingReplacement(marker.ToUpperInvariant()));
        Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.ReplacementData(marker));
    }

    [Fact]
    public void AReplacementMarkerCanMoveOnlyToItsOwnCatalogEntry()
    {
        Guid catalog = Guid.NewGuid(), previous = Guid.NewGuid(), incoming = Guid.NewGuid(), operation = Guid.NewGuid();
        string entry = AnalysisPrivateNames.CatalogEntry(catalog, previous), marker = AnalysisPrivateNames.PendingReplacement(catalog, previous, incoming, operation);
        AnalysisPrivateNames.MaintenanceMove(entry, marker); AnalysisPrivateNames.MaintenanceMove(marker, entry);
        Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.MaintenanceMove(AnalysisPrivateNames.CatalogEntry(catalog, incoming), marker));
        Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.MaintenanceMove(marker, AnalysisPrivateNames.CatalogEntry(Guid.NewGuid(), previous)));
        Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.MaintenanceMove(marker, AnalysisPrivateNames.PrivateState));
    }

    [Fact]
    public void ReplacementIdentitiesCannotBeEmptyOrReusedAndOrdinaryDataCannotBeAnIncomingReplacement()
    {
        Guid id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.PendingReplacement(Guid.Empty, id, Guid.NewGuid(), id));
        Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.PendingReplacement(id, id, id, id));
        Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.ReplacementIncoming(Guid.Empty, id));
        Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.ReplacementPrevious(id, Guid.Empty));
        Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.ReplacementTarget("ordinary.fcdsa", "named.fcdsa"));
    }

    [Theory]
    [InlineData("private-state.json")]
    [InlineData("catalog.json")]
    [InlineData("write-lock.state")]
    [InlineData("../named.fcdsa")]
    [InlineData("C:\\named.fcdsa")]
    [InlineData("pair.fcdsc")]
    [InlineData("incoming-not-generated.payload")]
    [InlineData("named.fcdsa/other.fcdsa")]
    [InlineData("named.fcdsa:stream")]
    public void AtomicReplacementCannotTargetControlsPathsComparisonsOrAnUnrecognizedPayload(string destination)
    {
        string source = AnalysisPrivateNames.ReplacementIncoming(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => AnalysisPrivateNames.ReplacementTarget(source, destination));
    }
}
