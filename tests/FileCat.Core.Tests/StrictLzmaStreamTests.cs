using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using SharpCompress.Compressors.LZMA;

namespace FileCat.Core.Tests;

public sealed class StrictLzmaStreamTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public static IEnumerable<object[]> Cases() => from mode in Enumerable.Range(0, 3)
        from size in new[] { 0, 1, 2, 5, 257, 4095, 4096, 65524, 65553, 131089 }
        from noise in new[] { false, true } select new object[] { mode, size, noise };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Marked_markerless_and_LZMA2_streams_require_exact_complete_extents(int mode, int size, bool noise)
    {
        byte[] data = Data(size, noise); var vector = Encode(data, mode);
        foreach (int chunk in new[] { 1, 65524 })
        {
            using var input = new Source(vector.Packed, chunk: 7);
            using var decoder = new StrictLzmaReadStream(input, vector.Properties, mode == 2, vector.Packed.Length, data.Length, Ct);
            using var output = new MemoryStream(); byte[] buffer = new byte[chunk]; int n;
            while ((n = decoder.Read(buffer)) != 0) output.Write(buffer, 0, n);
            Assert.Equal(data, output.ToArray()); Assert.True(decoder.Complete); Assert.Equal(mode == 1, decoder.FinishedWithMarker);
            Assert.Equal(vector.Packed.Length, decoder.CompressedBytesConsumed); Assert.Equal(data.Length, decoder.Position);
            Assert.InRange(input.MaximumRead, 1, ArchiveIoAuthority.MaximumReadBytes); Assert.True(input.CanRead);
            Assert.Equal(0, decoder.Read(buffer));
        }
        Export($"valid-{mode}-{size}-{noise}", vector, mode, data.Length, true, data);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Every_truncated_prefix_and_extra_packed_byte_refuses_even_after_declared_output_is_available(int mode)
    {
        byte[] data = Data(257, false); var original = Encode(data, mode);
        for (int length = 0; length < original.Packed.Length; length++)
        {
            var shortVector = original with { Packed = original.Packed[..length] };
            Refuse(shortVector, mode, data.Length);
            Export($"truncated-{mode}-{length}", shortVector, mode, data.Length, false, []);
        }
        foreach (byte[] suffix in new byte[][] { [0], [255], new byte[16], original.Packed })
        {
            var extra = original with { Packed = [.. original.Packed, .. suffix] };
            Refuse(extra, mode, data.Length);
            Export($"trailing-{mode}-{suffix.Length}-{suffix[0]}", extra, mode, data.Length, false, []);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void A_source_shorter_or_longer_than_its_packed_grant_cannot_be_marked_complete(int mode)
    {
        byte[] data = Data(4096, false); var vector = Encode(data, mode);
        foreach (byte[] bytes in new[] { vector.Packed[..^1], (byte[])[.. vector.Packed, 1] })
        {
            using var input = new Source(bytes); using var output = new MemoryStream();
            Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() =>
            {
                using var reader = new StrictLzmaReadStream(input, vector.Properties, mode == 2, vector.Packed.Length, data.Length, Ct);
                reader.CopyTo(output);
            }).Reason);
            Assert.True(input.CanRead);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Wrong_output_sizes_do_not_substitute_for_a_valid_range_and_coder_ending(int mode)
    {
        byte[] data = Data(4096, false); var vector = Encode(data, mode);
        foreach (int size in new[] { 0, 1, data.Length - 1, data.Length + 1 })
        {
            Refuse(vector, mode, size); Export($"size-{mode}-{size}", vector, mode, size, false, []);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void Range_initialization_and_terminal_code_are_verified(int mode)
    {
        byte[] data = Data(257, false); var vector = Encode(data, mode);
        byte[] prefix = (byte[])vector.Packed.Clone(); prefix[0] = 1;
        Refuse(vector with { Packed = prefix }, mode, data.Length); Export($"prefix-{mode}", vector with { Packed = prefix }, mode, data.Length, false, []);
        byte[] terminal = (byte[])vector.Packed.Clone(); terminal[^1] ^= 1;
        Refuse(vector with { Packed = terminal }, mode, data.Length); Export($"terminal-{mode}", vector with { Packed = terminal }, mode, data.Length, false, []);
    }

    [Fact]
    public void A_short_repeat_cannot_read_uninitialized_dictionary_history()
    {
        var vector = new Vector([0x5d, 0, 0x10, 0, 0], [0, 0xc0, 0, 0, 0]);
        Refuse(vector, 0, 1); Export("short-repeat-without-history", vector, 0, 1, false, []);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LZMA2_raw_continuation_and_dictionary_reset_chunks_are_complete(bool reset)
    {
        byte[] data = Data(257, false); int size = data.Length - 1;
        var vector = new Vector([0], [1, (byte)(size >> 8), (byte)size, .. data,
            reset ? (byte)1 : (byte)2, (byte)(size >> 8), (byte)size, .. data, 0]);
        using var input = new Source(vector.Packed, chunk: 7); using var output = new MemoryStream();
        using var reader = new StrictLzmaReadStream(input, vector.Properties, true, vector.Packed.Length, 2 * data.Length, Ct);
        reader.CopyTo(output); byte[] expected = [.. data, .. data]; Assert.Equal(expected, output.ToArray()); Assert.True(reader.Complete);
        Export("raw-chunks-reset-" + reset, vector, 2, expected.Length, true, expected);
    }

    [Fact]
    public void A_LZMA_end_marker_inside_an_LZMA2_chunk_is_invalid()
    {
        byte[] data = Data(257, false); var marked = Encode(data, 1); int unpacked = data.Length - 1, packed = marked.Packed.Length - 1;
        var vector = new Vector([0], [(byte)(0xe0 | (unpacked >> 16)), (byte)(unpacked >> 8), (byte)unpacked,
            (byte)(packed >> 8), (byte)packed, marked.Properties[0], .. marked.Packed, 0]);
        Refuse(vector, 2, data.Length); Export("LZMA-marker-in-LZMA2", vector, 2, data.Length, false, []);
    }

    public static IEnumerable<object[]> BadLzma2Headers()
    {
        yield return [(byte[])[2, 0, 0, 42, 0], 1];
        yield return [(byte[])[3], 1];
        yield return [(byte[])[0x7f], 1];
        yield return [(byte[])[1], 1];
        yield return [(byte[])[1, 0], 1];
        yield return [(byte[])[0xe0, 0, 0, 0, 3, 0x5d, 0, 0, 0, 0, 0], 1];
        yield return [(byte[])[0xe0, 0, 0, 0, 4, 8, 0, 0, 0, 0, 0, 0], 1];
        yield return [(byte[])[1, 0, 0, 42, 0xa0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0], 2];
    }
    [Theory]
    [MemberData(nameof(BadLzma2Headers))]
    public void LZMA2_reset_property_and_chunk_size_rules_are_checked(byte[] packed, int size)
    { var vector = new Vector([0], packed); Refuse(vector, 2, size); Export("bad-LZMA2-" + Convert.ToHexString(packed), vector, 2, size, false, []); }

    [Fact]
    public void Small_dictionary_claims_use_the_SDK_minimum_without_mutating_source_properties()
    {
        byte[] data = Data(257, false); var vector = Encode(data, 0); byte[] properties = (byte[])vector.Properties.Clone(); properties.AsSpan(1).Clear();
        using var input = new Source(vector.Packed); using var reader = new StrictLzmaReadStream(input, properties, false, vector.Packed.Length, data.Length, Ct);
        using var output = new MemoryStream(); reader.CopyTo(output); Assert.Equal(data, output.ToArray()); Assert.True(reader.Complete);
        Assert.Equal(new byte[] { vector.Properties[0], 0, 0, 0, 0 }, properties);
        Export("minimum-dictionary", vector with { Properties = properties }, 0, data.Length, true, data);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Dictionary_policy_is_checked_before_reading_or_allocating_claimed_windows(bool lzma2)
    {
        byte[] properties = lzma2 ? [40] : [0x5d, 0, 0, 0, 0x40]; using var input = new Source(new byte[5]);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => new StrictLzmaReadStream(input, properties, lzma2, 5, 1, Ct)).Reason);
        Assert.Equal(0, input.BytesRead); Assert.True(input.CanRead);
    }

    [Fact]
    public void Cancellation_closes_owned_buffers_and_leaves_the_parent_source_open()
    {
        byte[] data = Data(131089, true); var vector = Encode(data, 1); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var input = new Source(vector.Packed, afterRead: () => stop.Cancel());
        Assert.ThrowsAny<OperationCanceledException>(() => { using var reader = new StrictLzmaReadStream(input, vector.Properties, false, vector.Packed.Length, data.Length, stop.Token); });
        Assert.True(input.CanRead);
    }

    [Fact]
    public void Strict_dictionary_storage_is_cleared_before_returning_to_the_shared_pool()
    {
        byte[] data = Data(257, false); var vector = Encode(data, 0); using var input = new Source(vector.Packed);
        var decoder = LzmaStream.CreateStrict(vector.Properties, input, vector.Packed.Length, data.Length, false);
        var window = typeof(LzmaStream).GetField("_outWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(decoder)!;
        byte[] bytes = (byte[])window.GetType().GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        byte[] output = new byte[data.Length]; Assert.Equal(data.Length, decoder.Read(output)); Assert.Contains(data[0], bytes);
        decoder.Dispose(); Assert.All(bytes, value => Assert.Equal(0, value)); Assert.True(input.CanRead);
    }

    private sealed record Vector(byte[] Properties, byte[] Packed);
    private static Vector Encode(byte[] data, int mode)
    {
        using var output = new MemoryStream(); byte[] properties;
        if (mode == 2)
        { using var encoder = new Lzma2EncoderStream(output, 1 << 20, 32); properties = (byte[])encoder.Properties.Clone(); encoder.Write(data); }
        else
        { using var encoder = LzmaStream.Create(new LzmaEncoderProperties(mode == 1, 1 << 20, 32), false, output); properties = (byte[])encoder.Properties.Clone(); encoder.Write(data); }
        return new(properties, output.ToArray());
    }
    private static byte[] Data(int size, bool noise)
    {
        byte[] data = new byte[size]; new Random(42829310).NextBytes(data);
        const string pattern = "FileCat exact range endings.\n";
        if (!noise) for (int index = 0; index < size; index++) data[index] = (byte)pattern[index % pattern.Length];
        return data;
    }
    private static void Refuse(Vector vector, int mode, int length)
    {
        using var input = new Source(vector.Packed); using var output = new MemoryStream();
        Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() =>
        { using var reader = new StrictLzmaReadStream(input, vector.Properties, mode == 2, vector.Packed.Length, length, Ct); reader.CopyTo(output); }).Reason);
        Assert.True(input.CanRead);
    }
    private static void Export(string name, Vector vector, int mode, int length, bool accepted, byte[] data)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_LZMA_END_EXPORT_ROOT") is not { Length: > 0 } root) return;
        string directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(root)!, "runtime-lzma.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            runtime_directory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        }));
        byte[] header = new byte[14]; header[0] = mode == 2 ? (byte)1 : (byte)0; header[1] = (byte)vector.Properties.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(2), (ulong)length); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(10), (uint)vector.Packed.Length);
        File.WriteAllBytes(Path.Combine(directory, "vector.bin"), [.. header, .. vector.Properties, .. vector.Packed]);
        File.WriteAllText(Path.Combine(directory, "expected.json"), System.Text.Json.JsonSerializer.Serialize(new { accepted, length, mode,
            sha256 = accepted ? Convert.ToHexString(SHA256.HashData(data)) : null, expected_marker = mode == 1 }));
    }
    private sealed class Source(byte[] bytes, int chunk = 65524, Action? afterRead = null) : Stream
    {
        private int _position; private bool _disposed;
        internal long BytesRead; internal int MaximumRead;
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); MaximumRead = Math.Max(MaximumRead, buffer.Length);
            int n = Math.Min(Math.Min(buffer.Length, chunk), bytes.Length - _position);
            bytes.AsSpan(_position, n).CopyTo(buffer); _position += n; BytesRead += n; afterRead?.Invoke(); return n;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        protected override void Dispose(bool disposing) { if (disposing) _disposed = true; base.Dispose(disposing); }
        public override bool CanRead => !_disposed; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => bytes.Length; public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
