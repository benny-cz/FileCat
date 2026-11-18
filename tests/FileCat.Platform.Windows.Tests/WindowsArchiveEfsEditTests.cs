using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;

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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void An_open_working_file_cannot_be_encrypted_during_capture(bool nativeStorage, bool saveCopy)
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
            string search = nativeStorage && !saveCopy ? store.SessionDirectory(record) : output;
            string? stage = null;
            Exception? rejectedEncryption = null;
            void EncryptAfterStageCreation()
            {
                stage ??= Directory.EnumerateFiles(search, Path.GetFileName(record.WorkingPath), SearchOption.AllDirectories)
                    .FirstOrDefault(candidate => candidate != record.WorkingPath);
                if (stage is not null && rejectedEncryption is null)
                    rejectedEncryption = Record.Exception(() => File.Encrypt(record.WorkingPath));
            }

            using (var copy = saveCopy
                ? store.CaptureSaveCopy(record, output, new LocalFileSystemProvider(), EncryptAfterStageCreation)
                : store.PrepareCommit(record, output, ct, EncryptAfterStageCreation))
            {
                Assert.Equal(copy.Path, stage);
                AssertBlockedEncryption(rejectedEncryption);
                using var reader = new StreamReader(new FileStream(copy.Path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete));
                Assert.Equal(Contents, reader.ReadToEnd());
                Assert.False(File.GetAttributes(record.WorkingPath).HasFlag(FileAttributes.Encrypted));
            }
            File.Encrypt(record.WorkingPath);
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

            if (placement == "late source")
            {
                Exception? rejectedEncryption = null;
                using var published = restarted.SaveRecoveredNativeCopy(record, capture, target, ct, () =>
                {
                    if (rejectedEncryption is null && Directory.EnumerateFiles(destination).Any())
                        rejectedEncryption = Record.Exception(() => File.Encrypt(copy.Path));
                });
                AssertBlockedEncryption(rejectedEncryption);
                Assert.NotNull(published.Published);
                Assert.Null(published.Issue);
                Assert.Equal(Contents, File.ReadAllText(target));
            }
            else
            {
                var failure = Assert.Throws<ArchiveWriteException>(() => restarted.SaveRecoveredNativeCopy(record, capture, target, ct));
                Assert.Equal(placement == "destination stage" ? ArchiveOuterEfs.EditTemporaryOutputUnavailable
                    : ArchiveOuterEfs.EditSnapshotUnavailable, failure.Message);
                Assert.False(File.Exists(target));
                Assert.All(Directory.EnumerateFiles(destination), file => Assert.Equal(0, new FileInfo(file).Length));
            }
            Assert.Equal(placement != "initial source", File.Exists(Path.Combine(capture, EditSaveCopyIntent.FileName)));
            Assert.Equal(Contents, File.ReadAllText(copy.Path));
            Assert.Equal(Contents, File.ReadAllText(record.WorkingPath));
            Assert.Equal(sourceProtected, File.GetAttributes(copy.Path).HasFlag(FileAttributes.Encrypted));
        }
        finally { provider.Release(archive); Directory.Delete(root, recursive: true); }
    }

    private static void AssertBlockedEncryption(Exception? error)
    {
        const int efsInTransaction = unchecked((int)0x80071AAF);
        const int sharingViolation = unchecked((int)0x80070020);
        const int transactionConflict = unchecked((int)0x80071A90);
        Assert.True(error is IOException { HResult: efsInTransaction or sharingViolation or transactionConflict }, error?.ToString());
    }

    [Theory]
    [InlineData("new")]
    [InlineData("replace")]
    [InlineData("cancel")]
    [InlineData("protected snapshot")]
    [InlineData("protected destination")]
    [InlineData("protected folder")]
    [InlineData("protected name arrives")]
    public void Live_save_copy_binds_payload_and_replacement_to_the_reviewed_files(string scenario)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows native saved-copy publication.");
        if (scenario.StartsWith("protected", StringComparison.Ordinal)) RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-live-efs-edit-copy-").FullName;
        string folder = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        string source = Path.Combine(root, "snapshot.txt"), target = Path.Combine(folder, "edit.txt");
        const string previous = "Original destination bytes", zone = "[ZoneTransfer]\r\nZoneId=3\r\n";
        try
        {
            byte[] payload = "Owned captured edit"u8.ToArray();
            File.WriteAllBytes(source, payload);
            File.WriteAllText(source + ":Zone.Identifier", zone, Encoding.Unicode);
            byte[] mark = File.ReadAllBytes(source + ":Zone.Identifier");
            bool existing = scenario is "replace" or "cancel" or "protected destination";
            if (existing) File.WriteAllText(target, previous);
            var files = new WindowsFileOperations();
            string? originalId = existing ? files.GetFileIdentity(target) : null;
            using var copy = new EditCommitCopy(source, Convert.ToHexString(SHA256.HashData(payload)), 0, payload.Length, files);
            const uint transactionSupport = 0x00200000;
            Assert.SkipUnless((WindowsFileTransaction.VolumeFlags(copy.HeldFile) & transactionSupport) != 0,
                "Requires a test directory on local NTFS with transaction support.");
            if (scenario == "protected snapshot") File.Encrypt(source);
            if (scenario == "protected destination") File.Encrypt(target);
            if (scenario == "protected folder") File.Encrypt(folder);
            bool reachedCommit = false, arrived = false;
            int checkpoints = 0;
            void Check()
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                if (++checkpoints != 3 || scenario != "protected name arrives") return;
                File.WriteAllText(target, previous);
                File.Encrypt(target);
                originalId = files.GetFileIdentity(target);
                arrived = true;
            }

            string? issue = null;
            var failure = Record.Exception(() =>
            {
                if (scenario is "replace" or "cancel")
                    issue = files.PublishEditCopyCore(copy.HeldFile, copy.Sha256, copy.Length, target, Check, () =>
                    {
                        reachedCommit = true;
                        var outsideRead = Record.Exception(() =>
                        {
                            using var reader = new StreamReader(new FileStream(target, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete));
                            Assert.Equal(previous, reader.ReadToEnd());
                        });
                        const int sharingViolation = unchecked((int)0x80070020), transactionConflict = unchecked((int)0x80071A90);
                        Assert.True(outsideRead is null or IOException { HResult: sharingViolation or transactionConflict }, outsideRead?.ToString());
                        Assert.Empty(Directory.EnumerateFiles(folder, ".filecat-edit-copy-*"));
                        if (Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1")
                        {
                            var protectionChange = Record.Exception(() => File.Encrypt(target));
                            Assert.True(protectionChange is IOException or UnauthorizedAccessException, protectionChange?.ToString());
                        }
                        if (scenario == "cancel") throw new OperationCanceledException("Owned fixture canceled before commit.");
                    });
                else issue = EditSessionStore.PublishSaveCopy(copy, target, files, Check);
            });

            Assert.Null(issue);
            Assert.Equal(scenario is "replace" or "cancel", reachedCommit);
            Assert.Equal(scenario == "protected name arrives", arrived);
            Assert.Empty(Directory.EnumerateFiles(folder, ".filecat-edit-copy-*"));
            Assert.Equal(payload, File.ReadAllBytes(source));
            if (scenario is "new" or "replace")
            {
                Assert.Null(failure);
                Assert.Equal(payload, File.ReadAllBytes(target));
                Assert.Equal(mark, File.ReadAllBytes(target + ":Zone.Identifier"));
                Assert.NotEqual(originalId, files.GetFileIdentity(target));
                Assert.False(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            }
            else
            {
                if (scenario == "cancel") Assert.True(failure is OperationCanceledException, failure?.ToString());
                else Assert.IsAssignableFrom<IOException>(failure);
                if (existing || arrived)
                {
                    Assert.NotNull(originalId);
                    Assert.Equal(originalId, files.GetFileIdentity(target));
                    Assert.Equal(previous, File.ReadAllText(target));
                    Assert.Equal(scenario != "cancel", File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
                }
                else Assert.False(File.Exists(target));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("FILECAT_EDIT_LIVE_FAT", false)]
    [InlineData("FILECAT_EDIT_LIVE_SMB", true)]
    public void Live_save_copy_uses_the_protection_capabilities_of_the_prepared_destination(string variable, bool remote)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows saved-copy publication.");
        string? prepared = Environment.GetEnvironmentVariable(variable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(prepared), "Requires the explicit disposable FAT or SMB fixture.");
        string output = Directory.CreateDirectory(Path.Combine(prepared!, $"filecat-live-copy-{Guid.NewGuid():N}")).FullName;
        string root = Directory.CreateTempSubdirectory("filecat-live-output-volume-").FullName;
        string capture = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "capture")).FullName, "capture.bin");
        string target = Path.Combine(output, "saved.bin");
        var files = new WindowsFileOperations();
        byte[] bytes = "Owned test file"u8.ToArray();
        try
        {
            var volume = files.GetVolumeInfo(output);
            Assert.Equal(remote, volume.IsRemote);
            if (!remote) Assert.Equal("FAT32", volume.FileSystem);
            File.WriteAllBytes(capture, bytes);
            using var copy = new EditCommitCopy(capture, Convert.ToHexString(SHA256.HashData(bytes)), 0, bytes.Length, files);
            var check = TestContext.Current.CancellationToken;
            if (remote)
            {
                Assert.Null(EditSessionStore.PublishSaveCopy(copy, target, files, check.ThrowIfCancellationRequested));
                Assert.Equal(bytes, File.ReadAllBytes(target));
            }
            else
            {
                var error = Assert.Throws<IOException>(() => EditSessionStore.PublishSaveCopy(copy, target, files, check.ThrowIfCancellationRequested));
                Assert.Contains("EFS state", error.Message, StringComparison.Ordinal);
                Assert.Empty(Directory.EnumerateFileSystemEntries(output));
            }

            File.WriteAllText(target, "existing before second Save Copy");
            var overwrite = Assert.Throws<IOException>(() => EditSessionStore.PublishSaveCopy(copy, target, files, check.ThrowIfCancellationRequested));
            Assert.Contains("cannot safely replace", overwrite.Message, StringComparison.Ordinal);
            if (!remote)
            {
                File.Encrypt(target);
                Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
                var encrypted = Assert.Throws<ArchiveWriteException>(() => EditSessionStore.PublishSaveCopy(copy, target, files, check.ThrowIfCancellationRequested));
                Assert.Contains("EFS protection", encrypted.Message, StringComparison.Ordinal);
                Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            }
            Assert.Equal("existing before second Save Copy", File.ReadAllText(target));
            Assert.Equal([target], Directory.GetFiles(output));
        }
        finally { Directory.Delete(output, recursive: true); Directory.Delete(root, recursive: true); }
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
