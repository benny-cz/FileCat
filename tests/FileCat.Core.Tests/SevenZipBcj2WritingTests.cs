using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using SharpCompress.Compressors.LZMA;

namespace FileCat.Core.Tests;

public sealed class SevenZipBcj2WritingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string PublicPassword = "FileCat-Public-Bcj2-Vector-暗号-42829304";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(127)]
    [InlineData(256)]
    [InlineData(4093)]
    [InlineData(65523)]
    [InlineData(65524)]
    [InlineData(65525)]
    [InlineData(131089)]
    [InlineData(1048611)]
    public void Four_streams_are_chunk_invariant_roundtrip_and_export_exact_SDK_vectors(int size)
    {
        byte[] data = Corpus(size), original = (byte[])data.Clone(); var expected = Split(65524);
        foreach (int chunk in size < 4096 ? new[] { 1, 2, 3, 5, 7 } : new[] { 7, 4093 })
        {
            var observed = Split(chunk);
            for (int i = 0; i < 4; i++) Assert.Equal(expected.Bytes[i], observed.Bytes[i]);
        }
        Assert.Equal(original, data); Assert.Equal(size, expected.Facts.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), expected.Facts.Sha256);
        Assert.Equal(size, expected.Bytes.Take(3).Sum(p => p.Length));
        if (size > 127) { Assert.True(expected.Facts.Calls > 0); Assert.True(expected.Facts.Jumps > 0); }
        var inputs = expected.Bytes.Select(p => (Stream)new MemoryStream(p, false)).ToArray();
        using (var decoder = new Bcj2DecoderStream(inputs))
        {
            using var output = new MemoryStream(); byte[] buffer = new byte[7]; int read;
            while ((read = decoder.Read(buffer, 0, buffer.Length)) > 0) { Ct.ThrowIfCancellationRequested(); Assert.True(output.Length + read <= data.Length); output.Write(buffer, 0, read); }
            Assert.Equal(data, output.ToArray());
        }
        string? root = Environment.GetEnvironmentVariable("FILECAT_SEVENZIP_BCJ2_EXPORT_ROOT");
        if (root is not null)
        {
            Assert.True(Path.IsPathFullyQualified(root)); string folder = Path.Combine(root, "raw-" + size); Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "input.bin"), data);
            for (int i = 0; i < 4; i++) File.WriteAllBytes(Path.Combine(folder, "stream-" + i + ".bin"), expected.Bytes[i]);
            File.WriteAllText(Path.Combine(folder, "expected.json"), JsonSerializer.Serialize(expected.Facts));
        }

        (byte[][] Bytes, SevenZipBcj2Content Facts) Split(int chunk)
        {
            using var input = new Chunked(data, chunk); var outputs = Enumerable.Range(0, 4).Select(_ => new Observed()).ToArray();
            try
            {
                var facts = SevenZipBcj2Encoder.Encode(input, data.Length, outputs, Ct);
                Assert.InRange(input.MaximumRead, 1, ArchiveIoAuthority.MaximumReadBytes);
                foreach (var target in outputs) Assert.InRange(target.MaximumWrite, 0, ArchiveIoAuthority.MaximumReadBytes);
                for (int i = 0; i < 4; i++) Assert.Equal(outputs[i].Length, facts.Streams[i].Length);
                return (outputs.Select(p => p.ToArray()).ToArray(), facts);
            }
            finally { foreach (var target in outputs) target.Dispose(); }
        }
    }

    public static IEnumerable<object[]> Profiles()
    {
        for (int codec = 0; codec < 7; codec++)
            for (int header = 0; header < 2; header++)
                for (int secret = 0; secret < 3; secret++) yield return [codec, header, secret];
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deflate_small_reads_keep_compressed_input_until_the_codec_reports_its_end(bool asynchronous)
    {
        byte[] data = Corpus(65541); byte[] compressed;
        using (var target = new MemoryStream())
        {
            using (var encoder = new System.IO.Compression.DeflateStream(target,
                System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) encoder.Write(data);
            compressed = target.ToArray();
        }
        foreach (int chunk in new[] { 1, 2, 3, 4093 })
        {
            using var source = new Chunked(compressed, 7);
            using var decoder = new SharpCompress.Compressors.Deflate.DeflateStream(source,
                SharpCompress.Compressors.CompressionMode.Decompress);
            using var decoded = new MemoryStream(); byte[] buffer = new byte[chunk];
            while (true)
            {
                Ct.ThrowIfCancellationRequested();
                int count;
                if (asynchronous) count = await decoder.ReadAsync(buffer, 0, buffer.Length, Ct);
                else if (chunk == 1)
                {
                    int value = decoder.ReadByte(); count = value < 0 ? 0 : 1;
                    if (count != 0) buffer[0] = (byte)value;
                }
                else count = decoder.Read(buffer, 0, buffer.Length);
                if (count == 0) break;
                Assert.True(decoded.Length + count <= data.Length); decoded.Write(buffer, 0, count);
            }
            Assert.Equal(data, decoded.ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void All_codecs_headers_and_password_modes_create_four_stream_folders(int codecId, int headerId, int secret)
    {
        byte[] data = Corpus(65541); string sha = Convert.ToHexString(SHA256.HashData(data));
        var options = Options((SevenZipCodec)codecId);
        var entries = new[]
        {
            new SevenZipWriteEntry("code", ArchiveMemberType.Directory, new(1700000000, 123456700), 0x1ed),
            new SevenZipWriteEntry("code/暗号.bin", ArchiveMemberType.File, new(1700000000, 123456700), 0x1a0, 1),
            new SevenZipWriteEntry("empty", ArchiveMemberType.File, new(1700000000, 123456700), 0x1a4, 2),
        };
        var inputs = new Dictionary<int, SevenZipInputDescriptor>
        { [1] = new(1, data.Length, sha), [2] = new(2, 0, Convert.ToHexString(SHA256.HashData([]))) };
        int opens = 0; using var stage = new Observed();
        using var password = secret == 0 ? null : new SevenZipEncryptionSession(PublicPassword, secret == 2, new(), Ct);
        var result = SevenZipArchiveWriter.Create(stage, entries, inputs, id => { opens++; return new MemoryStream(id == 1 ? data : [], false); },
            options, Ct, (SevenZipHeaderMode)headerId, password);
        Assert.Equal(5, opens); Assert.Equal(3, result.Members.Count); Assert.Equal(secret != 0, result.Encrypted); Assert.Equal(secret == 2, result.HiddenNames);
        var content = result.Members[1].Content!; Assert.Equal(4, content.PackedStreams.Count); Assert.Equal(data.Length, content.Length);
        Assert.Equal(new byte[] { 3, 3, 1, 0x1b }, content.Coders[^1].MethodId); Assert.Equal(4, content.Coders[^1].Inputs);
        Assert.Equal(secret == 0 ? 4 : 8, content.Coders.Count); Assert.InRange(stage.MaximumWrite, 1, ArchiveIoAuthority.MaximumReadBytes);
        using var temporary = new TempDir(); string path = Path.Combine(temporary.Path, "bcj2.7z"); File.WriteAllBytes(path, stage.ToArray());
        using var readPassword = secret == 0 ? null : new SevenZipReadPasswordSession(PublicPassword, new(), Ct);
        using var reader = readPassword is null ? SharpArchiveReader.Open(path) : SharpArchiveReader.OpenWithPassword(path, readPassword);
        var listed = reader.List(message => Assert.Fail(message), Ct).ToArray(); Assert.Equal(3, listed.Length);
        foreach (var member in listed.Where(m => m.Kind == MemberKind.File))
        {
            using var decoded = reader.Open(member.Index, Ct);
            string expected = member.Path == "empty" ? inputs[2].Sha256 : sha;
            Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(decoded)));
        }
        string? root = Environment.GetEnvironmentVariable("FILECAT_SEVENZIP_BCJ2_EXPORT_ROOT");
        if (root is not null)
        {
            string folder = Path.Combine(root, $"archive-{codecId}-{headerId}-{secret}"); Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "members.7z"), stage.ToArray());
            File.WriteAllText(Path.Combine(folder, "members.json"), JsonSerializer.Serialize(entries.Select((e, i) => new
            {
                name = e.Name, directory = e.Type == ArchiveMemberType.Directory, length = i == 1 ? data.Length : 0,
                sha256 = i == 1 ? sha : inputs[2].Sha256, mode = e.Mode, mtime_seconds = e.Modified.Seconds,
                mtime_nanos = e.Modified.Nanoseconds, ctime_seconds = (long?)null, ctime_nanos = (int?)null,
                atime_seconds = (long?)null, atime_nanos = (int?)null,
            })));
            File.WriteAllText(Path.Combine(folder, "profile.json"), JsonSerializer.Serialize(new
            { codec = codecId, header = headerId, encrypted = secret != 0, hidden_names = secret == 2, synthetic_public_vector = true, public_synthetic_password = PublicPassword }));
        }
    }

    [Fact]
    public void A_changed_second_source_pass_refuses_without_disposing_the_owned_stage()
    {
        byte[] data = Corpus(4093), changed = (byte[])data.Clone(); changed[17] ^= 1;
        var entry = new SevenZipWriteEntry("code.bin", ArchiveMemberType.File, new(1700000000, 0), 0x1a4, 1);
        var input = new SevenZipInputDescriptor(1, data.Length, Convert.ToHexString(SHA256.HashData(data)));
        using var output = new MemoryStream(); int opens = 0;
        var error = Assert.Throws<ArchiveWriteException>(() => SevenZipArchiveWriter.Create(output, [entry], new Dictionary<int, SevenZipInputDescriptor> { [1] = input },
            _ => new MemoryStream(++opens == 1 ? data : changed, false), Options(SevenZipCodec.Lzma2), Ct));
        Assert.Equal(ArchiveWriteReason.SourceChanged, error.Reason); Assert.Equal(2, opens); Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Truncated_or_longer_sources_refuse_and_leave_caller_streams_open(bool longer)
    {
        byte[] data = Corpus(127); using var source = new MemoryStream(data, false);
        var outputs = Enumerable.Range(0, 4).Select(_ => new MemoryStream()).ToArray();
        try
        {
            var error = Assert.Throws<ArchiveWriteException>(() => SevenZipBcj2Encoder.Encode(source, data.Length + (longer ? -1 : 1), outputs, Ct));
            Assert.Equal(ArchiveWriteReason.SourceChanged, error.Reason); Assert.True(source.CanRead);
            Assert.All(outputs, output => Assert.True(output.CanWrite));
        }
        finally { foreach (var output in outputs) output.Dispose(); }
    }

    [Fact]
    public void Invalid_aliases_and_over_budget_lengths_are_refused_before_any_output()
    {
        using var source = new MemoryStream(new byte[1], false); using var target = new MemoryStream();
        var outputs = Enumerable.Range(0, 4).Select(_ => new MemoryStream()).ToArray();
        try
        {
            Assert.Throws<ArchiveWriteException>(() => SevenZipBcj2Encoder.Encode(source, 1, [target, target, outputs[2], outputs[3]], Ct));
            Assert.Throws<ArchiveWriteException>(() => SevenZipBcj2Encoder.Encode(source, 1, [source, outputs[1], outputs[2], outputs[3]], Ct));
            Assert.Throws<ArchiveWriteException>(() => SevenZipBcj2Encoder.Encode(source, ArchiveWritePolicy.MaxPayloadBytes + 1, outputs, Ct));
            Assert.Throws<ArchiveWriteException>(() => SevenZipBcj2Encoder.Encode(source, 1, outputs[..3], Ct));
            Assert.Equal(0, source.Position); Assert.Equal(0, target.Length); Assert.All(outputs, output => Assert.Equal(0, output.Length));
        }
        finally { foreach (var output in outputs) output.Dispose(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Any_failed_output_aborts_without_closing_the_other_grants(int failing)
    {
        using var source = new MemoryStream(Corpus(65541), false);
        var outputs = Enumerable.Range(0, 4).Select(i => (Stream)(i == failing ? new Broken() : new MemoryStream())).ToArray();
        try
        {
            Assert.Throws<IOException>(() => SevenZipBcj2Encoder.Encode(source, source.Length, outputs, Ct));
            Assert.All(outputs, output => Assert.True(output.CanWrite)); Assert.True(source.CanRead);
        }
        finally { foreach (var output in outputs) output.Dispose(); }
    }

    [Fact]
    public void Cancellation_refuses_without_output_and_preserves_caller_ownership()
    {
        using var source = new MemoryStream(Corpus(65541), false); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var outputs = Enumerable.Range(0, 4).Select(_ => new MemoryStream()).ToArray();
        try
        {
            Assert.Throws<OperationCanceledException>(() => SevenZipBcj2Encoder.Encode(source, source.Length, outputs, cancellation.Token));
            Assert.Equal(0, source.Position); Assert.All(outputs, output => { Assert.Equal(0, output.Length); Assert.True(output.CanWrite); });
        }
        finally { foreach (var output in outputs) output.Dispose(); }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Decoder_disposes_all_four_inputs_even_when_one_dispose_fails(int failing)
    {
        var inputs = Enumerable.Range(0, 4).Select(i => new Disposal(i == 3 ? new byte[5] : [], i == failing)).ToArray();
        var reader = new Bcj2DecoderStream(inputs);
        if (failing < 0) reader.Dispose(); else Assert.Throws<IOException>(() => reader.Dispose());
        Assert.All(inputs, input => Assert.Equal(1, input.Disposals)); reader.Dispose();
        Assert.All(inputs, input => Assert.Equal(1, input.Disposals));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Decoder_construction_failure_closes_all_inputs_and_preserves_the_original_error(int failing)
    {
        var inputs = Enumerable.Range(0, 4).Select(i => new Disposal(i == 3 ? new byte[4] : [], i == failing)).ToArray();
        Assert.Throws<SharpCompress.Common.IncompleteArchiveException>(() => new Bcj2DecoderStream(inputs));
        Assert.All(inputs, input => Assert.Equal(1, input.Disposals));
    }

    private static SevenZipCodecOptions Options(SevenZipCodec codec) => new(codec, codec == SevenZipCodec.Copy ? 0 : 1,
        DictionaryBytes: codec is SevenZipCodec.Lzma or SevenZipCodec.Lzma2 ? 1 << 20 : 8 << 20,
        ModelOrder: codec == SevenZipCodec.Ppmd7 ? 4 : 0, ModelMemoryBytes: codec == SevenZipCodec.Ppmd7 ? 1 << 20 : 0, Filter: SevenZipFilter.Bcj2);

    private static byte[] Corpus(int length)
    {
        byte[] bytes = new byte[length]; new Random(42829304).NextBytes(bytes);
        for (int at = 16, kind = 0; at + 8 < length; at += 31, kind++)
        {
            int opcode = at;
            if (kind % 3 == 2) { bytes[at] = 0x0f; opcode++; bytes[opcode] = (byte)(0x80 | (kind & 15)); }
            else bytes[opcode] = kind % 3 == 0 ? (byte)0xe8 : (byte)0xe9;
            int target = kind % 4 == 0 ? length : kind % 4 == 1 ? 0 : Math.Min(length - 1, opcode + 11);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(opcode + 1), target - opcode - 5);
        }
        if (length > 65528) { bytes[65522] = 0xe8; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(65523), -65527); }
        if (length > 0) bytes[^1] = 0xe8;
        return bytes;
    }

    private sealed class Chunked(byte[] bytes, int chunk) : Stream
    {
        private int position; internal int MaximumRead;
        public override int Read(Span<byte> buffer)
        { MaximumRead = Math.Max(MaximumRead, buffer.Length); int count = Math.Min(Math.Min(chunk, buffer.Length), bytes.Length - position); bytes.AsSpan(position, count).CopyTo(buffer); position += count; return count; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override bool CanRead => true; public override bool CanWrite => false; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long length) => throw new NotSupportedException();
    }
    private sealed class Observed : MemoryStream
    {
        internal int MaximumWrite;
        public override void Write(ReadOnlySpan<byte> buffer) { MaximumWrite = Math.Max(MaximumWrite, buffer.Length); base.Write(buffer); }
    }
    private sealed class Disposal(byte[] bytes, bool fail) : MemoryStream(bytes, false)
    {
        internal int Disposals;
        protected override void Dispose(bool disposing)
        { if (disposing) { Disposals++; base.Dispose(disposing); if (fail) throw new IOException("Synthetic disposal failure."); } }
    }
    private sealed class Broken : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Synthetic BCJ2 output failure.");
    }
}
