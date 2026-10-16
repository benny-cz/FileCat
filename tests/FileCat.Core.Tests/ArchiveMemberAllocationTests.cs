using System.Formats.Tar;
using FileCat.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveMemberAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void In_memory_tar_member_does_not_allocate_a_second_full_payload_copy()
    {
        using var dir = new TempDir();
        const int length = 16 * 1024 * 1024;
        var original = new byte[length];
        new Random(537).NextBytes(original);
        string path = Path.Combine(dir.Path, "owned.tar");
        using (var file = File.Create(path))
        using (var writer = new TarWriter(file, TarEntryFormat.Ustar))
        using (var data = new MemoryStream(original, writable: false))
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "member.bin")
            { DataStream = data, ModificationTime = DateTimeOffset.UnixEpoch });
        output.WriteLine($"TAR_INPUT_SHA256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original))}");
        using (var input = File.OpenRead(path))
            output.WriteLine($"TAR_ARCHIVE_SHA256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input))}");

        var registry = new ProviderRegistry();
        registry.Register(new LocalFileSystemProvider());
        var provider = new ArchiveProvider(Path.Combine(dir.Path, "spool"), registry);
        var folder = ArchiveProvider.ForFile(path);
        long before = GC.GetAllocatedBytesForCurrentThread();
        using var content = Assert.IsType<MemoryContentSource>(provider.OpenContent(new ItemRef(folder, "member.bin", EntryKind.File, length, 0)));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"TAR_MEMBER_ALLOCATED_BYTES={allocated}");
        Assert.True(allocated < 25L * 1024 * 1024, $"Opening one 16 MiB TAR member allocated {allocated:N0} bytes on this thread.");
        Assert.Equal(length, content.Length);
        Span<byte> first = stackalloc byte[32], last = stackalloc byte[32];
        Assert.Equal(first.Length, content.Read(0, first));
        Assert.Equal(last.Length, content.Read(length - last.Length, last));
        Assert.True(first.SequenceEqual(original.AsSpan(0, first.Length)));
        Assert.True(last.SequenceEqual(original.AsSpan(length - last.Length)));
        Assert.Equal(0, content.Read(length, last));
        provider.Release(path);
    }
}
