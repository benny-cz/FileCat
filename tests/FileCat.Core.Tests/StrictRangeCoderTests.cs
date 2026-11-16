using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using SharpCompress.Compressors.PPMd;

namespace FileCat.Core.Tests;

public sealed class StrictRangeCoderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public static IEnumerable<object[]> PpmdCases() => from profile in Enumerable.Range(0, 4)
        from size in new[] { 0, 1, 7, 257, 4093, 65553, 131089 } from noise in new[] { false, true } select new object[] { profile, size, noise };
    private static readonly (int Order, int Memory)[] Profiles = [(2, 1 << 11), (8, 1 << 16), (32, 1 << 20), (64, 4 << 20)];
    [Theory] [MemberData(nameof(PpmdCases))]
    public void PPMd_known_size_finishes_without_decoding_a_synthetic_end_symbol(int profile, int size, bool noise)
    {
        byte[] data = Data(size, noise); var vector = Encode(data, profile); using var source = new Chunked(vector.Packed);
        using var reader = new StrictPpmdReadStream(source, vector.Properties, vector.Packed.Length, size, Ct);
        using var output = new MemoryStream(); reader.CopyTo(output); Assert.Equal(data, output.ToArray()); Assert.True(reader.Complete);
        Assert.Equal(vector.Packed.Length, reader.CompressedBytesConsumed); Assert.Equal(-1, reader.ReadByte());
        reader.Dispose(); Assert.True(source.CanRead); ExportPpmd($"p-{profile}-{size}-{noise}", vector, size, true, data);
    }
    [Fact]
    public void Every_PPMd_compressed_prefix_is_a_controlled_refusal_with_a_borrowed_source()
    {
        byte[] data = Data(127, true); var vector = Encode(data, 1);
        for (int size = 0; size < vector.Packed.Length; size++)
        {
            var clipped = vector with { Packed = vector.Packed[..size] }; RefusePpmd(clipped, data.Length);
            ExportPpmd("prefix-" + size, clipped, data.Length, false, []);
        }
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void PPMd_trailing_input_bad_prefix_terminal_code_and_declared_sizes_refuse(int damage)
    {
        byte[] data = Data(257, true); var vector = Encode(data, 1); int length = data.Length;
        if (damage == 0) vector = vector with { Packed = [.. vector.Packed, 0] };
        if (damage == 1) vector.Packed[0] = 1;
        if (damage == 2) vector.Packed[^1] ^= 1;
        if (damage == 3) length = 0;
        if (damage == 4) length--;
        if (damage == 5) length++;
        RefusePpmd(vector, length); ExportPpmd("damaged-" + damage, vector, length, false, []);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(15)]
    [InlineData(16)] [InlineData(31)] [InlineData(127)] [InlineData(256)] [InlineData(4093)] [InlineData(65523)] [InlineData(65524)]
    [InlineData(65525)] [InlineData(131089)] [InlineData(1048611)]
    public void BCJ2_verifies_all_four_branches_terminal_code_and_complete_output(int size)
    {
        byte[] data = Data(size, true); byte[][] packed = Split(data); Stream[] sources = packed.Select(bytes => (Stream)new Chunked(bytes)).ToArray();
        try
        {
            using var reader = new StrictBcj2ReadStream(sources, packed.Select(bytes => (long)bytes.Length).ToArray(), data.Length, Ct);
            using var output = new MemoryStream(); reader.CopyTo(output); Assert.Equal(data, output.ToArray()); Assert.True(reader.Complete);
            Assert.Equal(packed.Select(bytes => (long)bytes.Length), reader.CompressedBytesConsumed); Assert.Equal(-1, reader.ReadByte());
            reader.Dispose(); Assert.All(sources, source => Assert.True(source.CanRead)); ExportBcj2("b-" + size, packed, data.Length, true, data);
        }
        finally { foreach (var source in sources) source.Dispose(); }
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void BCJ2_unfinished_range_extra_branches_and_incomplete_lengths_refuse(int damage)
    {
        byte[] data = Data(damage is 1 or 2 or 3 ? 0 : 4093, true); byte[][] packed = Split(data); int length = data.Length;
        if (damage == 0) packed[3] = [.. packed[3], 0];
        if (damage == 1) packed[3][0] = 1;
        if (damage == 2) packed[3][^1] = 1;
        if (damage == 3) packed[3] = packed[3][..^1];
        if (damage == 4) { packed[1] = [.. packed[1], 0, 0, 0, 0]; length += 4; }
        if (damage == 5) { packed[2] = [.. packed[2], 0, 0, 0, 0]; length += 4; }
        if (damage == 6) packed[1] = [.. packed[1], 1];
        if (damage == 7) packed[2] = packed[2][..^4];
        if (damage == 8) packed[0] = packed[0][..^1];
        Stream[] sources = packed.Select(bytes => (Stream)new Chunked(bytes)).ToArray();
        try
        {
            Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() =>
            { using var reader = new StrictBcj2ReadStream(sources, packed.Select(bytes => (long)bytes.Length).ToArray(), length, Ct); reader.CopyTo(Stream.Null); }).Reason);
            Assert.All(sources, source => Assert.True(source.CanRead)); ExportBcj2("b-damaged-" + damage, packed, length, false, []);
        }
        finally { foreach (var source in sources) source.Dispose(); }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void PPMd_encoder_and_decoder_model_storage_is_cleared_on_disposal(bool decoding)
    {
        byte[] data = Data(257, true); object model; Array states;
        using var output = new MemoryStream(); using var source = new MemoryStream(Encode(data, 0).Packed, false); IDisposable owned;
        if (decoding)
        {
            var reader = PpmdStream.CreateStrict(new(Properties(0)), source, data.Length, Ct); reader.ReadExactly(new byte[data.Length]); owned = reader;
            model = Field<object>(Field<object>(reader, "_modelH"), "_model");
        }
        else { var writer = new Ppmd7zEncoderStream(output, Profiles[0].Order, Profiles[0].Memory, Ct); writer.Write(data); owned = writer; model = Field<object>(writer, "_model"); }
        states = Field<Array>(model, "_states"); Assert.Contains(states.Cast<object>(), value => !value.Equals(Activator.CreateInstance(value.GetType())));
        owned.Dispose(); Assert.All(states.Cast<object>(), value => Assert.Equal(Activator.CreateInstance(value.GetType()), value)); Assert.Empty(Field<Array>(model, "_states").Cast<object>());
        Assert.True(source.CanRead); Assert.True(output.CanWrite);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void BCJ2_resource_limits_are_budget_refusals_before_any_source_read(int extent)
    {
        using var a = new MemoryStream(); using var b = new MemoryStream(); using var c = new MemoryStream(); using var d = new MemoryStream(new byte[5]);
        long length = extent == 0 ? ArchiveWritePolicy.MaxPayloadBytes + 1 : 0;
        long[] lengths = [extent == 1 ? ArchiveWritePolicy.MaxPayloadBytes + 1 : 0, 0, 0, extent == 2 ? ArchiveWritePolicy.MaxPhysicalBytes + 1 : 5];
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => new StrictBcj2ReadStream([a, b, c, d], lengths, length, Ct)).Reason);
        Assert.Equal(0, a.Position); Assert.Equal(0, b.Position); Assert.Equal(0, c.Position); Assert.Equal(0, d.Position);
    }
    [Fact]
    public void PPMd_model_growth_clears_the_superseded_state_array()
    {
        var range = new Ppmd7zEncodingState(Stream.Null, Ct); var model = new PpmdModelH<Ppmd7zEncodingRange> { Coder = new(range), EncodingHints = range };
        model.Init(2, 2048); model.EncodeSymbol(42); Array old = Field<Array>(model, "_states"); model.Init(2, 4096);
        Assert.All(old.Cast<object>(), value => Assert.Equal(Activator.CreateInstance(value.GetType()), value)); model.Clear();
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void PPMd_properties_and_model_ceiling_refuse_before_decoder_allocation(int damage)
    {
        byte[] properties = Properties(0);
        if (damage == 0) properties = properties[..^1];
        if (damage == 1) properties[0] = 1;
        if (damage == 2) BinaryPrimitives.WriteInt32LittleEndian(properties.AsSpan(1), 2047);
        if (damage == 3) BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(1), uint.MaxValue);
        using var source = new MemoryStream(new byte[5]); Assert.Throws<ArchiveWriteException>(() => new StrictPpmdReadStream(source, properties, 5, 0, Ct)); Assert.Equal(0, source.Position);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Cancellation_during_range_initialization_closes_owned_buffers_and_retains_sources(bool bcj2)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct); using var input = new Chunked(new byte[5], stop.Cancel);
        if (bcj2)
        {
            using var a = new MemoryStream(); using var b = new MemoryStream(); using var c = new MemoryStream();
            Assert.ThrowsAny<OperationCanceledException>(() => new StrictBcj2ReadStream([a, b, c, input], [0, 0, 0, 5], 0, stop.Token));
        }
        else Assert.ThrowsAny<OperationCanceledException>(() => new StrictPpmdReadStream(input, Properties(0), 5, 0, stop.Token));
        Assert.True(input.CanRead);
    }
    private sealed record Vector(byte[] Properties, byte[] Packed);
    private static Vector Encode(byte[] bytes, int profile)
    { using var output = new MemoryStream(); using (var writer = new Ppmd7zEncoderStream(output, Profiles[profile].Order, Profiles[profile].Memory, Ct)) writer.Write(bytes); return new(Properties(profile), output.ToArray()); }
    private static byte[] Properties(int profile)
    { byte[] bytes = new byte[5]; bytes[0] = (byte)Profiles[profile].Order; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), Profiles[profile].Memory); return bytes; }
    private static byte[][] Split(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, false); var outputs = Enumerable.Range(0, 4).Select(_ => new MemoryStream()).ToArray();
        try { SevenZipBcj2Encoder.Encode(input, bytes.Length, outputs, Ct); return outputs.Select(output => output.ToArray()).ToArray(); }
        finally { foreach (var output in outputs) output.Dispose(); }
    }
    private static byte[] Data(int size, bool noise)
    {
        byte[] bytes = new byte[size]; new Random(42829314).NextBytes(bytes);
        const string pattern = "PPMd range finalization and complete verification.\0";
        if (!noise) for (int i = 0; i < size; i++) bytes[i] = (byte)pattern[i % pattern.Length];
        for (int i = 8; i + 12 < size; i += 31)
        {
            bytes[i] = (byte)(i % 2 == 0 ? 0xe8 : 0xe9); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i + 1), (i + 97) % size - (i + 5));
            bytes[i + 6] = 0x0f; bytes[i + 7] = 0x85; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i + 8), (i + 53) % size - (i + 12));
        }
        return bytes;
    }
    private static void RefusePpmd(Vector vector, int length)
    { using var input = new Chunked(vector.Packed); Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() => { using var reader = new StrictPpmdReadStream(input, vector.Properties, vector.Packed.Length, length, Ct); reader.CopyTo(Stream.Null); }).Reason); Assert.True(input.CanRead); }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static string? ExportRoot => Environment.GetEnvironmentVariable("FILECAT_RANGE_END_EXPORT_ROOT");
    private static void ExportPpmd(string name, Vector vector, int length, bool accepted, byte[] data)
    {
        if (ExportRoot is not { Length: > 0 } root) return; string directory = Path.Combine(root, "ppmd", name); Directory.CreateDirectory(directory);
        using var packet = new MemoryStream(); using var writer = new BinaryWriter(packet); writer.Write(vector.Properties); writer.Write((long)length); writer.Write(vector.Packed.Length); writer.Write(vector.Packed); writer.Flush();
        File.WriteAllBytes(Path.Combine(directory, "packet.bin"), packet.ToArray()); ExportExpected(directory, length, accepted, data);
    }
    private static void ExportBcj2(string name, byte[][] packed, int length, bool accepted, byte[] data)
    {
        if (ExportRoot is not { Length: > 0 } root) return; string directory = Path.Combine(root, "bcj2", name); Directory.CreateDirectory(directory);
        using var packet = new MemoryStream(); using var writer = new BinaryWriter(packet); foreach (byte[] bytes in packed) writer.Write(bytes.Length); foreach (byte[] bytes in packed) writer.Write(bytes); writer.Flush();
        File.WriteAllBytes(Path.Combine(directory, "packet.bin"), packet.ToArray()); ExportExpected(directory, length, accepted, data);
    }
    private static void ExportExpected(string directory, int length, bool accepted, byte[] data) => File.WriteAllText(Path.Combine(directory, "expected.json"),
        System.Text.Json.JsonSerializer.Serialize(new { synthetic_public_vector = true, accepted, length, sha256 = Convert.ToHexString(SHA256.HashData(data)) }));
    private sealed class Chunked(byte[] bytes, Action? afterRead = null) : Stream
    {
        private readonly MemoryStream inner = new(bytes, false);
        public override int Read(Span<byte> buffer) { int n = inner.Read(buffer[..Math.Min(buffer.Length, 7)]); afterRead?.Invoke(); return n; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
