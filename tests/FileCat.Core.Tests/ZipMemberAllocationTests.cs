using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ZipMemberAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void Borrowed_content_exposes_only_its_logical_slice()
    {
        byte[] backing = [0xEE, 1, 2, 3, 0xFF];
        using var content = new MemoryContentSource("borrowed", new ArraySegment<byte>(backing, 1, 3));
        Span<byte> read = stackalloc byte[5];
        Assert.Equal(3, content.Length);
        Assert.Equal(3, content.Read(0, read));
        Assert.Equal([1, 2, 3], read[..3].ToArray());
        Assert.Equal(2, content.Read(1, read));
        Assert.Equal([2, 3], read[..2].ToArray());
        Assert.Equal(0, content.Read(3, read));
        Assert.Equal(new ContentRevision(3, 0), content.GetRevision());
    }

    [Fact]
    public void In_memory_member_does_not_allocate_a_second_full_payload_copy()
    {
        using var dir = new TempDir();
        const int length = 16 * 1024 * 1024;
        var original = new byte[length];
        new Random(536).NextBytes(original);
        string path = Path.Combine(dir.Path, "owned.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var entry = archive.CreateEntry("member.bin", CompressionLevel.NoCompression).Open())
            entry.Write(original);
        output.WriteLine($"ZIP_INPUT_SHA256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original))}");
        using (var input = File.OpenRead(path))
            output.WriteLine($"ZIP_ARCHIVE_SHA256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input))}");

        var provider = new ZipProvider(Path.Combine(dir.Path, "spool"));
        var folder = ZipProvider.ForFile(path);
        var member = new ItemRef(folder, "member.bin", EntryKind.File, length, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        using var content = Assert.IsType<MemoryContentSource>(provider.OpenContent(member));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"ZIP_MEMBER_ALLOCATED_BYTES={allocated}");
        Assert.True(allocated < 25L * 1024 * 1024, $"Opening one 16 MiB ZIP member allocated {allocated:N0} bytes on this thread.");
        Assert.Equal(length, content.Length);
        Span<byte> first = stackalloc byte[32], last = stackalloc byte[32];
        Assert.Equal(first.Length, content.Read(0, first));
        Assert.Equal(last.Length, content.Read(length - last.Length, last));
        Assert.True(first.SequenceEqual(original.AsSpan(0, first.Length)));
        Assert.True(last.SequenceEqual(original.AsSpan(length - last.Length)));
        Assert.Equal(0, content.Read(length, last));
    }
}
