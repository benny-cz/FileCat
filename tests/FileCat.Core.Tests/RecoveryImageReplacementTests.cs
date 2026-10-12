using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

[Collection("Recovery image handle ownership")]
public sealed class RecoveryImageReplacementTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string route in new[] { "content", "catalog", "lease", "free-space", "disk-search" })
        foreach (bool changed in new[] { false, true })
        foreach (bool identityAvailable in new[] { false, true }) yield return [route, changed, identityAvailable];
    }
    private static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc);
    private static byte[] Image(int seed, bool otherName)
    {
        var bytes = new byte[64 * 512];
        bytes[0] = 0xEB; bytes[1] = 0x3C; bytes[2] = 0x90;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(11), 512); bytes[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 1); bytes[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(17), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(19), 64); bytes[21] = 0xF8;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1); bytes[38] = 0x29;
        Encoding.ASCII.GetBytes("OWNED_LEASE").CopyTo(bytes, 43);
        bytes[510] = 0x55; bytes[511] = 0xAA;
        Encoding.ASCII.GetBytes(otherName ? "OTHER   BIN" : "LEASE   BIN").CopyTo(bytes, 3 * 512);
        bytes[3 * 512] = 0xE5; bytes[3 * 512 + 11] = 0x20;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(3 * 512 + 26), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(3 * 512 + 28), 4096);
        for (int i = 0; i < 4096; i++) bytes[4 * 512 + i] = (byte)(i * 29 + seed);
        return bytes;
    }
    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Rows { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Rows.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }
    private static byte[] Read(IContentSource source)
    {
        byte[] bytes = new byte[4097]; int done = 0;
        while (done < bytes.Length) { int n = source.Read(done, bytes.AsSpan(done)); if (n == 0) break; done += n; }
        return bytes[..done];
    }
    private static bool Exclusive(string path)
    {
        try { using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
    }

    [Fact]
    public async Task In_place_image_write_with_restored_mtime_refreshes_recovery_catalog()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-in-place-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.img");
        byte[] original = Image(17, false), replacement = Image(97, true);
        File.WriteAllBytes(path, original);
        File.SetLastWriteTimeUtc(path, Stamp);
        var files = new IdentityAwareTestFiles();
        var provider = new RecoveryProvider
        {
            GetSourceIdentity = files.GetFileIdentity,
            GetOpenedSourceIdentity = files.GetHandleIdentity,
        };
        var location = RecoveryProvider.ForImage(path, 1);
        async Task<Sink> List() { var sink = new Sink(); await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken); return sink; }
        try
        {
            string? originalId = files.GetFileIdentity(path);
            string originalName = Assert.Single((await List()).Rows).Name;
            using (var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                RandomAccess.Write(handle, replacement, 0);
                RandomAccess.FlushToDisk(handle);
            }
            File.SetLastWriteTimeUtc(path, Stamp);
            string? currentId = files.GetFileIdentity(path);
            string currentName = Assert.Single((await List()).Rows).Name;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                originalId, currentId, originalName, currentName,
                sameLength = new FileInfo(path).Length == original.Length,
                sameMtime = File.GetLastWriteTimeUtc(path) == Stamp,
                actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                replacementSha256 = Convert.ToHexString(SHA256.HashData(replacement)),
            }));
            Assert.Equal(originalId, currentId);
            Assert.Equal(original.Length, new FileInfo(path).Length);
            Assert.Equal(Stamp, File.GetLastWriteTimeUtc(path));
            Assert.Equal(replacement, File.ReadAllBytes(path));
            Assert.NotEqual(originalName, currentName);
        }
        finally
        {
            provider.CloseAll();
            bool sourceClosed = Exclusive(path);
            File.Delete(path);
            Directory.Delete(root);
            output.WriteLine(JsonSerializer.Serialize(new { root, sourceClosed, absent = !Directory.Exists(root) }));
            Assert.True(sourceClosed);
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public async Task Held_image_reader_refuses_in_place_source_rewrite()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-held-rewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.img");
        byte[] original = Image(17, false), replacement = Image(97, true);
        File.WriteAllBytes(path, original);
        File.SetLastWriteTimeUtc(path, Stamp);
        var files = new IdentityAwareTestFiles();
        var provider = new RecoveryProvider
        {
            GetSourceIdentity = files.GetFileIdentity,
            GetOpenedSourceIdentity = files.GetHandleIdentity,
        };
        var location = RecoveryProvider.ForImage(path, 1);
        IContentSource? held = null;
        try
        {
            var first = new Sink();
            await provider.EnumerateAsync(location, first, TestContext.Current.CancellationToken);
            held = provider.OpenContent(provider.GetItemRef(location, Assert.Single(first.Rows)))!;
            Assert.Equal(original.AsSpan(4 * 512, 4096).ToArray(), Read(held));
            string? identity = files.GetFileIdentity(path);
            using (var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                RandomAccess.Write(handle, replacement, 0);
                RandomAccess.FlushToDisk(handle);
            }
            File.SetLastWriteTimeUtc(path, Stamp);
            byte[]? actual = null;
            Exception? error = Record.Exception(() => actual = Read(held));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                identity, currentIdentity = files.GetFileIdentity(path),
                sameMtime = File.GetLastWriteTimeUtc(path) == Stamp,
                sourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                heldReadSha256 = actual is null ? null : Convert.ToHexString(SHA256.HashData(actual)),
                error = error?.GetType().Name,
            }));
            Assert.IsType<IOException>(error);
            var current = new Sink();
            await provider.EnumerateAsync(location, current, TestContext.Current.CancellationToken);
            using var fresh = provider.OpenContent(provider.GetItemRef(location, Assert.Single(current.Rows)))!;
            Assert.Equal(replacement.AsSpan(4 * 512, 4096).ToArray(), Read(fresh));
            Assert.Equal(identity, files.GetFileIdentity(path));
        }
        finally
        {
            held?.Dispose();
            provider.CloseAll();
            bool sourceClosed = Exclusive(path);
            File.Delete(path);
            Directory.Delete(root);
            output.WriteLine(JsonSerializer.Serialize(new { root, sourceClosed, absent = !Directory.Exists(root) }));
            Assert.True(sourceClosed);
            Assert.False(Directory.Exists(root));
        }
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Same_metadata_image_replacement_refreshes_recovery_content_and_listing(string route, bool changed, bool identityAvailable)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-image-replacement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.img"), next = Path.Combine(root, "next.img"), moved = Path.Combine(root, "original.img");
        byte[] original = Image(17, false), replacement = Image(changed ? 97 : 17, changed && route == "catalog");
        File.WriteAllBytes(path, original); File.WriteAllBytes(next, replacement);
        File.SetLastWriteTimeUtc(path, Stamp); File.SetLastWriteTimeUtc(next, Stamp);
        var files = new IdentityAwareTestFiles(); var provider = new RecoveryProvider();
        provider.GetType().GetProperty("GetSourceIdentity")?.SetValue(provider, (Func<string, string?>)(p => identityAvailable ? files.GetFileIdentity(p) : null));
        provider.GetType().GetProperty("GetOpenedSourceIdentity")!.SetValue(provider, (Func<Microsoft.Win32.SafeHandles.SafeFileHandle, string?>)(handle => identityAvailable ? files.GetHandleIdentity(handle) : null));
        var location = RecoveryProvider.ForImage(path, 1); IContentSource? held = null;
        async Task<Sink> List() { var sink = new Sink(); await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken); return sink; }
        try
        {
            string originalId = Assert.IsType<string>(files.GetFileIdentity(path)), nextId = Assert.IsType<string>(files.GetFileIdentity(next)); Assert.NotEqual(originalId, nextId);
            Assert.Equal(new FileInfo(path).Length, new FileInfo(next).Length); Assert.Equal(File.GetLastWriteTimeUtc(path), File.GetLastWriteTimeUtc(next));
            var first = Assert.Single((await List()).Rows); held = provider.OpenContent(provider.GetItemRef(location, first))!;
            byte[] firstRead = Read(held);
            if (route != "lease") { held.Dispose(); held = null; }
            if (changed) { File.Move(path, moved); File.Move(next, path); }
            if (route == "free-space") provider.SearchFreeSpace(location);
            if (route == "disk-search") provider.SearchDisk(location);
            var current = Assert.Single((await List()).Rows);
            byte[] currentRead;
            using (var source = provider.OpenContent(provider.GetItemRef(location, current))!) currentRead = Read(source);
            byte[]? heldRead = held is null ? null : Read(held);
            bool freeSpaceSearched = provider.DescribeFreeSpaceSearch(location)?.Searched == true;
            bool diskSearched = provider.DescribeDiskSearch(location)?.Searched == true;
            bool sourceUnchanged = File.ReadAllBytes(changed ? moved : path).SequenceEqual(original);
            bool currentUnchanged = File.ReadAllBytes(changed ? path : next).SequenceEqual(replacement);
            output.WriteLine(JsonSerializer.Serialize(new { route, changed, identityAvailable, originalId, nextId, CurrentIdentity = files.GetFileIdentity(path),
                FirstName = first.Name, CurrentName = current.Name, FirstRead = Convert.ToBase64String(firstRead), CurrentRead = Convert.ToBase64String(currentRead),
                HeldRead = heldRead is null ? null : Convert.ToBase64String(heldRead), freeSpaceSearched, diskSearched, sourceUnchanged, currentUnchanged,
                OriginalImage = Convert.ToBase64String(original), ReplacementImage = Convert.ToBase64String(replacement) }));
            Assert.Equal(original.AsSpan(4 * 512, 4096).ToArray(), firstRead);
            Assert.Equal(replacement.AsSpan(4 * 512, 4096).ToArray(), currentRead);
            Assert.Equal(changed && route == "catalog", first.Name != current.Name);
            if (heldRead is not null) Assert.Equal(original.AsSpan(4 * 512, 4096).ToArray(), heldRead);
            if (!changed && route == "free-space") Assert.True(freeSpaceSearched);
            if (!changed && route == "disk-search") Assert.True(diskSearched);
            Assert.True(sourceUnchanged); Assert.True(currentUnchanged);
        }
        finally
        {
            held?.Dispose(); provider.CloseAll();
            bool sourcesClosed = new[] { path, next, moved }.Where(File.Exists).All(Exclusive);
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { root, sourcesClosed, Absent = !Directory.Exists(root) }));
            Assert.True(sourcesClosed); Assert.False(Directory.Exists(root));
            GC.KeepAlive(held); GC.KeepAlive(provider);
        }
    }
}
