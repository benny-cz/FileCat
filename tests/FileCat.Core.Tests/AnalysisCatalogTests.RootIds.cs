using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisCatalogTests
{
    [Theory]
    [InlineData("r")]
    [InlineData("2")]
    [InlineData("source \"quoted\" 😀")]
    public void SavedRootQueriesKeepTheirCapturedIdentifiersAcrossTwoSaves(string rootId)
    {
        Ordinary(); var original = AnalysisAccountingTests.Golden();
        original = original with { Roots = [original.Roots[0] with { Id = rootId }], Nodes = original.Nodes.Select(n => n with { RootId = rootId }).ToImmutableArray() };
        string query = "root:\"" + rootId.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\" AND name:x.bin";
        using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory); var catalog = new AnalysisSavedCatalog(store);
        var view = AnalysisSavedView.Default with { Query = query, Root = rootId };
        var wire = AnalysisSavedCatalog.ViewForInventory(original, view);
        catalog.SaveNew("first.fcdsa", original, Guid.NewGuid(), wire, Cancellation);
        using var first = catalog.Read(Cancellation); var entry = Assert.Single(first.Entries);
        Assert.Equal(wire, entry.View); Assert.Equal(wire.GetHashCode(), entry.View.GetHashCode());
        using var loaded = catalog.Load(entry, Cancellation); AssertMatches(loaded.Snapshot, entry.View);
        var second = AnalysisSavedCatalog.ViewForInventory(loaded.Snapshot, entry.View);
        Assert.Equal(entry.View, second); Assert.Equal(query, second.Query); Assert.Equal(rootId, second.RootDisplayIds!["1"]);
        catalog.SaveNew("second.fcdsa", loaded.Snapshot, Guid.NewGuid(), second, Cancellation);
        using var again = catalog.Read(Cancellation); var next = Assert.Single(again.Entries, e => e.Name == "second.fcdsa");
        using var reopened = catalog.Load(next, Cancellation); AssertMatches(reopened.Snapshot, next.View);
        void AssertMatches(AnalysisSnapshot snapshot, AnalysisSavedView saved)
        {
            var filter = AnalysisFilter.Parse(saved.Query, TimeZoneInfo.Utc, roots: snapshot.Roots, rootDisplayIds: saved.RootDisplayIds);
            var matches = snapshot.Nodes.Where(n => n.Kind == AnalysisKind.File && filter.Evaluate(new(n, snapshot.Objects.Single(o => o.Id == n.ObjectId), n.Name,
                AnalysisMeasurement.Missing(MeasurementState.Unsupported))) == AnalysisTruth.True);
            Assert.Equal("x.bin", Assert.Single(matches).Name); Assert.Equal("1", saved.Root);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("array")]
    [InlineData("empty")]
    [InlineData("duplicate-key")]
    [InlineData("duplicate-value")]
    [InlineData("wrong-key")]
    [InlineData("empty-id")]
    [InlineData("nul-id")]
    [InlineData("long-id")]
    [InlineData("non-string")]
    [InlineData("excessive")]
    [InlineData("unknown-field")]
    [InlineData("future")]
    public void PoisonedRootQueryContextRefusesCatalogWithoutChangingAnyFile(string fault)
    {
        Ordinary(); using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory); var catalog = new AnalysisSavedCatalog(store);
        var snapshot = AnalysisAccountingTests.Golden();
        catalog.SaveNew("roots.fcdsa", snapshot, Guid.NewGuid(), AnalysisSavedCatalog.ViewForInventory(snapshot, AnalysisSavedView.Default), Cancellation);
        string name = directory.Files.Keys.Single(n => n.StartsWith("catalog-", StringComparison.Ordinal));
        string original = Encoding.UTF8.GetString(directory.Files[name]); const string context = "\"rootIds\":{\"1\":\"r\"}";
        Assert.Contains(context, original);
        string replacement = fault switch
        {
            "null" => "null", "array" => "[]", "empty" => "{}", "duplicate-key" => "{\"1\":\"r\",\"1\":\"other\"}",
            "duplicate-value" => "{\"1\":\"r\",\"2\":\"r\"}", "wrong-key" => "{\"01\":\"r\"}", "empty-id" => "{\"1\":\"\"}",
            "long-id" => JsonSerializer.Serialize(new Dictionary<string, string> { ["1"] = new string('x', 257) }),
            "nul-id" => "{\"1\":\"r\\u0000\"}",
            "non-string" => "{\"1\":1}", "excessive" => JsonSerializer.Serialize(Enumerable.Range(1, 33).ToDictionary(i => i.ToString(), i => "r" + i)),
            _ => "{\"1\":\"r\"}"
        };
        string poisoned = fault switch
        {
            "missing" => original.Replace("," + context, "", StringComparison.Ordinal),
            "unknown-field" => original.Replace(context, context + ",\"path\":\"elsewhere\"", StringComparison.Ordinal),
            "future" => original.Replace("\"schema\":\"5\"", "\"schema\":\"6\"", StringComparison.Ordinal),
            _ => original.Replace(context, "\"rootIds\":" + replacement, StringComparison.Ordinal)
        };
        directory.Files[name] = Encoding.UTF8.GetBytes(poisoned); var before = directory.Files.ToDictionary(p => p.Key, p => p.Value.ToArray());
        Assert.Throws<InvalidDataException>(() => catalog.Read(Cancellation));
        Assert.Throws<InvalidDataException>(() => catalog.SaveNew("refused.fcdsa", snapshot, Guid.NewGuid(), AnalysisSavedView.Default, Cancellation));
        Assert.Equal(before.Keys, directory.Files.Keys); foreach (var file in before) Assert.Equal(file.Value, directory.Files[file.Key]);
        using var inert = store.Load("roots.fcdsa", Cancellation); Assert.Equal(AnalysisOrigin.ImportedInventory, inert.Snapshot.Origin);
    }

    [Fact]
    public void MaximumEscapedQueryAndAllRootIdentifiersFitTheBoundedCatalogEntry()
    {
        Ordinary(); var snapshot = AnalysisAccountingTests.Golden();
        var roots = Enumerable.Range(1, AnalysisLimits.Roots).Select(i => snapshot.Roots[0] with { Id = new string('\u0001', 254) + i.ToString("D2") }).ToImmutableArray();
        snapshot = snapshot with { Roots = roots, Nodes = roots.Select((r, i) => new AnalysisNode((ulong)i + 1, r.Id, 0, "Root", AnalysisKind.Directory)).ToImmutableArray(), Objects = [], Facts = [] };
        string query = "name:" + new string('\u0001', AnalysisLimits.QueryBytes - 5);
        var wire = AnalysisSavedCatalog.ViewForInventory(snapshot, AnalysisSavedView.Default with { Query = query });
        using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory); var catalog = new AnalysisSavedCatalog(store);
        catalog.SaveNew("max.fcdsa", snapshot, Guid.NewGuid(), wire, Cancellation);
        using var read = catalog.Read(Cancellation); Assert.Equal(wire, Assert.Single(read.Entries).View);
        int bytes = directory.Files.Single(p => p.Key.StartsWith("catalog-", StringComparison.Ordinal)).Value.Length;
        Assert.InRange(bytes, 64 * 1024, 128 * 1024);
    }
}
