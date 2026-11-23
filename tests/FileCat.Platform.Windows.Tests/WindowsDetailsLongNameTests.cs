using FileCat.Core.Records;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// N-WIN long names: a file with a 255-unit Unicode name at the end of a path well past MAX_PATH is described by the
/// summary and the metadata-only full report. Both describe the exact object and length, without a legacy payload read.
/// </summary>
public sealed class WindowsDetailsLongNameTests
{
    [Fact]
    public void Details_describe_a_255_unit_name_beyond_MAX_PATH()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native metadata reader.");
        string root = Directory.CreateTempSubdirectory("fc-long-").FullName;
        try
        {
            string folder = root;
            for (int i = 0; i < 4; i++) folder = Directory.CreateDirectory(Path.Join(folder, new string((char)('a' + i), 100))).FullName;
            const string middle = " 名前 ü ";
            string name = new string('n', 200) + middle + new string('z', 255 - 200 - middle.Length - 4) + ".txt";
            Assert.Equal(255, name.Length);
            string file = Path.Join(folder, name);
            Assert.True(file.Length > 400, $"The fixture path is only {file.Length} characters.");
            File.WriteAllText(file, "long-name payload");

            var reader = new WindowsFileRecords();
            var ct = TestContext.Current.CancellationToken;
            var summary = reader.ReadSummary(file, ct);
            Assert.Equal(FileObjectKind.File, summary.Kind);
            Assert.Equal(new FileInfo(file).Length, summary.LogicalSize.Value);
            Assert.Equal(FactState.Known, summary.Identity.State);
            var full = reader.ReadFull(file, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.Equal(summary.Identity.Value, full.Summary.Identity.Value);
            Assert.DoesNotContain("long-name payload", full.ToText());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
