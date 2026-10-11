using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Distinct archive sources and case-distinct members must keep their own bytes and cache lifetime.</summary>
public sealed class ArchiveSourceCaseTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> OuterCases()
    {
        foreach (string format in new[] { "zip", "tar" })
        foreach (string route in new[] { "repeat", "content", "release", "refresh", "capability" })
        foreach (bool reverse in new[] { false, true }) yield return [format, route, reverse];
    }

    public static IEnumerable<object[]> NestedCases()
    {
        foreach (string format in new[] { "zip", "tar" })
        foreach (string route in new[] { "repeat", "filename", "folder" })
        foreach (bool reverse in new[] { false, true }) yield return [format, route, reverse];
    }

    private static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc);
    private static byte[] Payload(byte value) => Enumerable.Range(0, 4096).Select(i => (byte)(i * 17 + value)).ToArray();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static byte[] Zip(params (string Name, byte[] Bytes)[] files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var file in files)
            {
                var entry = zip.CreateEntry(file.Name, CompressionLevel.NoCompression);
                entry.LastWriteTime = Stamp;
                using var target = entry.Open();
                target.Write(file.Bytes);
            }
        return buffer.ToArray();
    }

    private static void Write(string path, string format, params (string Name, byte[] Bytes)[] files)
    {
        if (format == "zip") File.WriteAllBytes(path, Zip(files));
        else
        {
            using var target = File.Create(path);
            using var tar = new TarWriter(target, TarEntryFormat.Ustar);
            foreach (var file in files)
            {
                using var data = new MemoryStream(file.Bytes, writable: false);
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, file.Name) { DataStream = data, ModificationTime = Stamp });
            }
        }
        File.SetLastWriteTimeUtc(path, Stamp);
    }

    private sealed class Providers : IDisposable
    {
        public readonly ZipProvider Zip;
        public readonly ArchiveProvider Archive;
        private readonly List<string> _paths = [];
        public Providers(string temp)
        {
            var registry = new ProviderRegistry();
            Zip = new ZipProvider(temp);
            Archive = new ArchiveProvider(temp, registry);
            registry.Register(Zip); registry.Register(Archive);
            Zip.OtherArchives = Archive;
            Zip.SpoolForeignMember = Archive.Spool;
        }
        public ResourceProvider For(string format) => format == "zip" ? Zip : Archive;
        public Location Root(string path, string format)
        {
            _paths.Add(path);
            return format == "zip" ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
        }
        public int Count(string format) => format == "zip" ? Zip.RetainedIndexes : Archive.RetainedIndexes;
        public void Release(string path, string format)
        {
            if (format == "zip") Zip.Release(path); else Archive.Release(path);
        }
        public void Dispose()
        {
            foreach (string path in _paths.Distinct()) { Zip.Release(path); Archive.Release(path); }
        }
    }

    private static byte[] Read(ResourceProvider provider, Location location)
    {
        using var source = provider.OpenContent(new ItemRef(location, "payload.bin", EntryKind.File))!;
        var bytes = new byte[4097]; int done = 0;
        while (done < bytes.Length)
        {
            int n = source.Read(done, bytes.AsSpan(done));
            if (n == 0) break;
            done += n;
        }
        return bytes[..done];
    }

    [Theory]
    [MemberData(nameof(OuterCases))]
    public void Case_distinct_outer_archives_keep_content_capabilities_and_retirement(string format, string route, bool reverse)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-archive-source-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string upper = Path.Combine(root, "SOURCE." + format), lower = Path.Combine(root, "source." + format);
        byte[] a = Payload(11), b = Payload(79);
        using var providers = new Providers(Path.Combine(root, "scratch"));
        try
        {
            Write(upper, format, ("payload.bin", a));
            Write(lower, format, ("payload.bin", b));
            var fs = new IdentityAwareTestFiles();
            string? upperId = fs.GetFileIdentity(upper), lowerId = fs.GetFileIdentity(lower);
            if (upperId == lowerId)
            {
                if (route != "repeat")
                {
                    Assert.Skip("The owned filesystem aliases case-only archive paths; distinct-source controls require a case-sensitive filesystem.");
                    return;
                }
                // Repeat controls stay usable on a filesystem that aliases the two spellings.
                a = b;
            }
            Assert.NotNull(upperId); Assert.NotNull(lowerId);
            string first = reverse ? lower : upper, second = reverse ? upper : lower;
            byte[] firstBytes = reverse ? b : a, secondBytes = reverse ? a : b;
            if (route == "repeat") { second = first; secondBytes = firstBytes; }
            if (route == "capability") File.SetAttributes(first, File.GetAttributes(first) | FileAttributes.ReadOnly);
            byte[] firstContainer = File.ReadAllBytes(first), secondContainer = File.ReadAllBytes(second);
            var provider = providers.For(format);
            var firstRoot = providers.Root(first, format);
            var secondRoot = providers.Root(second, format);
            byte[] firstRead = Read(provider, firstRoot), secondRead = Read(provider, secondRoot);
            int before = providers.Count(format), after = before;
            string? secondRefusal = format == "zip" ? providers.Zip.WhyReadOnly(secondRoot) : null;
            byte[] laterRead = secondRead;
            if (route == "release")
            {
                providers.Release(first, format);
                after = providers.Count(format);
                laterRead = Read(provider, secondRoot);
            }
            if (route == "refresh")
            {
                // A timestamp-only refresh of one file must not retire its case-distinct neighbor.
                File.SetLastWriteTimeUtc(first, Stamp.AddSeconds(4));
                Read(provider, firstRoot);
                after = providers.Count(format);
                laterRead = Read(provider, secondRoot);
            }
            bool firstUnchanged = firstContainer.SequenceEqual(File.ReadAllBytes(first));
            bool secondUnchanged = secondContainer.SequenceEqual(File.ReadAllBytes(second));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Scope = "outer", format, route, reverse, upperId, lowerId, before, after, secondRefusal,
                FirstExpected = Convert.ToBase64String(firstBytes), SecondExpected = Convert.ToBase64String(secondBytes),
                FirstRead = Convert.ToBase64String(firstRead), SecondRead = Convert.ToBase64String(secondRead), LaterRead = Convert.ToBase64String(laterRead),
                FirstArchiveSHA256 = Hash(firstContainer), SecondArchiveSHA256 = Hash(secondContainer), firstUnchanged, secondUnchanged
            }));
            Assert.Equal(firstBytes, firstRead); Assert.Equal(secondBytes, secondRead); Assert.Equal(secondBytes, laterRead);
            Assert.True(firstUnchanged); Assert.True(secondUnchanged);
            Assert.Equal(route == "repeat" ? 1 : 2, before);
            Assert.Equal(route == "release" ? 1 : route == "repeat" ? 1 : 2, after);
            if (format == "zip" && route == "capability") Assert.Null(secondRefusal);
        }
        finally
        {
            providers.Dispose();
            foreach (string path in new[] { upper, lower }.Distinct())
                if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { Restoration = "outer", root, Absent = !Directory.Exists(root) }));
        }
    }

    [Theory]
    [MemberData(nameof(NestedCases))]
    public void Case_distinct_nested_member_names_keep_their_own_complete_bytes(string format, string route, bool reverse)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-archive-member-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string outer = Path.Combine(root, "outer." + format);
        byte[] a = Payload(11), b = Payload(79);
        string upper = route == "folder" ? "FOLDER/inner.zip" : "INNER.zip";
        string lower = route == "folder" ? "folder/inner.zip" : "inner.zip";
        using var providers = new Providers(Path.Combine(root, "scratch"));
        try
        {
            Write(outer, format, (upper, Zip(("payload.bin", a))), (lower, Zip(("payload.bin", b))));
            byte[] container = File.ReadAllBytes(outer);
            Location parent = providers.Root(outer, format);
            string first = reverse ? lower : upper, second = reverse ? upper : lower;
            byte[] firstBytes = reverse ? b : a, secondBytes = reverse ? a : b;
            if (route == "repeat") { second = first; secondBytes = firstBytes; }
            var one = new Location(Schemes.Zip, string.Empty, parent.WithPath(first));
            var two = new Location(Schemes.Zip, string.Empty, parent.WithPath(second));
            byte[] firstRead = Read(providers.Zip, one), secondRead = Read(providers.Zip, two), repeatedRead = Read(providers.Zip, one);
            bool unchanged = container.SequenceEqual(File.ReadAllBytes(outer));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Scope = "nested", format, route, reverse, first, second,
                FirstExpected = Convert.ToBase64String(firstBytes), SecondExpected = Convert.ToBase64String(secondBytes),
                FirstRead = Convert.ToBase64String(firstRead), SecondRead = Convert.ToBase64String(secondRead), RepeatedRead = Convert.ToBase64String(repeatedRead),
                OuterArchiveSHA256 = Hash(container), unchanged
            }));
            Assert.Equal(firstBytes, firstRead); Assert.Equal(secondBytes, secondRead); Assert.Equal(firstBytes, repeatedRead);
            Assert.True(unchanged);
        }
        finally
        {
            providers.Dispose();
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { Restoration = "nested", root, Absent = !Directory.Exists(root) }));
        }
    }
}
