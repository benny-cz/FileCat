using System.Buffers.Binary;
using FileCat.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

/// <summary>
/// 7-Zip packs an archive's header by default, and the packed header states the size it decodes to. FileCat's build of
/// SharpCompress allocated that size before decoding a byte, so a 100-byte archive could make it allocate 2 GiB (release
/// review finding R-H5). The buffer now grows only as data decodes, and a header claiming more than 256 MiB is refused.
/// </summary>
public sealed class SevenZipEncodedHeaderTests
{
    [Fact]
    public void A_packed_header_claiming_200_MiB_is_not_allocated_before_it_decodes()
    {
        using var root = new TempDir();
        string path = Path.Combine(root.Path, "claims-200-mib.7z");
        File.WriteAllBytes(path, EncodedHeaderArchive(200u << 20));

        long before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<InvalidDataException>(() =>
        {
            using var reader = ArchiveFormats.Open(path, ArchiveKind.SevenZip, "claims-200-mib.7z");
            _ = reader.List(_ => { }, TestContext.Current.CancellationToken).ToList();
        });
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Current.TestOutputHelper?.WriteLine($"archive bytes={new FileInfo(path).Length}, thread allocation={allocated}: {error.Message}");
        Assert.InRange(allocated, 0, 16 << 20);
    }

    [Fact]
    public void A_packed_header_claiming_more_than_256_MiB_is_refused_before_decoding()
    {
        using var root = new TempDir();
        string path = Path.Combine(root.Path, "claims-2-gib.7z");
        File.WriteAllBytes(path, EncodedHeaderArchive(0x7FFFFFC7));
        Assert.True(new FileInfo(path).Length < 100);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<InvalidDataException>(() =>
        {
            using var reader = ArchiveFormats.Open(path, ArchiveKind.SevenZip, "claims-2-gib.7z");
            _ = reader.List(_ => { }, TestContext.Current.CancellationToken).ToList();
        });
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Contains("256 MiB", error.Message, StringComparison.Ordinal);
        Assert.InRange(allocated, 0, 4 << 20);
    }

    [Theory]
    [InlineData("EmptyEntries.7z")]
    [InlineData("FileCat.Methods.LZMA.7z")]
    [InlineData("FileCat.SolidPartial.7z")]
    public void Archives_whose_headers_7_Zip_packed_still_list(string fixture)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", fixture);
        using var reader = ArchiveFormats.Open(path, ArchiveKind.SevenZip, fixture);
        Assert.NotEmpty(reader.List(_ => { }, TestContext.Current.CancellationToken).ToList());
    }

    /// <summary>
    /// A 7z archive whose header is packed with the Copy coder and claims to decode to <paramref name="claimed"/> bytes,
    /// while only 16 packed bytes follow the signature header.
    /// </summary>
    private static byte[] EncodedHeaderArchive(uint claimed)
    {
        byte[] packed = new byte[16];
        byte[] header =
        [
            0x17,                               // kEncodedHeader: a packed header, described as streams
            0x06, 0x00, 0x01,                   // kPackInfo: at offset 0 after the signature header, one packed stream
            0x09, (byte)packed.Length, 0x00,    // kSize of it, kEnd
            0x07, 0x0B, 0x01, 0x00,             // kUnPackInfo, kFolder: one folder, stored here
            0x01, 0x01, 0x00,                   // one coder: a one-byte ID, simple; ID 00 (Copy)
            0x0C,                               // kCodersUnPackSize: a number with four bytes following
            0xF0, (byte)claimed, (byte)(claimed >> 8), (byte)(claimed >> 16), (byte)(claimed >> 24),
            0x00,                               // kEnd of the unpack info
            0x00,                               // kEnd of the streams info
        ];
        var archive = new byte[32 + packed.Length + header.Length];
        new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04 }.CopyTo(archive, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(archive.AsSpan(12, 8), (ulong)packed.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(archive.AsSpan(20, 8), (ulong)header.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(28, 4), Crc32.HashToUInt32(header));
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(8, 4), Crc32.HashToUInt32(archive.AsSpan(12, 20)));
        packed.CopyTo(archive, 32);
        header.CopyTo(archive, 32 + packed.Length);
        return archive;
    }
}
