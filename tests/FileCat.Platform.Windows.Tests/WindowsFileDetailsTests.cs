using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Records;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsFileDetailsTests
{
    [Fact]
    public void Native_details_refuse_a_missing_or_denied_subject_with_a_typed_error_and_no_snapshot()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native metadata fixture.");
        var ct = TestContext.Current.CancellationToken;
        var reader = new WindowsFileRecords();
        string root = Directory.CreateTempSubdirectory("filecat-details-subjects-").FullName;
        string closed = Path.Join(root, "closed"), denied = Path.Join(closed, "denied.txt");
        FileSecurity? fileOriginal = null; DirectorySecurity? folderOriginal = null;
        try
        {
            string missing = Path.Join(root, "never-created.txt");
            Assert.Throws<FileNotFoundException>(() => reader.ReadSummary(missing, ct));
            Assert.Throws<FileNotFoundException>(() => reader.ReadFull(missing, FileDetailsPolicy.MetadataOnly, ct));
            Assert.Throws<FileNotFoundException>(() => reader.ReadSummary(Path.Join(root, "absent-folder", "child.txt"), ct));
            // An elevated process read this fixture's metadata despite the deny entries (observed on Windows 11 x64);
            // FileCat details run in the normal user process, where the denial must hold.
            if (Core.State.WindowsTokenAuthority.IsHighIntegrity) return;
            Directory.CreateDirectory(closed);
            File.WriteAllText(denied, "payload-sentinel-behind-a-deny-entry");
            using var identity = WindowsIdentity.GetCurrent();
            // NTFS grants read-attributes on a child implicitly when its folder allows listing, so both entries are needed.
            var file = new FileInfo(denied); fileOriginal = file.GetAccessControl();
            var fileSecurity = file.GetAccessControl();
            fileSecurity.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadAttributes | FileSystemRights.ReadData, AccessControlType.Deny));
            file.SetAccessControl(fileSecurity);
            var folder = new DirectoryInfo(closed); folderOriginal = folder.GetAccessControl();
            var folderSecurity = folder.GetAccessControl();
            folderSecurity.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
            folder.SetAccessControl(folderSecurity);
            Assert.Throws<UnauthorizedAccessException>(() => reader.ReadSummary(denied, ct));
            Assert.Throws<UnauthorizedAccessException>(() => reader.ReadFull(denied, FileDetailsPolicy.MetadataOnly, ct));
            var summary = reader.ReadSummary(closed, ct); // the folder itself is still described from its own metadata
            Assert.Equal((FileObjectKind.Directory, FactState.Known), (summary.Kind, summary.Identity.State));
        }
        finally
        {
            if (folderOriginal is not null)
            {
                var restored = new DirectorySecurity();
                restored.SetSecurityDescriptorBinaryForm(folderOriginal.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                new DirectoryInfo(closed).SetAccessControl(restored);
            }
            if (fileOriginal is not null)
            {
                var restored = new FileSecurity();
                restored.SetSecurityDescriptorBinaryForm(fileOriginal.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                new FileInfo(denied).SetAccessControl(restored);
            }
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("FILECAT_STORAGE_NATIVE_64K_TARGET", "NTFS")]
    [InlineData("FILECAT_STORAGE_NATIVE_READONLY_TARGET", "NTFS")]
    [InlineData("FILECAT_STORAGE_NATIVE_FAT_TARGET", "FAT32")]
    [InlineData("FILECAT_STORAGE_NATIVE_EXFAT_TARGET", "exFAT")]
    [InlineData("FILECAT_STORAGE_NATIVE_REFS_TARGET", "ReFS")]
    public void Native_details_describe_owned_files_on_their_actual_volumes(string variable, string expectedFormat)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native metadata fixture.");
        string? path = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(path)) Assert.Skip("Set " + variable + " to a file on the prepared volume.");
        Assert.True(File.Exists(path));
        var ct = TestContext.Current.CancellationToken;
        var reader = new WindowsFileRecords();
        var summary = reader.ReadSummary(path, ct);
        Assert.Equal(FileObjectKind.File, summary.Kind);
        Assert.Equal(FactState.Known, summary.FileSystem.State);
        Assert.Equal(FactScope.Volume, summary.FileSystem.Scope);
        Assert.Equal(expectedFormat, summary.FileSystem.Value, ignoreCase: true);
        Assert.Equal(new FileInfo(path).Length, summary.LogicalSize.Value);
        Assert.Equal(FactState.Known, summary.Allocated.State);
        Assert.Equal(FactScope.MainData, summary.Allocated.Scope);
        var full = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
        Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
        Assert.Equal(summary.FileSystem, full.Summary.FileSystem);
        Assert.NotEqual(summary.Revision, full.Summary.Revision);
        if (expectedFormat != "NTFS")
            Assert.DoesNotContain(full.Technical.Sections, section => section.Title == "Main-stream extent records");
    }

    [Fact]
    public void Summary_and_metadata_full_inspect_one_object_without_legacy_payload_sections()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows native metadata fixture."); return; }
        string root = Path.Combine(Path.GetTempPath(), "filecat-details-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "sample.txt");
            File.WriteAllText(file, "payload-must-not-appear-in-report");
            var reader = new WindowsFileRecords();
            var summary = reader.ReadSummary(file, TestContext.Current.CancellationToken);
            Assert.Equal(FileObjectKind.File, summary.Kind);
            Assert.Equal(new FileInfo(file).Length, summary.LogicalSize.Value);
            Assert.Equal(FactState.Known, summary.Identity.State);
            Assert.Equal(FactScope.MainData, summary.Allocated.Scope);
            Assert.Equal(1u, summary.LinkCount.Value);
            Assert.Equal(FactState.NotApplicable, summary.ReparseTag.State);
            var full = reader.ReadFull(file, FileDetailsPolicy.MetadataOnly, TestContext.Current.CancellationToken);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.NotEqual(summary.Revision, full.Summary.Revision);
            Assert.DoesNotContain("payload-must-not-appear", full.ToText());
            Assert.Contains("Not acquired:", full.ToText());
            Assert.Contains("Created", full.ToText());
            Assert.DoesNotContain(full.Technical.Sections, section => section.Title == "Windows Overlay Filter backing");
            var directory = reader.ReadSummary(root, TestContext.Current.CancellationToken);
            Assert.Equal(FileObjectKind.Directory, directory.Kind);
            Assert.Contains("not its contents", directory.ToReport().ToText());
            Assert.Equal(FactScope.DirectoryPolicy, directory.Compression.Scope);
            Assert.Throws<OperationCanceledException>(() => reader.ReadSummary(file, new CancellationToken(true)));
            Assert.Throws<NotSupportedException>(() => reader.ReadFull(file, (FileDetailsPolicy)10, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, true); }
    }
}
