using System.Buffers.Binary;
using FileCat.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class SevenZipHeaderBudgetTests
{
    [Fact]
    public void Tiny_archive_with_absent_large_header_refuses_before_large_allocation()
    {
        using var root = new TempDir();
        string path = Path.Combine(root.Path, "oversized.7z");
        File.WriteAllBytes(path, Header(64UL << 20));

        long before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<InvalidDataException>(() =>
        {
            using var reader = ArchiveFormats.Open(path, ArchiveKind.SevenZip, "oversized.7z");
            _ = reader.List(_ => { }, TestContext.Current.CancellationToken).ToList();
        });
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Current.TestOutputHelper?.WriteLine($"7z malformed bytes={new FileInfo(path).Length}, declared-header={64UL << 20}, thread-allocation={allocated}");
        Assert.Contains("metadata header", error.Message, StringComparison.Ordinal);
        Assert.InRange(allocated, 0, 4 << 20);
        Assert.Equal(32, new FileInfo(path).Length);
    }

    [Fact]
    public void Valid_seven_zip_still_lists_its_members()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", "EmptyEntries.7z");
        using var reader = ArchiveFormats.Open(path, ArchiveKind.SevenZip, "EmptyEntries.7z");
        Assert.Equal(4, reader.List(_ => { }, TestContext.Current.CancellationToken).Count());
    }

    [Fact]
    public void Physical_header_at_budget_is_admitted_and_one_byte_over_is_refused()
    {
        using var root = new TempDir();
        string path = Path.Combine(root.Path, "boundary.7z");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        file.SetLength(32L + SharpArchiveReader.MaxSevenZipNextHeaderBytes + 1);
        file.Write(Header((ulong)SharpArchiveReader.MaxSevenZipNextHeaderBytes));
        SharpArchiveReader.AdmitSevenZipHeader(file);

        file.Position = 0;
        file.Write(Header((ulong)SharpArchiveReader.MaxSevenZipNextHeaderBytes + 1));
        var error = Assert.Throws<InvalidDataException>(() => SharpArchiveReader.AdmitSevenZipHeader(file));
        Assert.Contains("limit", error.Message, StringComparison.Ordinal);
    }

    private static byte[] Header(ulong size)
    {
        var bytes = new byte[32];
        new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(20, 8), size);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), Crc32.HashToUInt32(bytes.AsSpan(12, 20)));
        return bytes;
    }
}
