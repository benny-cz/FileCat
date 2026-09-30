using FileCat.Core.Inspect;

namespace FileCat.Platform.Windows.Tests;

/// <summary>D-56 on Windows, as administrator: an item's raw directory entry on a FAT32 drive (CI makes one from a virtual disk).</summary>
public sealed class FatDriveRecordTests
{
    private static string Field(InspectionSection section, string name) => section.Fields.Single(f => f.Name == name).Value;

    [Fact]
    public void As_administrator_a_file_on_FAT32_shows_its_entry_and_Windows_case_bits()
    {
        string? drive = Environment.GetEnvironmentVariable("FILECAT_TEST_FAT_DRIVE");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(drive) || !Environment.IsPrivilegedProcess)
        {
            Assert.Skip("Set FILECAT_TEST_FAT_DRIVE to a FAT32 drive's root, and run as administrator.");
            return;
        }
        string folder = Directory.CreateDirectory(Path.Combine(drive, "filecat-records", Guid.NewGuid().ToString("N")[..8])).FullName;
        try
        {
            // A name that fits 8.3 in lower case: Windows keeps no long name for it, only the case bits.
            string lower = Path.Combine(folder, "readme.txt");
            File.WriteAllText(lower, "x");
            var entry = new WindowsFileRecords().Read(lower, TestContext.Current.CancellationToken).Sections.Single(s => s.Title == "Directory entry (FAT32)");
            Assert.StartsWith("readme.txt (stored as “README  TXT”)", Field(entry, "Short name"), StringComparison.Ordinal);
            Assert.DoesNotContain(entry.Fields, f => f.Name == "Long name");
            Assert.Contains("base in lower case, extension in lower case", Field(entry, "Reserved byte"), StringComparison.Ordinal);

            string longName = Path.Combine(folder, "Report Final.txt");
            File.WriteAllText(longName, new string('x', 5000));
            var named = new WindowsFileRecords().Read(longName, TestContext.Current.CancellationToken).Sections.Single(s => s.Title == "Directory entry (FAT32)");
            Assert.StartsWith("Report Final.txt", Field(named, "Long name"), StringComparison.Ordinal);
            Assert.EndsWith("(to 10 ms)", Field(named, "Created"), StringComparison.Ordinal);
        }
        finally { Directory.Delete(Path.Combine(drive, "filecat-records"), recursive: true); }
    }
}
