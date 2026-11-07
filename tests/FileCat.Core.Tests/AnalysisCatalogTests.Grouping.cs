using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisCatalogTests
{
    [Theory]
    [MemberData(nameof(AnalysisGroupingTests.DimensionsAndMetrics), MemberType = typeof(AnalysisGroupingTests))]
    public void SavedGroupingRulesAndWireScopesRoundTripForEveryMetricWithoutSourceAuthority(AnalysisGroupingDimension dimension, AnalysisMetric metric)
    {
        Ordinary(); var snapshot = AnalysisGroupingTests.Fixture();
        var groups = AnalysisGrouping.Project(snapshot, AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation), dimension, Cancellation);
        var scope = Assert.Single(groups.Groups, group => group.Kind == AnalysisGroupKind.Entries && group.Members.Contains(4)).Definition.Key;
        var view = new AnalysisSavedView(metric, "Sunburst", "", snapshot.Roots[0].Id, scope, true, false, AnalysisTableSort.Name, false)
            { Grouping = dimension, ColorDimension = AnalysisColorDimension.HierarchyDepth };
        var wire = AnalysisSavedCatalog.ViewForInventory(snapshot, view); Assert.Equal("1", wire.Root);
        using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory); var catalog = new AnalysisSavedCatalog(store);
        catalog.SaveNew("group.fcdsa", snapshot, Guid.NewGuid(), wire, Cancellation);
        using var entries = catalog.Read(Cancellation); var entry = Assert.Single(entries.Entries); Assert.Equal(wire, entry.View);
        Assert.Equal(AnalysisGroupingRules.RuleVersion, entry.View.GroupRuleVersion);
        using var inventory = catalog.Load(entry, Cancellation); Assert.Equal(AnalysisOrigin.ImportedInventory, inventory.Snapshot.Origin);
        Assert.True(inventory.Snapshot.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        var restored = AnalysisGrouping.Project(inventory.Snapshot, AnalysisAggregate.Project(inventory.Snapshot, metric, cancellation: Cancellation), dimension, Cancellation);
        Assert.Contains(restored.Groups, group => group.Definition.Key == wire.Scope);
        Assert.Equal(dimension == AnalysisGroupingDimension.CapturedLocation, scope != wire.Scope);
        Assert.Contains("\"schema\":\"5\"", Encoding.UTF8.GetString(directory.Files.Values.Single(bytes => Encoding.UTF8.GetString(bytes).Contains("\"kind\":\"named\"", StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedLocationScopeAndIndependentRootFilterRemapOnceEvenWhenSourceIdsAreSwapped(bool aggregate)
    {
        var source = AnalysisGroupingTests.Fixture();
        source = source with
        {
            Roots = source.Roots.Select(root => root with { Id = root.Id == "r" ? "2" : "1" }).ToImmutableArray(),
            Nodes = source.Nodes.Select(node => node with { RootId = node.RootId == "r" ? "2" : "1" }).ToImmutableArray()
        };
        var grouped = AnalysisGrouping.Project(source, AnalysisAggregate.Project(source, AnalysisMetric.FileSize, cancellation: Cancellation), AnalysisGroupingDimension.CapturedLocation, Cancellation);
        string key = Assert.Single(grouped.Groups, group => group.Definition.Value == "location:2").Definition.Key;
        var view = AnalysisSavedView.Default with { Grouping = AnalysisGroupingDimension.CapturedLocation, Root = "1", Scope = aggregate ? AnalysisTree.OtherKey(AnalysisTree.OtherKey(key, 3), 4) : key };
        var wire = AnalysisSavedCatalog.ViewForInventory(source, view); var split = AnalysisSavedCatalog.SplitAggregationScope(wire.Scope);
        var expected = AnalysisGroupingRules.Classify(AnalysisGroupingDimension.CapturedLocation, source.Nodes.Single(node => node.Id == 4) with { RootId = "1" }, null,
            source.Identity, [source.Roots[0] with { Id = "1" }]);
        Assert.Equal("2", wire.Root); Assert.Equal(expected.Key, split.Scope); Assert.Equal(aggregate ? 7 : 0, split.Skipped);
        if (aggregate) Assert.Equal(AnalysisTree.OtherKey(expected.Key, 7), wire.Scope);
        Assert.Equal(key, AnalysisSavedCatalog.SplitAggregationScope(view.Scope).Scope);
    }

    [Fact]
    public void DeepRuntimeAggregationNormalizesBeforeSavedScopeLimitsAndRemapsCapturedLocation()
    {
        Ordinary(); var snapshot = AnalysisGroupingTests.Fixture();
        var groups = AnalysisGrouping.Project(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize, cancellation: Cancellation), AnalysisGroupingDimension.CapturedLocation, Cancellation);
        string key = Assert.Single(groups.Groups, group => group.Definition.Value == "location:r").Definition.Key;
        string nested = key; for (int index = 0; index < 40; index++) nested = AnalysisTree.OtherKey(nested, 1);
        Assert.True(Encoding.UTF8.GetByteCount(nested) > 256);
        var view = AnalysisSavedView.Default with { Grouping = AnalysisGroupingDimension.CapturedLocation, Root = "r", Scope = nested };
        var wire = AnalysisSavedCatalog.ViewForInventory(snapshot, view);
        Assert.Equal("1", wire.Root); Assert.Equal(40, AnalysisSavedCatalog.SplitAggregationScope(wire.Scope).Skipped);
        Assert.NotEqual(key, AnalysisSavedCatalog.SplitAggregationScope(wire.Scope).Scope);
        Assert.True(Encoding.UTF8.GetByteCount(wire.Scope) <= 256);
        using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory); var catalog = new AnalysisSavedCatalog(store);
        catalog.SaveNew("deep.fcdsa", snapshot, Guid.NewGuid(), wire, Cancellation);
        using var entries = catalog.Read(Cancellation); var entry = Assert.Single(entries.Entries); Assert.Equal(wire, entry.View);
        using var inventory = catalog.Load(entry, Cancellation); Assert.Equal(AnalysisOrigin.ImportedInventory, inventory.Snapshot.Origin);
        var restored = AnalysisGrouping.Project(inventory.Snapshot, AnalysisAggregate.Project(inventory.Snapshot, AnalysisMetric.FileSize, cancellation: Cancellation), AnalysisGroupingDimension.CapturedLocation, Cancellation);
        Assert.Contains(restored.Groups, group => group.Definition.Key == AnalysisSavedCatalog.SplitAggregationScope(wire.Scope).Scope);
        Assert.Equal(nested, view.Scope);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("numeric")]
    [InlineData("numeric-string")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("future-rule")]
    [InlineData("numeric-rule")]
    [InlineData("noncanonical-rule")]
    [InlineData("missing-rule")]
    [InlineData("node-scope")]
    [InlineData("uppercase-scope")]
    [InlineData("bad-other")]
    [InlineData("overflow-other")]
    public void SavedGroupingPoisonIsRejectedWithoutRewritingCatalogOrInventory(string fault)
    {
        Ordinary(); using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory); var catalog = new AnalysisSavedCatalog(store);
        catalog.SaveNew("group.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default with { Grouping = AnalysisGroupingDimension.Extension }, Cancellation);
        string file = directory.Files.Keys.Single(name => name.StartsWith("catalog-", StringComparison.Ordinal));
        byte[] inventory = directory.Files["group.fcdsa"].ToArray(); string json = Encoding.UTF8.GetString(directory.Files[file]);
        string hash = "group:1:" + new string('a', 64);
        json = fault switch
        {
            "unknown" => json.Replace("\"grouping\":\"Extension\"", "\"grouping\":\"ArbitraryOwner\"", StringComparison.Ordinal),
            "numeric" => json.Replace("\"grouping\":\"Extension\"", "\"grouping\":2", StringComparison.Ordinal),
            "numeric-string" => json.Replace("\"grouping\":\"Extension\"", "\"grouping\":\"2\"", StringComparison.Ordinal),
            "duplicate" => json.Replace("\"grouping\":\"Extension\"", "\"grouping\":\"Extension\",\"grouping\":\"FileType\"", StringComparison.Ordinal),
            "missing" => json.Replace(",\"grouping\":\"Extension\"", "", StringComparison.Ordinal),
            "future-rule" => json.Replace("\"groupRule\":\"1\"", "\"groupRule\":\"2\"", StringComparison.Ordinal),
            "numeric-rule" => json.Replace("\"groupRule\":\"1\"", "\"groupRule\":1", StringComparison.Ordinal),
            "noncanonical-rule" => json.Replace("\"groupRule\":\"1\"", "\"groupRule\":\"01\"", StringComparison.Ordinal),
            "missing-rule" => json.Replace(",\"groupRule\":\"1\"", "", StringComparison.Ordinal),
            "node-scope" => json.Replace("\"scope\":\"scope\"", "\"scope\":\"n:4\"", StringComparison.Ordinal),
            "uppercase-scope" => json.Replace("\"scope\":\"scope\"", "\"scope\":\"" + hash.ToUpperInvariant() + "\"", StringComparison.Ordinal),
            "bad-other" => json.Replace("\"scope\":\"scope\"", "\"scope\":\"other:0:" + hash + "\"", StringComparison.Ordinal),
            _ => json.Replace("\"scope\":\"scope\"", "\"scope\":\"other:2147483647:other:1:" + hash + "\"", StringComparison.Ordinal)
        };
        directory.Files[file] = Encoding.UTF8.GetBytes(json);
        Assert.Throws<InvalidDataException>(() => catalog.Read(Cancellation));
        Assert.Equal(json, Encoding.UTF8.GetString(directory.Files[file])); Assert.Equal(inventory, directory.Files["group.fcdsa"]);
        using var loaded = store.Load("group.fcdsa", Cancellation); Assert.Equal(AnalysisOrigin.ImportedInventory, loaded.Snapshot.Origin);
    }

    [Fact]
    public void LegacyColorStateRetainsItsColorButDefaultsGroupingWithoutAnyRewrite()
    {
        Ordinary(); using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory); var catalog = new AnalysisSavedCatalog(store);
        var original = AnalysisSavedView.Default with { ColorDimension = AnalysisColorDimension.ModifiedAge };
        catalog.SaveNew("legacy.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), original, Cancellation);
        string file = directory.Files.Keys.Single(name => name.StartsWith("catalog-", StringComparison.Ordinal));
        string json = Encoding.UTF8.GetString(directory.Files[file]).Replace("\"schema\":\"4\"", "\"schema\":\"3\"", StringComparison.Ordinal)
            .Replace(",\"grouping\":\"FolderHierarchy\",\"groupRule\":\"1\"", "", StringComparison.Ordinal);
        directory.Files[file] = Encoding.UTF8.GetBytes(json); using var read = catalog.Read(Cancellation);
        Assert.Equal(original, Assert.Single(read.Entries).View); Assert.Equal(json, Encoding.UTF8.GetString(directory.Files[file]));
    }
}
