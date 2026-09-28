using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>P8 read-only archive formats (ADR-07): TAR family, 7z, RAR, single compressed files, and disc images.</summary>
public sealed class ArchiveFormatTests : IDisposable
{
    private readonly TempDir _dir = new();
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");

    public void Dispose() => _dir.Dispose();

    private (ProviderRegistry Providers, ArchiveProvider Archives, ZipProvider Zip) Providers()
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var zip = new ZipProvider(Path.Combine(_dir.Path, "tmp"));
        providers.Register(zip);
        var archives = new ArchiveProvider(Path.Combine(_dir.Path, "tmp"), providers);
        providers.Register(archives);
        zip.OtherArchives = archives;
        zip.SpoolForeignMember = archives.Spool;
        return (providers, archives, zip);
    }

    private sealed class Sink(List<EntryData> list, List<string>? issues = null) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) => issues?.Add(message);
    }

    private static List<EntryData> List(ResourceProvider provider, Location location, List<string>? issues = null)
    {
        var list = new List<EntryData>();
        provider.EnumerateAsync(location, new Sink(list, issues), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return list;
    }

    private static byte[] Read(ResourceProvider provider, Location folder, string name)
    {
        var entry = List(provider, folder).Single(e => e.Name == name);
        using var content = provider.OpenContent(provider.GetItemRef(folder, entry))!;
        var bytes = new byte[content.Length];
        int total = 0, n;
        while (total < bytes.Length && (n = content.Read(total, bytes.AsSpan(total))) > 0) total += n;
        return bytes;
    }

    [Theory]
    [InlineData("Rar5.rar")]
    [InlineData("Rar.rar")]
    [InlineData("Rar5.solid.rar")]
    [InlineData("Rar5.multi.part03.rar")]
    [InlineData("7Zip.LZMA2.7z")]
    [InlineData("7Zip.solid.7z")]
    [InlineData("Tar.tar.xz")]
    [InlineData("Tar.tar.zst")]
    public void Every_format_lists_the_same_tree_and_extracts_the_same_bytes(string fixture)
    {
        var (_, archives, _) = Providers();
        var root = archives.GetContainerLocation(Path.Combine(Fixtures, fixture))!;
        Assert.Contains("exe", List(archives, root).Where(e => e.IsContainer).Select(e => e.Name));
        var jpg = Read(archives, root.WithPath("jpg"), "test.jpg");
        var exe = Read(archives, root.WithPath("exe"), "test.exe");
        Assert.Equal((40372, 45056), (jpg.Length, exe.Length));
        Assert.Equal((0xFF, 0xD8, (byte)'M', (byte)'Z'), (jpg[0], jpg[1], exe[0], exe[1]));
        // Every format holds the very same files.
        var reference = archives.GetContainerLocation(Path.Combine(Fixtures, "Rar5.rar"))!;
        Assert.Equal(Read(archives, reference.WithPath("jpg"), "test.jpg"), jpg);
    }

    [Fact]
    public void Solid_archives_give_the_same_bytes_in_any_order()
    {
        var (_, archives, _) = Providers();
        foreach (var fixture in new[] { "7Zip.solid.7z", "Rar5.solid.rar", "Tar.tar.xz" })
        {
            var root = archives.GetContainerLocation(Path.Combine(Fixtures, fixture))!;
            // Backwards first (every read restarts the cursor), then forwards.
            var exe1 = Read(archives, root.WithPath("exe"), "test.exe");
            var jpg1 = Read(archives, root.WithPath("jpg"), "test.jpg");
            var jpg2 = Read(archives, root.WithPath("jpg"), "test.jpg");
            var exe2 = Read(archives, root.WithPath("exe"), "test.exe");
            Assert.Equal(exe1, exe2);
            Assert.Equal(jpg1, jpg2);
        }
    }

    [Fact]
    public void Encrypted_members_are_listed_but_not_opened_and_an_encrypted_file_list_is_explained()
    {
        var (_, archives, _) = Providers();
        var root = archives.GetContainerLocation(Path.Combine(Fixtures, "7Zip.encryptedFiles.7z"))!;
        var jpg = List(archives, root.WithPath("jpg")).Single();
        Assert.True(jpg.Has(EntryFlags.Protected));
        Assert.Null(archives.OpenContent(archives.GetItemRef(root.WithPath("jpg"), jpg)));

        var hidden = archives.GetContainerLocation(Path.Combine(Fixtures, "Rar5.encrypted_filesAndHeader.rar"))!;
        var ex = Assert.Throws<InvalidDataException>(() => List(archives, hidden));
        Assert.Contains("password", ex.Message);
    }

    /// <summary>A TAR with a file, a folder, links, a device, and names that try to escape.</summary>
    private static void WriteTar(Stream destination)
    {
        using var tar = new TarWriter(destination, TarEntryFormat.Pax, leaveOpen: true);
        var a = new PaxTarEntry(TarEntryType.RegularFile, "a.txt") { DataStream = new MemoryStream("alpha"u8.ToArray()) };
        tar.WriteEntry(a);
        tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "sub/"));
        tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "sub/b.bin") { DataStream = new MemoryStream(Enumerable.Range(0, 70_000).Select(i => (byte)i).ToArray()) });
        tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "a.txt" });
        tar.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "hard") { LinkName = "a.txt" });
        tar.WriteEntry(new PaxTarEntry(TarEntryType.Fifo, "pipe"));
        tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "../evil.txt") { DataStream = new MemoryStream("x"u8.ToArray()) });
        tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "sub/../../escape.txt") { DataStream = new MemoryStream("x"u8.ToArray()) });
    }

    [Theory]
    [InlineData("sample.tar")]
    [InlineData("sample.tar.gz")]
    [InlineData("sample.tar.bz2")]
    public void A_TAR_shows_links_and_devices_as_unavailable_and_never_extracts_escaping_names(string name)
    {
        var path = Path.Combine(_dir.Path, name);
        using (var file = File.Create(path))
        {
            Stream target = name.EndsWith(".gz") ? new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true)
                : name.EndsWith(".bz2") ? SharpCompress.Compressors.BZip2.BZip2Stream.Create(file, SharpCompress.Compressors.CompressionMode.Compress, false, true, false)
                : file;
            WriteTar(target);
            if (!ReferenceEquals(target, file)) target.Dispose();
        }
        var (_, archives, _) = Providers();
        var root = archives.GetContainerLocation(path)!;
        var issues = new List<string>();
        var entries = List(archives, root, issues).ToDictionary(e => e.Name);
        Assert.Equal("alpha", System.Text.Encoding.UTF8.GetString(Read(archives, root, "a.txt")));
        Assert.Equal(70_000, Read(archives, root.WithPath("sub"), "b.bin").Length);
        Assert.True(entries["link"].Has(EntryFlags.Link) && entries["hard"].Has(EntryFlags.Link));
        Assert.True(entries["pipe"].Has(EntryFlags.Unavailable));
        // Escaping names are listed where they claim to be, flagged, and never extracted.
        Assert.True(entries["evil.txt"].Has(EntryFlags.Unavailable));
        Assert.True(List(archives, root.WithPath("sub")).Single(e => e.Name == "escape.txt").Has(EntryFlags.Unavailable));
        Assert.Throws<InvalidDataException>(() => archives.OpenContent(archives.GetItemRef(root, entries["link"])));
        Assert.Throws<InvalidDataException>(() => archives.OpenContent(archives.GetItemRef(root, entries["evil.txt"])));
        Assert.Equal("→ a.txt", ((ArchiveMemberTag)entries["link"].Tag!).DetailsText);
        Assert.Equal(LocationCapabilities.None, archives.GetCapabilities(root) & (LocationCapabilities.Delete | LocationCapabilities.TransferTarget | LocationCapabilities.Rename));
    }

    [Fact]
    public async Task Extracting_a_compressed_TAR_restores_files_and_skips_links_and_escapes()
    {
        var path = Path.Combine(_dir.Path, "sample.tgz");
        using (var file = File.Create(path))
        using (var gz = new GZipStream(file, CompressionLevel.Fastest))
            WriteTar(gz);
        var (providers, archives, _) = Providers();
        var root = archives.GetContainerLocation(path)!;
        var dest = _dir.Dir("out");
        var manager = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "journal"));
        var job = manager.Submit(new JobRequest
        {
            Kind = JobKind.Extract,
            Sources = List(archives, root).Select(e => archives.GetItemRef(root, e)).ToList(),
            Destination = Location.FileSystem(dest),
        });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal(70_000, new FileInfo(Path.Combine(dest, "sub", "b.bin")).Length);
        Assert.False(File.Exists(Path.Combine(dest, "link")) || File.Exists(Path.Combine(dest, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_dir.Path, "evil.txt")) || File.Exists(Path.Combine(_dir.Path, "escape.txt")));
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Contains(job.Issues, i => i.Message.Contains("Links inside archives"));
    }

    [Fact]
    public void A_single_compressed_file_is_one_member_and_a_bomb_is_stopped()
    {
        var notes = Path.Combine(_dir.Path, "notes.txt.gz");
        using (var gz = new GZipStream(File.Create(notes), CompressionLevel.Optimal)) gz.Write("hello"u8);
        var (_, archives, _) = Providers();
        var root = archives.GetContainerLocation(notes)!;
        Assert.Equal("notes.txt", Assert.Single(List(archives, root)).Name);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(Read(archives, root, "notes.txt")));

        var bomb = Path.Combine(_dir.Path, "zeros.bin.gz");
        using (var gz = new GZipStream(File.Create(bomb), CompressionLevel.SmallestSize))
        {
            var zeros = new byte[1 << 20];
            for (int i = 0; i < 80; i++) gz.Write(zeros);
        }
        var bombRoot = archives.GetContainerLocation(bomb)!;
        var entry = Assert.Single(List(archives, bombRoot));
        var ex = Assert.Throws<InvalidDataException>(() => archives.OpenContent(archives.GetItemRef(bombRoot, entry)));
        Assert.Contains("expansion-ratio", ex.Message);
    }

    [Fact]
    public void A_disc_image_lists_folders_and_reads_files()
    {
        var iso = Path.Combine(_dir.Path, "disc.iso");
        var builder = new DiscUtils.Iso9660.CDBuilder { UseJoliet = true, VolumeIdentifier = "FILECAT" };
        builder.AddFile(@"docs\readme.txt", "disc text"u8.ToArray());
        builder.AddFile("top.bin", new byte[5000]);
        builder.Build(iso);
        var (_, archives, _) = Providers();
        var root = archives.GetContainerLocation(iso)!;
        Assert.Equal(["docs", "top.bin"], List(archives, root).Select(e => e.Name).Order());
        Assert.Equal("disc text", System.Text.Encoding.UTF8.GetString(Read(archives, root.WithPath("docs"), "readme.txt")));
        Assert.Equal(5000, Read(archives, root, "top.bin").Length);
    }

    [Fact]
    public void Archives_nest_in_both_directions_and_open_by_signature()
    {
        // A .tar.gz inside a ZIP, and a ZIP inside a plain TAR.
        var tgz = new MemoryStream();
        using (var gz = new GZipStream(tgz, CompressionLevel.Fastest, leaveOpen: true)) WriteTar(gz);
        var outerZip = Path.Combine(_dir.Path, "outer.zip");
        using (var zipFile = ZipFile.Open(outerZip, ZipArchiveMode.Create))
        using (var s = zipFile.CreateEntry("inner/data.tar.gz").Open()) s.Write(tgz.ToArray());
        var (_, archives, zip) = Providers();
        var innerFolder = zip.GetContainerLocation(outerZip)!.WithPath("inner");
        var member = List(zip, innerFolder).Single();
        Assert.True(member.Has(EntryFlags.Container));
        var tarRoot = zip.GetChildLocation(innerFolder, member)!;
        Assert.Equal(Schemes.Archive, tarRoot.Scheme);
        Assert.Equal("alpha", System.Text.Encoding.UTF8.GetString(Read(archives, tarRoot, "a.txt")));

        var zipBytes = File.ReadAllBytes(outerZip);
        var outerTar = Path.Combine(_dir.Path, "outer.tar");
        using (var file = File.Create(outerTar))
        using (var tar = new TarWriter(file, TarEntryFormat.Pax))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "packed.zip") { DataStream = new MemoryStream(zipBytes) });
        var tarRootLocation = archives.GetContainerLocation(outerTar)!;
        var packed = List(archives, tarRootLocation).Single();
        var zipRoot = archives.GetChildLocation(tarRootLocation, packed)!;
        Assert.Equal(Schemes.Zip, zipRoot.Scheme);
        Assert.Equal("inner", Assert.Single(List(zip, zipRoot)).Name);

        // A 7z under another name opens by its signature.
        var renamed = Path.Combine(_dir.Path, "backup.dat");
        File.Copy(Path.Combine(Fixtures, "7Zip.LZMA2.7z"), renamed);
        using (var fs = File.OpenRead(renamed)) Assert.Equal(ArchiveKind.SevenZip, ArchiveFormats.BySignature(fs));
        Assert.Contains("jpg", List(archives, ArchiveProvider.ForFile(renamed, ArchiveKind.SevenZip)).Select(e => e.Name));
        Assert.Equal(outerTar + Path.DirectorySeparatorChar + "packed.zip", zip.GetDisplayPath(zipRoot));
    }

    [Fact]
    public void Damaged_archives_of_every_format_report_damage_never_other_errors()
    {
        var iso = Path.Combine(_dir.Path, "seed.iso");
        var builder = new DiscUtils.Iso9660.CDBuilder { UseJoliet = true };
        builder.AddFile(@"a.txt", new byte[3000]);
        builder.Build(iso);
        var tar = Path.Combine(_dir.Path, "seed.tar");
        using (var file = File.Create(tar)) WriteTar(file);
        var seeds = new[] { "Rar5.rar", "Rar.rar", "Rar5.solid.rar", "7Zip.LZMA2.7z", "7Zip.solid.7z", "Tar.tar.xz", "Tar.tar.zst" }
            .Select(f => (Name: f, Bytes: File.ReadAllBytes(Path.Combine(Fixtures, f))))
            .Append(("seed.iso", File.ReadAllBytes(iso))).Append(("seed.tar", File.ReadAllBytes(tar))).ToList();
        var (_, archives, _) = Providers();
        var rng = new Random(77);
        int n = 0;
        foreach (var (name, seed) in seeds)
        {
            for (int round = 0; round < 40; round++)
            {
                var copy = seed[..rng.Next(1, seed.Length + 1)];
                for (int flips = rng.Next(1, 30); flips > 0; flips--) copy[rng.Next(copy.Length)] = (byte)rng.Next(256);
                var path = Path.Combine(_dir.Path, $"{n++}-{name}");
                File.WriteAllBytes(path, copy);
                try
                {
                    var pending = new Stack<Location>([archives.GetContainerLocation(path)!]);
                    for (int guard = 0; pending.Count > 0 && guard < 50; guard++)
                    {
                        var folder = pending.Pop();
                        foreach (var e in List(archives, folder))
                        {
                            if (e.Kind == EntryKind.Directory) pending.Push(folder.WithPath(folder.Path.Length == 0 ? e.Name : folder.Path + "/" + e.Name));
                            else
                            {
                                try { using var content = archives.OpenContent(archives.GetItemRef(folder, e)); }
                                catch (Exception ex) when (ex is IOException or InvalidDataException) { }
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { }
                archives.Release(path);
            }
        }
    }
}
