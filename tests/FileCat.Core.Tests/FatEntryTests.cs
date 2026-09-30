using System.Text;
using FileCat.Core.Inspect;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>D-56: an item's raw directory entry on FAT12/16/32 and exFAT volumes made by the Linux kernel's own drivers.</summary>
public sealed class FatEntryTests
{
    private static Func<long, int, byte[]> Reader(string fixture)
    {
        var bytes = File.ReadAllBytes(RecoveryFixtures.Image(fixture));
        return (offset, count) => offset >= bytes.Length ? [] : bytes.AsSpan((int)offset, (int)Math.Min(count, bytes.Length - offset)).ToArray();
    }

    private static string Field(InspectionSection section, string name) => section.Fields.Single(f => f.Name == name).Value;

    [Theory]
    [InlineData("fat12", "FAT12")]
    [InlineData("fat16", "FAT16")]
    [InlineData("fat32", "FAT32")]
    public void A_FAT_entry_reads_with_its_short_and_long_names_times_and_clusters(string fixture, string kind)
    {
        var section = FatEntries.Describe(Reader(fixture), @"\keep.txt", out string? problem);
        Assert.Null(problem);
        Assert.Equal($"Directory entry ({kind})", section!.Title);
        Assert.StartsWith("KEEP.TXT (stored as “KEEP    TXT”)", Field(section, "Short name"), StringComparison.Ordinal);
        Assert.StartsWith("keep.txt (1 long-name entry, checksum matches)", Field(section, "Long name"), StringComparison.Ordinal);
        Assert.StartsWith("1,500 bytes".Replace(",", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator, StringComparison.Ordinal), Field(section, "Size"), StringComparison.Ordinal);
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d\d \(to 10 ms\)$", Field(section, "Created"));
        Assert.EndsWith("(a date only)", Field(section, "Accessed"), StringComparison.Ordinal);
        Assert.Contains($"(FAT{kind[3..]} chain)", Field(section, "Clusters"), StringComparison.Ordinal);
        // The raw entries follow: the long-name entry, then the short one.
        Assert.Contains(section.Lines, l => l.StartsWith("0000  41 6B 00 65 00", StringComparison.Ordinal)); // 'A' ordinal, "ke"

        // A file inside a folder: the walk goes through the folder's own chain.
        var nested = FatEntries.Describe(Reader(fixture), "fill/zeros.bin", out problem);
        Assert.Null(problem);
        Assert.StartsWith("zeros.bin", Field(nested!, "Long name"), StringComparison.Ordinal);

        Assert.Null(FatEntries.Describe(Reader(fixture), @"docs\report.txt", out problem)); // deleted
        Assert.Contains("“report.txt” was not found in “docs”", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exFAT_entry_set_reads_with_its_checksum_zones_and_stream()
    {
        var section = FatEntries.Describe(Reader("exfat"), @"\keep.txt", out string? problem);
        Assert.Null(problem);
        Assert.Equal("Directory entry (exFAT)", section!.Title);
        Assert.Equal("keep.txt", Field(section, "Name"));
        Assert.EndsWith("matches", Field(section, "Entry set"), StringComparison.Ordinal);
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d\d( UTC[+−]\d\d:\d\d| \(no time zone recorded\))$", Field(section, "Modified"));
        Assert.Equal("all of it", Field(section, "Valid data"));
        Assert.StartsWith("0x0", Field(section, "Stream flags"), StringComparison.Ordinal);
        Assert.Contains(section.Lines, l => l.StartsWith("0000  85", StringComparison.Ordinal)); // the file entry
        Assert.NotNull(FatEntries.Describe(Reader("exfat"), "fill/zeros.bin", out problem));
        Assert.Null(problem);
    }

    [Fact]
    public void Windows_case_bits_make_an_8_3_name_lower_case_without_a_long_name()
    {
        var entry = new byte[32];
        Encoding.ASCII.GetBytes("README  TXT").CopyTo(entry, 0);
        Assert.Equal("README.TXT", FatEntries.ShortNameOf(entry));
        entry[12] = 0x08;
        Assert.Equal("readme.TXT", FatEntries.ShortNameOf(entry));
        entry[12] = 0x18;
        Assert.Equal("readme.txt", FatEntries.ShortNameOf(entry));
        entry[0] = 0x05; // a name that begins with 0xE5 is stored with 0x05
        Assert.StartsWith("\u00E5", FatEntries.ShortNameOf(entry), StringComparison.Ordinal);
    }
}
