using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.Selection;

namespace FileCat.Core.Tests;

/// <summary>Untrusted input stays bounded (SEC-001): lying archives, corrupted indexes, hostile masks and bytes.</summary>
public sealed class HardeningTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>A minimal shortcut: header, then a LinkInfo with a local base path (ANSI, or Unicode as well).</summary>
    private static byte[] Shortcut(string basePath, bool unicode, bool directory)
    {
        var header = new byte[0x4C];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x4C);
        new Guid("00021401-0000-0000-c000-000000000046").TryWriteBytes(header.AsSpan(4));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 0x2 | 0x80); // HasLinkInfo | IsUnicode
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), directory ? 0x10u : 0x20u);
        int headerSize = unicode ? 0x24 : 0x1C;
        var volume = new byte[0x11];
        BinaryPrimitives.WriteUInt32LittleEndian(volume, 0x11);
        BinaryPrimitives.WriteUInt32LittleEndian(volume.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(volume.AsSpan(12), 0x10);
        var ansi = Encoding.ASCII.GetBytes(unicode ? @"C:\ignored" : basePath).Append((byte)0).ToArray();
        byte[] ansiSuffix = [0];
        var wide = unicode ? Encoding.Unicode.GetBytes(basePath).Concat(new byte[2]).ToArray() : [];
        byte[] wideSuffix = unicode ? [0, 0] : [];
        int volumeAt = headerSize, baseAt = volumeAt + volume.Length, suffixAt = baseAt + ansi.Length;
        int wideAt = suffixAt + ansiSuffix.Length, wideSuffixAt = wideAt + wide.Length;
        var info = new byte[wideSuffixAt + wideSuffix.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(info, (uint)info.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(4), (uint)headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(12), (uint)volumeAt);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(16), (uint)baseAt);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(24), (uint)suffixAt);
        if (unicode)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(28), (uint)wideAt);
            BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(32), (uint)wideSuffixAt);
        }
        volume.CopyTo(info, volumeAt);
        ansi.CopyTo(info, baseAt);
        ansiSuffix.CopyTo(info, suffixAt);
        wide.CopyTo(info, wideAt);
        wideSuffix.CopyTo(info, wideSuffixAt);
        return [.. header, .. info, 0, 0, 0, 0];
    }

    [Fact]
    public void Shortcut_targets_are_read_raw_and_malformed_links_are_ignored()
    {
        var ansi = _dir.File("ansi.lnk");
        File.WriteAllBytes(ansi, Shortcut(@"C:\Data", unicode: false, directory: true));
        Assert.True(Core.FileSystem.ShellLinkReader.TryRead(ansi, out var a));
        Assert.Equal(new Core.FileSystem.ShellLinkTarget(@"C:\Data", true), a);

        var wide = _dir.File("wide.lnk");
        File.WriteAllBytes(wide, Shortcut(@"D:\Dáta\žluť.txt", unicode: true, directory: false));
        Assert.True(Core.FileSystem.ShellLinkReader.TryRead(wide, out var w));
        Assert.Equal(new Core.FileSystem.ShellLinkTarget(@"D:\Dáta\žluť.txt", false), w);

        // Truncated and corrupted links never throw; they are simply not followed.
        var bytes = Shortcut(@"C:\Data", unicode: true, directory: true);
        var random = new Random(3);
        var fuzz = _dir.File("fuzz.bin"); // the reader ignores the extension; antivirus inspects every .lnk written
        for (int round = 0; round < 400; round++)
        {
            var copy = bytes.Take(random.Next(bytes.Length + 1)).ToArray();
            for (int i = 0; i < 4 && copy.Length > 0; i++) copy[random.Next(copy.Length)] = (byte)random.Next(256);
            File.WriteAllBytes(fuzz, copy);
            Core.FileSystem.ShellLinkReader.TryRead(fuzz, out _);
        }
        File.WriteAllText(fuzz, "not a shortcut");
        Assert.False(Core.FileSystem.ShellLinkReader.TryRead(fuzz, out _));
    }

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }

    private string ZipWith(string name, byte[] content)
    {
        var path = Path.Combine(_dir.Path, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(content);
        return path;
    }

    /// <summary>Rewrites the uncompressed size in the local and central headers: the archive now lies about it.</summary>
    private static void DeclareSize(string zipPath, uint size)
    {
        var bytes = File.ReadAllBytes(zipPath);
        for (int i = 0; i + 30 <= bytes.Length; i++)
        {
            uint sig = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i));
            if (sig == 0x04034b50) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 22), size);
            else if (sig == 0x02014b50 && i + 46 <= bytes.Length) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 24), size);
        }
        File.WriteAllBytes(zipPath, bytes);
    }

    private static List<EntryData> List(ZipProvider provider, Location location)
    {
        var list = new List<EntryData>();
        provider.EnumerateAsync(location, new Sink(list), CancellationToken.None).GetAwaiter().GetResult();
        return list;
    }

    [Fact]
    public void A_member_lying_about_its_size_never_expands_past_the_declared_size()
    {
        var zipPath = ZipWith("bomb.bin", new byte[8 * 1024 * 1024]); // 8 MiB of zeros compresses to a few KiB
        DeclareSize(zipPath, 10);
        var temp = Path.Combine(_dir.Path, "tmp");
        var provider = new ZipProvider(temp);
        var root = provider.GetContainerLocation(zipPath)!;
        var entry = Assert.Single(List(provider, root));
        Assert.Equal(10, entry.Size);
        long produced;
        try
        {
            using var content = provider.OpenContent(provider.GetItemRef(root, entry))!;
            var buffer = new byte[1024 * 1024];
            produced = content.Read(0, buffer);
            Assert.True(content.Length <= 10 + 1024 * 1024);
        }
        catch (InvalidDataException)
        {
            produced = 0; // refusing the member outright is equally acceptable
        }
        Assert.InRange(produced, 0, 10);
        provider.Release(zipPath);
        Assert.True(!Directory.Exists(temp) || Directory.GetFiles(temp).Length == 0);
    }

    [Fact]
    public void Corrupted_archives_fail_cleanly_instead_of_crashing_or_hanging()
    {
        var zipPath = Path.Combine(_dir.Path, "good.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (int i = 0; i < 20; i++)
            {
                using var w = new StreamWriter(zip.CreateEntry($"dir{i % 3}/file{i}.txt").Open());
                w.Write(new string((char)('a' + i), 200 + i));
            }
        }
        var original = File.ReadAllBytes(zipPath);
        var random = new Random(20260927);
        var unexpected = new List<string>();
        for (int round = 0; round < 300; round++)
        {
            var bytes = (byte[])original.Clone();
            int flips = 1 + random.Next(8);
            // Bias toward the central directory at the end, where the index is parsed.
            for (int f = 0; f < flips; f++)
            {
                int at = random.Next(2) == 0 ? random.Next(bytes.Length) : bytes.Length - 1 - random.Next(Math.Min(bytes.Length, 900));
                bytes[at] = (byte)random.Next(256);
            }
            var path = Path.Combine(_dir.Path, $"fuzz{round}.zip");
            File.WriteAllBytes(path, bytes);
            var provider = new ZipProvider(Path.Combine(_dir.Path, "tmp"));
            try
            {
                var root = provider.GetContainerLocation(path)!;
                var entries = List(provider, root);
                foreach (var e in entries.Where(e => e.Kind == EntryKind.File).Take(5))
                {
                    try
                    {
                        using var c = provider.OpenContent(provider.GetItemRef(root, e));
                        if (c is not null)
                        {
                            var buffer = new byte[Math.Min(4096, Math.Max(1, c.Length))];
                            c.Read(0, buffer);
                        }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException) { }
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                unexpected.Add($"round {round}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                provider.Release(path);
            }
        }
        Assert.Empty(unexpected);
    }

    [Fact]
    public void Masks_never_throw_while_matching_and_report_parse_errors()
    {
        var random = new Random(7);
        const string alphabet = "*?[]!/\\|;,\"'^$(){}+.-_ aZ09é́";
        var names = new[] { "file.txt", "", ".hidden", "a/b", "名前.doc", new string('a', 300), "x́y" };
        for (int round = 0; round < 3000; round++)
        {
            var text = new string(Enumerable.Range(0, random.Next(1, 24)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            if (!Mask.TryParse(text, out var mask, out var error))
            {
                Assert.False(string.IsNullOrWhiteSpace(error));
                continue;
            }
            foreach (var n in names)
            {
                _ = mask.IsMatch(n, isDirectory: false);
                _ = mask.IsMatch(n, isDirectory: true);
                _ = mask.IsMatchPath("dir/" + n);
            }
        }
    }

    [Fact]
    public void Encoding_detection_accepts_any_bytes()
    {
        var random = new Random(11);
        for (int round = 0; round < 3000; round++)
        {
            var bytes = new byte[random.Next(0, 600)];
            random.NextBytes(bytes);
            if (round % 3 == 0 && bytes.Length >= 3) { bytes[0] = 0xEF; bytes[1] = 0xBB; bytes[2] = 0xBF; }
            if (round % 5 == 0 && bytes.Length >= 2) { bytes[0] = 0xFF; bytes[1] = 0xFE; }
            var guess = TextDecoding.Detect(bytes);
            Assert.NotNull(guess.Encoding);
            _ = TextDecoding.IsValidUtf8(bytes);
            _ = guess.Encoding.GetString(bytes);
        }
    }
}
