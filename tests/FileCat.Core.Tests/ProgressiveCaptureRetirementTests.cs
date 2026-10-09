using System.Collections;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

[Collection("Native archive volume sharing")]
public sealed class ProgressiveCaptureRetirementTests(ITestOutputHelper output)
{
    private const long Large = 33L * 1024 * 1024 + 17;
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    public static IEnumerable<object[]> ArchiveCases()
    {
        foreach (bool zip in new[] { false, true })
        foreach (int members in new[] { 64, 4096, 16384 })
        foreach (string mode in new[] { "unread", "read", "live-lease" }) yield return [zip, members, mode];
    }

    [Theory]
    [MemberData(nameof(ArchiveCases))]
    public void Disposed_members_retire_evicted_indexes_but_live_leases_keep_their_reader(bool zip, int members, string mode)
    {
        string root = Directory.CreateTempSubdirectory("filecat-progressive-captures-").FullName;
        string path = Path.Combine(root, zip ? "owned.zip" : "owned.tar");
        Rig? rig = null;
        try
        {
            Make(path, zip, members);
            string hash = FileHash(path);
            rig = Open(path, root, zip, members);
            Assert.True(rig.Index.IsAlive);
            if (mode != "unread") ReadAllKnownBytes(rig.Content);
            Release(rig.Provider, path);
            int cached = rig.Provider is ZipProvider z ? z.RetainedIndexes : ((ArchiveProvider)rig.Provider).RetainedIndexes;
            Assert.Equal(0, cached);
            bool before = rig.Index.IsAlive;
            bool? heldLive = null;
            if (mode == "live-lease")
            {
                Collect(); heldLive = rig.Index.IsAlive;
                Assert.True(heldLive.Value);
                ReadAllKnownBytes(rig.Content); // An evicted but leased reader remains fully usable.
            }
            rig.Content.Dispose();
            Assert.Null(Record.Exception(rig.Content.Dispose));
            bool? nativeClosed = OperatingSystem.IsWindows() ? Exclusive(path) : null;
            Collect();
            bool retained = rig.Index.IsAlive;
            bool unchanged = FileHash(path) == hash;
            bool readRefused = Record.Exception(() => rig.Content.Read(0, new byte[1])) is ObjectDisposedException;
            output.WriteLine("PROGRESSIVE_CAPTURE_RETIREMENT " + JsonSerializer.Serialize(new {
                kind = "archive", zip, members, mode, root, declaredBytes = Large,
                rig.EstimatedBytes, cached, before, heldLive, retained, nativeClosed, unchanged, readRefused,
                allKnownMemberBytesVerified = mode != "unread", originalArchiveSHA256 = hash,
                heldDisposedWrapper = true, managedReachabilityOnlyNotProcessMemory = true }));
            GC.KeepAlive(rig.Content); GC.KeepAlive(rig.Provider);
            Assert.True(unchanged); Assert.True(readRefused);
            if (nativeClosed is not null) Assert.True(nativeClosed.Value);
            // Live leases are the positive control; other routes require reclamation while the wrapper stays held.
            if (mode != "live-lease") Assert.False(retained, "A disposed member retains an evicted archive index through its retired callbacks.");
        }
        finally
        {
            if (rig is not null) { rig.Content.Dispose(); Release(rig.Provider, path); }
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("none")]
    [InlineData("source")]
    [InlineData("callback")]
    [InlineData("both")]
    public void Retired_callbacks_release_owned_bytes_even_when_cleanup_throws(string fault)
    {
        string root = Directory.CreateTempSubdirectory("filecat-progressive-callbacks-").FullName;
        string path = Path.Combine(root, "owned.bin");
        byte[] bytes = Enumerable.Range(0, 32768).Select(i => (byte)(i * 31 + 17)).ToArray();
        File.WriteAllBytes(path, bytes);
        var (content, weak, counts, sourceError, callbackError) = Owned(path, bytes, fault);
        try
        {
            byte[] read = new byte[bytes.Length]; Assert.Equal(read.Length, content.Read(0, read)); Assert.Equal(bytes, read);
            Exception? first = Record.Exception(content.Dispose);
            Exception? expected = fault is "source" or "both" ? sourceError : fault == "callback" ? callbackError : null;
            bool exact = ReferenceEquals(first, expected);
            bool repeat = Record.Exception(content.Dispose) is null;
            bool? closed = OperatingSystem.IsWindows() ? Exclusive(path) : null;
            Collect(); bool retained = weak.IsAlive;
            bool unchanged = File.ReadAllBytes(path).SequenceEqual(bytes);
            output.WriteLine("PROGRESSIVE_CAPTURE_RETIREMENT " + JsonSerializer.Serialize(new {
                kind = "callback", fault, root, retained, exact, repeat, closed, unchanged,
                counts.Closes, counts.Callbacks, error = first?.GetType().Name, message = first?.Message,
                ownedBytes = bytes.Length, allKnownMemberBytesVerified = true,
                heldDisposedWrapper = true, nativeFaultIncidenceNotClaimed = true }));
            GC.KeepAlive(content);
            Assert.True(exact); Assert.True(repeat); Assert.Equal(1, counts.Closes); Assert.Equal(1, counts.Callbacks);
            Assert.True(unchanged); if (closed is not null) Assert.True(closed.Value);
            Assert.False(retained, "Disposed callbacks retain their owned payload after cleanup finishes.");
        }
        finally { content.Dispose(); Directory.Delete(root, true); }
    }

    private sealed record Rig(ResourceProvider Provider, ProgressiveContent Content, WeakReference Index, long EstimatedBytes);
    private sealed class Sink : IEnumerationSink
    {
        public int Count; public EntryData? Payload;
        public void AddBatch(ReadOnlySpan<EntryData> entries) { Count += entries.Length; foreach (var e in entries) if (e.Name == "payload.bin") Payload = e; }
        public void ReportIssue(string message) => throw new InvalidDataException(message);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Rig Open(string path, string root, bool zip, int members)
    {
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
        ResourceProvider provider = zip ? new ZipProvider(root) : new ArchiveProvider(root, registry);
        var location = zip ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
        var sink = new Sink(); provider.EnumerateAsync(location, sink, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(members + 1, sink.Count); Assert.NotNull(sink.Payload);
        var content = Assert.IsType<ProgressiveContent>(provider.OpenContent(provider.GetItemRef(location, sink.Payload.Value)));
        var cache = (IEnumerable)provider.GetType().GetField("_cache", Fields)!.GetValue(provider)!;
        object pair = Assert.Single(cache.Cast<object>());
        object index = pair.GetType().GetProperty("Value")!.GetValue(pair)!;
        long estimate = provider is ZipProvider z ? z.RetainedIndexBytes : ((ArchiveProvider)provider).RetainedIndexBytes;
        Assert.True(estimate > 0); Assert.Equal(Large, content.Length);
        return new Rig(provider, content, new WeakReference(index), estimate);
    }
    private static void Release(ResourceProvider provider, string path)
    { if (provider is ZipProvider z) z.Release(path); else ((ArchiveProvider)provider).Release(path); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect()
    { for (int i = 0; i < 4; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); } }
    private static string FileHash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)); }
    private static bool Exclusive(string path)
    { try { using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; } catch (IOException) { return false; } }
    private static void ReadAllKnownBytes(IContentSource content)
    {
        byte[] buffer = new byte[65536]; long at = 0;
        while (at < Large)
        {
            int n = content.Read(at, buffer.AsSpan(0, (int)Math.Min(buffer.Length, Large - at)));
            Assert.True(n > 0);
            for (int i = 0; i < n; i++) if (buffer[i] != (byte)((at + i) * 31 + 17)) Assert.Fail($"Unexpected owned member byte at {at + i}.");
            at += n;
        }
        Assert.Equal(Large, at); Assert.Equal(0, content.Read(at, buffer));
    }
    private static void Make(string path, bool zip, int members)
    {
        byte[] buffer = Enumerable.Range(0, 65536).Select(i => (byte)(i * 31 + 17)).ToArray();
        if (zip)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            for (int i = 0; i < members; i++) archive.CreateEntry($"member-{i:D7}.bin", CompressionLevel.NoCompression);
            using var stream = archive.CreateEntry("payload.bin", CompressionLevel.Fastest).Open();
            for (long at = 0; at < Large; at += buffer.Length) stream.Write(buffer.AsSpan(0, (int)Math.Min(buffer.Length, Large - at)));
        }
        else
        {
            using var file = File.Create(path); using var writer = new TarWriter(file, TarEntryFormat.Ustar);
            for (int i = 0; i < members; i++) writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, $"member-{i:D7}.bin") { ModificationTime = DateTimeOffset.UnixEpoch });
            using var payload = new PatternStream(Large);
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "payload.bin") { ModificationTime = DateTimeOffset.UnixEpoch, DataStream = payload });
        }
    }
    private sealed class PatternStream(long length) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => length; public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) { int n = (int)Math.Min(buffer.Length, length - Position); for (int i = 0; i < n; i++) buffer[i] = (byte)((Position + i) * 31 + 17); Position += n; return n; }
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, SeekOrigin.End => length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class Counters { public int Closes, Callbacks; }
    private sealed class Payload(string path, byte[] bytes, Counters counts, Exception? sourceError, Exception? callbackError)
    {
        private readonly byte[] _bytes = bytes;
        public Stream Open() { Assert.Equal(File.ReadAllBytes(path), _bytes); return new CloseFaultStream(File.OpenRead(path), counts, sourceError); }
        public void Closed() { counts.Callbacks++; GC.KeepAlive(_bytes); if (callbackError is not null) throw callbackError; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ProgressiveContent, WeakReference, Counters, Exception, Exception) Owned(string path, byte[] bytes, string fault)
    {
        var counts = new Counters(); var source = new IOException("owned progressive source close"); var callback = new IOException("owned progressive callback close");
        var payload = new Payload(path, bytes.ToArray(), counts, fault is "source" or "both" ? source : null, fault is "callback" or "both" ? callback : null);
        return (new ProgressiveContent("owned bytes", new object(), payload.Open, MemberLimits.Of(bytes.Length, bytes.Length, bytes.Length, 1000), bytes.Length, Path.GetDirectoryName(path)!, payload.Closed), new WeakReference(payload), counts, source, callback);
    }
    private sealed class CloseFaultStream(FileStream inner, Counters counts, Exception? error) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer); public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { counts.Closes++; inner.Dispose(); if (error is not null) throw error; } base.Dispose(disposing); }
    }
}
