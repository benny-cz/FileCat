using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>
/// F-OBS on macOS: the summary is a separate minimal getattrlist request. For a folder holding many entries it describes
/// the folder object itself and states that it requested no entry count, fork values or APFS sharing statistics. The
/// request asks for no directory attributes at all (UnixFileRecords.ReadMacDetailsFields).
/// </summary>
public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public void Mac_summary_of_a_large_folder_is_a_minimal_request_without_an_entry_count()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Darwin getattrlist summary.");
        using var temp = new TempDir();
        // macOS reaches its temporary folder through /var, a link; the reader does not follow links in the path.
        string root = temp.Path.StartsWith("/var/", StringComparison.Ordinal) ? "/private" + temp.Path : temp.Path;
        string folder = Directory.CreateDirectory(Path.Join(root, "many")).FullName;
        for (int i = 0; i < 347; i++) File.WriteAllText(Path.Join(folder, $"entry-{i}.txt"), "x");

        var summary = new UnixFileRecords().ReadSummary(folder, TestContext.Current.CancellationToken);
        Assert.Equal(FileObjectKind.Directory, summary.Kind);
        Assert.Contains(summary.Notes, note => note.Contains("excludes directory entry count", StringComparison.Ordinal));
        Assert.DoesNotContain("347 entries", summary.ToReport().ToText());
    }
}
