using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisComparisonTests
{
    [Theory]
    [InlineData("new-binding")]
    [InlineData("missing-binding")]
    [InlineData("legacy-profile")]
    [InlineData("unknown-host")]
    [InlineData("other-profile")]
    [InlineData("other-host")]
    [InlineData("other-root-object")]
    [InlineData("unqualified-binding")]
    public void MatchingSerialObjectIdAndVersionCannotBridgeAnUnqualifiedSourceContext(string fault)
    {
        var old = Sample(("before", "same-object", 10, 8, "same-version"));
        var current = Sample(("after", "same-object", 10, 8, "same-version"));
        current = fault switch
        {
            "new-binding" => current with { Roots = [current.Roots[0] with { ConnectionIdentityReference = Guid.NewGuid().ToString("N") }] },
            "missing-binding" => current with { Roots = [current.Roots[0] with { ConnectionIdentityReference = null }] },
            "legacy-profile" => current with { Identity = current.Identity with { HostProfile = "legacy-host/profile" } },
            "unknown-host" => current with { Identity = current.Identity with { HostProfile = AnalysisHostProfileIdentity.Unverified(old.Identity.HostProfileIdentity!.ProfileId).ToString() } },
            "other-profile" => current with { Identity = current.Identity with { HostProfile = AnalysisHostProfileIdentity.Create(Guid.NewGuid(), AnalysisHostObservation.LinuxApplicationMachineId, Enumerable.Range(0, 16).Select(i => (byte)i).ToArray()).ToString() } },
            "other-host" => current with { Identity = current.Identity with { HostProfile = AnalysisHostProfileIdentity.Create(old.Identity.HostProfileIdentity!.ProfileId, AnalysisHostObservation.LinuxApplicationMachineId, Enumerable.Range(1, 16).Select(i => (byte)i).ToArray()).ToString() } },
            "other-root-object" => current with { Roots = [current.Roots[0] with { ObservedObjectIdentity = "different-root-object" }] },
            _ => current with { Roots = [current.Roots[0] with { ConnectionIdentityReference = "unqualified-provider-reference" }] }
        };
        if (fault is "legacy-profile" or "unknown-host") old = old with { Identity = old.Identity with { HostProfile = current.Identity.HostProfile } };
        if (fault == "unqualified-binding") old = old with { Roots = [old.Roots[0] with { ConnectionIdentityReference = current.Roots[0].ConnectionIdentityReference }] };
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.SuggestRootMapping(old, current));
        using var comparison = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, new Dictionary<string, string> { ["r"] = "r" }, Cancellation);
        Assert.False(comparison.Compatibility.VerifiedIdentityContext); Assert.Equal(2, comparison.Rows.Length);
        Assert.DoesNotContain(comparison.Rows, row => row.Change == AnalysisChange.Moved);
        Assert.All(comparison.Rows, row => Assert.Equal(AnalysisComparisonEvidence.Structural, row.Evidence));
        Assert.Equal(new AnalysisDelta(false, 0), comparison.NetChange);
        Assert.Contains(comparison.Compatibility.Explanations, reason => reason.Contains("Explicit structural comparison", StringComparison.Ordinal));
    }

    [Fact]
    public void OneLegacyAcquisitionStillPairsByRecordedScopeWithoutUpgradingIdentity()
    {
        var old = Sample(("before", "same-object", 10, 8, "same-version")); old = old with { Identity = old.Identity with { HostProfile = "legacy-host/profile" } };
        var current = old with { Nodes = old.Nodes.SetItem(1, old.Nodes[1] with { Name = "after" }) };
        using var comparison = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.False(comparison.Compatibility.VerifiedIdentityContext); Assert.Equal(2, comparison.Rows.Length);
        Assert.All(comparison.Rows, row => Assert.Equal(AnalysisComparisonEvidence.PathBased, row.Evidence));
        Assert.DoesNotContain(comparison.Rows, row => row.Change == AnalysisChange.Moved);
    }
}
