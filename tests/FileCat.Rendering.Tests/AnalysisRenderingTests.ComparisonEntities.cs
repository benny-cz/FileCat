using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task NativeIndexedComparisonEntitiesKeepMetricAccountingAndHardLinkMembershipAfterSourceOwnerClose(AnalysisMetric metric)
    {
        OrdinaryPreparation(); var token = TestContext.Current.CancellationToken;
        await using var fixture = await IndexedFixture.CreateAsync(sharedAlias: true);
        var source = fixture.Lease!; var expected = await source.SummarizeAsync(metric, token);
        ulong alias = 0, original = 0, at = 0;
        while (true)
        {
            var paths = await source.ReadPathsAsync(at, 64, token);
            foreach (var path in paths.Rows)
            { if (path.Name == "alias.jpg") alias = path.Id; if (path.Name == "match-target.bin") original = path.Id; }
            if (!paths.HasMore) break; at = paths.NextOffset;
        }
        Assert.NotEqual(0UL, alias); Assert.NotEqual(0UL, original);
        await using var entities = await source.IndexComparisonEntitiesAsync(metric, token);
        await fixture.ReleaseInitialLeaseAsync();
        Assert.Equal(expected.Totals.Known, entities.Totals.Known); Assert.Equal(expected.Totals.IdentityUnverified, entities.Totals.IdentityUnverified);
        Assert.Equal(expected.Totals.MatchingPaths, entities.Totals.MatchingPaths); Assert.Equal(expected.Totals.CompatibleBasis, entities.Totals.CompatibleBasis);
        Assert.Equal(expected.Totals.Missing.OrderBy(p => p.Key), entities.Totals.Missing.OrderBy(p => p.Key));
        bool shared = metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk;
        Assert.Equal(shared ? 303UL : 304UL, entities.Count);
        var a = await entities.FindPathAsync(alias, token); var b = await entities.FindPathAsync(original, token);
        Assert.NotNull(a); Assert.NotNull(b); Assert.Equal(shared, a.Id == b.Id);
        Assert.Equal(shared ? 2UL : 1UL, a.ObservedPaths);
        ulong offset = 0, members = 0;
        while (true)
        {
            var page = await entities.ReadAsync(offset, 37, token);
            Assert.InRange(page.Rows.Length, 1, 37);
            foreach (var entity in page.Rows)
            {
                ulong memberOffset = 0;
                while (true)
                {
                    var paths = await entities.ReadMembersAsync(entity.Id, memberOffset, 1, token); members += (ulong)paths.Rows.Length;
                    Assert.All(paths.Rows, path => Assert.Equal(AnalysisKind.File, path.Kind));
                    if (!paths.HasMore) break; memberOffset = paths.NextOffset;
                }
            }
            if (!page.HasMore) break; offset = page.NextOffset;
        }
        Assert.Equal(304UL, members); Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
    }

    [Theory]
    [InlineData(AnalysisReportFormat.Markdown)]
    [InlineData(AnalysisReportFormat.Html)]
    [InlineData(AnalysisReportFormat.Csv)]
    public async Task NativeComparisonEntitiesLimitedBasisReportsKeepKnownSidesAndWithholdCombinedAmounts(AnalysisReportFormat format)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var old = Fixture();
        old = old with { Roots = old.Roots.Add(old.Roots[0] with { Id = "s", CanonicalLocator = "/other", DisplayLocator = "/other", Label = "Other" }),
            Nodes = old.Nodes.Add(new(7, "s", 0, "Other", AnalysisKind.Directory)).Add(new(8, "s", 7, "other.bin", AnalysisKind.File, "q")),
            Objects = old.Objects.Add(new("q", "other:q", AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1))),
            Facts = old.Facts.Add(new("q", "q", null, "primary", "different", AnalysisMeasurement.Known(997), "fixture", DateTimeOffset.UnixEpoch, [])) };
        var now = old with { Facts = old.Facts.SetItem(0, old.Facts[0] with { Measurement = AnalysisMeasurement.Known(131072) }) };
        using var before = AnalysisInventoryFormat.Read(new MemoryStream(ExternalInventoryBytes(old)), token);
        using var current = AnalysisInventoryFormat.Read(new MemoryStream(ExternalInventoryBytes(now)), token);
        using var a = before.Retain(); using var b = current.Retain();
        using var comparison = AnalysisComparison.Build(a.Snapshot, b.Snapshot, AnalysisMetric.OnDisk, cancellation: token);
        Assert.Null(comparison.NetChange); Assert.All(comparison.Rows, row => Assert.Null(row.Delta));
        await using var report = await AnalysisCapturedComparisonReport.CaptureAsync(profile.Services.Io, a, b, comparison,
            new AnalysisComparisonView { Metric = AnalysisMetric.OnDisk }, format, true, token);
        string folder = Path.Combine(profile.Root, "limited-report"); Directory.CreateDirectory(folder);
        string name = "Limited" + (format == AnalysisReportFormat.Csv ? ".csv" : format == AnalysisReportFormat.Html ? ".html" : ".md");
        await report.ExportNewAsync(name, PathUtil.GetDeviceKey(folder), () => OperatingSystem.IsWindows()
            ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
            : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "limited-stage")), true, token);
        string text = File.ReadAllText(Path.Combine(folder, name));
        Assert.Contains("65536", text); Assert.Contains("131072", text); Assert.Contains("997", text);
        Assert.Contains("Incomparable bases", text); Assert.Contains("Unknown", text);
        Assert.DoesNotContain("0 known;", text); Assert.DoesNotContain("+65536", text);
    }
}
