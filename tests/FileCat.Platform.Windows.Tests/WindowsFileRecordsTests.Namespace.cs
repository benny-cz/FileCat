using FileCat.Core.Records;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsFileRecordsTests
{
    [Fact]
    public void Full_details_observe_the_case_flag_on_each_held_NTFS_directory()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires NTFS per-directory case sensitivity.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_NTFS_NAMESPACE_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Requires a disposable NTFS fixture with both directory policies prepared.");

        string ordinary = Path.Combine(root, "ordinary"), sensitive = Path.Combine(root, "sensitive");
        Assert.True(File.Exists(Path.Combine(ordinary, "mixed.txt")));
        Assert.False(File.Exists(Path.Combine(sensitive, "mixed.txt")));

        var reader = new WindowsFileRecords();
        var ct = TestContext.Current.CancellationToken;
        const string title = "NTFS directory namespace";
        foreach ((string directory, string flag) in new[] { (ordinary, "Clear"), (sensitive, "Set") })
        {
            var summary = reader.ReadSummary(directory, ct);
            Assert.Equal("NTFS", summary.FileSystem.Value);
            Assert.DoesNotContain(title, summary.ToReport().ToText());
            var full = reader.ReadFull(directory, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(summary.Identity.Value, full.Summary.Identity.Value);
            var section = Assert.Single(full.Technical.Sections, entry => entry.Title == title);
            Assert.Contains(("Scope", "Name lookup in this directory"), section.Fields);
            Assert.Contains(("Case-sensitive directory flag", flag), section.Fields);
        }

        foreach (string path in new[] { Path.Combine(sensitive, "MiXeD.txt"), Path.Combine(root, "sensitive-junction") })
            Assert.DoesNotContain(reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct).Technical.Sections, entry => entry.Title == title);
    }
}
