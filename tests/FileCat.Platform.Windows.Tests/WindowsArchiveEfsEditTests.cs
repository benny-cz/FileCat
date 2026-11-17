using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

[Collection("Native edit snapshot reservations")]
public sealed class WindowsArchiveEfsEditTests
{
    private const string Contents = "Synthetic FileCat archive member";

    [Fact]
    public void Encrypting_the_outer_zip_refuses_the_legacy_edit_before_working_output()
    {
        RequireEfs();
        string path = Directory.CreateTempSubdirectory("filecat-efs-edit-").FullName;
        string archive = Path.Join(path, "source.zip");
        var provider = new ZipProvider(Path.Join(path, "cache"));
        try
        {
            CreateZip(archive);
            var member = Member(provider, archive);
            var baseline = ArchiveBaseline.Of(archive);
            File.Encrypt(archive);
            File.SetLastWriteTimeUtc(archive, new DateTime(baseline.LastWriteUtcTicks, DateTimeKind.Utc));
            Assert.True(baseline.Matches(archive));
            string sessions = Path.Join(path, "sessions");
            var store = new EditSessionStore(sessions, new WindowsFileOperations(), WindowsEditDirectoryLease.OpenRoot);
            var error = Assert.Throws<ArchiveWriteException>(() => store.Create(provider, member, TestContext.Current.CancellationToken));
            Assert.Equal(ArchiveOuterEfs.EditUnavailable, error.Message);
            Assert.False(Directory.Exists(sessions));
            using var readOnly = Assert.IsAssignableFrom<IContentSource>(provider.OpenContent(member));
            byte[] bytes = new byte[Contents.Length];
            Assert.Equal(bytes.Length, readOnly.Read(0, bytes));
            Assert.Equal(Contents, System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally { provider.Release(archive); Directory.Delete(path, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_existing_working_file_encrypted_outside_FileCat_is_refused_before_capture(bool nativeStorage)
    {
        RequireEfs();
        var ct = TestContext.Current.CancellationToken;
        string path = Directory.CreateTempSubdirectory("filecat-efs-working-").FullName;
        string archive = Path.Join(path, "source.zip");
        var provider = new ZipProvider(Path.Join(path, "cache"));
        try
        {
            CreateZip(archive);
            var store = new EditSessionStore(Path.Join(path, "sessions"), new WindowsFileOperations(),
                nativeStorage ? WindowsEditDirectoryLease.OpenRoot : null);
            var record = store.Create(provider, Member(provider, archive), ct);
            Assert.Equal(nativeStorage, record.StorageRootIdentity.Length != 0);
            File.Encrypt(record.WorkingPath);

            string output = Directory.CreateDirectory(Path.Join(path, "temporary")).FullName;
            Assert.Equal(ArchiveOuterEfs.WorkingCopyUnavailable,
                Assert.Throws<ArchiveWriteException>(() => store.PrepareCommit(record, output, ct)).Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
            Assert.Empty(Directory.GetDirectories(store.SessionDirectory(record), "commit-*"));
            var local = new LocalFileSystemProvider();
            Assert.Equal(ArchiveOuterEfs.WorkingCopyUnavailable,
                Assert.Throws<ArchiveWriteException>(() => store.CaptureSaveCopy(record, output, local, ct.ThrowIfCancellationRequested)).Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
            Assert.Equal(EditState.Unchanged, store.ReviewWorking(record, local).State);
            Assert.Equal(Contents, File.ReadAllText(record.WorkingPath));
            Assert.True(File.GetAttributes(record.WorkingPath).HasFlag(FileAttributes.Encrypted));
        }
        finally { provider.Release(archive); Directory.Delete(path, recursive: true); }
    }

    private static void RequireEfs()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Requires a disposable normal-user Windows VM with EFS enabled.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }

    private static void CreateZip(string archive)
    {
        using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
        using var content = new StreamWriter(zip.CreateEntry("member.txt").Open());
        content.Write(Contents);
    }

    private static ItemRef Member(ZipProvider provider, string archive) =>
        new(provider.GetContainerLocation(archive)!, "member.txt", EntryKind.File, Contents.Length, 0);
}
