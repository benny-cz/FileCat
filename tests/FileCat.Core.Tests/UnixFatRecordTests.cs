using FileCat.Core.Inspect;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>D-56 on Linux, as root: an item's raw directory entry on mounted FAT32 and exFAT volumes (CI mounts them).</summary>
public sealed class UnixFatRecordTests
{
    private static string Field(InspectionSection section, string name) => section.Fields.Single(f => f.Name == name).Value;

    [Fact]
    public void As_root_a_file_on_FAT32_and_exFAT_shows_its_own_directory_entry()
    {
        string? mounts = Environment.GetEnvironmentVariable("FILECAT_TEST_FAT_MOUNTS");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(mounts))
        {
            Assert.Skip("Set FILECAT_TEST_FAT_MOUNTS to FAT32 and exFAT mounts, and run as root.");
            return;
        }
        foreach (string mount in mounts.Split(','))
        {
            string folder = Directory.CreateDirectory(Path.Combine(mount, "sub folder")).FullName;
            string file = Path.Combine(folder, "Report Final.txt");
            File.WriteAllText(file, new string('x', 5000));
            var report = new UnixFileRecords().Read(file, TestContext.Current.CancellationToken);
            var section = report.Sections.Single(s => s.Title.StartsWith("Directory entry", StringComparison.Ordinal));
            if (mount.Contains("exfat", StringComparison.Ordinal))
            {
                Assert.Equal("Directory entry (exFAT)", section.Title);
                Assert.Equal("Report Final.txt", Field(section, "Name"));
                Assert.EndsWith("matches", Field(section, "Entry set"), StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal("Directory entry (FAT32)", section.Title);
                Assert.StartsWith("Report Final.txt", Field(section, "Long name"), StringComparison.Ordinal);
                Assert.StartsWith("REPORT~1.TXT", Field(section, "Short name"), StringComparison.Ordinal);
            }
        }
    }
}
