using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Recovery;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

[Collection("Native archive volume sharing")]
public sealed partial class OpenedSourceBindingTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string kind in new[] { "zip", "tar", "image" })
        foreach (string capability in new[] { "both", "none", "path-only" })
        foreach (bool changed in new[] { false, true }) yield return [kind, capability, changed];
    }
    private static byte[] Payload(int seed) => Enumerable.Range(0, 4096).Select(i => (byte)(i * 29 + seed)).ToArray();
    private static byte[] Image(int seed)
    {
        var b = new byte[32768]; b[0] = 0xEB; b[1] = 0x3C; b[2] = 0x90;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(11), 512); b[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(14), 1); b[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(17), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(19), 64); b[21] = 0xF8;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(22), 1); b[38] = 0x29;
        Encoding.ASCII.GetBytes("OWNED_LEASE").CopyTo(b, 43); b[510] = 0x55; b[511] = 0xAA;
        Encoding.ASCII.GetBytes("LEASE   BIN").CopyTo(b, 1536); b[1536] = 0xE5; b[1547] = 0x20;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1562), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(1564), 4096);
        Payload(seed).CopyTo(b, 2048); return b;
    }
    private static void Write(string path, string kind, int seed)
    {
        if (kind == "image") { File.WriteAllBytes(path, Image(seed)); return; }
        if (kind == "zip")
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var member = zip.CreateEntry("payload.bin", CompressionLevel.NoCompression).Open();
            member.Write(Payload(seed)); return;
        }
        using var file = File.Create(path); using var writer = new TarWriter(file, TarEntryFormat.Ustar);
        using var data = new MemoryStream(Payload(seed));
        writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "payload.bin") { DataStream = data, ModificationTime = DateTimeOffset.UnixEpoch });
    }
    private static unsafe string? HandleIdentity(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) return UnixFiles.Stat(handle)?.Identity.ToString();
        if (!ReadIdentity(handle, 18, out IdentityInfo info, (uint)sizeof(IdentityInfo))) return null;
        return new FileIdentity(info.Volume, Convert.ToHexString(new ReadOnlySpan<byte>(info.Id, 16))).ToString();
    }
    private unsafe struct IdentityInfo { public ulong Volume; public fixed byte Id[16]; }
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadIdentity(SafeFileHandle handle, int kind, out IdentityInfo info, uint size);
    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Rows { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> rows) => Rows.AddRange(rows.ToArray());
        public void ReportIssue(string message) { }
    }
    private static byte[] Read(IContentSource source)
    {
        byte[] b = new byte[4097]; int done = 0;
        while (done < b.Length) { int n = source.Read(done, b.AsSpan(done)); if (n == 0) break; done += n; }
        return b[..done];
    }
    private static bool Exclusive(string path)
    {
        try { using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Fresh_source_admission_binds_observed_path_identity_to_the_actual_open_handle(string kind, string capability, bool changed)
    {
        string root = Directory.CreateTempSubdirectory("filecat-opened-source-binding-").FullName;
        string path = Path.Combine(root, "source." + kind), next = Path.Combine(root, "next." + kind), moved = Path.Combine(root, "original." + kind);
        Write(path, kind, 17); Write(next, kind, 97);
        var stamp = new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp); File.SetLastWriteTimeUtc(next, stamp);
        byte[] original = File.ReadAllBytes(path), replacement = File.ReadAllBytes(next);
        var files = new IdentityAwareTestFiles(); var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
        ResourceProvider provider = kind == "zip" ? new ZipProvider(root) : kind == "tar" ? new ArchiveProvider(root, registry) : new RecoveryProvider();
        var location = kind == "zip" ? ZipProvider.ForFile(path) : kind == "tar" ? ArchiveProvider.ForFile(path) : RecoveryProvider.ForImage(path, 1);
        using var observed = new ManualResetEventSlim(); using var swapped = new ManualResetEventSlim();
        string? workerError = null; int calls = 0, handleCalls = 0; bool started = false;
        var worker = new Thread(() =>
        {
            try
            {
                if (!observed.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("No path observation");
                if (changed) { File.Move(path, moved); File.Move(next, path); }
            }
            catch (Exception ex) { workerError = ex.ToString(); }
            finally { swapped.Set(); }
        }) { IsBackground = true };
        string originalId = Assert.IsType<string>(files.GetFileIdentity(path)), nextId = Assert.IsType<string>(files.GetFileIdentity(next));
        Func<string, string?> identity = p =>
        {
            string? current = capability == "none" ? null : files.GetFileIdentity(p);
            if (Interlocked.Increment(ref calls) == 1)
            {
                observed.Set(); if (!swapped.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Owned replacement did not finish");
            }
            return current;
        };
        provider.GetType().GetProperty("GetSourceIdentity")!.SetValue(provider, identity);
        provider.GetType().GetProperty("GetOpenedSourceIdentity")?.SetValue(provider, (Func<SafeFileHandle, string?>)(h =>
        {
            Interlocked.Increment(ref handleCalls); return capability == "both" ? HandleIdentity(h) : null;
        }));
        async Task<byte[]> Content()
        {
            ItemRef item;
            if (kind == "image")
            {
                var sink = new Sink(); await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
                item = provider.GetItemRef(location, Assert.Single(sink.Rows));
            }
            else item = new ItemRef(location, "payload.bin", EntryKind.File);
            using var source = provider.OpenContent(item)!; return Read(source);
        }
        void Close() { if (provider is ZipProvider z) z.Release(path); else if (provider is ArchiveProvider a) a.Release(path); else ((RecoveryProvider)provider).CloseAll(); }
        byte[]? first = null, retry = null; string? error = null; bool refused = false;
        try
        {
            Assert.NotEqual(originalId, nextId); Assert.Equal(original.Length, replacement.Length);
            Assert.Equal(File.GetLastWriteTimeUtc(path), File.GetLastWriteTimeUtc(next));
            using (var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                Assert.Equal(originalId, HandleIdentity(handle));
            worker.Start(); started = true;
            try { first = await Content(); }
            catch (IOException ex) { refused = true; error = ex.GetType().FullName + ": " + ex.Message; }
            Assert.True(worker.Join(TimeSpan.FromSeconds(15)));
            bool firstClosed = Exclusive(path) && (!changed || Exclusive(moved));
            if (capability != "path-only") retry = await Content();
            Close();
            bool unchanged = File.ReadAllBytes(changed ? moved : path).SequenceEqual(original) && File.ReadAllBytes(changed ? path : next).SequenceEqual(replacement);
            output.WriteLine(JsonSerializer.Serialize(new { kind, capability, changed, originalId, nextId, CurrentIdentity = files.GetFileIdentity(path), calls, handleCalls,
                refused, error, First = first is null ? null : Convert.ToBase64String(first), Retry = retry is null ? null : Convert.ToBase64String(retry),
                firstClosed, unchanged, workerError, Original = Convert.ToBase64String(original), Replacement = Convert.ToBase64String(replacement),
                noInjectedProductHook = true, nativeIdentityControl = true }));
            Assert.Null(workerError); Assert.True(unchanged);
            bool mustRefuse = capability == "path-only" || capability == "both" && changed;
            Assert.Equal(mustRefuse, refused);
            if (mustRefuse) { Assert.Null(first); Assert.True(firstClosed); }
            else Assert.Equal(Payload(changed ? 97 : 17), first);
            if (retry is not null) Assert.Equal(Payload(changed ? 97 : 17), retry);
        }
        finally
        {
            observed.Set(); if (started) Assert.True(worker.Join(TimeSpan.FromSeconds(15)));
            Close();
            bool sourcesClosed = new[] { path, next, moved }.Where(File.Exists).All(Exclusive);
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { root, sourcesClosed, Absent = !Directory.Exists(root) }));
            Assert.True(sourcesClosed);
        }
    }
}
