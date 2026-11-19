using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("device")]
    [InlineData("missing mask")]
    [InlineData("short")]
    [InlineData("overflow")]
    [InlineData("raw device")]
    public void Mac_APFS_protection_requires_a_bounded_device_and_volume_uuid(string fixture)
    {
        Guid uuid = Guid.Parse("c6731724-f16e-46a8-a79a-faba8f6bb242");
        var fields = new byte[80];
        BinaryPrimitives.WriteUInt32LittleEndian(fields, fixture == "short" ? 59u : (uint)fields.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(8), fixture == "missing mask" ? 0x108000u : 0x148000u);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(24), 7);
        BinaryPrimitives.WriteInt32LittleEndian(fields.AsSpan(28), fixture == "overflow" ? int.MaxValue : 32);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(32), 13);
        Assert.True(uuid.TryWriteBytes(fields.AsSpan(36, 16), bigEndian: true, out _));
        BinaryPrimitives.WriteInt32LittleEndian(fields.AsSpan(52), 23);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(56), 5);
        (fixture == "raw device" ? "/dev/rdisk5s\0"u8 : "/dev/disk5s1\0"u8).CopyTo(fields.AsSpan(60));
        "apfs\0"u8.CopyTo(fields.AsSpan(75));

        var volume = UnixFileRecords.ParseMacApfsVolumeIdentity(fields, fixture == "device" ? 8u : 7u);
        if (fixture == "valid") Assert.Equal(new("/dev/disk5s1", uuid), volume);
        else Assert.Null(volume);
    }

    [Theory]
    [InlineData(false, 0, false, false, false)]
    [InlineData(true, 0, false, false, false)]
    [InlineData(true, 1, false, true, false)]
    [InlineData(true, 2, true, false, false)]
    [InlineData(true, 7, true, true, true)]
    public void Mac_APFS_protection_keeps_FileVault_separate_from_backing_media(bool encrypted, long flags,
        bool fileVault, bool backing, bool changing) =>
        Assert.Equal(new(fileVault, backing, changing), UnixFileRecords.ParseMacApfsProtection(encrypted, flags));

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 8)]
    [InlineData(true, -1)]
    public void Mac_APFS_protection_rejects_unknown_and_inconsistent_flags(bool encrypted, long flags) =>
        Assert.Null(UnixFileRecords.ParseMacApfsProtection(encrypted, flags));

    [Fact]
    public void Mac_full_report_distinguishes_FileVault_and_encrypted_backing_on_the_selected_volume()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
            "Requires Apple silicon and prepared APFS volumes.");
        var plain = Environment.GetEnvironmentVariable("FILECAT_TEST_MAC_PROTECTION_PLAIN");
        var protectedVolume = Environment.GetEnvironmentVariable("FILECAT_TEST_MAC_PROTECTION_FILEVAULT");
        var wrapped = Environment.GetEnvironmentVariable("FILECAT_TEST_MAC_PROTECTION_WRAPPED");
        if (plain is null || protectedVolume is null || wrapped is null) Assert.Skip("Requires plain, FileVault and encrypted-image fixtures.");

        const string title = "Location · APFS volume protection";
        var ct = TestContext.Current.CancellationToken;
        var reader = new UnixFileRecords();
        Assert.DoesNotContain(reader.ReadSummary(protectedVolume, ct).ToReport().Sections, section => section.Title == title);
        Verify(plain, "Off", "No");
        Verify(protectedVolume, "On", "No");
        Verify(wrapped, "Off", "Yes");
        if (Environment.GetEnvironmentVariable("FILECAT_TEST_MAC_PROTECTION_BOOT_DATA") is { } bootData)
            Verify(bootData, "On", "No");

        string file = Path.Join(protectedVolume, "item.txt");
        string leafAlias = Path.Join(plain, "link-to-protected");
        string parentAlias = Path.Join(plain, "alias-dir");
        File.WriteAllText(file, "filevault-report-must-not-read-this-sentinel");
        File.CreateSymbolicLink(leafAlias, file);
        Directory.CreateSymbolicLink(parentAlias, protectedVolume);
        try
        {
            Verify(file, "On", "No");
            Verify(leafAlias, "Off", "No");
            Verify(Path.Join(parentAlias, "item.txt"), "On", "No");
        }
        finally { File.Delete(leafAlias); Directory.Delete(parentAlias); File.Delete(file); }

        void Verify(string path, string fileVault, string backing)
        {
            var report = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, report.ContentKind);
            Assert.DoesNotContain("filevault-report-must-not-read-this-sentinel", report.ToText());
            var section = Assert.Single(report.Technical.Sections, section => section.Title == title);
            Assert.Contains(("FileVault", fileVault), section.Fields);
            Assert.Contains(("Backing media encryption reported", backing), section.Fields);
        }
    }
}
