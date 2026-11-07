using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisCatalogTests
{
    [Fact]
    public void CatalogBudgetCountsStoredBytesInsteadOfReencodedJsonBeforePublishingAnInventory()
    {
        Ordinary(); using var output = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(output); var catalog = new AnalysisSavedCatalog(store);
        catalog.SaveNew("old-00.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation);
        var sample = JsonNode.Parse(output.Files.Single(p => p.Key.StartsWith("catalog-", StringComparison.Ordinal)).Value)!;
        Guid catalogId = Guid.Parse(sample["catalog"]!.GetValue<string>()); byte[] inventory = output.Files["old-00.fcdsa"];
        foreach (string key in output.Files.Keys.Where(key => key.StartsWith("catalog-", StringComparison.Ordinal)).ToArray()) output.Files.Remove(key);
        for (int n = 0; n < 64; n++)
        {
            Guid id = Guid.NewGuid(); string name = $"old-{n:D2}.fcdsa"; sample["inventory"] = id.ToString("D"); sample["name"] = name;
            string json = sample.ToJsonString(); output.Files[AnalysisPrivateNames.CatalogEntry(catalogId, id)] = Encoding.UTF8.GetBytes(json.PadRight(32 * 1024));
            output.Files[name] = inventory;
        }
        using (var index = catalog.Read(Cancellation)) { Assert.Equal(64, index.Entries.Length); Assert.Equal(2 * 1024 * 1024, index.MetadataBytes); }
        var before = output.Files.ToDictionary(p => p.Key, p => p.Value.ToArray());
        Assert.Throws<InvalidDataException>(() => catalog.SaveNew("no.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation));
        Assert.Equal(before.Keys, output.Files.Keys); foreach (var item in before) Assert.Equal(item.Value, output.Files[item.Key]);
    }

    [Fact]
    public void CatalogWritersSerializeBeforePublishingInventoriesAndIgnoreRuntimeControlMetadata()
    {
        Ordinary(); using var output = new MemoryDirectory(); using var first = new AnalysisPrivateInventoryStore(output); using var second = new AnalysisPrivateInventoryStore(output);
        var snapshot = AnalysisAccountingTests.Golden(); var catalog = new AnalysisSavedCatalog(first);
        output.Files[AnalysisPrivateNames.CapacityToken(0)] = [1]; output.Files[AnalysisPrivateNames.PrivateState] = [2];
        using (catalog.BeginNamedWrite("first.fcdsa", Guid.NewGuid(), snapshot.Coverage, snapshot.Taint, AnalysisSavedView.Default, Cancellation))
        {
            var error = Assert.Throws<IOException>(() => new AnalysisSavedCatalog(second).SaveNew("second.fcdsa", snapshot, Guid.NewGuid(), AnalysisSavedView.Default, Cancellation));
            Assert.Contains("writer is active", error.Message); Assert.DoesNotContain("second.fcdsa", output.Files.Keys);
        }
        catalog.SaveNew("after.fcdsa", snapshot, Guid.NewGuid(), AnalysisSavedView.Default, Cancellation);
        using var index = catalog.Read(Cancellation); Assert.Equal("after.fcdsa", Assert.Single(index.Entries).Name); Assert.Empty(index.UnindexedNames);
    }

    [Fact]
    public void UnsupportedCatalogWriteLockPreservesEveryByteAndNeverPublishesAnInventory()
    {
        Ordinary(); using var output = new MemoryDirectory(); output.Files[AnalysisPrivateNames.CatalogWriteLock] = [0, 1, 2];
        using var store = new AnalysisPrivateInventoryStore(output);
        Assert.Throws<InvalidDataException>(() => new AnalysisSavedCatalog(store).SaveNew("no.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation));
        Assert.Single(output.Files); Assert.Equal(new byte[] { 0, 1, 2 }, output.Files[AnalysisPrivateNames.CatalogWriteLock]);
    }
}
