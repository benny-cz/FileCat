using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveSourceReplacementTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string format in new[] { "zip", "tar" })
        foreach (string route in new[] { "repeat", "content", "catalog", "lease" })
        foreach (bool identityAvailable in new[] { false, true }) yield return [format, route, identityAvailable];
    }
    private static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc);
    private static byte[] Payload(byte salt) => Enumerable.Range(0, 4096).Select(i => (byte)(i * 17 + salt)).ToArray();
    private static void Write(string path, string format, string name, byte[] bytes)
    {
        using (var file = File.Create(path))
        {
            if (format == "zip")
            {
                using var zip = new ZipArchive(file, ZipArchiveMode.Create);
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression); entry.LastWriteTime = Stamp;
                using var target = entry.Open(); target.Write(bytes);
            }
            else
            {
                using var tar = new TarWriter(file, TarEntryFormat.Ustar);
                using var data = new MemoryStream(bytes, writable: false);
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = data, ModificationTime = Stamp });
            }
        }
        File.SetLastWriteTimeUtc(path, Stamp);
    }
    private static byte[] Read(IContentSource source)
    {
        var bytes = new byte[4097]; int done = 0;
        while (done < bytes.Length) { int n = source.Read(done, bytes.AsSpan(done)); if (n == 0) break; done += n; }
        return bytes[..done];
    }
    private sealed class Sink(List<EntryData> entries) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> batch) { foreach (var e in batch) entries.Add(e); }
        public void ReportIssue(string message) => Assert.Fail(message);
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void Same_metadata_replacement_does_not_reuse_the_previous_archive(string format, string route, bool identityAvailable)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-archive-replacement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source." + format), next = Path.Combine(root, "next." + format), moved = Path.Combine(root, "original." + format);
        var zip = new ZipProvider(Path.Combine(root, "scratch"));
        var archive = new ArchiveProvider(Path.Combine(root, "scratch"), new ProviderRegistry());
        ResourceProvider provider = format == "zip" ? zip : archive;
        var files = new IdentityAwareTestFiles();
        // The same control also compiles against the original provider, before this optional capability existed.
        provider.GetType().GetProperty("GetSourceIdentity")?.SetValue(provider,
            (Func<string, string?>)(p => identityAvailable ? files.GetFileIdentity(p) : null));
        provider.GetType().GetProperty("GetOpenedSourceIdentity")!.SetValue(provider, (Func<Microsoft.Win32.SafeHandles.SafeFileHandle, string?>)(handle => identityAvailable ? files.GetHandleIdentity(handle) : null));
        var location = format == "zip" ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
        IContentSource? held = null;
        try
        {
            byte[] a = Payload(11), b = route == "repeat" ? a : Payload(79);
            string secondName = route == "catalog" ? "another.bin" : "payload.bin";
            Write(path, format, "payload.bin", a); Write(next, format, secondName, b);
            string originalId = Assert.IsType<string>(files.GetFileIdentity(path)), nextId = Assert.IsType<string>(files.GetFileIdentity(next));
            Assert.NotEqual(originalId, nextId);
            Assert.Equal(new FileInfo(path).Length, new FileInfo(next).Length);
            Assert.Equal(File.GetLastWriteTimeUtc(path), File.GetLastWriteTimeUtc(next));
            byte[] originalContainer = File.ReadAllBytes(path), nextContainer = File.ReadAllBytes(next);
            held = provider.OpenContent(new ItemRef(location, "payload.bin", EntryKind.File))!;
            byte[] firstRead = Read(held);
            if (route != "lease") { held.Dispose(); held = null; }
            if (route != "repeat") { File.Move(path, moved); File.Move(next, path); }
            var entries = new List<EntryData>();
            provider.EnumerateAsync(location, new Sink(entries), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            byte[]? currentRead = null; string? refusal = null;
            try { using var source = provider.OpenContent(new ItemRef(location, secondName, EntryKind.File))!; currentRead = Read(source); }
            catch (Exception ex) { refusal = ex.GetType().Name + ": " + ex.Message; }
            byte[]? heldRead = held is null ? null : Read(held);
            bool sourceUnchanged = File.ReadAllBytes(route == "repeat" ? path : moved).SequenceEqual(originalContainer);
            bool currentUnchanged = File.ReadAllBytes(route == "repeat" ? next : path).SequenceEqual(nextContainer);
            output.WriteLine(JsonSerializer.Serialize(new { format, route, identityAvailable, originalId, nextId, CurrentIdentity = files.GetFileIdentity(path),
                FirstExpected = Convert.ToBase64String(a), CurrentExpected = Convert.ToBase64String(b), FirstRead = Convert.ToBase64String(firstRead),
                CurrentRead = currentRead is null ? null : Convert.ToBase64String(currentRead), HeldRead = heldRead is null ? null : Convert.ToBase64String(heldRead),
                Names = entries.Select(e => e.Name).ToArray(), secondName, refusal, sourceUnchanged, currentUnchanged,
                OriginalContainer = Convert.ToBase64String(originalContainer), ReplacementContainer = Convert.ToBase64String(nextContainer) }));
            Assert.Equal(a, firstRead); Assert.Equal(b, currentRead); Assert.Null(refusal);
            Assert.Contains(entries, e => e.Name == secondName);
            if (heldRead is not null) Assert.Equal(a, heldRead);
            Assert.True(sourceUnchanged); Assert.True(currentUnchanged);
        }
        finally
        {
            held?.Dispose(); zip.Release(path); archive.Release(path);
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { root, Absent = !Directory.Exists(root) }));
        }
    }

    public static IEnumerable<object[]> NestedCases()
    {
        foreach (string outer in new[] { "zip", "tar" })
        foreach (string inner in new[] { "zip", "tar" })
        foreach (bool changed in new[] { false, true })
        foreach (bool identityAvailable in new[] { false, true }) yield return [outer, inner, changed, identityAvailable];
    }
    [Theory]
    [MemberData(nameof(NestedCases))]
    public void Same_metadata_outer_replacement_does_not_reuse_an_old_nested_spool(string outer, string inner, bool changed, bool identityAvailable)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-nested-replacement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source." + outer), next = Path.Combine(root, "next." + outer), moved = Path.Combine(root, "original." + outer);
        string innerA = Path.Combine(root, "first." + inner), innerB = Path.Combine(root, "second." + inner), member = "inside." + inner;
        var registry = new ProviderRegistry();
        var zip = new ZipProvider(Path.Combine(root, "scratch"));
        var archive = new ArchiveProvider(Path.Combine(root, "scratch"), registry);
        registry.Register(zip); registry.Register(archive); zip.OtherArchives = archive; zip.SpoolForeignMember = archive.Spool;
        var files = new IdentityAwareTestFiles();
        foreach (var p in new ResourceProvider[] { zip, archive })
        {
            p.GetType().GetProperty("GetSourceIdentity")?.SetValue(p, (Func<string, string?>)(f => identityAvailable ? files.GetFileIdentity(f) : null));
            p.GetType().GetProperty("GetOpenedSourceIdentity")!.SetValue(p, (Func<Microsoft.Win32.SafeHandles.SafeFileHandle, string?>)(handle => identityAvailable ? files.GetHandleIdentity(handle) : null));
        }
        var outerLocation = outer == "zip" ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
        var location = new Location(inner == "zip" ? Schemes.Zip : Schemes.Archive, "", outerLocation.WithPath(member));
        ResourceProvider provider = inner == "zip" ? zip : archive;
        try
        {
            byte[] a = Payload(11), b = changed ? Payload(79) : a;
            Write(innerA, inner, "payload.bin", a); Write(innerB, inner, "payload.bin", b);
            Write(path, outer, member, File.ReadAllBytes(innerA)); Write(next, outer, member, File.ReadAllBytes(innerB));
            Assert.Equal(new FileInfo(path).Length, new FileInfo(next).Length); Assert.Equal(File.GetLastWriteTimeUtc(path), File.GetLastWriteTimeUtc(next));
            string originalId = Assert.IsType<string>(files.GetFileIdentity(path)), nextId = Assert.IsType<string>(files.GetFileIdentity(next)); Assert.NotEqual(originalId, nextId);
            byte[] originalContainer = File.ReadAllBytes(path), replacementContainer = File.ReadAllBytes(next);
            byte[] firstRead;
            using (var source = provider.OpenContent(new ItemRef(location, "payload.bin", EntryKind.File))!) firstRead = Read(source);
            if (changed) { File.Move(path, moved); File.Move(next, path); }
            byte[] currentRead;
            using (var source = provider.OpenContent(new ItemRef(location, "payload.bin", EntryKind.File))!) currentRead = Read(source);
            bool sourceUnchanged = File.ReadAllBytes(changed ? moved : path).SequenceEqual(originalContainer);
            bool currentUnchanged = File.ReadAllBytes(changed ? path : next).SequenceEqual(replacementContainer);
            output.WriteLine(JsonSerializer.Serialize(new { outer, inner, changed, identityAvailable, originalId, nextId,
                CurrentIdentity = files.GetFileIdentity(path), FirstExpected = Convert.ToBase64String(a), CurrentExpected = Convert.ToBase64String(b),
                FirstRead = Convert.ToBase64String(firstRead), CurrentRead = Convert.ToBase64String(currentRead), sourceUnchanged, currentUnchanged,
                OriginalContainer = Convert.ToBase64String(originalContainer), ReplacementContainer = Convert.ToBase64String(replacementContainer) }));
            Assert.Equal(a, firstRead); Assert.Equal(b, currentRead); Assert.True(sourceUnchanged); Assert.True(currentUnchanged);
        }
        finally
        {
            // Dispose each provider's bounded spools, including private intermediate indexes.
            zip.Release(path); archive.Release(path);
            foreach (var field in new[] { "_nested", "_spools" })
            {
                object owner = field == "_nested" ? zip : archive;
                var cache = (System.Collections.IEnumerable)owner.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
                foreach (var pair in cache)
                {
                    var value = pair.GetType().GetProperty("Value")!.GetValue(pair)!;
                    string spool = field == "_nested" ? (string)value.GetType().GetProperty("Path")!.GetValue(value)! : (string)value.GetType().GetField("Item1")!.GetValue(value)!;
                    zip.Release(spool); archive.Release(spool);
                    if (value is IDisposable disposable) disposable.Dispose();
                    else ((FileStream)value.GetType().GetField("Item2")!.GetValue(value)!).Dispose();
                }
            }
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { root, Absent = !Directory.Exists(root) }));
        }
    }
}
