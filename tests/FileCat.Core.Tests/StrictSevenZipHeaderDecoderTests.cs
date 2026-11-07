using System.Security.Cryptography;
using System.Text;
using FileCat.Archives;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class StrictSevenZipHeaderDecoderTests
{
    private const string Password = "Public-7z-exact-header-暗号-42829313";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static SevenZipCoder Copy => new([0], [], 1, 1);
    private static SevenZipCoder Lzma2 => new([0x21], [0], 1, 1);
    private static SevenZipRawFolder Folder(SevenZipCoder coder, long length) => new([coder], [], [0], [length], 0, null, 0);
    private static byte[] Pack(byte[] bytes) => bytes.Length == 0 ? [0] : [1, (byte)((bytes.Length - 1) >> 8), (byte)(bytes.Length - 1), .. bytes, 0];
    public static IEnumerable<object[]> PaddingCases() => Enumerable.Range(0, 34).Select(length => new object[] { length });

    [Theory] [MemberData(nameof(PaddingCases))]
    public void AES_checks_every_cipher_byte_and_accepts_all_legal_nonzero_padding_lengths(int length)
    {
        byte[] bytes = Enumerable.Range(0, length).Select(index => (byte)(index * 17)).ToArray();
        var (packed, properties) = Encrypt(bytes); using var input = new MemoryStream(packed, false); using var password = new SevenZipReadPasswordSession(Password, new(), Ct);
        using var decoder = new StrictSevenZipHeaderDecoder(input, packed.Length, Folder(new([6, 0xf1, 7, 1], properties, 1, 1), length), password, new(), Ct);
        using var output = new MemoryStream(); decoder.CopyTo(output); Assert.Equal(bytes, output.ToArray()); Assert.Equal(-1, decoder.ReadByte());
        Assert.Equal(packed.Length, input.Position); decoder.Dispose(); Assert.True(input.CanRead);
        if (Environment.GetEnvironmentVariable("FILECAT_7Z_HEADER_LAYER_EXPORT_ROOT") is { Length: > 0 } exports)
        {
            string directory = Path.Combine(exports, length.ToString("D2")); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "cipher.bin"), packed); File.WriteAllBytes(Path.Combine(directory, "properties.bin"), properties);
            File.WriteAllBytes(Path.Combine(directory, "plain.bin"), bytes);
            File.WriteAllText(Path.Combine(directory, "expected.json"), System.Text.Json.JsonSerializer.Serialize(new { synthetic_public_vector = true,
                password = Password, length, padding_byte = 0xa5, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) }));
        }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Linear_graphs_follow_bindings_instead_of_coder_array_order_and_verify_every_predecessor(int shape)
    {
        byte[] bytes = [1, 5, 0, 0], compressed = Pack(bytes), packed = compressed;
        SevenZipRawFolder folder;
        if (shape == 0) folder = new([Copy, Lzma2], [new(0, 1)], [1], [bytes.Length, bytes.Length], 0, null, 0);
        else if (shape == 1) folder = new([Lzma2, Copy], [new(0, 1)], [1], [bytes.Length, compressed.Length], 0, null, 0);
        else
        {
            var encrypted = Encrypt(compressed); packed = encrypted.Packed; var aes = new SevenZipCoder([6, 0xf1, 7, 1], encrypted.Properties, 1, 1);
            folder = shape == 2 ? new([Lzma2, aes], [new(0, 1)], [1], [bytes.Length, compressed.Length], 0, null, 0)
                : shape == 3 ? new([aes, Lzma2], [new(1, 0)], [0], [compressed.Length, bytes.Length], 1, null, 0)
                : new([Copy, Lzma2, aes], [new(0, 1), new(1, 2)], [2], [bytes.Length, bytes.Length, compressed.Length], 0, null, 0);
        }
        using var input = new ShortInput(packed); using var password = new SevenZipReadPasswordSession(Password, new(), Ct);
        using var decoder = new StrictSevenZipHeaderDecoder(input, packed.Length, folder, password, new(), Ct);
        using var output = new MemoryStream(); decoder.CopyTo(output); Assert.Equal(bytes, output.ToArray()); Assert.Equal(-1, decoder.ReadByte());
        decoder.Dispose(); Assert.True(input.CanRead);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Missing_ends_extra_compressed_bytes_and_wrong_intermediate_extents_refuse(int damage)
    {
        byte[] bytes = [1, 5, 0, 0], packed = Pack(bytes); long length = bytes.Length;
        if (damage == 0) packed = packed[..^1];
        if (damage == 1) packed = [.. packed, 0];
        if (damage == 2) length--;
        if (damage == 3) length++;
        var folder = damage < 4 ? Folder(Lzma2, length) : new SevenZipRawFolder([Lzma2, Copy], [new(0, 1)], [1],
            [length, packed.Length + (damage == 4 ? -1 : 1)], 0, null, 0);
        using var input = new MemoryStream(packed, false);
        Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() =>
        { using var decoder = new StrictSevenZipHeaderDecoder(input, packed.Length, folder, null, new(), Ct); decoder.CopyTo(Stream.Null); }).Reason);
        Assert.True(input.CanRead);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void AES_extent_alignment_short_source_extra_source_and_excess_padding_refuse(int damage)
    {
        var encrypted = Encrypt([1]); byte[] packed = encrypted.Packed; long grant = packed.Length, length = 1;
        if (damage == 0) { packed = packed[..^1]; grant--; }
        if (damage == 1) packed = packed[..^1];
        if (damage == 2) packed = [.. packed, 0];
        if (damage == 3) length = 0;
        using var input = new MemoryStream(packed, false); using var password = new SevenZipReadPasswordSession(Password, new(), Ct);
        Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() =>
        { using var decoder = new StrictSevenZipHeaderDecoder(input, grant, Folder(new([6, 0xf1, 7, 1], encrypted.Properties, 1, 1), length), password, new(), Ct); decoder.CopyTo(Stream.Null); }).Reason);
        Assert.True(input.CanRead);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Unknown_graphs_properties_and_resource_excess_refuse_before_accepting_metadata(int damage)
    {
        SevenZipRawFolder folder = Folder(Copy, 1); var budget = new ArchiveWriteBudget();
        if (damage == 0) folder = Folder(new([0x7e], [], 1, 1), 1);
        if (damage == 1) folder = Folder(new([0], [1], 1, 1), 1);
        if (damage == 2) folder = Folder(new([0x21], [40], 1, 1), 1);
        if (damage == 3) folder = new([Copy, Copy], [new(1, 1)], [0], [1, 1], 0, null, 0);
        if (damage == 4) budget.AddMetadata(0, ArchiveWritePolicy.MaxRetainedHeapBytes);
        if (damage == 5) folder = Folder(Copy, ArchiveWritePolicy.MaxMetadataBytes + 1);
        using var input = new MemoryStream(new byte[] { 1 }); Assert.Throws<ArchiveWriteException>(() => new StrictSevenZipHeaderDecoder(input, 1, folder, null, budget, Ct));
        Assert.True(input.CanRead);
    }

    private static (byte[] Packed, byte[] Properties) Encrypt(byte[] bytes)
    {
        byte[] salt = [1, 2, 3, 4], iv = Enumerable.Range(0, 16).Select(index => (byte)index).ToArray(), encoded = Encoding.Unicode.GetBytes(Password);
        byte[] plain = new byte[(bytes.Length + 15) & ~15]; plain.AsSpan().Fill(0xa5); bytes.CopyTo(plain, 0);
        try
        {
            using var key = SevenZipEncryptionPrimitives.Derive(encoded, salt, 63, new(), Ct);
            using var aes = Aes.Create(); aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.None;
            using var encrypt = aes.CreateEncryptor(key.Key.ToArray(), iv); return (encrypt.TransformFinalBlock(plain, 0, plain.Length), [0xff, 0x3f, .. salt, .. iv]);
        }
        finally { CryptographicOperations.ZeroMemory(encoded); CryptographicOperations.ZeroMemory(plain); }
    }
    private sealed class ShortInput(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes, false);
        public override int Read(Span<byte> buffer) => inner.Read(buffer[..Math.Min(3, buffer.Length)]);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(3, count));
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
