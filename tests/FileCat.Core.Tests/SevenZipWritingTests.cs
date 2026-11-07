using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class SevenZipWritingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly ArchiveTimestamp Modified = new(1700000000, 123456700);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void Codecs_create_independent_folders_exact_content_names_empty_members_and_times(int codecId, bool compressedHeader)
    {
        var codec = (SevenZipCodec)codecId;
        byte[] noise = new byte[(2 << 20) + 1]; new Random(42829299).NextBytes(noise);
        var bytes = new byte[][]
        {
            noise, Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("FileCat 7z container and codec boundary.\n", 20000))),
            [], [0], [.. noise.AsSpan(0, 65535), .. new byte[65537], .. noise.AsSpan(65535, 65537)],
        };
        var inputs = bytes.Select((data, id) => new SevenZipInputDescriptor(id, data.Length, Digest(data))).ToDictionary(input => input.Id);
        var entries = new List<SevenZipWriteEntry>
        {
            new("界-noise.bin", ArchiveMemberType.File, Modified, 0x1a4, 0, new(-1, 123456700), new(4294967296, 123456700)),
            new("text.txt", ArchiveMemberType.File, Modified, 0x180, 1),
            new("empty.bin", ArchiveMemberType.File, Modified, 0x1a4, 2),
            new("empty-dir", ArchiveMemberType.Directory, Modified, 0x1ed),
            new("nested", ArchiveMemberType.Directory, Modified, 0x1c0),
            new("nested/é.bin", ArchiveMemberType.File, new(0, 999999900), 0x1a0, 3),
            new("boundary.bin", ArchiveMemberType.File, Modified, 0x1a4, 4),
        };
        using var stage = new MemoryStream(); int opens = 0, disposes = 0;
        var result = SevenZipArchiveWriter.Create(stage, entries, inputs, id =>
        {
            opens++;
            return new ShortSource(bytes[id], () => disposes++);
        }, new(codec, DictionaryBytes: codec is SevenZipCodec.Lzma or SevenZipCodec.Lzma2 ? 1 << 20 : 8 << 20), Ct,
            compressedHeader ? SevenZipHeaderMode.Lzma2 : SevenZipHeaderMode.Raw);
        Assert.Equal(bytes.Length, opens); Assert.Equal(opens, disposes);
        Assert.Equal(stage.Length, stage.Position); Assert.Equal(stage.Length, result.PhysicalBytes);
        Assert.Equal(entries, result.Members.Select(member => member.Entry));
        Assert.Equal(compressedHeader ? SevenZipHeaderMode.Lzma2 : SevenZipHeaderMode.Raw, result.HeaderMode);
        byte[] archive = stage.ToArray(); Assert.Equal(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c }, archive[..6]);
        long offset = BinaryPrimitives.ReadInt64LittleEndian(archive.AsSpan(12)), headerBytes = BinaryPrimitives.ReadInt64LittleEndian(archive.AsSpan(20));
        Assert.Equal(archive.Length, 32 + offset + headerBytes);
        Assert.Equal(ArchiveCrc32.Compute(archive.AsSpan(12, 20)), BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(8)));
        Assert.Equal(ArchiveCrc32.Compute(archive.AsSpan((int)(32 + offset))), BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(28)));
        using var dir = new TempDir(); string path = Path.Combine(dir.Path, "owned.7z"); File.WriteAllBytes(path, archive);
        using var reader = SharpArchiveReader.Open(path);
        var warnings = new List<string>(); var actual = reader.List(warnings.Add, Ct).ToArray();
        Assert.Empty(warnings); Assert.Equal(entries.Count, actual.Length);
        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index]; var member = actual[index];
            Assert.Equal(entry.Name, member.Path.TrimEnd('/'));
            Assert.Equal(entry.Type == ArchiveMemberType.Directory ? MemberKind.Directory : MemberKind.File, member.Kind);
            Assert.Equal(entry.Modified.ToDateTimeOffset().UtcDateTime, member.ModifiedUtc);
            Assert.False(member.Encrypted);
            if (entry.Type == ArchiveMemberType.Directory) continue;
            byte[] expected = bytes[entry.InputId!.Value]; Assert.Equal(expected.Length, member.Size);
            using var content = reader.Open(member.Index, Ct); using var decoded = new MemoryStream(); content.CopyTo(decoded);
            Assert.Equal(expected, decoded.ToArray());
        }

        string? root = Environment.GetEnvironmentVariable("FILECAT_SEVENZIP_CODEC_EXPORT_ROOT");
        if (root is not null)
        {
            Assert.True(Path.IsPathFullyQualified(root)); string folder = Path.Combine(root, (compressedHeader ? "compressed-codec-" : "codec-") + codecId); Directory.CreateDirectory(folder);
            using (var output = new FileStream(Path.Combine(folder, "members.7z"), FileMode.CreateNew)) stage.WriteTo(output);
            var rows = entries.Select(entry => new
            {
                name = entry.Name, directory = entry.Type == ArchiveMemberType.Directory, mode = entry.Mode,
                length = entry.InputId is { } id ? bytes[id].Length : 0, sha256 = Digest(entry.InputId is { } inputId ? bytes[inputId] : []),
                mtime_seconds = entry.Modified.Seconds, mtime_nanos = entry.Modified.Nanoseconds,
                ctime_seconds = entry.Created?.Seconds, ctime_nanos = entry.Created?.Nanoseconds,
                atime_seconds = entry.Accessed?.Seconds, atime_nanos = entry.Accessed?.Nanoseconds,
            });
            File.WriteAllText(Path.Combine(folder, "members.json"), JsonSerializer.Serialize(rows));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_archives_and_directory_only_archives_have_no_phantom_streams(bool directory)
    {
        using var stage = new MemoryStream();
        SevenZipArchiveWriter.Create(stage, directory ? [new("dir", ArchiveMemberType.Directory, Modified, 0x1ed)] : [],
            new Dictionary<int, SevenZipInputDescriptor>(), _ => throw new InvalidOperationException(), new(SevenZipCodec.Copy), Ct);
        using var dir = new TempDir(); string path = Path.Combine(dir.Path, "empty.7z"); File.WriteAllBytes(path, stage.ToArray());
        using var reader = SharpArchiveReader.Open(path); var members = reader.List(message => Assert.Fail(message), Ct).ToArray();
        Assert.Equal(directory ? 1 : 0, members.Length);
        if (directory) Assert.Equal(MemberKind.Directory, members[0].Kind);
    }

    [Theory]
    [InlineData(0, 1, 8388608)]
    [InlineData(1, 4, 1048576)]
    [InlineData(1, 1, 1048577)]
    [InlineData(1, 1, 33554432)]
    [InlineData(3, 1, 1048576)]
    [InlineData(99, 0, 8388608)]
    public void Invalid_or_ineffective_options_refuse_before_stream_io(int codec, int level, int dictionary)
    {
        using var source = new ForbiddenRead(); using var stage = new MemoryStream();
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => SevenZipCodecEncoder.Encode(source, 10, stage, new((SevenZipCodec)codec, level, dictionary), Ct)).Reason);
        Assert.Equal(0, stage.Length);
    }

    [Theory]
    [InlineData(1, 8388608)]
    [InlineData(65, 8388608)]
    [InlineData(6, 2047)]
    [InlineData(6, 33554433)]
    [InlineData(6, 0)]
    [InlineData(0, 8388608)]
    public void Invalid_or_incomplete_ppmd_model_options_refuse_before_io(int order, int memory)
    {
        using var source = new ForbiddenRead(); using var stage = new MemoryStream();
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => SevenZipCodecEncoder.Encode(source, 10, stage,
            new(SevenZipCodec.Ppmd7, ModelOrder: order, ModelMemoryBytes: memory), Ct)).Reason);
        Assert.Equal(0, stage.Length);
    }

    [Fact]
    public void Canonically_equivalent_raw_names_remain_distinct_in_the_codec_primitive()
    {
        // Production namespace admission still has to resolve native aliases before enabling a profile. The codec
        // itself must never normalize names or merge distinct physical records during preservation/verification.
        var entries = new SevenZipWriteEntry[]
        {
            new("é.bin", ArchiveMemberType.File, Modified, 0x1a4, 0),
            new("e\u0301.bin", ArchiveMemberType.File, new(0, 999999900), 0x180, 1),
        };
        byte[][] bytes = [[1], [2]];
        var inputs = bytes.Select((data, id) => new SevenZipInputDescriptor(id, data.Length, Digest(data))).ToDictionary(x => x.Id);
        using var stage = new MemoryStream();
        SevenZipArchiveWriter.Create(stage, entries, inputs, id => new MemoryStream(bytes[id], false), new(SevenZipCodec.Copy), Ct);
        using var dir = new TempDir(); string path = Path.Combine(dir.Path, "names.7z"); File.WriteAllBytes(path, stage.ToArray());
        using var reader = SharpArchiveReader.Open(path); var actual = reader.List(message => Assert.Fail(message), Ct).ToArray();
        Assert.Equal(entries.Select(x => x.Name), actual.Select(x => x.Path));
        foreach (var member in actual)
        {
            using var content = reader.Open(member.Index, Ct); Assert.Equal(bytes[member.Index], Read(content));
        }

        static byte[] Read(Stream source) { using var result = new MemoryStream(); source.CopyTo(result); return result.ToArray(); }
    }

    [Theory]
    [InlineData("../escape", "safe", 1700000000, 0)]
    [InlineData("same", "same", 1700000000, 0)]
    [InlineData("a", "a/child", 1700000000, 0)]
    [InlineData("a", "b", 1700000000, 1)]
    [InlineData("a", "b", -11644473601L, 0)]
    public void Bad_names_collisions_and_unrepresentable_times_refuse_before_stage_io(string first, string second, long seconds, int nanos)
    {
        using var stage = new MemoryStream(); var input = new SevenZipInputDescriptor(0, 0, Digest([]));
        Assert.Throws<ArchiveWriteException>(() => SevenZipArchiveWriter.Create(stage,
            [new(first, ArchiveMemberType.File, new(seconds, nanos), 0x1a4, 0), new(second, ArchiveMemberType.File, Modified, 0x1a4, 0)],
            new Dictionary<int, SevenZipInputDescriptor> { [0] = input }, _ => throw new InvalidOperationException(), new(SevenZipCodec.Copy), Ct));
        Assert.Equal(0, stage.Length);
    }

    [Fact]
    public void Changed_source_digest_refuses_and_closes_its_lease()
    {
        using var stage = new MemoryStream(); int disposed = 0;
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => SevenZipArchiveWriter.Create(stage,
            [new("file", ArchiveMemberType.File, Modified, 0x1a4, 0)],
            new Dictionary<int, SevenZipInputDescriptor> { [0] = new(0, 3, Digest([4, 5, 6])) },
            _ => new ShortSource([1, 2, 3], () => disposed++), new(SevenZipCodec.Copy), Ct)).Reason);
        Assert.Equal(1, disposed); Assert.True(stage.CanWrite);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed class ShortSource(byte[] bytes, Action disposed) : MemoryStream(bytes, false)
    {
        private bool _disposed;
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(8191, buffer.Length)]);
        protected override void Dispose(bool disposing) { if (!_disposed) { _disposed = true; disposed(); } base.Dispose(disposing); }
    }
    private sealed class ForbiddenRead : MemoryStream
    {
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("No source IO is allowed.");
        public override int ReadByte() => throw new InvalidOperationException("No source IO is allowed.");
    }
}
