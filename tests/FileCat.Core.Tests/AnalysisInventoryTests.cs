using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

[CollectionDefinition("Analysis bounded work capacity", DisableParallelization = true)]
public sealed class AnalysisBoundedWorkCapacityCollection;

// This class deliberately exhausts process-wide import admission. Other inventory consumers must
// finish first; an unrelated concurrent load being refused is correct production behavior.
[Collection("Analysis bounded work capacity")]
public sealed partial class AnalysisInventoryTests
{
    [Fact]
    public void RetainedInventoryRevisionSurvivesOwnerCloseAndRemainsInert()
    {
        using var inventory = Read(Write(AnalysisAccountingTests.Golden()));
        using var revision = inventory.Retain(); inventory.Dispose();
        Assert.Throws<ObjectDisposedException>(() => inventory.Snapshot);
        Assert.Throws<ObjectDisposedException>(() => inventory.Retain());
        using var save = revision.Retain(); revision.Dispose();
        Assert.Equal(AnalysisOrigin.ImportedInventory, save.Snapshot.Origin);
        Assert.True(save.Snapshot.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        Assert.Equal(6, save.Snapshot.Nodes.Length);
        save.Dispose(); Assert.Throws<ObjectDisposedException>(() => save.Retain());
    }

    private static readonly Guid Id = Guid.Parse("99999999-1111-1111-1111-111111111111");
    private static byte[] Write(AnalysisSnapshot? snapshot = null)
    {
        using var stream = new MemoryStream();
        AnalysisInventoryFormat.Write(stream, snapshot ?? AnalysisAccountingTests.Golden(), Id, TestContext.Current.CancellationToken);
        return stream.ToArray();
    }
    private static AnalysisInventory Read(byte[] bytes) => AnalysisInventoryFormat.Read(new MemoryStream(bytes), TestContext.Current.CancellationToken);
    private static byte[] Mutate(Action<List<JsonObject>> mutation)
    {
        var records = Encoding.UTF8.GetString(Write()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => JsonNode.Parse(s)!.AsObject()).ToList();
        records.RemoveAt(records.Count - 1); mutation(records);
        string prefix = string.Join('\n', records.Select(r => r.ToJsonString())) + "\n";
        var counts = new JsonObject();
        foreach (string kind in new[] { "manifest", "object", "path-node", "storage-fact", "issue", "coverage" })
            counts[kind] = records.Count(r => r["kind"]!.GetValue<string>() == kind).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var trailer = new JsonObject { ["kind"] = "trailer", ["counts"] = counts, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prefix))) };
        return Encoding.UTF8.GetBytes(prefix + trailer.ToJsonString() + "\n");
    }
    [Fact]
    public void DeterministicRoundTripRecomputesAccountingAndNeverRestoresLiveOrigin()
    {
        byte[] bytes = Write(); Assert.Equal(bytes, Write());
        using var inventory = Read(bytes); Assert.Equal(Id, inventory.InventoryId);
        var s = inventory.Snapshot; Assert.Equal(AnalysisOrigin.ImportedInventory, s.Origin);
        Assert.Equal(AnalysisTaint.OrdinarySensitive | AnalysisTaint.ImportedUnverified, s.Taint);
        Assert.Equal(AnalysisPersistence.Temporary, s.Persistence);
        Assert.Equal(AnalysisAccountingTests.Golden().Policy.Fingerprint, s.Policy.Fingerprint);
        Assert.Equal((UInt128)220, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        Assert.Equal((UInt128)120, AnalysisAggregate.Project(s, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        Assert.Equal((UInt128)80, AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        Assert.Equal(new ulong[] { 4, 5 }, s.Objects[0].ObservedAliases);
        inventory.Dispose(); Assert.Throws<ObjectDisposedException>(() => inventory.Snapshot);
    }
    [Fact]
    public void PartialInventoryWithUnknownMeasurementsStillRequiresACompleteContainer()
    {
        var s = AnalysisAccountingTests.Golden() with { Phase = AnalysisPhase.Stopped, Coverage = NamespaceCoverage.Partial, Taint = AnalysisPrivacy.All };
        using var inventory = Read(Write(s)); Assert.Equal(AnalysisPhase.Stopped, inventory.Snapshot.Phase);
        Assert.Equal(AnalysisPrivacy.All, inventory.Snapshot.Taint);
        Assert.False(inventory.Snapshot.CoverageDetails!.DenominatorFinal);
        Assert.Equal(MeasurementState.Unsupported, inventory.Snapshot.Facts[1].Measurement.State);
    }
    [Fact]
    public void RawNamesAndRootTextPreserveNonUnicodeSourceFidelity()
    {
        var s = AnalysisAccountingTests.Golden(); byte[] raw = [0xff, (byte)'x'];
        s = s with { Roots = [s.Roots[0] with { Label = "root\ud800", CanonicalLocator = "root\ud800", DisplayLocator = "root\ud800" }],
            Nodes = s.Nodes.SetItem(3, s.Nodes[3] with { Name = AnalysisRawNames.Decode("unix-bytes", raw), NameEncoding = "unix-bytes", RawName = raw.ToImmutableArray() }) };
        using var inventory = Read(Write(s)); Assert.Equal(s.Roots[0].CanonicalLocator, inventory.Snapshot.Roots[0].CanonicalLocator);
        Assert.Equal(raw, inventory.Snapshot.Nodes[3].RawName); Assert.False(AnalysisRawNames.HasLosslessText(inventory.Snapshot.Nodes[3]));
    }
    [Theory]
    [InlineData("future")]
    [InlineData("authority")]
    [InlineData("missing")]
    [InlineData("kind")]
    [InlineData("order")]
    [InlineData("parent")]
    [InlineData("duplicateId")]
    [InlineData("objectRef")]
    [InlineData("numeric")]
    [InlineData("numberType")]
    [InlineData("overflow")]
    [InlineData("aggregateOverflow")]
    [InlineData("measurement")]
    [InlineData("policy")]
    [InlineData("cycle")]
    [InlineData("taint")]
    [InlineData("zeroTaint")]
    [InlineData("counts")]
    [InlineData("boundaries")]
    [InlineData("summary")]
    [InlineData("enum")]
    public void PoisonedDataWithAnUpdatedChecksumIsRefused(string poison)
    {
        byte[] bytes = Mutate(records =>
        {
            var manifest = records[0]; var obj = records.First(r => r["kind"]!.GetValue<string>() == "object");
            var nodes = records.Where(r => r["kind"]!.GetValue<string>() == "path-node").ToArray();
            var fact = records.First(r => r["kind"]!.GetValue<string>() == "storage-fact"); var coverage = records[^1];
            switch (poison)
            {
                case "future": manifest["schema"] = "2"; break;
                case "authority": manifest["runtimeOrigin"] = "LiveSession"; break;
                case "missing": manifest.Remove("producer"); break;
                case "kind": obj["kind"] = "executable-object"; break;
                case "order": records.Remove(obj); records.Insert(records.Count - 1, obj); break;
                case "parent": nodes[1]["parent"] = "999"; break;
                case "duplicateId": nodes[1]["id"] = nodes[0]["id"]!.GetValue<string>(); break;
                case "objectRef": nodes[3]["object"] = "99"; break;
                case "numeric": obj["length"]![1] = "01"; break;
                case "numberType": obj["length"]![1] = 100; break;
                case "overflow": obj["length"]![1] = "340282366920938463463374607431768211456"; break;
                case "aggregateOverflow": obj["length"]![1] = UInt128.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case "measurement": obj["length"]![0] = "Failed"; break;
                case "policy": manifest["policyFingerprint"] = new string('0', 64); break;
                case "cycle": fact["includes"] = new JsonArray(fact["component"]!.GetValue<string>()); break;
                case "taint": manifest["historicalTaint"] = "128"; break;
                case "zeroTaint": manifest["historicalTaint"] = "0"; break;
                case "counts": coverage["completed"] = "1"; break;
                case "boundaries": coverage["boundaryTotals"] = new JsonArray(new JsonArray("Mount", "99")); break;
                case "summary": coverage["knownBytes"] = "0"; break;
                case "enum": nodes[0]["entryKind"] = "0"; break;
            }
        });
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }
    [Fact]
    public void TruncationChecksumTrailingDataAndDuplicatePropertiesAreRefused()
    {
        byte[] bytes = Write(); int trailer = Encoding.UTF8.GetString(bytes).LastIndexOf("{\"kind\":\"trailer\"", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => Read(bytes[..trailer]));
        Assert.Throws<InvalidDataException>(() => Read(bytes[..^1]));
        Assert.Throws<InvalidDataException>(() => Read(bytes.Concat("{}\n"u8.ToArray()).ToArray()));
        bytes[trailer - 3] ^= 1; Assert.Throws<InvalidDataException>(() => Read(bytes));
        string duplicate = Encoding.UTF8.GetString(Write()).Replace("\"schema\":\"1\"", "\"schema\":\"1\",\"schema\":\"1\"", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => Read(Encoding.UTF8.GetBytes(duplicate)));
    }
    [Fact]
    public void InvalidUtf8OversizedLinesDepthAndPropertyFloodsAreBounded()
    {
        Assert.Throws<InvalidDataException>(() => Read([0xff, 10]));
        Assert.Throws<InvalidDataException>(() => Read(new byte[AnalysisLimits.RecordBytes + 1]));
        string deep = "{\"kind\":\"manifest\",\"x\":" + new string('[', 20) + "0" + new string(']', 20) + "}\n";
        Assert.Throws<InvalidDataException>(() => Read(Encoding.UTF8.GetBytes(deep)));
        string properties = "{" + string.Join(',', Enumerable.Range(0, 129).Select(i => "\"p" + i + "\":null")) + "}\n";
        Assert.Throws<InvalidDataException>(() => Read(Encoding.UTF8.GetBytes(properties)));
        byte[] surrogate = Encoding.UTF8.GetBytes("{\"kind\":\"manifest\",\"x\":\"\\ud800\"}\n");
        Assert.Throws<InvalidDataException>(() => Read(surrogate));
        Assert.Throws<InvalidDataException>(() => AnalysisInventoryFormat.Read(new Oversized(), TestContext.Current.CancellationToken));
    }
    private sealed class Oversized : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => AnalysisLimits.InventoryBytes + 1; public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Must refuse before reading.");
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    [Fact]
    public void FailedImportsReleaseTheSharedBudgetAndCancellationIsObserved()
    {
        for (int i = 0; i < 30; i++) Assert.Throws<InvalidDataException>(() => Read("{}\n"u8.ToArray()));
        using var valid = Read(Write()); Assert.Equal(Id, valid.InventoryId);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => AnalysisInventoryFormat.Read(new MemoryStream(Write()), cancel.Token));
    }
    [Fact]
    public void NonseekableFragmentedInputAndSharedImportBudgetHaveExplicitBehavior()
    {
        byte[] bytes = Write(); using var fragmented = new Fragmented(bytes);
        using (var inventory = AnalysisInventoryFormat.Read(fragmented, TestContext.Current.CancellationToken)) Assert.Equal(Id, inventory.InventoryId);
        var held = new List<AnalysisInventory>();
        try
        {
            var failure = Assert.Throws<InvalidDataException>(() => { for (int i = 0; i < 20; i++) held.Add(Read(bytes)); });
            Assert.Contains("import-memory budget", failure.Message); Assert.InRange(held.Count, 1, 15);
        }
        finally { foreach (var inventory in held) inventory.Dispose(); }
        using var afterRelease = Read(bytes); Assert.Equal(Id, afterRelease.InventoryId);
    }
    [Fact]
    public void WriterRefusesNonUnicodeScalarTextInsteadOfSilentlyReplacingIt()
    {
        var s = AnalysisAccountingTests.Golden(); s = s with { Identity = s.Identity with { HostProfile = "bad\ud800text" } };
        Assert.Throws<InvalidDataException>(() => Write(s));
    }
    [Fact]
    public void BoundaryCountersSurviveTruncatedDetailedSamplesWithoutBecomingExclusionCounts()
    {
        var s = AnalysisAccountingTests.Golden(); using var store = new AnalysisStore();
        for (int i = 0; i < AnalysisLimits.Issues; i++) store.Issue("ordinary-failure");
        store.Issue("source-volume-boundary", state: MeasurementState.Excluded);
        store.Issue("not-requested", state: MeasurementState.Excluded);
        s = store.Snapshot(s.Identity, s.Roots, s.Policy, AnalysisPhase.Finished, NamespaceCoverage.Partial);
        using var loaded = Read(Write(s)); var coverage = loaded.Snapshot.CoverageDetails!;
        Assert.Equal(1ul, coverage.Boundaries); Assert.Equal(2ul, coverage.ExcludedIssues);
        Assert.Equal(1ul, loaded.Snapshot.BoundaryTotals[AnalysisBoundaryKind.Mount]);
        Assert.DoesNotContain(loaded.Snapshot.Issues, i => i.Code == "source-volume-boundary");
    }
    private sealed class Fragmented(byte[] data) : Stream
    {
        private readonly MemoryStream _data = new(data);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _data.Read(buffer, offset, Math.Min(count, 7));
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _data.Dispose(); base.Dispose(disposing); }
    }
}
