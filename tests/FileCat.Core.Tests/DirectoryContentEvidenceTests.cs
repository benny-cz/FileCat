using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Compare;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class DirectoryContentEvidenceTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string> AdverseCases
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (string route in new[] { "direct", "mark", "tree", "worker" })
                foreach (string side in new[] { "left", "right" })
                    foreach (string problem in new[] { "revision-changed", "revision-lost", "revision-error", "incomplete-before", "incomplete-after", "caveat", "early-eof", "negative-count", "oversize-count" })
                        data.Add(route, side, problem);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AdverseCases))]
    public async Task Unstable_incomplete_or_invalid_content_is_unknown_in_every_directory_consumer(string route, string side, string problem)
    {
        using var fixture = new Fixture(side, problem);
        bool? result = null; Exception? error = null;
        try { result = await fixture.Compare(route, TestContext.Current.CancellationToken); }
        catch (Exception e) { error = e; }
        Emit(route, side, problem, result, fixture, error);
        Assert.Null(error);
        Assert.Null(result);
        fixture.AssertDisposed();
        if (route == "worker") fixture.AssertWorkers();
        if (problem is "incomplete-before" or "caveat") Assert.All(fixture.Provider.Sources, s => Assert.Equal(0, s.Reads));
        if (problem == "revision-changed") Assert.NotEqual(fixture.Before(side), fixture.After(side));
        else fixture.AssertUnchanged();
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mark")]
    [InlineData("tree")]
    [InlineData("worker")]
    public async Task Matching_premature_endings_are_unknown_even_without_revision_evidence(string route)
    {
        using var fixture = new Fixture("both", "early-eof-no-revision");
        var result = await fixture.Compare(route, TestContext.Current.CancellationToken);
        Emit(route, "both", "early-eof-no-revision", result, fixture);
        Assert.Null(result); fixture.AssertDisposed(); fixture.AssertUnchanged();
    }

    [Theory]
    [InlineData("direct", "left")]
    [InlineData("direct", "right")]
    [InlineData("mark", "left")]
    [InlineData("mark", "right")]
    [InlineData("tree", "left")]
    [InlineData("tree", "right")]
    [InlineData("worker", "left")]
    [InlineData("worker", "right")]
    public async Task Cancellation_during_revision_probe_stops_before_content_reads(string route, string side)
    {
        using var stop = new CancellationTokenSource();
        using var fixture = new Fixture(side, "revision-canceled", stop);
        Exception? error = null; bool? result = null;
        try { result = await fixture.Compare(route, stop.Token); }
        catch (OperationCanceledException e) { error = e; }
        Emit(route, side, "revision-canceled", result, fixture, error);
        // Recursive comparisons preserve an explicitly canceled partial preview.
        if (route is "tree" or "worker") Assert.True(fixture.Canceled);
        else Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.All(fixture.Provider.Sources, s => Assert.Equal(0, s.Reads));
        fixture.AssertDisposed(); fixture.AssertUnchanged();
    }

    public static TheoryData<string, string> PositiveCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (string route in new[] { "direct", "mark", "tree", "worker" })
                foreach (string mode in new[] { "equal", "different", "no-revision" }) data.Add(route, mode);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task Complete_stable_content_remains_comparable_with_short_reads_and_optional_revisions(string route, string mode)
    {
        using var fixture = new Fixture("right", mode);
        var result = await fixture.Compare(route, TestContext.Current.CancellationToken);
        Emit(route, "right", mode, result, fixture);
        Assert.Equal(mode != "different", result); fixture.AssertDisposed(); fixture.AssertUnchanged();
        if (route == "worker") fixture.AssertWorkers();
    }

    private void Emit(string route, string side, string problem, bool? result, Fixture f, Exception? error = null)
        => output.WriteLine("DIRECTORY_CONTENT_EVIDENCE " + JsonSerializer.Serialize(new { route, side, problem, result, Error = error?.GetType().Name,
            f.Canceled, f.TreeKind, f.SyncActions, Sources = f.Provider.Sources.Select(s => new { s.Side, s.Reads, s.Revisions, s.Disposals, s.MissingRanges, s.Caveat }),
            Calls = f.Provider.Calls, LeftBefore = f.Before("left"), LeftAfter = f.After("left"), RightBefore = f.Before("right"), RightAfter = f.After("right") }));

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _dir = new(); private readonly string _left, _right, _leftHash, _rightHash;
        private readonly ProviderRegistry _providers = new();
        public Provider Provider { get; }
        public bool Canceled; public string? TreeKind; public string[]? SyncActions;
        public Fixture(string side, string problem, CancellationTokenSource? stop = null)
        {
            _left = _dir.File("left.bin", new string('\0', 32768)); _right = _dir.File("right.bin", new string('\0', 32768));
            if (problem == "different") { var bytes = File.ReadAllBytes(_right); bytes[16321] = 7; File.WriteAllBytes(_right, bytes); }
            _leftHash = Hash(_left); _rightHash = Hash(_right);
            Provider = new Provider(_left, _right, side, problem, stop); _providers.Register(Provider);
        }
        public async Task<bool?> Compare(string route, CancellationToken ct)
        {
            var l = new Location(Provider.Scheme, "left"); var r = new Location(Provider.Scheme, "right");
            if (route is "direct" or "mark")
            {
                using var a = Provider.OpenContent(new ItemRef(l, "shared.bin", EntryKind.File, 32768, 0))!;
                using var b = Provider.OpenContent(new ItemRef(r, "shared.bin", EntryKind.File, 32768, 0))!;
                if (route == "direct") return DirectoryCompare.ContentEqual(a, b, ct);
                var entries = new[] { new EntryData("shared.bin", EntryKind.File, 32768) };
                var result = DirectoryCompare.Compare(entries, entries, CompareCriteria.Content, TimeSpan.Zero, (_, _) => DirectoryCompare.ContentEqual(a, b, ct), ct);
                if (result.Unknown > 0) { Assert.Equal(["shared.bin"], result.LeftMarks); Assert.Equal(["shared.bin"], result.RightMarks); return null; }
                return result.Same == 1;
            }
            using var io = new DeviceIoScheduler(hangThreshold: TimeSpan.FromMinutes(1));
            var tree = await TreeCompare.CompareAsync(_providers, l, r, CompareCriteria.Content, TimeSpan.Zero, false, ct,
                io: route == "worker" ? new ComparisonIo(io, _providers) : null);
            Canceled = tree.Canceled;
            if (tree.Canceled) return null;
            var entry = Assert.Single(tree.Entries); TreeKind = entry.Kind.ToString();
            SyncActions = SyncPlanner.Propose(tree, true, SyncMode.Mirror, false).Select(i => i.Action.ToString()).ToArray();
            if (entry.Kind == TreeDiffKind.Unknown) { Assert.Equal(["None"], SyncActions); return null; }
            return entry.Kind == TreeDiffKind.Same;
        }
        public string Before(string side) => side == "left" ? _leftHash : _rightHash;
        public string After(string side) => Hash(side == "left" ? _left : _right);
        public void AssertDisposed() => Assert.All(Provider.Sources, s => Assert.Equal(1, s.Disposals));
        public void AssertUnchanged() { Assert.Equal(_leftHash, Hash(_left)); Assert.Equal(_rightHash, Hash(_right)); }
        public void AssertWorkers() => Assert.All(Provider.Calls, c => Assert.StartsWith("FileCat I/O ", c.Thread));
        private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        public void Dispose() => _dir.Dispose();
    }

    private sealed record Call(string Side, string Operation, string Thread);
    private sealed class Provider(string left, string right, string adverseSide, string problem, CancellationTokenSource? stop) : ResourceProvider
    {
        public readonly List<Source> Sources = []; public readonly List<Call> Calls = [];
        public override string Scheme => "owneddirectorycontent";
        public override string GetDeviceKey(Location l) => Scheme + ":" + l.Path;
        public override string GetDisplayPath(Location l) => l.Path;
        public override Location? GetParent(Location l) => null;
        public override Location? GetChildLocation(Location l, in EntryData e) => null;
        public override LocationCapabilities GetCapabilities(Location l) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public void Boundary(string side, string operation) => Calls.Add(new(side, operation, Thread.CurrentThread.Name ?? ""));
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct)
        {
            Boundary(l.Path, "enumerate"); sink.AddBatch([new EntryData("shared.bin", EntryKind.File, 32768)]); return Task.CompletedTask;
        }
        public override IContentSource OpenContent(ItemRef item)
        {
            Boundary(item.Parent.Path, "open");
            var source = new Source(item.Parent.Path == "left" ? left : right, item.Parent.Path, adverseSide == "both" || adverseSide == item.Parent.Path ? problem : "equal", this, stop);
            Sources.Add(source); return source;
        }
    }

    private sealed class Source(string path, string side, string problem, Provider provider, CancellationTokenSource? stop) : IContentSource, IPartialContent
    {
        private readonly FileContentSource _inner = new(path);
        public string Side => side; public int Reads, Revisions, Disposals;
        public string DisplayName => _inner.DisplayName; public string? LocalPath => path; public bool CanSeek => true;
        public long Length { get { provider.Boundary(side, "length"); return _inner.Length; } }
        public IReadOnlyList<(long Offset, long Length)> MissingRanges => problem == "incomplete-before" || problem == "incomplete-after" && Reads > 0 ? [(16384L, 8192L)] : [];
        public string? Caveat => problem == "caveat" ? "owned recovered start is uncertain" : null;
        public ContentRevision? GetRevision()
        {
            provider.Boundary(side, "revision"); Revisions++;
            if (problem == "revision-canceled") stop!.Cancel();
            if (problem is "no-revision" or "early-eof-no-revision" || problem == "revision-lost" && Reads > 0) return null;
            if (problem == "revision-error" && Reads > 0) throw new IOException("owned revision unavailable");
            return _inner.GetRevision();
        }
        public int Read(long offset, Span<byte> buffer)
        {
            provider.Boundary(side, "read"); Reads++;
            if (problem is "early-eof" or "early-eof-no-revision") return 0;
            if (problem == "negative-count") return -1;
            if (problem == "oversize-count") return int.MaxValue;
            int n = _inner.Read(offset, buffer[..Math.Min(buffer.Length, 256)]);
            if (problem == "revision-changed" && Reads == 1)
            {
                var old = File.GetLastWriteTimeUtc(path); var bytes = File.ReadAllBytes(path); bytes[0] = 9;
                File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, old.AddSeconds(60));
            }
            return n;
        }
        public void Dispose() { Disposals++; _inner.Dispose(); }
    }
}
