using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Archives.Rar;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class Rar5WritingTests
{
    private static readonly ArchiveTimestamp Modified = new(1700000000, 123456789);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Crc_uses_the_standard_ieee_polynomial_and_incremental_empty_semantics()
    {
        Assert.Equal(0u, ArchiveCrc32.Compute([]));
        Assert.Equal(0xcbf43926u, ArchiveCrc32.Compute("123456789"u8));
        var crc = new ArchiveCrc32(); crc.Append("123"u8); crc.Append([]); crc.Append("456789"u8);
        Assert.Equal(0xcbf43926u, crc.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void All_methods_create_multiple_exact_members_directories_and_boundary_payloads(int methodId)
    {
        var method = (Rar5Compression)methodId;
        var random = new Random(42829297); byte[] noise = new byte[1 << 20]; random.NextBytes(noise);
        var fixtures = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["zeros"] = new byte[1 << 20], ["noise"] = noise, ["tiny"] = [0, 255, 17], ["empty"] = [],
            ["alphabet"] = Enumerable.Range(0, 1 << 18).Select(i => (byte)i).ToArray(),
            ["text"] = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("FileCat independent bounded RAR5 compression.\n", 20000))),
            ["boundary"] = [.. noise.AsSpan(0, Rar5Encoder.BlockBytes - 1), .. new byte[8194], .. noise.AsSpan(0, Rar5Encoder.BlockBytes + 1)],
            ["periodic"] = Enumerable.Range(0, 1004000).Select(i => (byte)(i % 251)).ToArray(),
            ["effort"] = Encoding.ASCII.GetBytes("abc" + new string('A', 60) + "abd" + new string('B', 60) + "abc" + new string('C', 60) + "abe" + new string('D', 60) + "abc" + new string('A', 60)),
        };
        var inputs = fixtures.Values.Select((bytes, id) => new Rar5InputDescriptor(id, bytes.Length, Digest(bytes))).ToDictionary(x => x.Id);
        var data = fixtures.Values.ToArray();
        var entries = fixtures.Keys.Select((label, id) => new Rar5WriteEntry("界-" + label + ".bin", ArchiveMemberType.File, Modified, 0x1a4, id)).ToList();
        entries.Add(new("empty-dir", ArchiveMemberType.Directory, Modified, 0x1ed));
        entries.Add(new("nested", ArchiveMemberType.Directory, Modified, 0x1c0));
        entries.Add(new("nested/é.txt", ArchiveMemberType.File, new ArchiveTimestamp(0, 999999999), 0x180, 2));
        entries.Add(new("past", ArchiveMemberType.Directory, new(-1, 123456700), 0x1c0));
        entries.Add(new("future", ArchiveMemberType.Directory, new(4294967296, 123456700), 0x1ed));
        using var stage = new MemoryStream(); int opened = 0, disposed = 0;
        var manifest = Rar5ArchiveWriter.Create(stage, entries, inputs, id =>
        {
            opened++;
            return new ShortSource(data[id], () => disposed++);
        }, method, Ct);
        Assert.Equal(2 * (fixtures.Count + 1), opened); Assert.Equal(opened, disposed);
        Assert.True(stage.CanWrite); Assert.Equal(stage.Length, manifest.PhysicalBytes);
        Assert.Equal(entries, manifest.Members.Select(x => x.Entry));
        stage.Position = 0;
        var raw = RarArchive.Read([stage], Ct);
        Assert.Empty(raw.Warnings);
        Assert.Equal(manifest.Members.Select(x => x.Content.Method), raw.Members.Select(x => x.Method));
        using var reader = RarReader.Open([new MemoryStream(stage.ToArray(), writable: false)], false, Ct);
        var warnings = new List<string>(); var actual = reader.List(warnings.Add, Ct).ToArray();
        Assert.Empty(warnings); Assert.Equal(entries.Select(x => x.Name), actual.Select(x => x.Path));
        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index]; var content = manifest.Members[index].Content;
            Assert.Equal(entry.Modified.Seconds * TimeSpan.TicksPerSecond + entry.Modified.Nanoseconds / 100 + DateTime.UnixEpoch.Ticks, actual[index].ModifiedUtc!.Value.Ticks);
            Assert.Equal(entry.Type == ArchiveMemberType.File ? MemberKind.File : MemberKind.Directory, actual[index].Kind);
            Assert.False(actual[index].Encrypted);
            Assert.Equal(entry.InputId is { } id ? data[id].Length : 0, actual[index].Size);
            using var stream = reader.Open(index, Ct); using var decoded = new MemoryStream(); stream.CopyTo(decoded);
            byte[] expected = entry.InputId is { } inputId ? data[inputId] : [];
            Assert.Equal(expected, decoded.ToArray()); Assert.Equal(Digest(expected), content.Sha256);
            Assert.Equal(expected.Length == 0 ? 0 : (int)method, content.Method);
            Assert.InRange(content.Comparisons, 0, (long)expected.Length * 4 * (1 << Math.Max(0, (int)method - 1)));
            if (method != Rar5Compression.Store && expected.Length > 1 << 18 && entry.Name != "界-noise.bin")
                Assert.True(content.PackedLength < expected.Length / 2);
        }

        // Optional owned-fixture export supports a separate native reader, not a production code path.
        string? root = Environment.GetEnvironmentVariable("FILECAT_RAR5_CODEC_EXPORT_ROOT");
        if (root is not null)
        {
            Assert.True(Path.IsPathFullyQualified(root));
            string folder = Path.Combine(root, "method-" + (int)method); Directory.CreateDirectory(folder);
            string archive = Path.Combine(folder, "members.rar"); using (var file = new FileStream(archive, FileMode.CreateNew)) stage.WriteTo(file);
            var rows = entries.Select((entry, index) => new
            {
                name = entry.Name, directory = entry.Type == ArchiveMemberType.Directory, mode = entry.Mode,
                mtime_seconds = entry.Modified.Seconds, mtime_nanos = entry.Modified.Nanoseconds,
                length = manifest.Members[index].Content.Length, sha256 = manifest.Members[index].Content.Sha256,
                method = manifest.Members[index].Content.Method,
            });
            File.WriteAllText(Path.Combine(folder, "members.json"), JsonSerializer.Serialize(rows));
        }
    }

    [Fact]
    public void Presets_change_actual_search_effort_and_compressed_bytes()
    {
        byte[] data = Encoding.ASCII.GetBytes("abc" + new string('A', 60) + "abd" + new string('B', 60) + "abc" + new string('C', 60) + "abe" + new string('D', 60) + "abc" + new string('A', 60));
        using var fast = new MemoryStream(); using var normal = new MemoryStream();
        var first = Rar5Encoder.Encode(new MemoryStream(data), data.Length, fast, Rar5Compression.Fastest, Ct);
        var second = Rar5Encoder.Encode(new MemoryStream(data), data.Length, normal, Rar5Compression.Normal, Ct);
        Assert.True(second.Comparisons > first.Comparisons);
        Assert.True(second.PackedLength < first.PackedLength);
        Assert.NotEqual(fast.ToArray(), normal.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8589934593L)]
    public void Invalid_lengths_refuse_before_reading_or_writing(long length)
    {
        using var source = new ForbiddenRead(); using var output = new MemoryStream();
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => Rar5Encoder.Encode(source, length, output, Rar5Compression.Normal, Ct)).Reason);
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Truncated_or_extended_inputs_are_not_accepted(int declared)
    {
        using var output = new MemoryStream();
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => Rar5Encoder.Encode(new MemoryStream([1, 2, 3]), declared, output, Rar5Compression.Normal, Ct)).Reason);
    }

    [Fact]
    public void Two_pass_writer_rejects_changed_content_and_disposes_both_leases()
    {
        byte[] bytes = [1, 2, 3]; int opened = 0, disposed = 0; using var stage = new MemoryStream();
        var input = new Rar5InputDescriptor(0, bytes.Length, Digest(bytes));
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => Rar5ArchiveWriter.Create(stage,
            [new("file", ArchiveMemberType.File, Modified, 0x1a4, 0)], new Dictionary<int, Rar5InputDescriptor> { [0] = input },
            _ => new ShortSource(++opened == 1 ? bytes : [4, 5, 6], () => disposed++), Rar5Compression.Best, Ct)).Reason);
        Assert.Equal(2, opened); Assert.Equal(2, disposed); Assert.True(stage.CanWrite);
    }

    [Theory]
    [InlineData("../escape", "safe", 1700000000, 0)]
    [InlineData("same", "same", 1700000000, 0)]
    [InlineData("a", "a/child", 1700000000, 0)]
    [InlineData("a", "b", -1, 1)]
    [InlineData("a", "b", -11644473601L, 0)]
    public void Invalid_namespace_or_unrepresentable_time_refuses_before_touching_stage(string first, string second, long seconds, int nanos)
    {
        using var stage = new MemoryStream(); var input = new Rar5InputDescriptor(0, 0, Digest([]));
        Assert.Throws<ArchiveWriteException>(() => Rar5ArchiveWriter.Create(stage,
            [new(first, ArchiveMemberType.File, new(seconds, nanos), 0x1a4, 0), new(second, ArchiveMemberType.File, Modified, 0x1a4, 0)],
            new Dictionary<int, Rar5InputDescriptor> { [0] = input }, _ => throw new InvalidOperationException("No source may open during metadata preflight."), Rar5Compression.Normal, Ct));
        Assert.Equal(0, stage.Length);
    }

    [Theory]
    [InlineData(-11644473600L, 0)]
    [InlineData(-1, 123456700)]
    [InlineData(4294967296L, 123456700)]
    public void Filetime_range_is_exact_instead_of_rounding_or_overflowing(long seconds, int nanos)
    {
        using var stage = new MemoryStream(); var time = new ArchiveTimestamp(seconds, nanos);
        Rar5ArchiveWriter.Create(stage, [new("dir", ArchiveMemberType.Directory, time, 0x1ed)],
            new Dictionary<int, Rar5InputDescriptor>(), _ => throw new InvalidOperationException(), Rar5Compression.Store, Ct);
        stage.Position = 0; var archive = RarArchive.Read([stage], Ct);
        Assert.Empty(archive.Warnings); Assert.Equal(time.ToDateTimeOffset().UtcDateTime, Assert.Single(archive.Members).ModifiedUtc);
    }

    [Fact]
    public void Cancellation_before_encoding_touches_neither_stream()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var source = new ForbiddenRead(); using var stage = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => Rar5Encoder.Encode(source, 32, stage, Rar5Compression.Best, cancellation.Token));
        Assert.Equal(0, stage.Length);
    }

    [Fact]
    public void Literal_and_block_boundaries_and_hash_collisions_decode_exactly()
    {
        var random = new Random(42829298);
        int[] lengths = [0, 1, 2, 3, 9, 10, 254, 255, 256, 32767, 32768, 32769, 65535, 65536, 65537];
        foreach (int length in lengths)
        {
            byte[] bytes = new byte[length]; random.NextBytes(bytes);
            for (int at = 512; at + 256 <= length; at += 1024) bytes.AsSpan(at, 256).Fill((byte)at);
            for (int preset = 0; preset <= 5; preset++)
            {
                Ct.ThrowIfCancellationRequested(); using var stage = new MemoryStream();
                var input = new Rar5InputDescriptor(0, length, Digest(bytes));
                Rar5ArchiveWriter.Create(stage, [new("boundary.bin", ArchiveMemberType.File, Modified, 0x1a4, 0)],
                    new Dictionary<int, Rar5InputDescriptor> { [0] = input }, _ => new MemoryStream(bytes, false), (Rar5Compression)preset, Ct);
                stage.Position = 0; using var reader = RarReader.Open([stage], false, Ct);
                Assert.Single(reader.List(message => Assert.Fail(message), Ct));
                using var content = reader.Open(0, Ct); using var decoded = new MemoryStream(); content.CopyTo(decoded);
                Assert.Equal(bytes, decoded.ToArray());
            }
        }
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class ShortSource(byte[] bytes, Action onDispose) : MemoryStream(bytes, writable: false)
    {
        private bool _disposed;
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(17, buffer.Length)]);
        protected override void Dispose(bool disposing)
        {
            if (!_disposed) { _disposed = true; onDispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed class ForbiddenRead : MemoryStream
    {
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Input must not be touched.");
        public override int ReadByte() => throw new InvalidOperationException("Input must not be touched.");
    }
}
