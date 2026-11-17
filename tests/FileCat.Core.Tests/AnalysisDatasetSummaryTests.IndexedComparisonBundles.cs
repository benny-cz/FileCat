using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    private sealed class ComparisonBundleInput(byte[] bytes) : AnalysisInventoryInput
    {
        private readonly MemoryStream _bytes = new(bytes, writable: false);
        internal bool Closed;
        internal override Stream Stream => _bytes;
        internal override string Identity => "chosen-comparison-fixture";
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        public override void Dispose() { Closed = true; _bytes.Dispose(); }
    }
    public static IEnumerable<object[]> IndexedComparisonBundleCases()
    {
        foreach (var metric in Enum.GetValues<AnalysisMetric>()) foreach (bool structural in new[] { false, true }) yield return [metric, structural];
    }
    [Theory]
    [MemberData(nameof(IndexedComparisonBundleCases))]
    public async Task IndexedComparisonBundlesStreamWholeRetainedInputsAndRestoreOnlyInertMetadata(AnalysisMetric metric, bool structural)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var (old, current) = QuerySamples();
        var firstDirectory = new MemoryDirectory(); var secondDirectory = new MemoryDirectory();
        await using var a = await Build(old, io, firstDirectory, enumeration: ComparisonEnumeration(old), arbitraryPathIds: true);
        await using var b = await Build(current, io, secondDirectory, enumeration: ComparisonEnumeration(current), arbitraryPathIds: true);
        await using var before = await a.AcquireAsync(Cancellation); await using var now = await b.AcquireAsync(Cancellation);
        var mapping = structural ? old.Roots.Zip(current.Roots).ToImmutableDictionary(pair => pair.First.Id, pair => pair.Second.Id, StringComparer.Ordinal) : null;
        await using var result = await now.CompareIndexedAsync(before, metric, mapping, Cancellation);
        var view = new AnalysisComparisonView { Metric = metric, Query = "name:x.bin", QuerySide = AnalysisComparisonSide.Either,
            Order = AnalysisComparisonOrder.Path, FocusNode = old.Nodes[^1].Id, PreviousFocus = true };
        await using var captured = await result.CaptureBundleAsync(view, "Before", cancellation: Cancellation);
        await before.DisposeAsync(); await now.DisposeAsync(); await a.DisposeAsync(); await b.DisposeAsync();
        var closing = result.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
        var output = new ComparisonReportDestination();
        var written = await captured.ExportNewAsync("comparison.fcdsc", Device, () => output, false, Cancellation);
        Assert.Equal(output.Bytes.LongLength, written.Bytes);
        using (var legacy = AnalysisComparisonFormat.Read(new MemoryStream(output.Bytes, writable: false), Cancellation))
        {
            Assert.Equal(old.Nodes.Length, legacy.Previous.Snapshot.Nodes.Length); Assert.Equal(current.Nodes.Length, legacy.Current.Snapshot.Nodes.Length);
            Assert.Equal(view, legacy.View); Assert.Equal("Before", legacy.PreviousLabel); Assert.Equal(structural, legacy.StructuralRoots is not null);
        }
        await captured.DisposeAsync(); await closing; Assert.True(firstDirectory.Closed); Assert.True(secondDirectory.Closed);
        Guid profile = Guid.NewGuid(); using var competing = AnalysisProfileQuota.Open(profile); var scratch = new MemoryDirectory();
        var input = new ComparisonBundleInput(output.Bytes);
        await using var loaded = await AnalysisComparisonFormat.ReadExternalIndexedAsync(io, Device, Device, () => scratch,
            () => AnalysisProfileQuota.Open(profile), () => input, Cancellation);
        Assert.True(input.Closed); Assert.Equal(view, loaded.View); Assert.Equal("Before", loaded.PreviousLabel);
        Assert.Equal(structural, loaded.StructuralRoots is not null); Assert.True(loaded.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        Assert.Equal(old.Roots.Select(root => root.Id).Order(), loaded.PreviousRootDisplayIds.Values.Order());
        await using var previous = await loaded.Previous.AcquireAsync(Cancellation); await using var currentLease = await loaded.Current.AcquireAsync(Cancellation);
        Assert.Equal(AnalysisOrigin.ImportedInventory, previous.Descriptor.Origin); Assert.Equal(AnalysisOrigin.ImportedInventory, currentLease.Descriptor.Origin);
        Equal(AnalysisAggregate.Project(old, metric, cancellation: Cancellation).Totals, (await previous.SummarizeAsync(metric, Cancellation)).Totals);
        Equal(AnalysisAggregate.Project(current, metric, cancellation: Cancellation).Totals, (await currentLease.SummarizeAsync(metric, Cancellation)).Totals);
        Assert.False(scratch.Closed); Assert.False(competing.TryReserve(AnalysisLimits.ProfileScratchBytes));
        await previous.DisposeAsync(); await currentLease.DisposeAsync(); await loaded.DisposeAsync();
        Assert.True(scratch.Closed); Assert.True(competing.TryReserve(AnalysisLimits.ProfileScratchBytes));
    }
    [Theory]
    [InlineData("digest")] [InlineData("truncated")] [InlineData("trailing")] [InlineData("future")]
    [InlineData("previous-id")] [InlineData("previous-bytes")] [InlineData("duplicate")] [InlineData("focus")]
    public async Task IndexedComparisonBundleImportRejectsMalformedOuterFramingAndReleasesBothGraphs(string corruption)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample();
        using var encoded = new MemoryStream(); AnalysisComparisonFormat.Write(encoded, Guid.NewGuid(), s, s, new(), null, "Before", Cancellation);
        byte[] bytes = CorruptIndexedComparisonBundle(encoded.ToArray(), corruption);
        Guid profile = Guid.NewGuid(); using var competing = AnalysisProfileQuota.Open(profile);
        var scratch = new MemoryDirectory(); var input = new ComparisonBundleInput(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await using var unexpected = await AnalysisComparisonFormat.ReadExternalIndexedAsync(io, Device, Device, () => scratch,
                () => AnalysisProfileQuota.Open(profile), () => input, Cancellation);
        });
        Assert.True(input.Closed); Assert.True(scratch.Closed); Assert.True(competing.TryReserve(AnalysisLimits.ProfileScratchBytes));
    }
    [Fact]
    public async Task IndexedComparisonBundleRefusesPathOmissionBeforeOpeningAnyDestination()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample();
        await using var dataset = await Build(s, io, enumeration: ComparisonEnumeration(s), arbitraryPathIds: true);
        await using var input = await dataset.AcquireAsync(Cancellation);
        await using var result = await input.CompareIndexedAsync(input, AnalysisMetric.FileSize, cancellation: Cancellation);
        await using var captured = await result.CaptureBundleAsync(new(), cancellation: Cancellation);
        bool opened = false;
        await Assert.ThrowsAsync<ArgumentException>(() => captured.ExportNewAsync("comparison.fcdsc", Device,
            () => { opened = true; return new ComparisonReportDestination(); }, true, Cancellation));
        Assert.False(opened);
    }
    private static byte[] CorruptIndexedComparisonBundle(byte[] original, string corruption)
    {
        if (corruption == "truncated") return original[..^1];
        if (corruption == "trailing") return [.. original, 42];
        string text = Encoding.UTF8.GetString(original); int first = text.IndexOf('\n'); int last = text.LastIndexOf("{\"kind\":\"comparison-trailer\"", StringComparison.Ordinal);
        var header = JsonNode.Parse(text[..first])!.AsObject();
        switch (corruption)
        {
            case "future": header["schema"] = "2"; break;
            case "previous-id": header["previous"] = Guid.NewGuid().ToString("D"); break;
            case "previous-bytes": header["previousBytes"] = (long.Parse(header["previousBytes"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture); break;
            case "focus": header["view"]!["focusNode"] = "9999999"; break;
        }
        string line = header.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        if (corruption == "duplicate") line = line[..^1] + ",\"schema\":\"1\"}";
        byte[] body = Encoding.UTF8.GetBytes(line + "\n" + text[(first + 1)..last]);
        string digest = corruption == "digest" ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(body));
        return [.. body, .. Encoding.UTF8.GetBytes("{\"kind\":\"comparison-trailer\",\"sha256\":\"" + digest + "\"}\n")];
    }
}
