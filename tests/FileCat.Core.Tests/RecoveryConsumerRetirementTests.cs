using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

public sealed class RecoveryConsumerRetirementTests(ITestOutputHelper output)
{
    public static TheoryData<string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, bool>();
            foreach (string kind in new[] { "resident", "extents", "compressed", "unreadable" })
            foreach (bool closed in new[] { false, true }) cases.Add(kind, closed);
            return cases;
        }
    }

    private static byte[] Known() => Enumerable.Range(0, 65536).Select(i => (byte)(i * 29 + 13)).ToArray();

    private sealed class Source(string path, bool unreadable = false) : IBlockSource
    {
        private readonly ImageFileSource _file = new(path);
        public int Disposals { get; private set; }
        public ManualResetEventSlim? Entered { get; set; }
        public ManualResetEventSlim? Release { get; set; }
        public Action? CloseDuringRead { get; set; }
        public string Description => "owned recovery source";
        public long Length => _file.Length;
        public int Read(long offset, Span<byte> buffer)
        {
            Entered?.Set();
            if (Release is { } release && !release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Owned read gate timed out.");
            var close = CloseDuringRead; CloseDuringRead = null; close?.Invoke();
            if (unreadable) throw new IOException("Owned unreadable range.");
            return _file.Read(offset, buffer);
        }
        public void Dispose() { Disposals++; _file.Dispose(); }
    }

    private sealed record Rig(RecoveryContent Held, WeakReference Volume, WeakReference Item,
        WeakReference Tree, WeakReference Payload, WeakReference? Unit, IReadOnlyList<(long Offset, long Length)> Snapshot);

    private static int MissingCapacity(RecoveryContent content) =>
        ((List<(long Offset, long Length)>)typeof(RecoveryContent).GetField("_missing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(content)!).Capacity;

    private static RecoveryItem Item(string kind)
    {
        var item = new RecoveryItem { Name = "known.bin", Size = 65536, State = RecoveryState.Recoverable,
            Resident = kind == "resident" ? Known() : null, RecordNumber = 17 };
        if (kind != "resident") item.Extents = new[] { new Extent(0, 65536, ExtentState.Free) };
        if (kind == "compressed") item.Compression = new CompressedLayout(65536,
            new[] { new CompressedUnit(new[] { (0L, 65536L) }, CompressedUnitKind.Raw, false) });
        return item;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Rig Visit(Source source, string kind, bool closed)
    {
        var payload = Known();
        var tree = new RecoveryItem { Name = "owned scan", IsDirectory = true, Resident = payload };
        var item = Item(kind); item.Parent = tree; tree.Children.Add(item);
        var volume = new WindowSource(source, 0, 65536, "owned window");
        var held = new RecoveryContent(volume, item);
        var bytes = new byte[65536]; Assert.Equal(bytes.Length, held.Read(0, bytes));
        Assert.Equal(kind == "unreadable" ? new byte[65536] : Known(), bytes);
        var unit = typeof(RecoveryContent).GetField("_unit", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(held);
        Assert.Equal(kind == "compressed", unit is not null);
        var snapshot = held.MissingRanges;
        var rig = new Rig(held, new(volume), new(item), new(tree), new(payload), unit is null ? null : new(unit), snapshot);
        if (closed) { held.Dispose(); held.Dispose(); }
        return rig;
    }

    [Theory, MemberData(nameof(Cases))]
    public void Closed_recovery_readers_retire_private_graphs_without_closing_the_shared_source(string kind, bool closed)
    {
        string root = Directory.CreateTempSubdirectory("fc-recovery-consumer-retirement-").FullName;
        string path = Path.Combine(root, "owned.img"); File.WriteAllBytes(path, Known());
        using var source = new Source(path, kind == "unreadable"); Rig? rig = null;
        try
        {
            rig = Visit(source, kind, closed);
            for (int i = 0; i < 8; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(10); }
            bool volume = rig.Volume.IsAlive, item = rig.Item.IsAlive, tree = rig.Tree.IsAlive, payload = rig.Payload.IsAlive;
            bool? unit = rig.Unit?.IsAlive;
            bool readRefused = false;
            try { rig.Held.Read(0, new byte[32]); }
            catch (ObjectDisposedException) { readRefused = true; }
            output.WriteLine("RECOVERY_CONSUMER_RETIREMENT " + JsonSerializer.Serialize(new {
                kind, closed, volume, item, tree, payload, unit, readRefused,
                sourceDisposals = source.Disposals, currentMissing = rig.Held.MissingRanges.Count,
                snapshotMissing = rig.Snapshot.Count, missingCapacity = MissingCapacity(rig.Held), knownBytes = 65536,
                sourceSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                heldWrapper = true, managedReachabilityNotProcessPeak = true, noPhysicalSource = true }));
            Assert.Equal(!closed, volume); Assert.Equal(!closed, item); Assert.Equal(!closed, tree); Assert.Equal(!closed, payload);
            if (unit is not null) Assert.Equal(!closed, unit);
            Assert.Equal(closed, readRefused); Assert.Equal(0, source.Disposals);
            if (closed) Assert.Equal(0, MissingCapacity(rig.Held));
            Assert.Equal(kind == "unreadable" ? 1 : 0, rig.Snapshot.Count);
            Assert.Equal(!closed && kind == "unreadable" ? 1 : 0, rig.Held.MissingRanges.Count);
            Assert.Equal("known.bin", rig.Held.DisplayName); Assert.Equal(65536, rig.Held.Length);
            Assert.Equal(65536, rig.Held.GetRevision()!.Value.Length);
            Assert.Equal(Known(), File.ReadAllBytes(path)); GC.KeepAlive(rig.Held);
        }
        finally
        {
            rig?.Held.Dispose(); Assert.Equal(0, source.Disposals); source.Dispose();
            Assert.Equal(Known(), File.ReadAllBytes(path)); Directory.Delete(root, true);
            Assert.False(Directory.Exists(root));
        }
    }

    [Theory]
    [InlineData("extents")]
    [InlineData("compressed")]
    [InlineData("unreadable")]
    public void A_reentrant_close_does_not_republish_decoded_or_missing_payloads(string kind)
    {
        string root = Directory.CreateTempSubdirectory("fc-recovery-consumer-reentrant-").FullName;
        string path = Path.Combine(root, "owned.img"); File.WriteAllBytes(path, Known());
        using var source = new Source(path, kind == "unreadable");
        var item = Item(kind); var content = new RecoveryContent(new WindowSource(source, 0, 65536, "owned window"), item);
        source.CloseDuringRead = content.Dispose;
        try
        {
            var bytes = new byte[65536]; Assert.Equal(bytes.Length, content.Read(0, bytes));
            bool unitRetained = typeof(RecoveryContent).GetField("_unit", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(content) is not null;
            bool readRefused = false; try { content.Read(0, new byte[32]); } catch (ObjectDisposedException) { readRefused = true; }
            output.WriteLine("RECOVERY_CONSUMER_REENTRANT " + JsonSerializer.Serialize(new {
                kind, unitRetained, readRefused, missingAfterClose = content.MissingRanges.Count,
                missingCapacity = MissingCapacity(content), sourceDisposals = source.Disposals, callerItemIntact = item.Size == 65536 && item.Extents.Count == 1,
                knownBytes = 65536, noPhysicalSource = true }));
            Assert.False(unitRetained); Assert.True(readRefused); Assert.Empty(content.MissingRanges);
            Assert.Equal(0, source.Disposals); Assert.Equal(kind == "unreadable" ? new byte[65536] : Known(), bytes);
            Assert.Single(item.Extents); Assert.Equal(Known(), File.ReadAllBytes(path));
        }
        finally { content.Dispose(); source.Dispose(); Directory.Delete(root, true); Assert.False(Directory.Exists(root)); }
    }

    [Theory]
    [InlineData("extents")]
    [InlineData("compressed")]
    [InlineData("unreadable")]
    public async Task Disposal_waits_for_an_active_read_and_prevents_late_cache_publication(string kind)
    {
        string root = Directory.CreateTempSubdirectory("fc-recovery-consumer-race-").FullName;
        string path = Path.Combine(root, "owned.img"); File.WriteAllBytes(path, Known());
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var source = new Source(path, kind == "unreadable") { Entered = entered, Release = release };
        var item = Item(kind); var content = new RecoveryContent(new WindowSource(source, 0, 65536, "owned window"), item);
        var actual = new byte[65536]; Task<int>? reading = null; Task? closing = null;
        using var closeStarted = new ManualResetEventSlim();
        try
        {
            reading = Task.Run(() => content.Read(0, actual)); Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            closing = Task.Run(() => { closeStarted.Set(); content.Dispose(); });
            Assert.True(closeStarted.Wait(TimeSpan.FromSeconds(10)));
            bool returnedDuringRead = closing.Wait(TimeSpan.FromMilliseconds(150));
            release.Set(); Assert.Equal(actual.Length, await reading.WaitAsync(TimeSpan.FromSeconds(10)));
            await closing.WaitAsync(TimeSpan.FromSeconds(10));
            bool unitRetained = typeof(RecoveryContent).GetField("_unit", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(content) is not null;
            bool readRefused = false; try { content.Read(0, new byte[32]); } catch (ObjectDisposedException) { readRefused = true; }
            output.WriteLine("RECOVERY_CONSUMER_RACE " + JsonSerializer.Serialize(new {
                kind, returnedDuringRead, unitRetained, readRefused, missingAfterClose = content.MissingRanges.Count,
                missingCapacity = MissingCapacity(content), sourceDisposals = source.Disposals, callerItemIntact = item.Size == 65536 && item.Extents.Count == 1,
                knownBytes = 65536, ownedReadGateReleased = release.IsSet, noPhysicalSource = true }));
            Assert.False(returnedDuringRead); Assert.False(unitRetained); Assert.True(readRefused);
            Assert.Empty(content.MissingRanges); Assert.Equal(0, MissingCapacity(content)); Assert.Equal(0, source.Disposals);
            Assert.Equal(kind == "unreadable" ? new byte[65536] : Known(), actual);
            Assert.Equal(65536, item.Size); Assert.Single(item.Extents); Assert.Equal(Known(), File.ReadAllBytes(path));
        }
        finally
        {
            release.Set(); if (reading is not null) await reading.WaitAsync(TimeSpan.FromSeconds(15));
            if (closing is not null) await closing.WaitAsync(TimeSpan.FromSeconds(15));
            content.Dispose(); Assert.Equal(0, source.Disposals); source.Dispose(); Directory.Delete(root, true);
            Assert.False(Directory.Exists(root));
        }
    }
}
