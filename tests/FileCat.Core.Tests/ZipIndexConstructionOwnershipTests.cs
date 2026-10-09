using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

[Collection("Native archive volume sharing")]
public sealed class ZipIndexConstructionOwnershipTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string damage in new[] { "header", "signature", "count" })
        foreach (bool openContent in new[] { false, true })
        foreach (int repetitions in new[] { 1, 4, 16 })
            yield return [damage, openContent, repetitions];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_failed_zip_index_build_releases_its_native_source(string damage, bool openContent, int repetitions)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "This control requires native Windows FileShare.None to observe source retirement.");
        string root = Directory.CreateTempSubdirectory("filecat-zip-build-").FullName;
        string good = Path.Combine(root, "good.zip"), bad = Path.Combine(root, "damaged.zip");
        byte[] expected = Enumerable.Range(0, 4096).Select(i => (byte)(i * 17 + 11)).ToArray();
        var provider = new ZipProvider(Path.Combine(root, "spools"));
        bool noGC = false;
        try
        {
            using (var zip = ZipFile.Open(good, ZipArchiveMode.Create))
            using (var member = zip.CreateEntry("payload.bin", CompressionLevel.NoCompression).Open()) member.Write(expected);
            ReadPositive(provider, good, expected);
            Assert.False(Exclusive(good)); provider.Release(good); Assert.True(Exclusive(good));
            byte[] bytes = File.ReadAllBytes(good);
            int central = Find(bytes, [0x50, 0x4b, 1, 2]);
            int end = Find(bytes, [0x50, 0x4b, 5, 6]);
            Assert.True(central > 0 && end > central);
            if (damage == "header") Array.Clear(bytes);
            else if (damage == "signature") bytes[central + 3] = 3;
            else { bytes[end + 8]++; bytes[end + 10]++; }
            File.WriteAllBytes(bad, bytes); string hash = Hash(bytes);
            bool constructed = false; Exception? directoryFailure = null;
            try
            {
                using var stream = File.OpenRead(bad);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                constructed = true; _ = zip.Entries.Count;
            }
            catch (Exception ex) { directoryFailure = ex; }
            Assert.IsType<InvalidDataException>(directoryFailure);
            Assert.Equal(damage != "header", constructed); Assert.True(Exclusive(bad));
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            noGC = GC.TryStartNoGCRegion(32L << 20);
            Assert.True(noGC, "The isolated ownership control requires a bounded no-GC observation interval.");
            int[] beforeGC = Counts();
            var observations = new List<object>(); bool allClosed = true;
            for (int i = 0; i < repetitions; i++)
            {
                Exception? failure = Record.Exception(() =>
                {
                    var location = ZipProvider.ForFile(bad);
                    if (openContent) provider.OpenContent(new ItemRef(location, "payload.bin", EntryKind.File))?.Dispose();
                    else provider.EnumerateAsync(location, new Sink(), CancellationToken.None).GetAwaiter().GetResult();
                });
                bool closed = Exclusive(bad); allClosed &= closed;
                observations.Add(new { iteration = i, closed, errorType = failure?.GetType().FullName, errorMessage = failure?.Message, cached = provider.RetainedIndexes });
                Assert.IsType<InvalidDataException>(failure); Assert.Equal(directoryFailure!.Message, failure!.Message); Assert.Equal(0, provider.RetainedIndexes);
            }
            int[] afterGC = Counts();
            GC.EndNoGCRegion(); noGC = false;
            bool unchanged = File.ReadAllBytes(bad).SequenceEqual(bytes);
            ReadPositive(provider, good, expected); provider.Release(good);
            output.WriteLine("ZIP_BUILD_OWNERSHIP " + JsonSerializer.Serialize(new
            {
                damage, openContent, repetitions, constructedBeforeDirectoryFailure = constructed, observations,
                exactBytes = bytes.Length, originalSHA256 = hash, unchanged,
                healthyMemberBytes = expected.Length, healthyMemberSHA256 = Hash(expected),
                healthyCacheRemainsReadableAndReleases = Exclusive(good),
                collectionCountsBefore = beforeGC, collectionCountsAfter = afterGC,
                nativeWindowsSharing = true, boundedNoGCObservation = true, root,
            }));
            Assert.Equal(beforeGC, afterGC); Assert.True(unchanged); Assert.True(allClosed, "Failed index construction must close its source before GC.");
        }
        finally
        {
            if (noGC) GC.EndNoGCRegion();
            provider.Release(good); provider.Release(bad);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ReadPositive(ZipProvider provider, string path, byte[] expected)
    {
        using var content = provider.OpenContent(new ItemRef(ZipProvider.ForFile(path), "payload.bin", EntryKind.File))!;
        byte[] read = new byte[expected.Length]; int offset = 0;
        while (offset < read.Length) { int n = content.Read(offset, read.AsSpan(offset)); if (n == 0) break; offset += n; }
        Assert.Equal(expected.Length, offset); Assert.Equal(expected, read);
    }
    private static int[] Counts() => [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static int Find(byte[] bytes, byte[] signature)
    {
        for (int i = 0; i <= bytes.Length - signature.Length; i++) if (bytes.AsSpan(i, signature.Length).SequenceEqual(signature)) return i;
        return -1;
    }
    private static bool Exclusive(string path)
    {
        try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    private sealed class Sink : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) { }
        public void ReportIssue(string message) { }
    }
}
