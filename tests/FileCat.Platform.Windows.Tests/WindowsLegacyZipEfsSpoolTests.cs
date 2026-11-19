using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.Edit;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsLegacyZipEfsSpoolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nested_edit_is_refused_before_cache_or_working_output(bool encrypted)
    {
        if (encrypted) RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-efs-nested-zip-").FullName;
        string path = Path.Join(root, "outer.zip"), cache = Path.Join(root, "cache");
        var zip = new ZipProvider(cache);
        try
        {
            using var inner = new MemoryStream();
            using (var archive = new ZipArchive(inner, ZipArchiveMode.Create, leaveOpen: true))
            using (var payload = archive.CreateEntry("payload.bin").Open())
                payload.Write([6, 7, 8]);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var member = archive.CreateEntry("inner.zip").Open())
                member.Write(inner.ToArray());
            if (encrypted) File.Encrypt(path);
            var location = new Location(Schemes.Zip, "", ZipProvider.ForFile(path).WithPath("inner.zip"));
            var item = new ItemRef(location, "payload.bin", EntryKind.File);
            string sessions = Path.Join(root, "sessions");
            var store = new EditSessionStore(sessions, new WindowsFileOperations());
            Assert.Equal(ZipProvider.NestedEditUnavailable, Assert.Throws<NotSupportedException>(() => zip.OpenEditContent(item)).Message);
            Assert.Equal(ZipProvider.NestedEditUnavailable, Assert.Throws<NotSupportedException>(() => store.Create(zip, item, TestContext.Current.CancellationToken)).Message);
            Assert.False(Directory.Exists(sessions));
            Assert.False(Directory.Exists(cache));
            if (encrypted)
            {
                Assert.Equal(ArchiveOuterEfs.SpoolUnavailable, Assert.Throws<ArchiveWriteException>(() => zip.OpenContent(item)).Message);
                Assert.False(Directory.Exists(cache));
                Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
            }
        }
        finally
        {
            zip.Release(path);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Protected_zip_members_remain_readable_in_memory_or_forward_only_without_disk_cache()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-efs-progressive-zip-").FullName;
        string path = Path.Join(root, "outer.zip"), cache = Path.Join(root, "cache");
        var zip = new ZipProvider(cache);
        try
        {
            const byte value = 0x2A;
            byte[] block = new byte[1024 * 1024];
            block.AsSpan().Fill(value);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var small = archive.CreateEntry("small.bin").Open()) small.Write([1, 3, 5]);
                using var large = archive.CreateEntry("large.bin").Open();
                for (int i = 0; i <= ZipProvider.MaxMemberInMemory / block.Length; i++) large.Write(block);
            }
            File.Encrypt(path);
            var location = ZipProvider.ForFile(path);
            var member = new ItemRef(location, "large.bin", EntryKind.File);
            using (var random = Assert.IsType<ProgressiveContent>(zip.OpenContent(member)))
            {
                var refusal = Assert.Throws<ArchiveWriteException>(() => random.Read(0, new byte[256]));
                Assert.Equal(ArchiveOuterEfs.SpoolUnavailable, refusal.Message);
            }
            using (var forward = ProgressiveContent.Sequential(Assert.IsType<ProgressiveContent>(zip.OpenContent(member)))!)
            {
                var read = new byte[256];
                Assert.Equal(read.Length, forward.Read(0, read));
                Assert.Equal(read.Length, forward.Read(read.Length, read));
                Assert.All(read, b => Assert.Equal(value, b));
            }
            using (var small = Assert.IsType<MemoryContentSource>(zip.OpenContent(new ItemRef(location, "small.bin", EntryKind.File))))
            {
                var read = new byte[3];
                Assert.Equal(read.Length, small.Read(0, read));
                Assert.Equal(new byte[] { 1, 3, 5 }, read);
            }
            Assert.False(Directory.Exists(cache));
        }
        finally
        {
            zip.Release(path);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RequireEfs() => WindowsStorageAbiTests.RequireNativeEfsOutputFixture();
}
