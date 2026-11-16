using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

/// <summary>
/// AW2 task 5: the strict ZIP catalog of the worker profile (ZIP_WORKER_PROFILE_DESIGN.md). Every admitted form is read with
/// exact members and metadata; every refused row of the design is read-only (UnsupportedFeature) or invalid, never admitted.
/// </summary>
public sealed class ArchiveZipCatalogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("FileCat strict ZIP catalog. ", 300)));

    internal sealed record RawEntry(string Name, byte[] Data, ushort Method = 8, ushort Flags = 0, ushort MadeBy = 0x033f, uint External = 0x81a40000,
        byte[]? LocalExtra = null, byte[]? CentralExtra = null, string Comment = "", int Descriptor = 0, bool WideDescriptor = false,
        byte[]? NameBytes = null, byte[]? LocalNameBytes = null, uint? Crc = null, ushort Needed = 20, ushort LocalNeeded = 0, byte[]? CommentBytes = null,
        bool ZeroLocalSizes = false, bool KeepLocalSizes = false, byte[]? Packed = null, ushort DiskStart = 0);

    /// <summary>A literal ZIP writer for test fixtures: every field is what the case says, nothing is corrected.</summary>
    internal static byte[] Build(IReadOnlyList<RawEntry> entries, byte[]? comment = null, byte[]? prefix = null, byte[]? gap = null,
        bool zip64End = false, bool saturateEnd = false, byte[]? trailing = null, bool reorder = false, ushort disk = 0)
    {
        using var output = new MemoryStream(); using var central = new MemoryStream();
        if (prefix is not null) output.Write(prefix);
        var records = new List<byte[]>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i]; byte[] name = e.NameBytes ?? Encoding.UTF8.GetBytes(e.Name), localName = e.LocalNameBytes ?? name;
            byte[] data = e.Packed ?? (e.Method == 8 ? Deflate(e.Data) : e.Data); uint crc = e.Crc ?? Crc32(e.Data);
            byte[] localExtra = e.LocalExtra ?? [], centralExtra = e.CentralExtra ?? [], note = e.CommentBytes ?? Encoding.UTF8.GetBytes(e.Comment);
            long offset = output.Position; bool descriptor = e.Descriptor != 0; ushort flags = (ushort)(e.Flags | (descriptor ? 8 : 0));
            bool zeros = e.ZeroLocalSizes || descriptor && !e.KeepLocalSizes;
            var w = new BinaryWriter(output);
            w.Write(StrictZipCatalog.LocalSignature); w.Write(e.LocalNeeded != 0 ? e.LocalNeeded : e.Needed); w.Write(flags); w.Write(e.Method);
            w.Write((ushort)0x6000); w.Write((ushort)0x5821); w.Write(zeros ? 0u : crc);
            w.Write(zeros ? 0u : (uint)data.Length); w.Write(zeros ? 0u : (uint)e.Data.Length);
            w.Write((ushort)localName.Length); w.Write((ushort)localExtra.Length); w.Write(localName); w.Write(localExtra); w.Write(data);
            if (descriptor)
            {
                if (e.Descriptor == 1) w.Write(StrictZipCatalog.DescriptorSignature);
                w.Write(crc);
                if (e.WideDescriptor) { w.Write((long)data.Length); w.Write((long)e.Data.Length); } else { w.Write((uint)data.Length); w.Write((uint)e.Data.Length); }
            }
            w.Flush();
            if (i == 0 && gap is not null) output.Write(gap);
            using var record = new MemoryStream(); var c = new BinaryWriter(record);
            c.Write(StrictZipCatalog.CentralSignature); c.Write(e.MadeBy); c.Write(e.Needed); c.Write(flags); c.Write(e.Method);
            c.Write((ushort)0x6000); c.Write((ushort)0x5821); c.Write(crc); c.Write((uint)data.Length); c.Write((uint)e.Data.Length);
            c.Write((ushort)name.Length); c.Write((ushort)centralExtra.Length); c.Write((ushort)note.Length); c.Write(e.DiskStart != 0 ? e.DiskStart : disk); c.Write((ushort)0);
            c.Write(e.External); c.Write((uint)offset); c.Write(name); c.Write(centralExtra); c.Write(note); c.Flush();
            records.Add(record.ToArray());
        }
        if (reorder) records.Reverse();
        foreach (var record in records) central.Write(record);
        long directoryOffset = output.Position; output.Write(central.ToArray()); long directorySize = output.Position - directoryOffset;
        var end = new BinaryWriter(output);
        if (zip64End)
        {
            long record = output.Position;
            end.Write(StrictZipCatalog.Zip64EndSignature); end.Write(44L); end.Write((ushort)0x033f); end.Write((ushort)45); end.Write(0u); end.Write(0u);
            end.Write((long)entries.Count); end.Write((long)entries.Count); end.Write(directorySize); end.Write(directoryOffset);
            end.Write(StrictZipCatalog.Zip64LocatorSignature); end.Write(0u); end.Write(record); end.Write(1u);
        }
        comment ??= [];
        end.Write(StrictZipCatalog.EndSignature); end.Write(disk); end.Write(disk);
        end.Write(saturateEnd ? (ushort)0xffff : (ushort)entries.Count); end.Write(saturateEnd ? (ushort)0xffff : (ushort)entries.Count);
        end.Write(saturateEnd ? uint.MaxValue : (uint)directorySize); end.Write(saturateEnd ? uint.MaxValue : (uint)directoryOffset);
        end.Write((ushort)comment.Length); end.Write(comment); end.Flush();
        if (trailing is not null) output.Write(trailing);
        return output.ToArray();
    }

    internal static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(data);
        return output.ToArray();
    }
    internal static uint Crc32(byte[] data)
    {
        uint crc = 0xffffffff;
        foreach (byte b in data) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1)); }
        return ~crc;
    }
    internal static byte[] Extra(ushort id, byte[] data)
    { byte[] result = new byte[4 + data.Length]; BinaryPrimitives.WriteUInt16LittleEndian(result, id); BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)data.Length); data.CopyTo(result, 4); return result; }
    internal static byte[] Times(byte flags, params int[] times)
    { byte[] data = new byte[1 + 4 * times.Length]; data[0] = flags; for (int i = 0; i < times.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(1 + 4 * i), times[i]); return Extra(0x5455, data); }
    internal static byte[] Owner(uint uid, uint gid, byte version = 1)
    { byte[] data = new byte[11]; data[0] = version; data[1] = 4; BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2), uid); data[6] = 4; BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(7), gid); return Extra(0x7875, data); }
    internal static byte[] Ntfs(ulong m, ulong a, ulong c, uint reserved = 0)
    {
        byte[] data = new byte[32]; BinaryPrimitives.WriteUInt32LittleEndian(data, reserved); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 24); BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8), m);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16), a); BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24), c); return Extra(0x000a, data);
    }
    private static ZipWriteCatalog Read(byte[] zip) => StrictZipCatalog.Read(new MemoryStream(zip, false), Ct);
    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    [Fact]
    public void Archives_written_by_the_runtime_are_admitted_with_every_member_comment_and_folder()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.Comment = "archive comment";
            archive.CreateEntry("folder/");
            var stored = archive.CreateEntry("folder/stored.txt", CompressionLevel.NoCompression);
            using (var s = stored.Open()) s.Write("stored payload"u8);
            var deflated = archive.CreateEntry("ñame é.txt", CompressionLevel.Optimal); deflated.Comment = "entry comment ✓";
            using (var s = deflated.Open()) s.Write(Text);
            archive.CreateEntry("empty.txt");
        }
        byte[] zip = stream.ToArray();
        var catalog = Read(zip);
        Assert.Equal(Sha(zip), catalog.Revision); Assert.Equal(zip.Length, catalog.Length);
        Assert.Equal("archive comment", Encoding.UTF8.GetString(catalog.Comment));
        Assert.Equal(new[] { "folder/", "folder/stored.txt", "ñame é.txt", "empty.txt" }, catalog.Members.Select(m => m.Name));
        Assert.Equal(ArchiveMemberType.Directory, catalog.Members[0].Type);
        Assert.Equal(Sha(Text), catalog.Members[2].Sha256); Assert.Equal(Text.Length, catalog.Members[2].Length);
        Assert.Equal("entry comment ✓", ArchiveZipMetadata.Decode(catalog.Members[2].Metadata).Comment);
        var manifest = StrictZipCatalog.Manifest(new(ArchiveWriteFormat.Zip), catalog, Ct);
        Assert.Equal("archive comment", Encoding.UTF8.GetString(ArchiveZipFeatures.Comment(manifest.Features)));
    }

    [Fact]
    public void Info_zip_times_owners_and_ntfs_times_are_observed_from_the_local_and_central_records()
    {
        byte[] local = [.. Times(3, 1700000000, 1700000100), .. Owner(501, 20), .. Ntfs(1, 2, 3)];
        byte[] central = [.. Times(3, 1700000000), .. Extra(0x7875, []), .. Ntfs(1, 2, 3)];
        var catalog = Read(Build([new("a.txt", Text, LocalExtra: local, CentralExtra: central)]));
        var metadata = ArchiveZipMetadata.Decode(Assert.Single(catalog.Members).Metadata);
        Assert.Equal((3, 1700000000, 1700000100, 0), (metadata.TimeFlags, metadata.Modified, metadata.Accessed, metadata.Created));
        Assert.Equal(new ArchiveZipOwner(4, 501, 4, 20), metadata.Owner); Assert.Equal(new ArchiveZipNtfsTimes(1, 2, 3), metadata.Ntfs);
        Assert.Equal(0x033f, metadata.VersionMadeBy); Assert.Equal(0x81a40000u, metadata.ExternalAttributes);
    }

    [Theory]
    [InlineData("signed")] [InlineData("unsigned")] [InlineData("wide")] [InlineData("zip64-end")] [InlineData("saturated-end")] [InlineData("duplicates")]
    [InlineData("stored-descriptor")] [InlineData("empty")] [InlineData("descriptor-with-sizes")]
    public void Admitted_layouts_have_the_same_members(string layout)
    {
        byte[] zip64Zero = Extra(0x0001, new byte[16]);
        byte[] zip = layout switch
        {
            "signed" => Build([new("a.txt", Text, Descriptor: 1), new("b.txt", Text, Method: 0, Descriptor: 1)]),
            "unsigned" => Build([new("a.txt", Text, Descriptor: 2), new("b.txt", Text, Method: 0, Descriptor: 2)]),
            "wide" => Build([new("a.txt", Text, Descriptor: 1, WideDescriptor: true, LocalExtra: zip64Zero), new("b.txt", Text, Method: 0, Descriptor: 2)]),
            "zip64-end" => Build([new("a.txt", Text), new("b.txt", Text, Method: 0)], zip64End: true),
            "saturated-end" => Build([new("a.txt", Text), new("b.txt", Text, Method: 0)], zip64End: true, saturateEnd: true),
            "duplicates" => Build([new("a.txt", Text), new("b.txt", Text, Method: 0), new("a.txt", Text)]),
            "stored-descriptor" => Build([new("a.txt", Text, Descriptor: 1), new("b.txt", Text, Method: 0, Descriptor: 1)]),
            "empty" => Build([]),
            _ => Build([new("a.txt", Text, Descriptor: 1, KeepLocalSizes: true), new("b.txt", Text, Method: 0)]),
        };
        var catalog = Read(zip);
        if (layout == "empty") { Assert.Empty(catalog.Members); return; }
        Assert.All(catalog.Members, m => Assert.Equal(Sha(Text), m.Sha256));
        Assert.Equal(layout == "duplicates" ? new[] { 0, 0, 1 } : new[] { 0, 0 }, catalog.Members.Select(m => m.Occurrence));
    }

    // Java writes a folder as Deflate with an empty two-byte stream; that is a folder without data, so it is admitted.
    [Fact]
    public void A_folder_with_an_empty_two_byte_deflate_stream_is_admitted()
    {
        var catalog = Read(Build([new("META-INF/", [], Method: 8, External: 0x41ed0010, Packed: [3, 0]), new("META-INF/a.txt", Text)]));
        Assert.Equal(new[] { "META-INF/", "META-INF/a.txt" }, catalog.Members.Select(m => m.Name));
        Assert.Equal(ArchiveMemberType.Directory, catalog.Members[0].Type); Assert.Equal(0, catalog.Members[0].Length);
    }

    // The central directory is one archive-wide record set: 12,000 members (about 1.2 MiB of directory) are admitted, where
    // the per-entry metadata limit used to refuse any directory over 1 MiB (about 10,000–20,000 members).
    [Fact]
    public void A_central_directory_larger_than_one_mebibyte_is_admitted()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 0; i < 12_000; i++)
            {
                var entry = archive.CreateEntry($"folder/member-{i:D6}-with-a-name-long-enough.txt", CompressionLevel.NoCompression);
                using var stream = entry.Open(); stream.WriteByte((byte)'x');
            }
        byte[] zip = buffer.ToArray();
        var catalog = Read(zip);
        Assert.Equal(12_000, catalog.Members.Count);
        Assert.True(zip.Length - catalog.Members[^1].DataOffset > 1 << 20, "The fixture's central directory must exceed 1 MiB.");
    }

    public static TheoryData<string, ArchiveWriteReason> Refusals => new()
    {
        { "encrypted", ArchiveWriteReason.UnsupportedFeature }, { "strong-encryption", ArchiveWriteReason.UnsupportedFeature },
        { "bzip2-method", ArchiveWriteReason.UnsupportedFeature }, { "unknown-extra", ArchiveWriteReason.UnsupportedFeature },
        { "repeated-extra", ArchiveWriteReason.UnsupportedFeature }, { "code-page-name", ArchiveWriteReason.UnsupportedFeature },
        { "invalid-utf8-name", ArchiveWriteReason.UnsupportedFeature }, { "self-extractor", ArchiveWriteReason.UnsupportedFeature },
        { "gap", ArchiveWriteReason.UnsupportedFeature }, { "reordered", ArchiveWriteReason.UnsupportedFeature },
        { "trailing-data", ArchiveWriteReason.InvalidArchive }, { "fake-end-in-comment", ArchiveWriteReason.UnsupportedFeature },
        { "local-name", ArchiveWriteReason.InvalidArchive }, { "local-version", ArchiveWriteReason.InvalidArchive },
        { "checksum", ArchiveWriteReason.InvalidArchive }, { "truncated-deflate", ArchiveWriteReason.InvalidArchive },
        { "folder-with-data", ArchiveWriteReason.UnsupportedFeature }, { "split", ArchiveWriteReason.UnsupportedFeature },
        { "needed-host", ArchiveWriteReason.UnsupportedFeature }, { "unknown-host", ArchiveWriteReason.UnsupportedFeature },
        { "central-access-time", ArchiveWriteReason.UnsupportedFeature }, { "owner-version", ArchiveWriteReason.UnsupportedFeature },
        { "ntfs-reserved", ArchiveWriteReason.UnsupportedFeature }, { "unneeded-zip64", ArchiveWriteReason.UnsupportedFeature },
        { "unsafe-name", ArchiveWriteReason.UnsupportedFeature }, { "missing-descriptor", ArchiveWriteReason.InvalidArchive },
        { "local-times", ArchiveWriteReason.InvalidArchive }, { "code-page-comment", ArchiveWriteReason.UnsupportedFeature },
        { "patched-data", ArchiveWriteReason.UnsupportedFeature }, { "no-end", ArchiveWriteReason.InvalidArchive },
        // Independent review (v15c): reader disagreement, special files, malformed owner field, saturated disk, padded folder.
        { "decoy-end-in-comment", ArchiveWriteReason.UnsupportedFeature }, { "zip64-locator-in-comment", ArchiveWriteReason.UnsupportedFeature },
        { "symlink", ArchiveWriteReason.UnsupportedFeature }, { "fifo", ArchiveWriteReason.UnsupportedFeature },
        { "dos-folder-bit-on-file", ArchiveWriteReason.UnsupportedFeature }, { "reparse-point", ArchiveWriteReason.UnsupportedFeature },
        { "folder-as-regular-file", ArchiveWriteReason.UnsupportedFeature }, { "owner-lengths", ArchiveWriteReason.UnsupportedFeature },
        { "saturated-disk", ArchiveWriteReason.UnsupportedFeature }, { "padded-folder", ArchiveWriteReason.UnsupportedFeature },
    };

    // A damaged Deflate stream may fail in the decoder or as a checksum or length mismatch; every other case has one reason.
    private static readonly Dictionary<string, string> Because = new()
    {
        ["encrypted"] = "Encrypted ZIP members", ["strong-encryption"] = "Encrypted ZIP members",
        ["bzip2-method"] = "other than stored or Deflate", ["unknown-extra"] = "extra field (0xcafe)", ["repeated-extra"] = "repeats an extra field",
        ["code-page-name"] = "code page is unknown", ["invalid-utf8-name"] = "not valid UTF-8", ["self-extractor"] = "not contiguous",
        ["gap"] = "not contiguous", ["reordered"] = "not contiguous", ["trailing-data"] = "does not reach the end",
        ["fake-end-in-comment"] = "more than one possible end record", ["local-name"] = "local and central names differ",
        ["local-version"] = "local and central headers disagree", ["checksum"] = "checksum does not match", ["folder-with-data"] = "folder entry holds data",
        ["split"] = "Split or spanned", ["needed-host"] = "newer ZIP version", ["unknown-host"] = "host or attributes",
        ["central-access-time"] = "unexpected length", ["owner-version"] = "Unix owner field", ["ntfs-reserved"] = "NTFS time field",
        ["unneeded-zip64"] = "without a saturated header field", ["unsafe-name"] = "archive name rules",
        ["missing-descriptor"] = "local sizes or checksum disagree", ["local-times"] = "local and central extra fields disagree",
        ["code-page-comment"] = "code page is unknown", ["patched-data"] = "general-purpose flag", ["no-end"] = "end record is missing",
        ["decoy-end-in-comment"] = "signature follows the end record", ["zip64-locator-in-comment"] = "signature follows the end record",
        ["symlink"] = "link, device", ["fifo"] = "link, device", ["dos-folder-bit-on-file"] = "disagree with its name",
        ["reparse-point"] = "link, device", ["folder-as-regular-file"] = "disagree with its name", ["owner-lengths"] = "Unix owner field",
        ["saturated-disk"] = "Split or spanned", ["padded-folder"] = "folder entry holds data",
    };

    [Theory, MemberData(nameof(Refusals))]
    public void Every_refused_row_is_read_only_or_invalid_and_never_admitted(string shape, ArchiveWriteReason reason)
    {
        RawEntry plain = new("a.txt", Text);
        byte[] zip = shape switch
        {
            "encrypted" => Build([plain with { Flags = 1 }]),
            "strong-encryption" => Build([plain with { Flags = 0x40 }]),
            "bzip2-method" => Build([plain with { Method = 12, Data = Text }]),
            "unknown-extra" => Build([plain with { LocalExtra = Extra(0xcafe, []), CentralExtra = Extra(0xcafe, []) }]),
            "repeated-extra" => Build([plain with { LocalExtra = [.. Times(1, 5), .. Times(1, 5)], CentralExtra = [.. Times(1, 5), .. Times(1, 5)] }]),
            "code-page-name" => Build([plain with { NameBytes = [0x8e, (byte)'.', (byte)'t'] }]),
            "invalid-utf8-name" => Build([plain with { Flags = 0x800, NameBytes = [0xc3, 0x28] }]),
            "self-extractor" => Build([plain], prefix: "MZ self-extractor stub"u8.ToArray()),
            "gap" => Build([plain, plain with { Name = "b.txt" }], gap: [0, 0, 0, 0]),
            "reordered" => Build([plain, plain with { Name = "b.txt" }], reorder: true),
            "trailing-data" => Build([plain], trailing: [1, 2, 3]),
            "fake-end-in-comment" => FakeEnd(),
            "local-name" => Build([plain with { LocalNameBytes = "b.txt"u8.ToArray() }]),
            "local-version" => Build([plain with { LocalNeeded = 10 }]),
            "checksum" => Build([plain with { Crc = 0x12345678 }]),
            "truncated-deflate" => Truncated(),
            "folder-with-data" => Build([new("dir/", Text, Method: 0, External: 0x41ed0010)]),
            "split" => Build([plain], disk: 1),
            "needed-host" => Build([plain with { Needed = 0x0314 }]),
            "unknown-host" => Build([plain with { MadeBy = 0x0714 }]),
            "central-access-time" => Build([plain with { LocalExtra = Times(3, 5, 6), CentralExtra = Times(3, 5, 6) }]),
            "owner-version" => Build([plain with { LocalExtra = Owner(1, 1, version: 2), CentralExtra = Extra(0x7875, []) }]),
            "ntfs-reserved" => Build([plain with { LocalExtra = Ntfs(1, 2, 3, reserved: 7), CentralExtra = Ntfs(1, 2, 3, reserved: 7) }]),
            "unneeded-zip64" => Build([plain with { CentralExtra = Extra(0x0001, new byte[8]) }]),
            "unsafe-name" => Build([plain with { Name = "../escape.txt" }]),
            "missing-descriptor" => Build([plain with { ZeroLocalSizes = true }]),
            "local-times" => Build([plain with { LocalExtra = Times(1, 5), CentralExtra = Times(1, 6) }]),
            "code-page-comment" => Build([plain with { CommentBytes = [0x8e] }]),
            "patched-data" => Build([plain with { Flags = 0x20 }]),
            // A decoy end record in the comment that does not itself reach the end: .NET's reader would still take it.
            "decoy-end-in-comment" => Build([plain], comment: [.. EndRecord(), 0x41]),
            "zip64-locator-in-comment" => Build([plain], comment: [0x50, 0x4b, 0x06, 0x07, 0, 0, 0, 0]),
            "symlink" => Build([plain with { MadeBy = 0x0314, External = 0xa1ff0000 }]),
            "fifo" => Build([plain with { MadeBy = 0x0314, External = 0x11a40000 }]),
            "dos-folder-bit-on-file" => Build([plain with { MadeBy = 0x0014, External = 0x10 }]),
            "reparse-point" => Build([plain with { MadeBy = 0x0a14, External = 0x400 }]),
            "folder-as-regular-file" => Build([new("dir/", [], Method: 0, External: 0x81a40000)]),
            // UID length 3 in a five-byte field: the GID length byte would lie past the field (it crashed the worker before).
            "owner-lengths" => Build([plain with { LocalExtra = Extra(0x7875, [1, 3, 0, 0, 0]), CentralExtra = Extra(0x7875, []) }]),
            "saturated-disk" => Build([plain with { DiskStart = 0xffff, CentralExtra = Extra(0x0001, new byte[4]) }]),
            // An empty folder whose Deflate stream is padded with empty stored blocks (22 bytes for no data).
            "padded-folder" => Build([new("dir/", [], Method: 8, External: 0x41ed0010,
                Packed: [.. Enumerable.Repeat<byte[]>([0, 0, 0, 0xff, 0xff], 4).SelectMany(block => block), 3, 0])]),
            _ => Build([plain])[..^22],
        };
        var error = Assert.Throws<ArchiveWriteException>(() => Read(zip));
        Assert.Equal(reason, error.Reason);
        // Each case must be refused for the row it constructs, not for an incidental defect of the fixture.
        if (Because.TryGetValue(shape, out string? because)) Assert.Contains(because, error.Message, StringComparison.Ordinal);

        static byte[] EndRecord() { byte[] end = new byte[22]; BinaryPrimitives.WriteUInt32LittleEndian(end, StrictZipCatalog.EndSignature); return end; }
        static byte[] FakeEnd()
        {
            // A comment that ends with its own well-formed end record makes the real end ambiguous.
            byte[] fake = new byte[22]; BinaryPrimitives.WriteUInt32LittleEndian(fake, StrictZipCatalog.EndSignature);
            return Build([new("a.txt", Text)], comment: fake);
        }
        static byte[] Truncated()
        {
            byte[] good = Build([new("a.txt", Text)]);
            // Corrupt the last deflate block marker inside the member's own extent; sizes and framing stay consistent.
            int data = 30 + "a.txt".Length; good[data] ^= 0x07; return good;
        }
    }
}
