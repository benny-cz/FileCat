using FileCat.Core.Records;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsFileDetailsTests
{
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
