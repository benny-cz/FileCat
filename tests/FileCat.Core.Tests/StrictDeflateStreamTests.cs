using System.Buffers;
using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using SharpCompress.Compressors.Deflate;
using DeflateMode = SharpCompress.Compressors.CompressionMode;

namespace FileCat.Core.Tests;

public sealed class StrictDeflateStreamTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public static IEnumerable<object[]> Cases() => from level in new[] { 0, 1, 9 } from size in new[] { 0, 1, 7, 257, 4093, 65535, 131089 }
        from noise in new[] { false, true } select new object[] { level, size, noise };
    [Theory] [MemberData(nameof(Cases))]
    public void Stored_and_compressed_streams_have_exact_end_output_and_consumption(int level, int size, bool noise)
    {
        byte[] data = Data(size, noise), packed = Encode(data, level);
        foreach (int chunk in new[] { 1, 7, 65524 })
        {
            using var input = new Chunked(packed, chunk); using var decoder = new StrictDeflateReadStream(input, packed.Length, size, Ct);
            using var output = new MemoryStream(); byte[] buffer = new byte[chunk]; int n;
            while ((n = decoder.Read(buffer)) != 0) output.Write(buffer, 0, n);
            Assert.Equal(data, output.ToArray()); Assert.True(decoder.Complete); Assert.Equal(packed.Length, decoder.CompressedBytesConsumed);
            Assert.Equal(-1, decoder.ReadByte()); decoder.Dispose(); Assert.True(input.CanRead);
        }
        Export($"valid-{level}-{size}-{noise}", packed, size, true, data);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Every_prefix_of_stored_and_dynamic_fixtures_refuses(bool noise)
    {
        byte[] data = Data(noise ? 127 : 4093, noise), packed = Encode(data, 9);
        for (int i = 0; i < packed.Length; i++)
        { byte[] prefix = packed[..i]; Refuse(prefix, data.Length); Export($"prefix-{noise}-{i}", prefix, data.Length, false, []); }
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void Trailing_truncated_reserved_history_and_wrong_size_cases_refuse(int damage)
    {
        byte[] packed = Encode(Data(257, true), 9); int length = 257;
        if (damage == 0) packed = [.. packed, 0];
        if (damage == 1) packed = [.. packed, .. packed];
        if (damage == 2) packed[0] |= 6;
        if (damage == 3) length = 0;
        if (damage == 4) length--;
        if (damage == 5) length++;
        if (damage == 6) packed = packed[..^1];
        if (damage == 7) { packed = [3, 2, 0]; length = 3; }
        if (damage == 8) { packed = [3, 2, 0, .. new byte[32]]; length = 3; }
        if (damage == 9) { packed = [1, 1, 0, 255, 255, 42]; length = 1; }
        if (damage == 10) { packed = [5, 0, 0, 0]; length = 0; }
        if (damage == 11) { packed = [0x1b, 3, 0]; length = 0; }
        Refuse(packed, length); Export("damaged-" + damage, packed, length, false, []);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Legal_nonzero_ignored_bits_are_not_mistaken_for_trailing_input(int kind)
    {
        byte[] packed = kind switch { 0 => [3, 0], 1 => [3, 0xfc], _ => [0xf9, 0, 0, 255, 255] };
        using var input = new Chunked(packed, 1); using var decoder = new StrictDeflateReadStream(input, packed.Length, 0, Ct);
        Assert.Equal(-1, decoder.ReadByte()); Assert.True(decoder.Complete); Export("padding-" + kind, packed, 0, true, []);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void The_legacy_inflater_never_emits_seeded_pool_bytes_for_missing_history(bool fast)
    {
        var codec = new ZlibCodec(); Assert.Equal(ZlibConstants.Z_OK, codec.InitializeInflate(false));
        var blocks = codec.istate.blocks; var owner = new TrackedOwner(); var previous = blocks.window; blocks.window = owner; previous.Dispose();
        try
        {
            codec.InputBuffer = fast ? [3, 2, 0, .. new byte[32]] : [3, 2, 0]; codec.AvailableBytesIn = codec.InputBuffer.Length;
            codec.OutputBuffer = new byte[65536]; codec.AvailableBytesOut = codec.OutputBuffer.Length;
            var error = Assert.Throws<ZlibException>(() => codec.Inflate(FlushType.None)); Assert.Contains("invalid distance too far back", error.Message);
            Assert.Equal(0, codec.TotalBytesOut);
        }
        finally { codec.EndInflate(); }
        Assert.True(owner.Disposed); Assert.Equal(SHA256.HashData(new byte[owner.Bytes.Length]), SHA256.HashData(owner.Bytes));
    }
    [Fact]
    public void Window_storage_is_cleared_before_returning_it_to_its_owner()
    {
        var codec = new ZlibCodec(); codec.InitializeInflate(false); var blocks = codec.istate.blocks;
        var owner = new TrackedOwner(); var previous = blocks.window; blocks.window = owner; previous.Dispose();
        codec.EndInflate(); Assert.True(owner.Disposed); Assert.Equal(SHA256.HashData(new byte[owner.Bytes.Length]), SHA256.HashData(owner.Bytes));
    }
    [Fact]
    public void A_history_reset_does_not_treat_a_previous_output_counter_as_new_history()
    {
        var codec = new ZlibCodec(); codec.InitializeInflate(false); codec.TotalBytesOut = 1024; codec.istate.blocks.Reset();
        codec.InputBuffer = [3, 2, 0]; codec.AvailableBytesIn = 3; codec.OutputBuffer = new byte[16]; codec.AvailableBytesOut = 16;
        try { var error = Assert.Throws<ZlibException>(() => codec.Inflate(FlushType.None)); Assert.Contains("invalid distance too far back", error.Message); Assert.Equal(1024, codec.TotalBytesOut); }
        finally { codec.EndInflate(); }
    }
    [Fact]
    public void Explicit_preset_dictionary_remains_valid_history_for_zlib_streams()
    {
        byte[] dictionary = Data(1024, false), data = dictionary.Concat(dictionary).ToArray();
        var encoder = new ZlibCodec(); encoder.InitializeDeflate(CompressionLevel.BestCompression, 15, true); encoder.SetDictionary(dictionary);
        using var encoded = new MemoryStream(); encoder.InputBuffer = data; encoder.AvailableBytesIn = data.Length; encoder.OutputBuffer = new byte[4096];
        int status;
        do { encoder.NextOut = 0; encoder.AvailableBytesOut = encoder.OutputBuffer.Length; status = encoder.Deflate(FlushType.Finish); encoded.Write(encoder.OutputBuffer, 0, encoder.NextOut); }
        while (status == ZlibConstants.Z_OK);
        Assert.Equal(ZlibConstants.Z_STREAM_END, status); encoder.EndDeflate();
        var decoder = new ZlibCodec(); decoder.InitializeInflate(true); decoder.InputBuffer = encoded.ToArray(); decoder.AvailableBytesIn = decoder.InputBuffer.Length;
        decoder.OutputBuffer = new byte[data.Length + 1]; decoder.AvailableBytesOut = decoder.OutputBuffer.Length;
        try
        {
            Assert.Equal(ZlibConstants.Z_NEED_DICT, decoder.Inflate(FlushType.None)); Assert.Equal(ZlibConstants.Z_OK, decoder.SetDictionary(dictionary));
            Assert.Equal(ZlibConstants.Z_STREAM_END, decoder.Inflate(FlushType.None)); Assert.Equal(data, decoder.OutputBuffer[..decoder.NextOut]);
        }
        finally { decoder.EndInflate(); }
        if (Environment.GetEnvironmentVariable("FILECAT_DEFLATE_END_EXPORT_ROOT") is { Length: > 0 } root)
        {
            string directory = Path.Combine(root, "preset"); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "compressed.bin"), encoded.ToArray()); File.WriteAllBytes(Path.Combine(directory, "dictionary.bin"), dictionary);
            File.WriteAllBytes(Path.Combine(directory, "plain.bin"), data);
        }
    }
    [Fact]
    public void Cancellation_and_grant_limits_preserve_the_source()
    {
        byte[] packed = Encode(Data(4093, false), 9); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct); using var input = new Chunked(packed, 7, stop.Cancel);
        using var reader = new StrictDeflateReadStream(input, packed.Length, 4093, stop.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => reader.CopyTo(Stream.Null)); Assert.True(input.CanRead);
        using var empty = new MemoryStream(); Assert.Equal(ArchiveWriteReason.BudgetExceeded,
            Assert.Throws<ArchiveWriteException>(() => new StrictDeflateReadStream(empty, ArchiveWritePolicy.MaxPhysicalBytes + 1, 0, Ct)).Reason);
        Assert.Equal(0, empty.Position);
    }
    public static IEnumerable<object[]> EncoderLifetimes() => from asynchronous in new[] { false, true }
        from leaveOpen in new[] { false, true } from size in new[] { 0, 257 } select new object[] { asynchronous, leaveOpen, size };
    [Theory] [MemberData(nameof(EncoderLifetimes))]
    public async Task Encoder_finalization_preserves_the_requested_captive_stream_lifetime(bool asynchronous, bool leaveOpen, int size)
    {
        byte[] data = Data(size, false); var output = new MemoryStream();
        var encoder = new SharpCompress.Compressors.Deflate.DeflateStream(output, DeflateMode.Compress, CompressionLevel.BestCompression, leaveOpen, null);
        if (asynchronous) { await encoder.WriteAsync(data, Ct); await encoder.DisposeAsync(); } else { encoder.Write(data); encoder.Dispose(); }
        Assert.Equal(leaveOpen, output.CanWrite); byte[] packed = output.ToArray();
        using var input = new MemoryStream(packed, false); using var reader = new StrictDeflateReadStream(input, packed.Length, size, Ct); using var plain = new MemoryStream();
        reader.CopyTo(plain); Assert.Equal(data, plain.ToArray()); Assert.True(reader.Complete);
        Export($"lifetime-{asynchronous}-{leaveOpen}-{size}", packed, size, true, data); output.Dispose();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task An_encoder_disposed_without_any_write_still_finishes_an_empty_stream(bool asynchronous)
    {
        using var output = new MemoryStream();
        var encoder = new SharpCompress.Compressors.Deflate.DeflateStream(output, DeflateMode.Compress, CompressionLevel.Default, true, null);
        if (asynchronous) await encoder.DisposeAsync(); else encoder.Dispose();
        byte[] packed = output.ToArray(); using var input = new MemoryStream(packed, false); using var reader = new StrictDeflateReadStream(input, packed.Length, 0, Ct);
        Assert.Equal(-1, reader.ReadByte()); Assert.True(reader.Complete); Assert.True(output.CanWrite); Export("no-write-" + asynchronous, packed, 0, true, []);
    }
    private static byte[] Data(int size, bool noise)
    { const string pattern = "Deflate exact history and endings.\0"; byte[] data = new byte[size]; new Random(42829315).NextBytes(data); if (!noise) for (int i = 0; i < size; i++) data[i] = (byte)pattern[i % pattern.Length]; return data; }
    private static byte[] Encode(byte[] data, int level)
    { using var output = new MemoryStream(); using (var encoder = new SharpCompress.Compressors.Deflate.DeflateStream(output, DeflateMode.Compress, (CompressionLevel)level, true, null)) encoder.Write(data); return output.ToArray(); }
    private static void Refuse(byte[] packed, int length)
    { using var input = new Chunked(packed, 7); Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() => { using var reader = new StrictDeflateReadStream(input, packed.Length, length, Ct); reader.CopyTo(Stream.Null); }).Reason); Assert.True(input.CanRead); }
    private static void Export(string name, byte[] packed, int length, bool accepted, byte[] data)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_DEFLATE_END_EXPORT_ROOT") is not { Length: > 0 } root) return;
        string directory = Path.Combine(root, name); Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "compressed.bin"), packed);
        File.WriteAllText(Path.Combine(directory, "expected.json"), System.Text.Json.JsonSerializer.Serialize(new { accepted, length, sha256 = Convert.ToHexString(SHA256.HashData(data)) }));
        if (name == "valid-0-0-False") File.WriteAllText(Path.Combine(Path.GetDirectoryName(root)!, "runtime-deflate.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            runtime_directory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        }));
    }
    private sealed class TrackedOwner : IMemoryOwner<byte>
    {
        internal readonly byte[] Bytes = Enumerable.Repeat((byte)0xa5, 32768).ToArray(); internal bool Disposed;
        public Memory<byte> Memory => Bytes;
        public void Dispose() { Disposed = true; }
    }
    private sealed class Chunked(byte[] bytes, int chunk, Action? afterRead = null) : Stream
    {
        private readonly MemoryStream inner = new(bytes, false);
        public override int Read(Span<byte> buffer) { int n = inner.Read(buffer[..Math.Min(chunk, buffer.Length)]); afterRead?.Invoke(); return n; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
