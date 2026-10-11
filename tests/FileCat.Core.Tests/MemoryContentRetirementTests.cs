using System.Formats.Tar;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class MemoryContentRetirementTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string route in new[] { "direct", "paged", "archive" })
        foreach (int size in new[] { 4096, 4 * 1024 * 1024 })
        foreach (bool retired in new[] { false, true }) yield return [route, size, retired];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Retired_memory_content_releases_only_its_own_payload_reference(string route, int size, bool retired)
    {
        using var root = new TempDir();
        var rig = Create(root, route, size);
        string originalHash = Hash(ReadAll(rig.Source));
        Assert.Equal(Hash(Pattern(size)), originalHash);
        string? pagedHash = rig.Reader is null ? null : Hash(ReadAll(rig.Reader));
        if (pagedHash is not null) Assert.Equal(originalHash, pagedHash);
        if (retired) rig.Retire();
        bool alive = AliveAfterCollection(rig.Payload);
        string? liveHash = retired ? null : Hash(ReadAll(rig.Source));
        bool refused = Record.Exception(() => rig.Source.Read(0, new byte[1])) is ObjectDisposedException;
        output.WriteLine(JsonSerializer.Serialize(new { route, size, retired, OriginalHash = originalHash,
            LiveHash = liveHash, PayloadAlive = alive, ReadRefused = refused,
            SourceType = rig.Source.GetType().FullName, LengthAfter = rig.Source.Length,
            KnownBytesHash = Hash(Pattern(size)), PagedHash = pagedHash, CachedPagesAfter = rig.Reader?.CachedPages, OwnedArchiveClosedBeforeCollection = route == "archive" }));
        try
        {
            Assert.Equal(!retired, alive);
            Assert.Equal(retired, refused);
            if (!retired) Assert.Equal(originalHash, liveHash);
            Assert.Equal(size, rig.Source.Length);
            if (retired && rig.Reader is not null) Assert.Equal(0, rig.Reader.CachedPages);
        }
        finally { rig.Retire(); GC.KeepAlive(rig); }
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(4 * 1024 * 1024)]
    public void Disposing_one_memory_wrapper_preserves_caller_bytes_and_an_independent_live_wrapper(int size)
    {
        byte[] caller = Pattern(size);
        using var live = new MemoryContentSource("borrowed live", caller);
        var retired = new MemoryContentSource("borrowed retired", caller);
        retired.Dispose(); retired.Dispose();
        string expected = Hash(Pattern(size)), callerHash = Hash(caller), liveHash = Hash(ReadAll(live));
        bool refused = Record.Exception(() => retired.Read(0, new byte[1])) is ObjectDisposedException;
        output.WriteLine(JsonSerializer.Serialize(new { route = "caller-borrowed", size, ExpectedHash = expected,
            CallerHash = callerHash, LiveHash = liveHash, RetiredReadRefused = refused }));
        Assert.Equal(expected, callerHash); Assert.Equal(expected, liveHash); Assert.True(refused);
        GC.KeepAlive(caller); GC.KeepAlive(retired);
    }

    private static byte[] Pattern(int size)
    {
        var bytes = new byte[size];
        for (int i = 0; i < size; i++) bytes[i] = (byte)((i * 31 + (i >> 8) * 17 + 113) & 255);
        return bytes;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] ReadAll(IContentSource source)
    {
        var bytes = new byte[checked((int)source.Length)]; int at = 0;
        while (at < bytes.Length) { int n = source.Read(at, bytes.AsSpan(at)); Assert.True(n > 0); at += n; }
        Assert.Equal(0, source.Read(bytes.Length, new byte[1])); return bytes;
    }
    private static byte[] ReadAll(PagedReader reader)
    {
        var bytes = new byte[checked((int)reader.Length)];
        Assert.Equal(bytes.Length, reader.Read(0, bytes));
        Assert.Equal(0, reader.Read(bytes.Length, new byte[1])); return bytes;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Rig Create(TempDir root, string route, int size)
    {
        IContentSource source;
        if (route == "archive")
        {
            string path = Path.Combine(root.Path, "owned.tar");
            using (var file = File.Create(path))
            using (var writer = new TarWriter(file))
            using (var bytes = new MemoryStream(Pattern(size)))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "member.bin") { DataStream = bytes });
            var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
            var archive = new ArchiveProvider(root.Dir("spool"), providers); providers.Register(archive);
            var location = ArchiveProvider.ForFile(path); var sink = new Sink();
            archive.EnumerateAsync(location, sink, CancellationToken.None).GetAwaiter().GetResult();
            source = archive.OpenContent(archive.GetItemRef(location, sink.Entries.Single(x => x.Name == "member.bin")))!;
            Assert.IsType<MemoryContentSource>(source); archive.Release(path);
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        else source = new MemoryContentSource("owned memory", Pattern(size));
        var array = Assert.Single(typeof(MemoryContentSource).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(f => f.GetValue(source)).OfType<byte[]>());
        return new Rig(source, route == "paged" ? new PagedReader(source) : null, new WeakReference<byte[]>(array));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool AliveAfterCollection(WeakReference<byte[]> payload)
    {
        for (int i = 0; i < 8; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        return payload.TryGetTarget(out _);
    }
    private sealed class Rig(IContentSource source, PagedReader? reader, WeakReference<byte[]> payload)
    {
        public IContentSource Source { get; } = source;
        public PagedReader? Reader { get; } = reader;
        public WeakReference<byte[]> Payload { get; } = payload;
        public void Retire() { if (Reader is null) Source.Dispose(); else Reader.Dispose(); }
    }
    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());
        public void ReportIssue(string message) => throw new InvalidDataException(message);
    }
}
