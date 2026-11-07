using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisComparisonTests
{
    private static byte[] ComparisonBytes(AnalysisSnapshot before, AnalysisSnapshot now, AnalysisComparisonView? view = null, IReadOnlyDictionary<string, string>? roots = null)
    {
        using var output = new MemoryStream();
        AnalysisComparisonFormat.Write(output, Guid.NewGuid(), before, now, view ?? new(), roots, "before.fcdsa", Cancellation);
        return output.ToArray();
    }
    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public void SavedComparisonRoundtripRecomputesBothInputsAndPreservesStructuralRootQueryContext(AnalysisMetric metric)
    {
        var before = Sample(("a", "a", 10, 16, "v1"), ("alias-a", "a", 10, 16, "v1"), ("old", "b", 5, 8, "v1")) with { Taint = AnalysisTaint.PrivilegedDerived };
        var now = Sample(("a", "a", 20, 32, "v2"), ("alias-a", "a", 20, 32, "v2"), ("new", "c", 3, 8, "v1"));
        now = now with { Roots = [now.Roots[0] with { Id = "after" }], Nodes = now.Nodes.Select(node => node with { RootId = "after" }).ToImmutableArray() };
        var roots = new Dictionary<string, string> { { "r", "after" } };
        var view = new AnalysisComparisonView { Metric = metric, Query = "root:after", QuerySide = AnalysisComparisonSide.Current, Chart = AnalysisComparisonChartMode.Sunburst, PreviousChart = true, ChartScope = "n:1", FocusNode = 2 };
        var bytes = ComparisonBytes(before, now, view, roots);
        using var bundle = AnalysisComparisonFormat.Read(new MemoryStream(bytes), Cancellation);
        Assert.Equal(view, bundle.View); Assert.Equal("1", Assert.Single(bundle.StructuralRoots!).Key); Assert.Equal("1", Assert.Single(bundle.StructuralRoots!).Value);
        Assert.Equal("r", bundle.PreviousRootDisplayIds["1"]); Assert.Equal("after", bundle.CurrentRootDisplayIds["1"]);
        Assert.Equal(AnalysisOrigin.ImportedInventory, bundle.Previous.Snapshot.Origin); Assert.Equal(AnalysisOrigin.ImportedInventory, bundle.Current.Snapshot.Origin);
        Assert.True(bundle.Taint.HasFlag(AnalysisTaint.PrivilegedDerived | AnalysisTaint.ImportedUnverified));
        using var comparison = AnalysisComparison.Build(bundle.Previous.Snapshot, bundle.Current.Snapshot, metric, bundle.StructuralRoots, Cancellation);
        UInt128 previous = metric switch { AnalysisMetric.FileSize => 25, AnalysisMetric.UniqueFileSize => 15, AnalysisMetric.OnDisk => 24, _ => 3 };
        UInt128 current = metric switch { AnalysisMetric.FileSize => 43, AnalysisMetric.UniqueFileSize => 23, AnalysisMetric.OnDisk => 40, _ => 3 };
        Assert.Equal(previous, comparison.PreviousTotals.Known); Assert.Equal(current, comparison.CurrentTotals.Known);
        Assert.Equal(AnalysisDelta.Between(previous, current), comparison.NetChange);
        var lease = comparison.Retain();
        using var query = AnalysisComparisonQuery.Apply(bundle.Previous.Snapshot, bundle.Current.Snapshot, metric, lease, view.Query,
            side: view.QuerySide, cancellation: Cancellation, previousRootDisplayIds: bundle.PreviousRootDisplayIds, currentRootDisplayIds: bundle.CurrentRootDisplayIds);
        Assert.Equal(comparison.Rows.Count(row => row.Current is not null), query.Matches.Length);
        Assert.All(query.Matches, row => Assert.NotNull(row.Current));
        // A second private save preserves the original query namespace rather than
        // silently switching root:after to the embedded inventory's numeric IDs.
        using var again = new MemoryStream();
        AnalysisComparisonFormat.Write(again, Guid.NewGuid(), bundle.Previous.Snapshot, bundle.Current.Snapshot, view, bundle.StructuralRoots, "before.fcdsa", Cancellation,
            bundle.PreviousRootDisplayIds, bundle.CurrentRootDisplayIds); again.Position = 0;
        using var second = AnalysisComparisonFormat.Read(again, Cancellation); Assert.Equal("after", second.CurrentRootDisplayIds["1"]);
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("future")]
    [InlineData("short-input")]
    [InlineData("truncated")]
    [InlineData("trailing")]
    [InlineData("digest")]
    [InlineData("activation")]
    [InlineData("wrong-type")]
    public void SavedComparisonRejectsCorruptionUnsupportedSchemasAndObjectActivation(string mutation)
    {
        var sample = Sample(("name", "a", 1, 4, "v1")); string text = Encoding.UTF8.GetString(ComparisonBytes(sample, sample));
        int end = text.IndexOf('\n'); string header = text[..end];
        text = mutation switch
        {
            "duplicate" => text.Replace("\"schema\":\"1\"", "\"schema\":\"1\",\"schema\":\"1\"", StringComparison.Ordinal),
            "future" => text.Replace("\"schema\":\"1\"", "\"schema\":\"999\"", StringComparison.Ordinal),
            "short-input" => ReplaceHeaderCount(text, "previousBytes", "1"),
            "truncated" => text[..(text.Length / 2)],
            "trailing" => text + "{}\n",
            "digest" => text[..^5] + (text[^5] == '0' ? "1" : "0") + text[^4..],
            "activation" => text.Insert(1, "\"$type\":\"System.Diagnostics.Process\","),
            _ => text.Replace("\"caseSensitive\":false", "\"caseSensitive\":\"false\"", StringComparison.Ordinal)
        };
        Assert.Throws<InvalidDataException>(() => AnalysisComparisonFormat.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), Cancellation));
    }
    private static string ReplaceHeaderCount(string text, string field, string value)
    {
        int first = text.IndexOf('\n'); using var doc = JsonDocument.Parse(text[..first]); string old = doc.RootElement.GetProperty(field).GetString()!;
        return text[..first].Replace("\"" + field + "\":\"" + old + "\"", "\"" + field + "\":\"" + value + "\"", StringComparison.Ordinal) + text[first..];
    }
    [Fact]
    public void FailedSecondInputsAndCanceledReadsReleaseEveryImportReservation()
    {
        var sample = Sample(("name", "a", 1, 4, "v1")); byte[] bytes = ComparisonBytes(sample, sample);
        using var header = JsonDocument.Parse(bytes.AsMemory(0, Array.IndexOf(bytes, (byte)10)));
        long threshold = Array.IndexOf(bytes, (byte)10) + 1 + long.Parse(header.RootElement.GetProperty("previousBytes").GetString()!, System.Globalization.CultureInfo.InvariantCulture) + 100;
        for (int i = 0; i < 18; i++)
        {
            using var cancel = new CancellationTokenSource(); using var input = new FragmentedComparisonStream(bytes, threshold, cancel);
            Assert.ThrowsAny<OperationCanceledException>(() => AnalysisComparisonFormat.Read(input, cancel.Token));
            using var good = AnalysisComparisonFormat.Read(new MemoryStream(bytes), Cancellation); Assert.Single(good.Current.Snapshot.Objects);
        }
    }
    private sealed class FragmentedComparisonStream(byte[] bytes, long threshold, CancellationTokenSource cancel) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => _inner.CanRead;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(Span<byte> buffer)
        { int count = _inner.Read(buffer[..Math.Min(37, buffer.Length)]); if (_inner.Position >= threshold) cancel.Cancel(); return count; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
    [Fact]
    public void ComparisonPrivatePublicationIsOneCreateNewFileAndNeverReplacesOrPublishesHalfInputs()
    {
        using var directory = new AnalysisCatalogTests.MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
        var sample = Sample(("name", "a", 1, 4, "v1"));
        var broken = sample with { Nodes = sample.Nodes.SetItem(1, sample.Nodes[1] with { ParentId = 999 }) };
        Assert.Throws<InvalidDataException>(() => store.SaveComparisonNew("pair.fcdsc", Guid.NewGuid(), sample, broken, new(), null, "before.fcdsa", Cancellation));
        Assert.DoesNotContain(directory.Files.Keys, name => name.EndsWith(".fcdsc", StringComparison.Ordinal) || name.EndsWith(".fcdsa", StringComparison.Ordinal));
        var receipt = store.SaveComparisonNew("pair.fcdsc", Guid.NewGuid(), sample, sample, new(), null, "before.fcdsa", Cancellation);
        byte[] saved = directory.Files[receipt.Name].ToArray();
        Assert.Equal(saved.Length, receipt.Bytes); Assert.Equal(new[] { "pair.fcdsc" }, store.ComparisonNames(Cancellation));
        Assert.Throws<IOException>(() => store.SaveComparisonNew("PAIR.fcdsc", Guid.NewGuid(), sample, sample, new(), null, "before.fcdsa", Cancellation));
        Assert.Equal(saved, directory.Files[receipt.Name]); using var loaded = store.LoadComparison(receipt.Name, Cancellation); Assert.Equal(receipt.ComparisonId, loaded.ComparisonId);
        Assert.Throws<ArgumentException>(() => store.Load(receipt.Name, Cancellation));
        using var catalog = new AnalysisSavedCatalog(store).Read(Cancellation); Assert.Empty(catalog.Entries); Assert.Empty(catalog.UnindexedNames);
    }
    [Theory]
    [InlineData("../escape.fcdsc")]
    [InlineData("C:\\escape.fcdsc")]
    [InlineData("CON.fcdsc")]
    [InlineData("x.fcdsa")]
    [InlineData(".fcdsc")]
    [InlineData("x.fcdsc ")]
    public void SavedComparisonNamesStayInsideTheTypedPortablePrivateNamespace(string name)
    { Assert.Throws<ArgumentException>(() => AnalysisPrivateInventoryStore.ValidateComparisonName(name)); }
}
