using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class DirectoryComparisonIntegrityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void Cancellation_during_a_short_read_stops_before_the_next_provider_call(string side)
    {
        using var dir = new TempDir();
        string path = dir.File("owned.bin", new string('x', 32768));
        string before = Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(path)));
        using var stop = new CancellationTokenSource();
        using var left = new ShortSource(path, side == "left" ? stop : null);
        using var right = new ShortSource(path, side == "right" ? stop : null);
        var error = Record.Exception(() => DirectoryCompare.ContentEqual(left, right, stop.Token));
        output.WriteLine("DIRECTORY_INTEGRITY " + JsonSerializer.Serialize(new { Case = "short-read", side, LeftReads = left.Reads, RightReads = right.Reads,
            Exception = error?.GetType().Name, BeforeSHA256 = before, AfterSHA256 = Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(path))) }));
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(1, side == "left" ? left.Reads : right.Reads);
        if (side == "left") Assert.Equal(0, right.Reads);
        Assert.Equal(before, Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(path))));
    }

    [Fact]
    public void Already_canceled_content_does_not_probe_lengths()
    {
        using var dir = new TempDir();
        using var left = new ShortSource(dir.File("left", "x"), null);
        using var right = new ShortSource(dir.File("right", "xx"), null);
        var error = Record.Exception(() => DirectoryCompare.ContentEqual(left, right, new CancellationToken(true)));
        output.WriteLine("DIRECTORY_INTEGRITY " + JsonSerializer.Serialize(new { Case = "already-canceled", left.Metadata, RightMetadata = right.Metadata, Exception = error?.GetType().Name }));
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(0, left.Metadata + right.Metadata);
    }

    [Fact]
    public void Content_requested_without_a_reader_is_undecided_and_marked()
    {
        var entries = new[] { new EntryData("owned", EntryKind.File, 1) };
        var result = DirectoryCompare.Compare(entries, entries, CompareCriteria.Content, TimeSpan.Zero, null, TestContext.Current.CancellationToken);
        output.WriteLine("DIRECTORY_INTEGRITY " + JsonSerializer.Serialize(new { Case = "no-content-reader", result }));
        Assert.Equal(0, result.Same); Assert.Equal(1, result.Unknown);
        Assert.Equal(["owned"], result.LeftMarks); Assert.Equal(["owned"], result.RightMarks);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void Reported_enumeration_failure_never_becomes_a_complete_equal_tree(string side)
    {
        var providers = new ProviderRegistry(); providers.Register(new PartialProvider(side));
        var result = TreeCompare.Compare(providers, new Location("ownedpartial", "left"), new Location("ownedpartial", "right"),
            CompareCriteria.Size, TimeSpan.Zero, false, TestContext.Current.CancellationToken);
        output.WriteLine("DIRECTORY_INTEGRITY " + JsonSerializer.Serialize(new { Case = "partial-enumeration", side, result }));
        Assert.DoesNotContain(result.Entries, e => e.Kind == TreeDiffKind.Same);
        Assert.Contains(result.Entries, e => e.Kind == TreeDiffKind.Unknown && e.Detail!.Contains("owned subtree unavailable", StringComparison.Ordinal));
    }

    private sealed class ShortSource(string path, CancellationTokenSource? stop) : IContentSource
    {
        private readonly FileContentSource _inner = new(path);
        public int Reads, Metadata;
        public string DisplayName => _inner.DisplayName;
        public string? LocalPath => path;
        public bool CanSeek => true;
        public long Length { get { Metadata++; return _inner.Length; } }
        public ContentRevision? GetRevision() => _inner.GetRevision();
        public int Read(long offset, Span<byte> into)
        {
            Reads++;
            int n = _inner.Read(offset, into[..Math.Min(256, into.Length)]);
            if (Reads == 1) stop?.Cancel();
            return n;
        }
        public void Dispose() => _inner.Dispose();
    }

    private sealed class PartialProvider(string failingSide) : ResourceProvider
    {
        public override string Scheme => "ownedpartial";
        public override string GetDisplayPath(Location l) => l.Path;
        public override Location? GetParent(Location l) => null;
        public override Location? GetChildLocation(Location l, in EntryData e) => null;
        public override LocationCapabilities GetCapabilities(Location l) => LocationCapabilities.Enumerate;
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct)
        {
            sink.AddBatch([new EntryData("known", EntryKind.File, 1)]);
            if (l.Path == failingSide) sink.ReportIssue("owned subtree unavailable");
            return Task.CompletedTask;
        }
    }
}
