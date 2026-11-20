using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public void Mac_full_report_distinguishes_HFS_Plus_and_HFSX_name_behavior()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires Darwin HFS volume attributes.");
        string? plain = Environment.GetEnvironmentVariable("FILECAT_TEST_HFS_INSENSITIVE");
        string? sensitive = Environment.GetEnvironmentVariable("FILECAT_TEST_HFS_SENSITIVE");
        if (plain is null || sensitive is null) Assert.Skip("Requires two prepared HFS Plus volumes.");

        const string title = "HFS volume namespace";
        var ct = TestContext.Current.CancellationToken;
        var reader = new UnixFileRecords();
        Assert.True(File.Exists(Path.Join(plain, "mixed")));
        Assert.False(File.Exists(Path.Join(sensitive, "mixed")));
        foreach (var (path, caseSensitive) in new[]
        {
            (plain, false), (Path.Join(plain, "MiXeD"), false),
            (sensitive, true), (Path.Join(sensitive, "MiXeD"), true),
            (Path.Join(plain, "link-to-sensitive"), false), (Path.Join(plain, "dir-alias", "MiXeD"), true),
        })
        {
            Assert.DoesNotContain(title, reader.ReadSummary(path, ct).ToReport().ToText());
            var report = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, report.ContentKind);
            var section = Assert.Single(report.Technical.Sections, section => section.Title == title);
            Assert.Contains(("Case sensitive", caseSensitive ? "Yes" : "No"), section.Fields);
            Assert.Contains(("Case preserving", "Yes"), section.Fields);
            Assert.Contains(section.Fields, field => field.Name == "Unicode normalization" &&
                field.Value.Contains("decomposed Unicode form", StringComparison.Ordinal));
            Assert.DoesNotContain(report.Technical.Sections, section => section.Title.StartsWith("APFS", StringComparison.Ordinal) ||
                section.Title == "Location · APFS volume protection");
        }

        const string composed = "caf\u00e9", decomposed = "cafe\u0301";
        foreach (string root in new[] { plain, sensitive })
        {
            var names = Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).ToArray();
            Assert.Contains(decomposed, names);
            Assert.DoesNotContain(composed, names);
            var original = reader.ReadSummary(Path.Join(root, decomposed), ct);
            var equivalent = reader.ReadFull(Path.Join(root, composed), FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(FactState.Known, original.Identity.State);
            Assert.Equal(original.Identity.Value, equivalent.Summary.Identity.Value);
            Assert.Contains(equivalent.Technical.Sections, section => section.Title == title);
        }
    }
}
