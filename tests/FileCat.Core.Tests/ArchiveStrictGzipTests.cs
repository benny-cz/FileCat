using System.Buffers.Binary;
using System.IO.Compression;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveStrictGzipTests
{
    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void Empty_and_nonempty_payloads_roundtrip_with_independent_runtime_readback(CompressionLevel level)
    {
        byte[] random = new byte[200_000];
        new Random(419).NextBytes(random);
        foreach (byte[] input in new[] { Array.Empty<byte>(), "payload"u8.ToArray(), random })
        {
            byte[] encoded = Encode(input, level);
            using var compressed = new MemoryStream(encoded);
            using var decoder = new StrictGzipReadStream(compressed, TestContext.Current.CancellationToken);
            using var output = new MemoryStream();
            decoder.CopyTo(output);
            Assert.Equal(input, output.ToArray());
            Assert.Equal(3, decoder.OperatingSystem);
            using var runtime = new GZipStream(new MemoryStream(encoded), CompressionMode.Decompress);
            using var independent = new MemoryStream();
            runtime.CopyTo(independent);
            Assert.Equal(input, independent.ToArray());
        }
    }

    [Fact]
    public void Every_truncation_corrupt_trailer_and_concatenation_is_refused()
    {
        byte[] encoded = Encode("hello world"u8.ToArray());
        for (int length = 0; length < encoded.Length; length++) AssertRefused(encoded[..length]);
        byte[] crc = (byte[])encoded.Clone();
        crc[^8] ^= 1;
        AssertRefused(crc);
        byte[] size = (byte[])encoded.Clone();
        size[^1] ^= 1;
        AssertRefused(size);
        AssertRefused(encoded.Concat(encoded).ToArray());
        AssertRefused(encoded.Concat(new byte[1]).ToArray());
    }

    [Fact]
    public void A_nonfinal_deflate_block_with_correct_crc_and_size_is_still_incomplete()
    {
        // Stored DEFLATE block: LEN/NLEN and data are correct, but BFINAL=0 and no final block follows it.
        byte[] bytes = [0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 0, 255, 0, 3, 0, 252, 255, (byte)'a', (byte)'b', (byte)'c', 0, 0, 0, 0, 3, 0, 0, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 8), Crc32.HashToUInt32("abc"u8));
        AssertRefused(bytes);
    }

    [Fact]
    public void Optional_header_metadata_cannot_be_silently_dropped()
    {
        byte[] encoded = Encode("payload"u8.ToArray());
        foreach (int flag in new[] { 1, 2, 4, 8, 16 })
        {
            byte[] bytes = (byte[])encoded.Clone();
            bytes[3] = (byte)flag;
            using var source = new MemoryStream(bytes);
            Assert.Equal(ArchiveWriteReason.UnsupportedFeature,
                Assert.Throws<ArchiveWriteException>(() => new StrictGzipReadStream(source, TestContext.Current.CancellationToken)).Reason);
        }
        encoded[4] = 1;
        using var timestamped = new MemoryStream(encoded);
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature,
            Assert.Throws<ArchiveWriteException>(() => new StrictGzipReadStream(timestamped, TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public void Bounded_mutations_have_only_success_or_controlled_refusal()
    {
        byte[] seed = Encode(ArchiveStrictTarTests.Make(System.Formats.Tar.TarEntryFormat.Pax, ("file", "content")));
        var random = new Random(495);
        for (int i = 0; i < 1000; i++)
        {
            byte[] bytes = (byte[])seed.Clone();
            bytes[random.Next(bytes.Length)] ^= (byte)random.Next(1, 256);
            try
            {
                using var source = new MemoryStream(bytes);
                using var decoder = new StrictGzipReadStream(source, TestContext.Current.CancellationToken);
                decoder.CopyTo(Stream.Null);
            }
            catch (ArchiveWriteException) { }
        }
    }

    private static byte[] Encode(byte[] bytes, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var output = new MemoryStream();
        using (var encoder = new StrictGzipWriteStream(output, level, operatingSystem: 3)) encoder.Write(bytes);
        return output.ToArray();
    }

    private static void AssertRefused(byte[] bytes)
    {
        Assert.Throws<ArchiveWriteException>(() =>
        {
            using var source = new MemoryStream(bytes);
            using var decoder = new StrictGzipReadStream(source, TestContext.Current.CancellationToken);
            decoder.CopyTo(Stream.Null);
        });
    }
}
