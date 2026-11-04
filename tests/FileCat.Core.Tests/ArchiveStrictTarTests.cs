using System.Formats.Tar;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveStrictTarTests
{
    [Theory]
    [InlineData(TarEntryFormat.Ustar)]
    [InlineData(TarEntryFormat.Pax)]
    public void Complete_catalog_preserves_order_duplicates_empty_folders_and_metadata(TarEntryFormat format)
    {
        byte[] bytes = Make(format, ("a.txt", "one"), ("a.txt", "two"), ("empty/", ""), ("É.txt", ""));
        using var input = new MemoryStream(bytes);
        var catalog = StrictTarCatalog.Read(input, TestContext.Current.CancellationToken);
        Assert.Equal(format, catalog.Format);
        Assert.Equal(new[] { "a.txt", "a.txt", "empty/", "É.txt" }, catalog.Members.Select(m => m.Name));
        Assert.Equal(new[] { 0, 1, 0, 0 }, catalog.Members.Select(m => m.Occurrence));
        Assert.Equal(ArchiveMemberType.Directory, catalog.Members[2].Type);
        Assert.All(catalog.Members, m =>
        {
            Assert.Equal(42, m.Uid);
            Assert.Equal(21, m.Gid);
            Assert.Equal("owner", m.UserName);
            Assert.Equal("group", m.GroupName);
            Assert.Equal(0x1ed, m.Mode);
        });
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), catalog.Revision);
        input.Position = 0;
        StrictTarCatalog.CompareDecoder(input, catalog, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Every_truncation_is_refused_including_after_the_last_member()
    {
        byte[] bytes = Make(TarEntryFormat.Pax, ("payload.txt", "hello"));
        for (int length = 0; length < bytes.Length; length++)
        {
            using var input = new MemoryStream(bytes, 0, length);
            Assert.Throws<ArchiveWriteException>(() => StrictTarCatalog.Read(input, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public void Unsupported_and_corrupt_members_cannot_be_deleted_to_gain_admission()
    {
        byte[] bytes = Make(TarEntryFormat.Ustar, ("safe", "hello"), ("unsupported", "bad"));
        int second = 1024;
        foreach (byte type in "12gLKSD"u8)
        {
            byte[] mutated = (byte[])bytes.Clone();
            mutated[second + 156] = type;
            Checksum(mutated.AsSpan(second, 512));
            using var input = new MemoryStream(mutated);
            Assert.Equal(ArchiveWriteReason.UnsupportedFeature,
                Assert.Throws<ArchiveWriteException>(() => StrictTarCatalog.Read(input, TestContext.Current.CancellationToken)).Reason);
        }
        bytes[second + 150] ^= 1;
        using var damaged = new MemoryStream(bytes);
        Assert.Equal(ArchiveWriteReason.InvalidArchive,
            Assert.Throws<ArchiveWriteException>(() => StrictTarCatalog.Read(damaged, TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public void Oversize_extension_is_rejected_before_its_payload_is_read_or_allocated()
    {
        byte[] bytes = Make(TarEntryFormat.Pax, ("safe", "hello"));
        WriteOctal(bytes.AsSpan(124, 12), 300_000_000);
        Checksum(bytes.AsSpan(0, 512));
        using var input = new MemoryStream(bytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(ArchiveWriteReason.BudgetExceeded,
            Assert.Throws<ArchiveWriteException>(() => StrictTarCatalog.Read(input, TestContext.Current.CancellationToken)).Reason);
        Assert.Equal(512, input.Position);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 256 << 10);
    }

    [Theory]
    [InlineData("SCHILY.xattr.user.test", "present")]
    [InlineData("GNU.sparse.size", "100")]
    [InlineData("mtime", "1700000000.123456789")]
    public void Unknown_metadata_and_unrepresentable_time_precision_are_read_only(string key, string value)
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "payload", new Dictionary<string, string> { [key] = value });
            entry.DataStream = new MemoryStream("data"u8.ToArray());
            writer.WriteEntry(entry);
        }
        byte[] bytes = output.ToArray();
        // .NET updates mtime when writing; replace an existing atime key instead to exercise precision admission.
        if (key == "mtime")
        {
            var needle = "mtime="u8;
            int start = bytes.AsSpan(512).IndexOf(needle) + 512 + needle.Length;
            int end = start + bytes.AsSpan(start).IndexOf((byte)'\n');
            if (end - start >= 10)
            {
                // Keep record length unchanged and introduce a nonrepresentable fractional tick.
                for (int i = start; i < end; i++) bytes[i] = (byte)'0';
                bytes[start + 1] = (byte)'.';
                bytes[end - 1] = (byte)'1';
            }
        }
        using var input = new MemoryStream(bytes);
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature,
            Assert.Throws<ArchiveWriteException>(() => StrictTarCatalog.Read(input, TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public void Invalid_utf8_nonzero_padding_and_trailing_archives_are_not_admitted()
    {
        byte[] original = Make(TarEntryFormat.Ustar, ("safe", "x"));
        byte[] utf8 = (byte[])original.Clone();
        utf8[0] = 0xff;
        Checksum(utf8.AsSpan(0, 512));
        using var badText = new MemoryStream(utf8);
        Assert.Throws<ArchiveWriteException>(() => StrictTarCatalog.Read(badText, TestContext.Current.CancellationToken));
        byte[] padding = (byte[])original.Clone();
        padding[513] = 1;
        using var badPadding = new MemoryStream(padding);
        Assert.Throws<ArchiveWriteException>(() => StrictTarCatalog.Read(badPadding, TestContext.Current.CancellationToken));
        using var concatenated = new MemoryStream(original.Concat(original).ToArray());
        Assert.Throws<ArchiveWriteException>(() => StrictTarCatalog.Read(concatenated, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Checksum_repaired_mutations_return_only_catalogs_or_controlled_refusals()
    {
        byte[] seed = Make(TarEntryFormat.Pax, ("safe", "x"));
        var random = new Random(7419);
        for (int round = 0; round < 1000; round++)
        {
            byte[] mutated = (byte[])seed.Clone();
            int offset = random.Next(mutated.Length);
            mutated[offset] ^= (byte)random.Next(1, 256);
            if (offset < 512) Checksum(mutated.AsSpan(0, 512));
            using var input = new MemoryStream(mutated);
            try
            {
                var catalog = StrictTarCatalog.Read(input, TestContext.Current.CancellationToken);
                input.Position = 0;
                StrictTarCatalog.CompareDecoder(input, catalog, TestContext.Current.CancellationToken);
            }
            catch (ArchiveWriteException) { }
        }
    }

    internal static byte[] Make(TarEntryFormat format, params (string Name, string Contents)[] members)
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, format, leaveOpen: true))
            foreach (var (name, contents) in members)
            {
                TarEntryType type = name.EndsWith('/') ? TarEntryType.Directory : TarEntryType.RegularFile;
                PosixTarEntry entry = format == TarEntryFormat.Ustar ? new UstarTarEntry(type, name) : new PaxTarEntry(type, name);
                entry.Uid = 42;
                entry.Gid = 21;
                entry.UserName = "owner";
                entry.GroupName = "group";
                entry.Mode = (UnixFileMode)0x1ed;
                entry.ModificationTime = DateTimeOffset.FromUnixTimeSeconds(1700000000);
                if (type == TarEntryType.RegularFile) entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(contents));
                writer.WriteEntry(entry);
            }
        return output.ToArray();
    }

    private static void WriteOctal(Span<byte> field, long value)
    {
        field.Clear();
        string octal = Convert.ToString(value, 8).PadLeft(field.Length - 1, '0');
        Encoding.ASCII.GetBytes(octal, field);
    }

    private static void Checksum(Span<byte> header)
    {
        header.Slice(148, 8).Fill((byte)' ');
        long sum = 0;
        foreach (byte b in header) sum += b;
        WriteOctal(header.Slice(148, 7), sum);
    }
}
