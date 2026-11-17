using FileCat.Core.Records;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsFileDetailsTests
{
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
