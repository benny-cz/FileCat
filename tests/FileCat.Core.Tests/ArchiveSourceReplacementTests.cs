using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
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
    [InlineData("7z")]
    [InlineData("iso")]
    [InlineData("tar.gz")]
    public async Task In_place_other_format_rewrite_refreshes_names_and_bytes(string format)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-archive-format-in-place-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "owned." + format), next = Path.Combine(root, "next." + format);
        byte[] firstBytes = format == "7z" ? new byte[4096] : Payload(11);
        byte[] secondBytes = format == "7z" ? Enumerable.Repeat((byte)0xA7, 4096).ToArray() : Payload(79);
        void Make(string target, string name, byte[] content)
        {
            if (format == "7z")
            {
                string fixture = name == "alpha.txt" ? "InPlaceAlpha.7z" : "InPlaceOmega.7z";
                File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", fixture), target);
            }
            else if (format == "iso")
            {
                var builder = new DiscUtils.Iso9660.CDBuilder { UseJoliet = true, VolumeIdentifier = "FILECAT" };
                builder.AddFile(name, content);
                builder.Build(target);
            }
            else
            {
                using var file = File.Create(target);
                using var gzip = new GZipStream(file, CompressionLevel.NoCompression);
                using var tar = new TarWriter(gzip, TarEntryFormat.Ustar);
                using var data = new MemoryStream(content, writable: false);
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name)
                    { DataStream = data, ModificationTime = Stamp });
            }
        }
        var provider = new ArchiveProvider(Path.Combine(root, "scratch"), new ProviderRegistry())
        {
            GetSourceIdentity = _ => "owned-stable-identity",
            GetOpenedSourceIdentity = _ => "owned-stable-identity",
        };
        try
        {
            Make(path, "alpha.txt", firstBytes);
            Make(next, "omega.txt", secondBytes);
            byte[] replacement = File.ReadAllBytes(next);
            long length = new FileInfo(path).Length;
            Assert.Equal(length, replacement.LongLength);
            var location = ArchiveProvider.ForFile(path);
            var first = new List<EntryData>();
            await provider.EnumerateAsync(location, new Sink(first), TestContext.Current.CancellationToken);
            var oldItem = Assert.Single(first);
            Assert.Equal("alpha.txt", oldItem.Name);
            using (var content = provider.OpenContent(provider.GetItemRef(location, oldItem)))
                Assert.Equal(firstBytes, Read(Assert.IsAssignableFrom<IContentSource>(content)));

            DateTime stamp = File.GetLastWriteTimeUtc(path);
            string? token = SourceChangeToken.FromPath(path);
            Assert.NotNull(token);
            string? changed = token;
            for (int attempt = 0; attempt < 8 && changed == token; attempt++)
            {
                File.WriteAllBytes(path, replacement);
                File.SetLastWriteTimeUtc(path, stamp);
                changed = SourceChangeToken.FromPath(path);
                if (changed == token) Thread.Sleep(25);
            }
            Assert.Equal(length, new FileInfo(path).Length);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
            Assert.NotEqual(token, changed);

            var current = new List<EntryData>();
            await provider.EnumerateAsync(location, new Sink(current), TestContext.Current.CancellationToken);
            var newItem = Assert.Single(current);
            Assert.Equal("omega.txt", newItem.Name);
            using (var content = provider.OpenContent(provider.GetItemRef(location, newItem)))
                Assert.Equal(secondBytes, Read(Assert.IsAssignableFrom<IContentSource>(content)));
        }
        finally
        {
            provider.Release(path);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task In_place_tar_rewrite_refreshes_a_cached_catalog_with_restored_mtime()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-tar-in-place-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "owned.tar"), next = Path.Combine(root, "next.tar");
        var provider = new ArchiveProvider(Path.Combine(root, "scratch"), new ProviderRegistry())
        {
            GetSourceIdentity = _ => "owned-stable-identity",
            GetOpenedSourceIdentity = _ => "owned-stable-identity",
        };
        try
        {
            Write(path, "tar", "alpha.txt", Payload(11));
            Write(next, "tar", "omega.txt", Payload(79));
            byte[] replacement = File.ReadAllBytes(next);
            long length = new FileInfo(path).Length;
            Assert.Equal(length, replacement.LongLength);
            var location = ArchiveProvider.ForFile(path);
            var initial = new List<EntryData>();
            await provider.EnumerateAsync(location, new Sink(initial), TestContext.Current.CancellationToken);
            Assert.Equal(["alpha.txt"], initial.Select(entry => entry.Name));

            DateTime originalTime = File.GetLastWriteTimeUtc(path);
            string? originalToken = SourceChangeToken.FromPath(path);
            Assert.NotNull(originalToken);
            string? changedToken = originalToken;
            for (int attempt = 0; attempt < 8 && changedToken == originalToken; attempt++)
            {
                File.WriteAllBytes(path, replacement);
                File.SetLastWriteTimeUtc(path, originalTime);
                changedToken = SourceChangeToken.FromPath(path);
                if (changedToken == originalToken) Thread.Sleep(25);
            }
            Assert.Equal(length, new FileInfo(path).Length);
            Assert.Equal(originalTime, File.GetLastWriteTimeUtc(path));
            Assert.NotEqual(originalToken, changedToken);

            var current = new List<EntryData>();
            await provider.EnumerateAsync(location, new Sink(current), TestContext.Current.CancellationToken);
            Assert.Equal(["omega.txt"], current.Select(entry => entry.Name));
        }
        finally
        {
            provider.Release(path);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Held_large_tar_member_refuses_bytes_after_an_in_place_rewrite()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-tar-held-rewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "owned.tar"), next = Path.Combine(root, "next.tar");
        var provider = new ArchiveProvider(Path.Combine(root, "scratch"), new ProviderRegistry())
        {
            GetSourceIdentity = _ => "owned-stable-identity",
            GetOpenedSourceIdentity = _ => "owned-stable-identity",
        };
        ProgressiveContent? held = null;
        try
        {
            byte[] first = new byte[(int)ArchiveProvider.MaxMemberInMemory + 4096];
            byte[] second = new byte[first.Length];
            Array.Fill(first, (byte)0x35);
            Array.Fill(second, (byte)0xA7);
            Write(path, "tar", "payload.bin", first);
            Write(next, "tar", "payload.bin", second);
            byte[] replacement = File.ReadAllBytes(next);
            Assert.Equal(new FileInfo(path).Length, replacement.LongLength);
            var location = ArchiveProvider.ForFile(path);
            held = Assert.IsType<ProgressiveContent>(provider.OpenContent(new ItemRef(location, "payload.bin", EntryKind.File)));
            byte[] prefix = new byte[4096];
            Assert.Equal(prefix.Length, held.Read(0, prefix));
            Assert.True(prefix.All(b => b == 0x35));
            DateTime originalTime = File.GetLastWriteTimeUtc(path);
            string? originalToken = SourceChangeToken.FromPath(path);
            Assert.NotNull(originalToken);
            File.WriteAllBytes(path, replacement);
            File.SetLastWriteTimeUtc(path, originalTime);
            Assert.Equal(originalTime, File.GetLastWriteTimeUtc(path));
            Assert.NotEqual(originalToken, SourceChangeToken.FromPath(path));
            Assert.Throws<IOException>(() => { held.Read(8192, new byte[4096]); });
        }
        finally
        {
            held?.Dispose();
            provider.Release(path);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("tar")]
    [InlineData("zip")]
    public async Task In_place_outer_rewrite_refreshes_a_nested_tar_spool(string outer)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-nested-in-place-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "owned." + outer), next = Path.Combine(root, "next." + outer);
        string innerA = Path.Combine(root, "first.tar"), innerB = Path.Combine(root, "second.tar");
        var registry = new ProviderRegistry();
        var zip = new ZipProvider(Path.Combine(root, "scratch"));
        var archive = new ArchiveProvider(Path.Combine(root, "scratch"), registry);
        registry.Register(zip); registry.Register(archive); zip.OtherArchives = archive; zip.SpoolForeignMember = archive.Spool;
        foreach (var provider in new ResourceProvider[] { zip, archive })
        {
            provider.GetType().GetProperty("GetSourceIdentity")!.SetValue(provider, (Func<string, string?>)(_ => "owned-stable-identity"));
            provider.GetType().GetProperty("GetOpenedSourceIdentity")!.SetValue(provider,
                (Func<Microsoft.Win32.SafeHandles.SafeFileHandle, string?>)(_ => "owned-stable-identity"));
        }
        try
        {
            Write(innerA, "tar", "alpha.txt", Payload(11));
            Write(innerB, "tar", "omega.txt", Payload(79));
            Write(path, outer, "inside.tar", File.ReadAllBytes(innerA));
            Write(next, outer, "inside.tar", File.ReadAllBytes(innerB));
            byte[] replacement = File.ReadAllBytes(next);
            Assert.Equal(new FileInfo(path).Length, replacement.LongLength);
            var outerLocation = outer == "zip" ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
            var innerLocation = new Location(Schemes.Archive, "", outerLocation.WithPath("inside.tar"));
            var initial = new List<EntryData>();
            await archive.EnumerateAsync(innerLocation, new Sink(initial), TestContext.Current.CancellationToken);
            Assert.Equal(["alpha.txt"], initial.Select(entry => entry.Name));
            DateTime originalTime = File.GetLastWriteTimeUtc(path);
            string? originalToken = SourceChangeToken.FromPath(path);
            Assert.NotNull(originalToken);
            File.WriteAllBytes(path, replacement);
            File.SetLastWriteTimeUtc(path, originalTime);
            Assert.Equal(originalTime, File.GetLastWriteTimeUtc(path));
            Assert.NotEqual(originalToken, SourceChangeToken.FromPath(path));
            var current = new List<EntryData>();
            await archive.EnumerateAsync(innerLocation, new Sink(current), TestContext.Current.CancellationToken);
            Assert.Equal(["omega.txt"], current.Select(entry => entry.Name));
        }
        finally
        {
            zip.Release(path); archive.Release(path);
            foreach (var (owner, field) in new (object Owner, string Field)[] { (zip, "_nested"), (archive, "_spools") })
            {
                var cache = (System.Collections.IEnumerable)owner.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
                foreach (var pair in cache)
                {
                    var value = pair.GetType().GetProperty("Value")!.GetValue(pair)!;
                    string spool = (string)value.GetType().GetProperty("Path")!.GetValue(value)!;
                    zip.Release(spool); archive.Release(spool);
                    ((IDisposable)value).Dispose();
                }
            }
            Directory.Delete(root, recursive: true);
        }
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Same_metadata_replacement_does_not_reuse_the_previous_archive(string format, string route, bool identityAvailable)
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
            await provider.EnumerateAsync(location, new Sink(entries), TestContext.Current.CancellationToken);
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
                    string spool = (string)value.GetType().GetProperty("Path")!.GetValue(value)!;
                    zip.Release(spool); archive.Release(spool);
                    ((IDisposable)value).Dispose();
                }
            }
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { root, Absent = !Directory.Exists(root) }));
        }
    }
}
