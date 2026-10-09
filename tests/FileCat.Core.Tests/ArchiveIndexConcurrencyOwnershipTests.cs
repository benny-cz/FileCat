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
public sealed class ArchiveIndexConcurrencyOwnershipTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool zip in new[] { false, true })
        foreach (int members in new[] { 64, 5000 })
        foreach (int callers in new[] { 2, 6, 12 }) yield return [zip, members, callers];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Concurrent_requests_share_one_owned_index_and_release_every_source(bool zip, int members, int callers)
    {
        string root = Directory.CreateTempSubdirectory("filecat-index-concurrent-").FullName;
        string path = Path.Combine(root, zip ? "owned.zip" : "owned.tar");
        byte[] expected = Enumerable.Range(0, 4096).Select(i => (byte)(i * 31 + 7)).ToArray();
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
        ResourceProvider Create() => zip ? new ZipProvider(root) : new ArchiveProvider(root, registry);
        void Release(ResourceProvider p) { if (p is ZipProvider z) z.Release(path); else ((ArchiveProvider)p).Release(path); }
        var provider = Create();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        MethodInfo method = provider.GetType().GetMethods(flags).Single(m => m.Name == "GetIndex" && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(zip ? [typeof(Location)] : [typeof(Location), typeof(CancellationToken)]));
        var location = zip ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
        object?[] indexes = new object?[callers]; Exception?[] failures = new Exception?[callers];
        using var ready = new CountdownEvent(callers); using var start = new ManualResetEventSlim(); using var done = new CountdownEvent(callers);
        var threads = new List<Thread>();
        try
        {
            if (zip)
            {
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                for (int i = 0; i < members; i++) archive.CreateEntry($"member-{i:D7}.bin", CompressionLevel.NoCompression);
                using var content = archive.CreateEntry("payload.bin", CompressionLevel.NoCompression).Open(); content.Write(expected);
            }
            else
            {
                using var file = File.Create(path); using var writer = new TarWriter(file, TarEntryFormat.Ustar);
                for (int i = 0; i < members; i++) writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, $"member-{i:D7}.bin") { ModificationTime = DateTimeOffset.UnixEpoch });
                using var data = new MemoryStream(expected); writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "payload.bin") { DataStream = data, ModificationTime = DateTimeOffset.UnixEpoch });
            }
            string wholeHash = Hash(File.ReadAllBytes(path));
            var warm = Create(); Read(warm, location, expected); Release(warm);
            if (OperatingSystem.IsWindows()) Assert.True(Exclusive(path));
            for (int n = 0; n < callers; n++)
            {
                int i = n;
                var thread = new Thread(() =>
                {
                    ready.Signal(); start.Wait();
                    try { indexes[i] = method.Invoke(provider, zip ? [location] : [location, CancellationToken.None]); }
                    catch (Exception ex) { failures[i] = ex is TargetInvocationException t ? t.InnerException : ex; }
                    finally { done.Signal(); }
                }) { IsBackground = true };
                threads.Add(thread); thread.Start();
            }
            Assert.True(ready.Wait(TimeSpan.FromSeconds(15))); start.Set(); Assert.True(done.Wait(TimeSpan.FromSeconds(30)));
            Assert.All(failures, e => Assert.Null(e)); Assert.All(indexes, i => Assert.NotNull(i));
            object[] distinct = indexes.Cast<object>().Distinct(ReferenceEqualityComparer.Instance).ToArray();
            var sink = new Sink(); provider.EnumerateAsync(location, sink, CancellationToken.None).GetAwaiter().GetResult(); Assert.Equal(members + 1, sink.Count);
            Read(provider, location, expected);
            var sources = distinct.Select(index =>
            {
                if (zip) return (FileStream)index.GetType().GetField("_stream", flags)!.GetValue(index)!;
                object reader = index.GetType().GetField("_reader", flags)!.GetValue(index)!;
                return (FileStream?)reader.GetType().GetField("_plain", flags)!.GetValue(reader);
            }).OfType<FileStream>().ToArray();
            int initiallyOpen = sources.Count(s => s.CanRead && !s.SafeFileHandle.IsClosed);
            bool? exclusiveBefore = OperatingSystem.IsWindows() ? Exclusive(path) : null;
            int cached = provider is ZipProvider zp ? zp.RetainedIndexes : ((ArchiveProvider)provider).RetainedIndexes;
            Release(provider);
            int closed = sources.Count(s => s.SafeFileHandle.IsClosed && !s.CanRead);
            bool? exclusiveAfter = OperatingSystem.IsWindows() ? Exclusive(path) : null;
            bool unchanged = Hash(File.ReadAllBytes(path)) == wholeHash;
            output.WriteLine("ARCHIVE_CONCURRENT_OWNERSHIP " + JsonSerializer.Serialize(new
            {
                zip, members, callers, returnedIndexes = indexes.Length, distinctIndexes = distinct.Length, nativeSourcesObserved = sources.Length, initiallyOpen, cached, closed, lazyTarSourcesNotForced = true,
                exclusiveBefore, exclusiveAfter, unchanged, wholeArchiveSHA256 = wholeHash, memberBytes = expected.Length, memberSHA256 = Hash(expected),
                nativeSourceWrappersDeliberatelyHeldBeforeAndAfterRelease = true, actualProviderIndexAdmissionAndPublicListingAndCompleteContentRead = true,
                startGatedThreadsWithoutInternalBuildHookOrInjectedFault = true, noGCClosureOracle = true, root,
            }));
            Assert.True(unchanged); Assert.Equal(sources.Length, initiallyOpen); Assert.Equal(1, cached);
            Assert.Equal(1, distinct.Length); Assert.Equal(sources.Length, closed);
            if (OperatingSystem.IsWindows()) { Assert.False(exclusiveBefore); Assert.True(exclusiveAfter); }
        }
        finally
        {
            start.Set(); foreach (var thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            Release(provider);
            foreach (var index in indexes.OfType<IDisposable>()) index.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
    private static void Read(ResourceProvider p, Location location, byte[] expected)
    {
        using var source = p.OpenContent(new ItemRef(location, "payload.bin", EntryKind.File))!;
        byte[] actual = new byte[expected.Length]; int offset = 0;
        while (offset < actual.Length) { int n = source.Read(offset, actual.AsSpan(offset)); if (n == 0) break; offset += n; }
        Assert.Equal(expected.Length, offset); Assert.Equal(expected, actual);
    }
    private static bool Exclusive(string path)
    {
        try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed class Sink : IEnumerationSink
    {
        public int Count;
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Count += entries.Length;
        public void ReportIssue(string message) { }
    }
}
