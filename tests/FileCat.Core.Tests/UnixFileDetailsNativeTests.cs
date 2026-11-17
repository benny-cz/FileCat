using System.Runtime.InteropServices;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public void Native_summary_describes_files_directories_and_links_without_legacy_value_reads()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) { Assert.Skip("Unix native metadata fixture."); return; }
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-details-").FullName;
        try
        {
            string file = Path.Combine(root, "file.txt"), link = Path.Combine(root, "link");
            File.WriteAllText(file, "payload-sentinel-must-not-appear-in-report");
            File.CreateSymbolicLink(link, "file.txt");
            var reader = new UnixFileRecords();
            var regular = reader.ReadSummary(file, ct);
            Assert.Equal(FileObjectKind.File, regular.Kind);
            Assert.Equal(FactState.Known, regular.Identity.State);
            Assert.Equal(FactState.Known, regular.Allocated.State);
            Assert.Equal(FactState.Known, regular.LinkCount.State);
            Assert.Equal(1u, regular.LinkCount.Value);
            Assert.Equal(FileObjectKind.Directory, reader.ReadSummary(root, ct).Kind);
            var symbolic = reader.ReadSummary(link, ct);
            Assert.Equal(FileObjectKind.SymbolicLink, symbolic.Kind);
            Assert.NotEqual(regular.Identity.Value, symbolic.Identity.Value);
            if (OperatingSystem.IsLinux()) Assert.Equal("file.txt", symbolic.Target.Value?.SubstituteName);
            var full = reader.ReadFull(file, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.DoesNotContain("payload-sentinel", full.ToText());
            Assert.Contains("Not acquired:", full.ToText());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Linux_summary_of_FIFO_does_not_open_it_for_data_or_wait_for_a_writer()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Linux O_PATH fixture."); return; }
        string root = Directory.CreateTempSubdirectory("filecat-fifo-details-").FullName;
        try
        {
            string fifo = Path.Combine(root, "fifo");
            Assert.Equal(0, MakeFifo(fifo, 0x180));
            var snapshot = new UnixFileRecords().ReadSummary(fifo, TestContext.Current.CancellationToken);
            Assert.Equal(FileObjectKind.Fifo, snapshot.Kind);
            Assert.Equal(FactState.Known, snapshot.Identity.State);
        }
        finally { Directory.Delete(root, true); }
    }

    [LibraryImport("libc", EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MakeFifo(string path, uint mode);
}
