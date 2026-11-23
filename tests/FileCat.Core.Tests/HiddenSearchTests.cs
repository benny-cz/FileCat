using System.Collections.Immutable;
using FileCat.Core.HiddenData;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

public sealed class HiddenSearchTests
{
    [Fact]
    public void Visible_managed_metadata_matches_Find_without_gaining_a_deletion_route()
    {
        var kind = OperatingSystem.IsWindows() ? HiddenKind.Stream : HiddenKind.Attribute;
        foreach (var role in new[] { HiddenMetadataRole.StorageBacking, HiddenMetadataRole.StoragePolicy,
                     HiddenMetadataRole.SecurityMetadata, HiddenMetadataRole.RequiredMetadata, HiddenMetadataRole.Unqualified })
        {
            var item = new HiddenItem("visible.metadata", kind, 8) { Role = role };
            Assert.True(HiddenSearch.IsPositiveInOrdinaryView(item));
            Assert.NotNull(HiddenMetadataRoles.DeleteRefusal(item));
        }
        var mark = new HiddenItem(OperatingSystem.IsWindows() ? "Zone.Identifier" : OperatingSystem.IsMacOS() ? "com.apple.quarantine" : "user.xdg.origin.url", kind, 8)
        { Role = HiddenMetadataRole.Ordinary };
        Assert.False(HiddenSearch.IsPositiveInOrdinaryView(mark));
    }

    private sealed class Data : IHiddenData
    {
        public bool IsSupported => true;
        public int Legacy, Values, EaAdmissions, CompressionViews;
        public IReadOnlyList<HiddenItem> List(string path) { Legacy++; throw new InvalidOperationException(); }
        public byte[] Read(string path, HiddenItem item, int max) { Values++; throw new InvalidOperationException(); }
        public Stream Open(string path, HiddenItem item) => throw new InvalidOperationException();
        public void Delete(string path, HiddenItem item) => throw new InvalidOperationException();
        public HiddenInventory ReadInventory(string path, HiddenInventoryRequest request, CancellationToken ct)
        {
            if (request.IncludeEaValues) EaAdmissions++;
            if (request.ShowCompressionBacking) CompressionViews++;
            string name = Path.GetFileName(path);
            if (name == "denied") throw new UnauthorizedAccessException("test denial");
            var kind = OperatingSystem.IsWindows() ? HiddenKind.Stream : HiddenKind.Attribute;
            var state = name is "positive" or "unknown" ? HiddenInventoryState.Partial : HiddenInventoryState.Complete;
            ImmutableArray<HiddenItem> items = name switch
            {
                "positive" => [new("ordinary.payload", kind, 8)],
                "fork" when OperatingSystem.IsMacOS() => [new("com.apple.ResourceFork", HiddenKind.ResourceFork, 8) { Role = HiddenMetadataRole.Unqualified }],
                "mark" => [new(OperatingSystem.IsWindows() ? "Zone.Identifier" : OperatingSystem.IsMacOS() ? "com.apple.quarantine" : "user.xdg.origin.url", kind, 8)],
                "wrong-category" => [new("ordinary.payload", OperatingSystem.IsWindows() ? HiddenKind.NtfsAttribute : HiddenKind.Stream, 8)],
                _ => [],
            };
            var categories = ImmutableArray.CreateBuilder<HiddenCategoryInventory>();
            categories.Add(new(kind, state, items.Count(item => item.Kind == kind), "test", state == HiddenInventoryState.Partial ? "test bound reached" : null));
            if (OperatingSystem.IsMacOS() && name != "missing-category")
                categories.Add(new(HiddenKind.ResourceFork, state, items.Count(item => item.Kind == HiddenKind.ResourceFork), "test"));
            return new(path, DateTimeOffset.UtcNow, items, categories.ToImmutable(), "test");
        }
    }

    [Fact]
    public void Darwin_ordinary_fork_matches_but_a_missing_fork_category_cannot_prove_a_negative()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Darwin ordinary-view category semantics.");
        using var dir = new TempDir();
        dir.File("fork"); dir.File("missing-category"); dir.File("clear");
        var data = new Data(); var results = new ResultSet("hidden", "hidden", "test");
        var session = new SearchSession(new SearchQuery { Roots = [dir.Path], CarriesHiddenData = data }, results);
        session.Run(TestContext.Current.CancellationToken);
        Assert.Equal(["fork"], results.Snapshot().Select(item => item.Item.Name));
        Assert.Equal(1, session.ExtraDataUnexamined);
        Assert.False(results.IsComplete);
        Assert.Equal(0, data.Values); Assert.Equal(0, data.CompressionViews);
    }

    [Fact]
    public void Search_requires_complete_negative_coverage_keeps_known_positives_and_never_reads_EA_values()
    {
        using var dir = new TempDir();
        foreach (string name in new[] { "positive", "unknown", "denied", "clear", "mark", "wrong-category" }) dir.File(name);
        var data = new Data(); var set = new ResultSet("hidden", "hidden", "test");
        var session = new SearchSession(new SearchQuery { Roots = [dir.Path], CarriesHiddenData = data }, set);
        session.Run(TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "positive" }, set.Snapshot().Select(i => i.Item.Name));
        Assert.Equal(2, session.ExtraDataUnexamined); Assert.False(set.IsComplete);
        Assert.Contains(set.Issues, i => i.Contains("2 candidates unexamined", StringComparison.Ordinal));
        Assert.Equal(0, data.Legacy); Assert.Equal(0, data.Values); Assert.Equal(0, data.EaAdmissions);
        Assert.Equal(0, data.CompressionViews);
    }

    [Fact]
    public void Saved_broad_or_other_platform_criterion_requires_explicit_replacement_or_clearing()
    {
        using var dir = new TempDir();
        var criteria = new SearchCriteria { LookIn = dir.Path, Advanced = new() { CarriesHiddenData = true } };
        Assert.False(criteria.TryBuildQuery(DateTime.UtcNow, [], null, out _, out var reason, hiddenData: new Data()));
        Assert.Contains("explicitly replace", reason);
        criteria.Advanced.HiddenDataScope = HiddenSearch.ScopeId;
        Assert.True(criteria.TryBuildQuery(DateTime.UtcNow, [], null, out var query, out _, hiddenData: new Data()));
        Assert.Contains(HiddenSearch.Label, query!.Describe());
        criteria.Advanced.HiddenDataScope = "other-platform-v1";
        Assert.False(criteria.TryBuildQuery(DateTime.UtcNow, [], null, out _, out _, hiddenData: new Data()));
        criteria.Advanced.CarriesHiddenData = false;
        Assert.True(criteria.TryBuildQuery(DateTime.UtcNow, [], null, out _, out _, hiddenData: new Data()));
    }
}
