using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class AnalysisSourceFrontierTests
{
    private const string Device = "live-frontier-fixture";
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static RootBinding Binding => new("r", "file", "/source", "/source", "source", IdentityAssurance.Verified, "source") { NameRules = AnalysisNameRules.Unix };
    private static AnalysisEntry Root()
    {
        var zero = AnalysisMeasurement.Known(0); var root = new AnalysisEntry([], "source", [], AnalysisKind.Directory, "native-root", zero, zero, zero, null, "root-v");
        return root with { Ancestry = new(Guid.NewGuid(), [], root.Identity, root.Version, null) };
    }
    private static AnalysisEntry Child(AnalysisEntry parent, string name, string? identity = null, string version = "v")
    {
        var raw = Encoding.UTF8.GetBytes(name).ToImmutableArray();
        byte[] relative = parent.RelativeName.IsEmpty ? raw.ToArray() : parent.RelativeName.Concat([(byte)'/']).Concat(raw).ToArray();
        var zero = AnalysisMeasurement.Known(0);
        var entry = new AnalysisEntry(relative.ToImmutableArray(), name, raw, AnalysisKind.Directory, identity ?? "native-" + name, zero, zero, zero, null, version);
        return entry with { Ancestry = new(parent.Ancestry!.BindingId, entry.RelativeName, entry.Identity, entry.Version, parent.Ancestry) };
    }
    private static Task<AnalysisSourceFrontier> Create(AnalysisScratchWorkspace workspace, DeviceIoScheduler io, AnalysisEntry root) =>
        AnalysisIo.Run(io, Device, _ => AnalysisSourceFrontier.Create(workspace, io, Device, root, Binding, 1), Cancellation);

    [Fact]
    public async Task ThousandsOfQueuedDirectoriesAndTheirPoppedParentsRetainExactReadWitnesses()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var root = Root();
        await using var frontier = await Create(workspace, io, root); var parent = await frontier.PopAsync(Cancellation); Assert.NotNull(parent);
        for (ulong first = 2; first <= 5001; first += 200)
        {
            var children = ImmutableArray.CreateBuilder<AnalysisFrontierChild>();
            for (ulong n = first; n < Math.Min(first + 200, 5002); n++) children.Add(new(Child(root, "directory-" + n), n));
            await frontier.PushAsync(parent, children.ToImmutable(), Cancellation);
        }
        AnalysisFrontierDirectory? firstPopped = null;
        for (ulong expected = 5001; expected >= 2; expected--)
        {
            var current = await frontier.PopAsync(Cancellation); Assert.NotNull(current); firstPopped ??= current;
            Assert.Equal(expected, current.Path); Assert.Equal(1, current.Depth); Assert.Equal("directory-" + expected, current.Entry.Name);
            Assert.Equal(Encoding.UTF8.GetBytes(current.Entry.Name), current.Entry.RelativeName); Assert.Same(root.Ancestry, current.Entry.Ancestry!.Parent);
            Assert.Equal(root.Ancestry!.BindingId, current.Entry.Ancestry.BindingId);
        }
        Assert.Null(await frontier.PopAsync(Cancellation)); Assert.NotNull(firstPopped);
        await frontier.PushAsync(firstPopped, [new(Child(firstPopped.Entry, "nested"), 5002)], Cancellation);
        var nested = await frontier.PopAsync(Cancellation); Assert.NotNull(nested); Assert.Equal(2, nested.Depth);
        Assert.Equal(Encoding.UTF8.GetBytes("directory-5001/nested"), nested.Entry.RelativeName);
        Assert.Equal("native-directory-5001", nested.Entry.Ancestry!.Parent!.Identity); Assert.Same(root.Ancestry, nested.Entry.Ancestry.Parent.Parent);
        await frontier.DisposeAsync(); Assert.Equal(0, workspace.ReservedBytes);
    }

    [Fact]
    public async Task ObservedRootMetadataWithoutItsLiveWitnessCannotCreateAFrontier()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler();
        var root = Root() with { Ancestry = null };
        await Assert.ThrowsAsync<AnalysisSourceChangedException>(() => Create(workspace, io, root)); Assert.Equal(0, workspace.ReservedBytes);
    }

    [Fact]
    public async Task ForeignTicketsAndForgedChildAncestryCannotPublishEvenTheGoodHalfOfABatch()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler();
        var root = Root(); await using var first = await Create(workspace, io, root); await using var second = await Create(workspace, io, Root());
        var parent = await first.PopAsync(Cancellation); var foreign = await second.PopAsync(Cancellation); Assert.NotNull(parent); Assert.NotNull(foreign);
        var good = Child(root, "good"); var bad = Child(foreign.Entry, "bad"); int writes = directory.Writes;
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.PushAsync(foreign, [new(bad, 2)], Cancellation));
        await Assert.ThrowsAsync<AnalysisSourceChangedException>(() => first.PushAsync(parent, [new(good, 2), new(bad, 3)], Cancellation));
        Assert.Equal(writes, directory.Writes); Assert.Null(await first.PopAsync(Cancellation));
        await first.PushAsync(parent, [new(good, 2)], Cancellation); Assert.Equal("good", (await first.PopAsync(Cancellation))!.Entry.Name);
    }

    [Fact]
    public async Task ExcessiveWitnessPayloadRefusesThatScopeAndKeepsItsReadableSiblingQueued()
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler(); var root = Root();
        await using var frontier = await Create(workspace, io, root); var parent = await frontier.PopAsync(Cancellation); Assert.NotNull(parent);
        await frontier.PushAsync(parent, [new(Child(root, "sibling"), 2), new(Child(root, "deep", new string('i', 4000), new string('v', 4000)), 3)], Cancellation);
        bool refused = false;
        for (ulong n = 4; n < 260; n++)
        {
            AnalysisFrontierDirectory? current;
            try { current = await frontier.PopAsync(Cancellation); }
            catch (AnalysisFrontierScopeException error) { Assert.Equal(n - 1, error.Path); refused = true; break; }
            Assert.NotNull(current);
            await frontier.PushAsync(current, [new(Child(current.Entry, "d", new string('i', 3990) + n, new string('v', 4000)), n)], Cancellation);
        }
        Assert.True(refused); Assert.Equal("sibling", (await frontier.PopAsync(Cancellation))!.Entry.Name); Assert.Null(await frontier.PopAsync(Cancellation));
    }

    [Fact]
    public async Task AChildWithCorrectBytesAndBindingStillNeedsTheExactPoppedParentWitness()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var root = Root();
        await using var frontier = await Create(workspace, io, root); var parent = await frontier.PopAsync(Cancellation); Assert.NotNull(parent);
        await frontier.PushAsync(parent, [new(Child(root, "a"), 2), new(Child(root, "b"), 3)], Cancellation);
        var b = await frontier.PopAsync(Cancellation); var a = await frontier.PopAsync(Cancellation); Assert.NotNull(a); Assert.NotNull(b);
        var good = Child(b.Entry, "nested"); var bad = good with { Ancestry = good.Ancestry! with { Parent = a.Entry.Ancestry } }; int writes = directory.Writes;
        await Assert.ThrowsAsync<AnalysisSourceChangedException>(() => frontier.PushAsync(b, [new(bad, 4)], Cancellation));
        Assert.Equal(writes, directory.Writes); Assert.Null(await frontier.PopAsync(Cancellation));
        await frontier.PushAsync(b, [new(good, 4)], Cancellation); Assert.Equal(Encoding.UTF8.GetBytes("b/nested"), (await frontier.PopAsync(Cancellation))!.Entry.RelativeName);
    }

    [Fact]
    public async Task DisposalRetainsActualBlockedFrontierWritesUntilTheirNativeReturn()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler(); var root = Root();
        var frontier = await Create(workspace, io, root); var parent = await frontier.PopAsync(Cancellation); Assert.NotNull(parent);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        directory.BeforeNextWrite = () => { entered.Set(); release.Wait(Cancellation); };
        var pushing = frontier.PushAsync(parent, [new(Child(root, "child"), 2)], Cancellation); Task? closing = null;
        try { Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation)); closing = frontier.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted); }
        finally { release.Set(); }
        await pushing; await closing!; Assert.Equal(0, workspace.ReservedBytes);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => frontier.PopAsync(Cancellation));
    }

    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        internal int Writes; internal Action? BeforeNextWrite;
        internal override void Verify() { }
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1 << 34, (UInt128)1 << 34);
        internal override AnalysisPrivateScratchFile CreateScratch() => new MemoryFile(this);
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { }
    }
    private sealed class MemoryFile(MemoryDirectory directory) : AnalysisPrivateScratchFile
    {
        private readonly MemoryStream _bytes = new();
        public override long Length => _bytes.Length;
        public override void Verify() { }
        public override int ReadAt(Span<byte> buffer, long offset) { _bytes.Position = offset; return _bytes.Read(buffer); }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset) { var before = directory.BeforeNextWrite; directory.BeforeNextWrite = null; before?.Invoke(); directory.Writes++; _bytes.Position = offset; _bytes.Write(buffer); }
        public override void Dispose() => _bytes.Dispose();
    }
}
