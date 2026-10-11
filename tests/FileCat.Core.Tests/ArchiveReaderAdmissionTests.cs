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
public sealed class ArchiveReaderAdmissionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool zip in new[] { false, true })
        foreach (int members in new[] { 64, 5000 })
        foreach (int callers in new[] { 2, 8 })
        foreach (bool identityAvailable in new[] { false, true }) yield return [zip, members, callers, identityAvailable];
    }
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    [Theory]
    [MemberData(nameof(Cases))]
    public void Concurrent_content_admission_keeps_each_index_alive_until_reader_acquisition(bool zip, int members, int callers, bool identityAvailable)
    {
        string root = Directory.CreateTempSubdirectory("filecat-archive-reader-admission-").FullName;
        string path = Path.Combine(root, zip ? "owned.zip" : "owned.tar");
        byte[] expected = Enumerable.Range(0, 4096).Select(i => (byte)(i * 37 + 23)).ToArray();
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
        var files = new IdentityAwareTestFiles();
        ResourceProvider provider = zip ? new ZipProvider(root) : new ArchiveProvider(root, registry);
        var indexes = new ConcurrentBag<object>(); var observations = new ConcurrentQueue<object>();
        void Capture()
        {
            object cache = provider.GetType().GetField("_cache", Flags)!.GetValue(provider)!;
            foreach (object index in (System.Collections.IEnumerable)cache.GetType().GetProperty("Values")!.GetValue(cache)!) indexes.Add(index);
        }
        provider.GetType().GetProperty("GetSourceIdentity")!.SetValue(provider, (Func<string, string?>)(p => { Capture(); return identityAvailable ? files.GetFileIdentity(p) : null; }));
        var location = zip ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
        int failures = 0, reads = 0; string before = "";
        try
        {
            if (zip)
            {
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                for (int i = 0; i < members; i++) archive.CreateEntry($"empty-{i:D7}", CompressionLevel.NoCompression);
                using var content = archive.CreateEntry("payload.bin", CompressionLevel.NoCompression).Open(); content.Write(expected);
            }
            else
            {
                using var stream = File.Create(path); using var writer = new TarWriter(stream, TarEntryFormat.Ustar);
                for (int i = 0; i < members; i++) writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, $"empty-{i:D7}") { ModificationTime = DateTimeOffset.UnixEpoch });
                using var content = new MemoryStream(expected);
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "payload.bin") { DataStream = content, ModificationTime = DateTimeOffset.UnixEpoch });
            }
            before = Hash(File.ReadAllBytes(path));
            for (int round = 0; round < 8; round++)
            {
                using var ready = new CountdownEvent(callers); using var start = new ManualResetEventSlim();
                var threads = new List<Thread>();
                for (int caller = 0; caller < callers; caller++)
                {
                    int r = round, c = caller;
                    var thread = new Thread(() =>
                    {
                        ready.Signal(); start.Wait();
                        try
                        {
                            using var source = provider.OpenContent(new ItemRef(location, "payload.bin", EntryKind.File))!;
                            byte[] actual = new byte[4097]; int offset = 0;
                            while (offset < actual.Length) { int count = source.Read(offset, actual.AsSpan(offset)); if (count == 0) break; offset += count; }
                            actual = actual[..offset]; Interlocked.Increment(ref reads);
                            observations.Enqueue(new { round = r, caller = c, Bytes = Convert.ToBase64String(actual), Error = (string?)null });
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failures);
                            observations.Enqueue(new { round = r, caller = c, Bytes = (string?)null, Error = ex.GetType().FullName + ": " + ex.Message });
                        }
                    }) { IsBackground = true };
                    threads.Add(thread); thread.Start();
                }
                try { Assert.True(ready.Wait(TimeSpan.FromSeconds(15))); start.Set(); }
                finally { start.Set(); foreach (var thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(30))); }
            }
            Capture();
            int cached = provider is ZipProvider zp ? zp.RetainedIndexes : ((ArchiveProvider)provider).RetainedIndexes;
            if (provider is ZipProvider z) z.Release(path); else ((ArchiveProvider)provider).Release(path);
            var distinct = indexes.Distinct(ReferenceEqualityComparer.Instance).ToArray();
            var handles = distinct.Select(index => zip ? (FileStream)index.GetType().GetField("_stream", Flags)!.GetValue(index)! :
                (FileStream?)index.GetType().GetField("_reader", Flags)!.GetValue(index)!.GetType().GetField("_plain", Flags)!.GetValue(index.GetType().GetField("_reader", Flags)!.GetValue(index)!)).OfType<FileStream>().ToArray();
            bool closed = handles.All(source => source.SafeFileHandle.IsClosed && !source.CanRead);
            bool unchanged = before == Hash(File.ReadAllBytes(path));
            output.WriteLine(JsonSerializer.Serialize(new { zip, members, callers, identityAvailable, rounds = 8, failures, reads, cached, distinctIndexes = distinct.Length, handles = handles.Length, closed, unchanged, before, Expected = Convert.ToBase64String(expected), Observations = observations.ToArray(), noForcedGC = true, noInjectedProductHook = true }));
            Assert.True(closed); Assert.True(unchanged); Assert.Equal(1, cached); Assert.Equal(8 * callers, reads); Assert.Equal(0, failures);
            foreach (var observation in observations)
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(observation));
                Assert.Equal(expected, Convert.FromBase64String(json.RootElement.GetProperty("Bytes").GetString()!));
            }
            GC.KeepAlive(distinct);
        }
        finally
        {
            if (provider is ZipProvider z) z.Release(path); else ((ArchiveProvider)provider).Release(path);
            foreach (var index in indexes.OfType<IDisposable>()) index.Dispose();
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { root, Absent = !Directory.Exists(root) }));
        }
    }
}
