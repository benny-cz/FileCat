using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisMaintenanceTests
{
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    public void ReplacementCatalogDescribesActualPresenceWithoutReplayingOrRepairingIt(bool committed, bool incomingPresent, bool previousPresent, bool mainPresent)
    {
        Ordinary(); using var directory = new Directory(); var fixture = ReplacementCatalogFixture(directory, committed);
        if (incomingPresent) directory.Files[AnalysisPrivateNames.ReplacementIncoming(fixture.Incoming, fixture.Operation)] = new([1]);
        if (previousPresent) directory.Files[AnalysisPrivateNames.ReplacementPrevious(fixture.Previous, fixture.Operation)] = new([2]);
        if (!mainPresent) directory.Files.Remove("owned.fcdsa");
        var untouched = directory.Files.ToArray();
        using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); using var snapshot = new AnalysisSavedCatalog(store).Read(Cancellation);
        var pending = Assert.Single(snapshot.PendingReplacements); Assert.Equal(fixture.Previous, pending.Previous.InventoryId); Assert.Equal(fixture.Incoming, pending.IncomingId);
        Assert.Equal(committed, pending.Current is not null); Assert.Equal(mainPresent, pending.OriginalPresent); Assert.Equal(incomingPresent, pending.IncomingPresent); Assert.Equal(previousPresent, pending.PreviousPresent);
        Assert.Empty(snapshot.UnindexedNames); Assert.Equal(committed ? 1 : 0, snapshot.Entries.Length); Assert.Equal(untouched, directory.Files.ToArray());
        Assert.Throws<IOException>(() => Save(directory)); Assert.Throws<IOException>(() => Save(directory, "OWNED.fcdsa"));
        Guid independent = Save(directory, "independent.fcdsa"); Assert.NotEqual(fixture.Previous, independent); Assert.NotEqual(fixture.Incoming, independent);
    }

    [Theory]
    [InlineData("foreign-catalog")]
    [InlineData("wrong-previous-id")]
    [InlineData("duplicate-old-record")]
    [InlineData("duplicate-marker")]
    [InlineData("current-wrong-name")]
    [InlineData("future")]
    [InlineData("missing-profile")]
    public void ReplacementCatalogRefusesConflictsOrUnsupportedRecordsBeforeAnyMutation(string fault)
    {
        Ordinary(); using var directory = new Directory(); var fixture = ReplacementCatalogFixture(directory, fault == "current-wrong-name");
        string marker = AnalysisPrivateNames.PendingReplacement(fixture.Catalog, fixture.Previous, fixture.Incoming, fixture.Operation);
        var original = directory.Files[marker];
        if (fault is "foreign-catalog" or "wrong-previous-id")
        {
            directory.Files.Remove(marker);
            directory.Files[AnalysisPrivateNames.PendingReplacement(fault == "foreign-catalog" ? Guid.NewGuid() : fixture.Catalog,
                fault == "wrong-previous-id" ? Guid.NewGuid() : fixture.Previous, fixture.Incoming, fixture.Operation)] = original;
        }
        else if (fault == "duplicate-old-record") directory.Files[AnalysisPrivateNames.CatalogEntry(fixture.Catalog, fixture.Previous)] = new(original.Bytes.ToArray());
        else if (fault == "duplicate-marker") directory.Files[AnalysisPrivateNames.PendingReplacement(fixture.Catalog, fixture.Previous, Guid.NewGuid(), Guid.NewGuid())] = new(original.Bytes.ToArray());
        else if (fault == "missing-profile") directory.Files.Remove(AnalysisPrivateNames.PrivateState);
        else if (fault == "future") Change(original, "schema", "9");
        else
        {
            Change(directory.Files[AnalysisPrivateNames.CatalogEntry(fixture.Catalog, fixture.Incoming)], "name", "different.fcdsa");
            directory.Files["different.fcdsa"] = new([3]);
        }
        var untouched = directory.Files.ToArray();
        using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
        var failure = Record.Exception(() => { using var snapshot = new AnalysisSavedCatalog(store).Read(Cancellation); });
        Assert.True(failure is InvalidDataException or FileNotFoundException, failure?.ToString() ?? "The conflicting replacement was accepted.");
        Assert.Equal(untouched, directory.Files.ToArray());
    }

    private static (Guid Catalog, Guid Previous, Guid Incoming, Guid Operation) ReplacementCatalogFixture(Directory directory, bool committed)
    {
        Guid previous = Save(directory), incoming = Save(directory, "incoming.fcdsa"), operation = Guid.NewGuid();
        using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
        Guid catalog; using (var before = new AnalysisSavedCatalog(store).Read(Cancellation)) catalog = before.CatalogId!.Value;
        string old = AnalysisPrivateNames.CatalogEntry(catalog, previous), marker = AnalysisPrivateNames.PendingReplacement(catalog, previous, incoming, operation);
        directory.Files[marker] = directory.Files[old]; directory.Files.Remove(old);
        string fresh = AnalysisPrivateNames.CatalogEntry(catalog, incoming);
        if (committed) Change(directory.Files[fresh], "name", "owned.fcdsa"); else directory.Files.Remove(fresh);
        directory.Files.Remove("incoming.fcdsa");
        return (catalog, previous, incoming, operation);
    }
    private static void Change(Node file, string key, string value)
    {
        var document = JsonNode.Parse(file.Bytes)!.AsObject(); document[key] = value; file.Bytes = Encoding.UTF8.GetBytes(document.ToJsonString() + "\n");
    }
}
