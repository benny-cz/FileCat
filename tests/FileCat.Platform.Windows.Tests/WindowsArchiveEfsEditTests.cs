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

    [Fact]
    public void A_legacy_commit_review_discloses_encryption_and_keeps_the_working_copy()
    {
        RequireEfs();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-efs-commit-review-").FullName;
        string archive = Path.Join(root, "source.zip");
        var provider = new ZipProvider(Path.Join(root, "cache"));
        try
        {
            CreateZip(archive);
            var store = new EditSessionStore(Path.Join(root, "sessions"), new WindowsFileOperations());
            var record = store.Create(provider, Member(provider, archive), ct);
            File.WriteAllText(record.WorkingPath, "edited member still available");
            byte[] original = File.ReadAllBytes(archive);
            File.Encrypt(archive);
            File.SetLastWriteTimeUtc(archive, new DateTime(record.Baseline.LastWriteUtcTicks, DateTimeKind.Utc));
            Assert.True(record.Baseline.Matches(archive));

            var review = store.ReviewCommit(record, ct);
            Assert.Equal(CommitCheck.ArchiveProtectedByEfs, review.Check);
            Assert.Null(review.Baseline);
            Assert.Equal(ArchiveOuterEfs.UpdateUnavailable,
                Assert.Throws<ArchiveWriteException>(() => store.CommitPlan(record, rebase: true)).Message);
            Assert.Equal("edited member still available", File.ReadAllText(record.WorkingPath));
            Assert.True(File.GetAttributes(archive).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(original, File.ReadAllBytes(archive));
            Assert.Empty(Directory.EnumerateFiles(root, ".filecat-zip-*"));
        }
        finally { provider.Release(archive); Directory.Delete(root, recursive: true); }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_efs_change_after_open_leaves_no_captured_working_payload(bool nativeStorage)
    {
        RequireEfs();
        var ct = TestContext.Current.CancellationToken;
        string path = Directory.CreateTempSubdirectory("filecat-efs-working-drift-").FullName;
        string archive = Path.Join(path, "source.zip");
        var provider = new ZipProvider(Path.Join(path, "cache"));
        try
        {
            CreateZip(archive);
            var store = new EditSessionStore(Path.Join(path, "sessions"), new WindowsFileOperations(),
                nativeStorage ? WindowsEditDirectoryLease.OpenRoot : null);
            var record = store.Create(provider, Member(provider, archive), ct);
            string output = Directory.CreateDirectory(Path.Join(path, "temporary")).FullName;
            string search = nativeStorage ? store.SessionDirectory(record) : output;
            string? stage = null;
            int stageChecks = 0;
            void EncryptAfterStageCreation()
            {
                stage ??= Directory.EnumerateFiles(search, Path.GetFileName(record.WorkingPath), SearchOption.AllDirectories)
                    .FirstOrDefault(candidate => candidate != record.WorkingPath);
                // Native capture creates the payload file before entering the initial two revision checks.
                if (stage is not null && ++stageChecks == (nativeStorage ? 3 : 1)) File.Encrypt(record.WorkingPath);
            }

            var error = Assert.Throws<ArchiveWriteException>(() => store.PrepareCommit(record, output, ct, EncryptAfterStageCreation));
            Assert.Equal(ArchiveOuterEfs.WorkingCopyUnavailable, error.Message);
            Assert.NotNull(stage);
            if (File.Exists(stage)) Assert.Equal(0, new FileInfo(stage).Length);
            Assert.Equal(Contents, File.ReadAllText(record.WorkingPath));
            Assert.True(File.GetAttributes(record.WorkingPath).HasFlag(FileAttributes.Encrypted));
        }
        finally { provider.Release(archive); Directory.Delete(path, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Save_copy_capture_refuses_an_efs_inherited_temporary_file_before_payload(bool nativeStorage)
    {
        RequireEfs();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-efs-edit-output-").FullName;
        string archive = Path.Join(root, "source.zip");
        var provider = new ZipProvider(Path.Join(root, "cache"));
        try
        {
            CreateZip(archive);
            var store = new EditSessionStore(Path.Join(root, "sessions"), new WindowsFileOperations(),
                nativeStorage ? WindowsEditDirectoryLease.OpenRoot : null);
            var record = store.Create(provider, Member(provider, archive), ct);
            string output = Directory.CreateDirectory(Path.Join(root, "encrypted-temp")).FullName;
            File.Encrypt(output);
            string control = Path.Join(output, "inheritance-control.bin");
            File.WriteAllBytes(control, []);
            Assert.True(File.GetAttributes(control).HasFlag(FileAttributes.Encrypted));
            File.Delete(control);

            Assert.Equal(ArchiveOuterEfs.EditTemporaryOutputUnavailable,
                Assert.Throws<ArchiveWriteException>(() => store.CaptureSaveCopy(record, output, new LocalFileSystemProvider(),
                    ct.ThrowIfCancellationRequested)).Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
            Assert.Equal(Contents, File.ReadAllText(record.WorkingPath));
            Assert.False(File.GetAttributes(record.WorkingPath).HasFlag(FileAttributes.Encrypted));
        }
        finally { provider.Release(archive); Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("initial source")]
    [InlineData("late source")]
    [InlineData("destination stage")]
    public void Recovered_save_copy_keeps_efs_payloads_out_of_unreviewed_publication(string placement)
    {
        RequireEfs();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-efs-recovered-edit-").FullName;
        string archive = Path.Join(root, "source.zip");
        var provider = new ZipProvider(Path.Join(root, "cache"));
        try
        {
            CreateZip(archive);
            var operations = new WindowsFileOperations();
            var store = new EditSessionStore(Path.Join(root, "sessions"), operations, WindowsEditDirectoryLease.OpenRoot,
                WindowsEditSaveCopyDestination.Open);
            var record = store.Create(provider, Member(provider, archive), ct);
            var copy = store.PrepareCommit(record, root, ct);
            string capture = Path.GetDirectoryName(Path.GetDirectoryName(copy.Path))!;
            string keep = Path.Join(capture, "simulate-restart.txt");
            File.WriteAllText(keep, "Keep this owned capture during disposal.");
            Assert.ThrowsAny<IOException>(copy.Dispose);
            File.Delete(keep);

            string destination = Directory.CreateDirectory(Path.Join(root, "output")).FullName;
            string target = Path.Join(destination, "saved.txt");
            var restarted = new EditSessionStore(store.Root, operations, WindowsEditDirectoryLease.OpenRoot,
                WindowsEditSaveCopyDestination.Open);
            bool sourceProtected = placement == "initial source";
            if (sourceProtected)
            {
                File.Encrypt(copy.Path);
                var finding = Assert.Single(restarted.ReviewRetainedCaptures(record, ct));
                Assert.Equal(EditCaptureRecoveryState.NeedsReview, finding.State);
                Assert.Contains(ArchiveOuterEfs.EditSnapshotUnavailable, finding.Reason, StringComparison.Ordinal);
            }
            if (placement == "destination stage") File.Encrypt(destination);

            var failure = Assert.Throws<ArchiveWriteException>(() => restarted.SaveRecoveredNativeCopy(record, capture, target, ct, () =>
            {
                if (placement != "late source" || sourceProtected || !Directory.EnumerateFiles(destination).Any()) return;
                File.Encrypt(copy.Path);
                sourceProtected = true;
            }));
            Assert.Equal(placement == "destination stage" ? ArchiveOuterEfs.EditTemporaryOutputUnavailable
                : ArchiveOuterEfs.EditSnapshotUnavailable, failure.Message);
            Assert.Equal(placement != "destination stage", sourceProtected);
            Assert.False(File.Exists(target));
            Assert.Equal(placement != "initial source", File.Exists(Path.Combine(capture, EditSaveCopyIntent.FileName)));
            Assert.All(Directory.EnumerateFiles(destination), file => Assert.Equal(0, new FileInfo(file).Length));
            Assert.Equal(Contents, File.ReadAllText(copy.Path));
            Assert.Equal(Contents, File.ReadAllText(record.WorkingPath));
            Assert.Equal(sourceProtected, File.GetAttributes(copy.Path).HasFlag(FileAttributes.Encrypted));
        }
        finally { provider.Release(archive); Directory.Delete(root, recursive: true); }
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
