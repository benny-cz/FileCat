using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveEfsSpoolTests
{
    private const string FixtureRequirement = "Requires a disposable Windows NTFS fixture with EFS enabled.";

    [Fact]
    public void Protected_tar_members_can_be_read_forward_without_creating_a_disk_cache()
    {
        if (!IsEfsFixture) Assert.Skip(FixtureRequirement);

        using var directory = new TempDir();
        string archivePath = Path.Join(directory.Path, "protected.tar.gz");
        string cache = Path.Join(directory.Path, "cache");
        var archives = new ArchiveProvider(cache, new ProviderRegistry());
        byte[] payload = new byte[33 * 1024 * 1024];
        payload.AsSpan().Fill(42);
        try
        {
            using (var file = File.Create(archivePath))
            using (var compressed = new GZipStream(file, CompressionLevel.Fastest))
            using (var writer = new TarWriter(compressed))
            using (var content = new MemoryStream(payload))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "payload.bin") { DataStream = content });

            File.Encrypt(archivePath);
            var item = new ItemRef(ArchiveProvider.ForFile(archivePath), "payload.bin", EntryKind.File);
            using (var viewer = Assert.IsType<ProgressiveContent>(archives.OpenContent(item)))
            {
                var refusal = Assert.Throws<ArchiveWriteException>(() => viewer.Read(0, new byte[256]));
                Assert.Equal(ArchiveOuterEfs.SpoolUnavailable, refusal.Message);
            }

            using (var forward = ProgressiveContent.Sequential(Assert.IsType<ProgressiveContent>(archives.OpenContent(item)))!)
            {
                byte[] actual = new byte[256];
                Assert.Equal(actual.Length, forward.Read(0, actual));
                Assert.Equal(actual.Length, forward.Read(actual.Length, actual));
                Assert.All(actual, value => Assert.Equal(42, value));
            }
            Assert.False(Directory.Exists(cache));
        }
        finally { archives.Release(archivePath); }
    }

    [Fact]
    public void An_encrypted_later_rar_volume_refuses_a_nested_disk_spool()
    {
        if (!IsEfsFixture) Assert.Skip(FixtureRequirement);
        using var directory = new TempDir();
        for (int part = 1; part <= 6; part++)
        {
            string name = $"Rar5.multi.part{part:00}.rar";
            File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", name), Path.Join(directory.Path, name));
        }
        string first = Path.Join(directory.Path, "Rar5.multi.part01.rar"), later = Path.Join(directory.Path, "Rar5.multi.part03.rar");
        File.Encrypt(later);
        Assert.False(File.GetAttributes(first).HasFlag(FileAttributes.Encrypted));
        Assert.True(File.GetAttributes(later).HasFlag(FileAttributes.Encrypted));
        string cache = Path.Join(directory.Path, "cache");
        var providers = new ProviderRegistry();
        var archives = new ArchiveProvider(cache, providers);
        providers.Register(archives);
        try
        {
            var archive = ArchiveProvider.ForFile(first);
            var member = new ItemRef(archive.WithPath("exe"), "test.exe", EntryKind.File);
            using var content = Assert.IsType<MemoryContentSource>(archives.OpenContent(member));
            Assert.Equal(45056, content.Length);
            var refusal = Assert.Throws<ArchiveWriteException>(() => archives.OpenSpool(archive.WithPath("exe/test.exe")));
            Assert.Equal(ArchiveOuterEfs.SpoolUnavailable, refusal.Message);
            Assert.False(Directory.Exists(cache));
        }
        finally { archives.Release(first); }
    }

    [Fact]
    public void A_zip_member_cannot_bypass_the_efs_disk_check_through_the_other_provider()
    {
        if (!IsEfsFixture) Assert.Skip(FixtureRequirement);
        using var directory = new TempDir();
        string archive = Path.Join(directory.Path, "protected.zip"), cache = Path.Join(directory.Path, "cache");
        var providers = new ProviderRegistry();
        var zip = new ZipProvider(cache);
        var archives = new ArchiveProvider(cache, providers);
        providers.Register(zip);
        try
        {
            using (var file = ZipFile.Open(archive, ZipArchiveMode.Create))
            using (var member = file.CreateEntry("payload.bin").Open()) member.Write([7, 8, 9]);
            File.Encrypt(archive);
            var location = ZipProvider.ForFile(archive).WithPath("payload.bin");
            Assert.Equal(ArchiveOuterEfs.SpoolUnavailable, Assert.Throws<ArchiveWriteException>(() => archives.OpenSpool(location)).Message);
            Assert.False(Directory.Exists(cache));
        }
        finally { zip.Release(archive); }
    }

    [System.Runtime.Versioning.SupportedOSPlatformGuard("windows")]
    private static bool IsEfsFixture => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1";
}
