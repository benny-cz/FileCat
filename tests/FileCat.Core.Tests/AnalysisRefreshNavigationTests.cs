using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisRefreshNavigationTests
{
    private static (AnalysisTree Tree, AnalysisSnapshot Next) Fixture()
    {
        var before = AnalysisAccountingTests.Golden();
        before = before with
        {
            Roots = before.Roots.Select(r => r with { SourceIdentity = "physical-volume", ObservedObjectIdentity = "native-root" }).ToImmutableArray(),
            Nodes = before.Nodes.Select(n => n with { ObservedIdentity = "native-" + n.Id,
                Times = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, DateTimeOffset.UnixEpoch, 1) }).ToImmutableArray()
        };
        var next = before with
        {
            Identity = before.Identity with { Epoch = before.Identity.Epoch + 1, Revision = before.Identity.Revision + 1 },
            Nodes = before.Nodes.Select(n => n with { Id = n.Id + 100, ParentId = n.ParentId == 0 ? 0 : n.ParentId + 100 }).ToImmutableArray()
        };
        return (AnalysisTree.Build(before, AnalysisAggregate.Project(before, AnalysisMetric.FileSize), false), next);
    }

    [Fact]
    public void RefreshNavigationMapsVerifiedNamespaceEntriesRatherThanReusingTheirNumericIds()
    {
        var (tree, next) = Fixture();
        Assert.Equal("n:104", AnalysisRefreshNavigation.Map(tree, next, "n:4"));
        Assert.Equal("n:102", AnalysisRefreshNavigation.Map(tree, next, "n:2"));
        Assert.Equal(AnalysisTree.RootKey, AnalysisRefreshNavigation.Map(tree, next, AnalysisTree.RootKey));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("epoch")]
    [InlineData("source")]
    [InlineData("root")]
    [InlineData("locator")]
    [InlineData("identity")]
    [InlineData("created")]
    [InlineData("created-precision")]
    [InlineData("missing-created")]
    [InlineData("name")]
    [InlineData("raw-name")]
    [InlineData("kind")]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    public void RefreshNavigationRefusesUnverifiedReplacementsAndAmbiguousEntries(string change)
    {
        var (tree, next) = Fixture();
        int index = next.Nodes.IndexOf(next.Nodes.Single(n => n.Id == 104));
        var node = next.Nodes[index];
        next = change switch
        {
            "session" => next with { Identity = next.Identity with { SessionId = Guid.NewGuid() } },
            "epoch" => next with { Identity = next.Identity with { Epoch = next.Identity.Epoch + 1 } },
            "source" => next with { Roots = next.Roots.Select(r => r with { SourceIdentity = "other" }).ToImmutableArray() },
            "root" => next with { Roots = next.Roots.Select(r => r with { ObservedObjectIdentity = "other" }).ToImmutableArray() },
            "locator" => next with { Roots = next.Roots.Select(r => r with { CanonicalLocator = r.CanonicalLocator + "other" }).ToImmutableArray() },
            "missing" => next with { Nodes = next.Nodes.RemoveAt(index) },
            "ambiguous" => next with { Nodes = next.Nodes.Add(node with { Id = 200 }) },
            _ => next with { Nodes = next.Nodes.SetItem(index, change switch
            {
                "identity" => node with { ObservedIdentity = "replacement" },
                "created" => node with { Times = node.Times! with { CreatedUtc = DateTimeOffset.UnixEpoch.AddSeconds(1) } },
                "created-precision" => node with { Times = node.Times! with { CreatedPrecisionTicks = 10 } },
                "missing-created" => node with { Times = node.Times! with { CreatedUtc = null } },
                "name" => node with { Name = "renamed" },
                "raw-name" => node with { RawName = [255] },
                "kind" => node with { Kind = AnalysisKind.Link },
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            }) }
        };
        Assert.Null(AnalysisRefreshNavigation.Map(tree, next, "n:4"));
        Assert.Null(AnalysisRefreshNavigation.Map(tree, next, "unknown-synthetic-group"));
    }
}
