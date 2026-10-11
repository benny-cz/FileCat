using System.Collections;
using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

[Collection("Native archive volume sharing")]
public sealed class ArchiveSpoolAdmissionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool innerZip in new[] { false, true })
        foreach (bool identity in new[] { false, true })
        foreach (bool pressure in new[] { false, true }) yield return [innerZip, identity, pressure];
    }
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static byte[] ReadShared(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream(); file.CopyTo(bytes); return bytes.ToArray();
    }
    private static byte[] Read(IContentSource source)
    {
        byte[] bytes = new byte[4097]; int offset = 0;
        while (offset < bytes.Length) { int n = source.Read(offset, bytes.AsSpan(offset)); if (n == 0) break; offset += n; }
        return bytes[..offset];
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static byte[] Container(bool zip, Dictionary<string, byte[]> entries)
    {
        using var result = new MemoryStream();
        if (zip)
        {
            using (var archive = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var (name, bytes) in entries) { using var stream = archive.CreateEntry(name, CompressionLevel.NoCompression).Open(); stream.Write(bytes); }
        }
        else
        {
            using var writer = new TarWriter(result, TarEntryFormat.Ustar, leaveOpen: true);
            foreach (var (name, bytes) in entries)
            {
                using var stream = new MemoryStream(bytes);
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = stream, ModificationTime = DateTimeOffset.UnixEpoch });
            }
        }
        return result.ToArray();
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void Eviction_keeps_a_nested_source_alive_during_reader_admission(bool innerZip, bool identity, bool pressure)
    {
        string root = Directory.CreateTempSubdirectory("filecat-spool-admission-").FullName;
        string outer = Path.Combine(root, innerZip ? "outer.tar" : "outer.zip");
        byte[] expected = Enumerable.Range(0, 4096).Select(i => (byte)(i * 19 + 47)).ToArray();
        byte[] inside = Container(innerZip, new() { ["payload.bin"] = expected });
        string suffix = innerZip ? "zip" : "tar";
        byte[] whole = Container(!innerZip, Enumerable.Range(0, 5).ToDictionary(i => $"nested-{i}.{suffix}", _ => inside));
        File.WriteAllBytes(outer, whole);
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
        var files = new IdentityAwareTestFiles(); var zp = new ZipProvider(root); var ap = new ArchiveProvider(root, registry);
        registry.Register(zp); registry.Register(ap); zp.OtherArchives = ap; zp.SpoolForeignMember = ap.Spool;
        zp.GetSourceIdentity = ap.GetSourceIdentity = path => identity ? files.GetFileIdentity(path) : null;
        zp.GetOpenedSourceIdentity = ap.GetOpenedSourceIdentity = handle => identity ? files.GetHandleIdentity(handle) : null;
        ResourceProvider provider = innerZip ? zp : ap;
        Location outerLocation = innerZip ? ArchiveProvider.ForFile(outer) : ZipProvider.ForFile(outer);
        Location Member(int n) => outerLocation.WithPath($"nested-{n}.{suffix}");
        Location Nested(int n) => new(innerZip ? Schemes.Zip : Schemes.Archive, string.Empty, Member(n));
        var spools = (ConcurrentDictionary<string, ArchiveSpool>)typeof(ArchiveProvider).GetField("_spools", Flags)!.GetValue(ap)!;
        object spoolLock = typeof(ArchiveProvider).GetField("_spoolLock", Flags)!.GetValue(ap)!;
        object cacheLock = provider.GetType().GetField("_cacheLock", Flags)!.GetValue(provider)!;
        byte[] actual = [], retry = [], originalSpool = []; Exception? error = null;
        string oldPath = ""; bool copied = false, blocked = false, afterPressure = false, unchanged = false, joined = false, oldest = false;
        int retained = 0; FileStream? originalHolder = null;
        var reader = new Thread(() =>
        {
            try { using var content = provider.OpenContent(new ItemRef(Nested(0), "payload.bin", EntryKind.File))!; actual = Read(content); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true, Name = "FileCat owned spool-admission reader" };
        try
        {
            Monitor.Enter(cacheLock);
            try
            {
                reader.Start();
                copied = SpinWait.SpinUntil(() =>
                {
                    if (spools.Count != 1 || !Monitor.TryEnter(spoolLock)) return false;
                    try { var spool = spools.Values.Single(); oldPath = spool.Path; originalHolder = spool.Holder; originalSpool = ReadShared(oldPath); return originalSpool.SequenceEqual(inside); }
                    finally { Monitor.Exit(spoolLock); }
                }, TimeSpan.FromSeconds(15));
                Assert.True(copied, "The actual outer-member copy must complete before pressure.");
                blocked = SpinWait.SpinUntil(() => reader.IsAlive && (reader.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5));
                Assert.True(blocked, "The real reader must be blocked on the owned admission lock.");
                if (pressure)
                {
                    var first = spools.Values.Single();
                    Assert.True(SpinWait.SpinUntil(() => DateTime.UtcNow > first.Used, 1000));
                    for (int i = 1; i < 4; i++) Assert.Equal(inside, ReadShared(ap.Spool(Member(i))));
                    oldest = spools.Values.Where(v => v.Path != oldPath).All(v => v.Used > first.Used); Assert.True(oldest);
                    Assert.Equal(inside, ReadShared(ap.Spool(Member(4))));
                }
                retained = spools.Count; afterPressure = File.Exists(oldPath);
            }
            finally { Monitor.Exit(cacheLock); }
            joined = reader.Join(TimeSpan.FromSeconds(15)); Assert.True(joined);
            using (var content = provider.OpenContent(new ItemRef(Nested(4), "payload.bin", EntryKind.File))!) retry = Read(content);
            unchanged = ReadShared(outer).SequenceEqual(whole);
        }
        finally
        {
            Assert.False(reader.IsAlive, "Owned reader must retire before fixture cleanup.");
            foreach (object owner in new object[] { zp, ap })
            {
                var cache = (IDictionary)owner.GetType().GetField("_cache", Flags)!.GetValue(owner)!;
                foreach (var index in cache.Values.OfType<IDisposable>()) index.Dispose();
            }
            foreach (var spool in spools.Values) spool.Dispose(); originalHolder?.Dispose();
            Directory.Delete(root, recursive: true);
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            InnerZip = innerZip, SourceIdentityAvailable = identity, EvictionPressure = pressure,
            ActualReaderBlockedAfterCompleteSpool = copied && blocked, ActualOriginalWasOldest = oldest,
            ActualOriginalSpoolPath = oldPath, ActualOriginalSpoolBytesBase64 = Convert.ToBase64String(originalSpool),
            ActualWholeOuterBytesBase64 = Convert.ToBase64String(whole), ActualWholeOuterUnchanged = unchanged,
            ActualOriginalSpoolExistsWhileAdmissionBlocked = afterPressure, ActualRetainedSpoolCountAfterPressure = retained,
            ActualReadErrorType = error?.GetType().FullName, ActualReadErrorMessage = error?.Message, ActualReadErrorStack = error?.StackTrace,
            ActualReadBytesBase64 = Convert.ToBase64String(actual), ActualRetryBytesBase64 = Convert.ToBase64String(retry),
            ActualExpectedBytes = expected.Length, ActualExpectedSHA256 = Hash(expected), ActualReaderRetired = joined,
            ActualOwnedRoot = root, ActualOwnedRootGone = !Directory.Exists(root), ActualOriginalHolderClosed = originalHolder!.SafeFileHandle.IsClosed,
            ControlledCacheLockAdmissionAndActualSpoolEviction = true, NoNativeAtomicOrPhysicalSourceClaim = true
        }));
        Assert.Null(error); Assert.True(afterPressure); Assert.Equal(expected, actual); Assert.Equal(expected, retry);
        Assert.True(unchanged && joined && !Directory.Exists(root) && originalHolder!.SafeFileHandle.IsClosed);
    }
}
