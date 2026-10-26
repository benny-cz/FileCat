using System.Security.Cryptography;
using FileCat.Platform.Windows.Recovery;
using FileCat.Recovery;
using Microsoft.Win32;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Opt-in native check for a disposable, mounted NTFS VHD in the Windows validation VM.
/// The fixture is prepared separately; this test only reads G: and copies one deleted
/// file to the guest's C: validation folder. It refuses a workstation or another disk.
/// </summary>
[Collection("Live USB")]
public sealed class VmVhdRecoveryTests
{
    [Fact]
    public void A_deleted_vhd_file_is_read_off_source_with_truthful_uncertainty_when_clusters_are_discarded()
    {
        string? vhd = Environment.GetEnvironmentVariable("FILECAT_RECOVERY_VM_VHD");
        if (string.IsNullOrWhiteSpace(vhd))
            Assert.Skip("Set FILECAT_RECOVERY_VM_VHD only for the isolated Windows validation VM.");

        Assert.True(OperatingSystem.IsWindows());
        string manufacturer = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS")?
            .GetValue("SystemManufacturer") as string ?? string.Empty;
        Assert.Contains("VMware", manufacturer, StringComparison.OrdinalIgnoreCase);
        string validation = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileCatValidation"));
        vhd = Path.GetFullPath(vhd);
        Assert.StartsWith(validation.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            vhd, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("source.vhd", Path.GetFileName(vhd), ignoreCase: true);
        Assert.True(File.Exists(vhd), "The exact fixture VHD must exist.");

        var drive = new DriveInfo("G:\\");
        Assert.True(drive.IsReady);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.Equal("FCTEST", drive.VolumeLabel);
        Assert.False(File.Exists(@"G:\recover-me.bin"), "The fixture file must be deleted before scanning.");
        string expected = Environment.GetEnvironmentVariable("FILECAT_RECOVERY_VM_SHA256") ?? string.Empty;
        Assert.Matches("^[A-Fa-f0-9]{64}$", expected);
        Assert.True(int.TryParse(Environment.GetEnvironmentVariable("FILECAT_RECOVERY_VM_DISK"), out int diskNumber));

        string output = Path.Combine(validation, "VmVhdRecovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        using var source = DirectDeviceSource.Open(@"\\.\G:", "G: FCTEST");
        Assert.Equal(diskNumber, Assert.Single(source.QuerySourceDisks() ?? []));
        Assert.False(source.SharesDestinationDisk(output) ?? true,
            "Recovered bytes must be written to another physical disk.");
        var volume = Assert.Single(RecoveryScanner.Scan(source, TestContext.Current.CancellationToken),
            item => item.FileSystem == "NTFS");
        static IEnumerable<RecoveryItem> All(RecoveryItem folder) =>
            folder.Children.SelectMany(item => item.IsDirectory ? All(item).Prepend(item) : [item]);
        var deleted = All(volume.Root).Where(item => item.IsDeleted && !item.IsDirectory).ToList();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"NTFS volume {volume.Label}: {deleted.Count} deleted files; " +
            $"warnings: {string.Join(" | ", volume.Warnings)}");
        var fixture = Assert.Single(deleted, item => item.Name == "recover-me.bin");
        Assert.Equal(81_920, fixture.Size);
        Assert.Contains(fixture.State, new[] { RecoveryState.Recoverable, RecoveryState.Uncertain });

        // The app chooses a volume GUID rather than a drive letter. Verify that
        // its actual source path exposes the same deleted record.
        string volumePath = Assert.IsType<string>(DeviceTopology.VolumeDevice(@"G:\"));
        using (var selected = SelectedDiskInterfaceSource.Bind(volumePath,
                   DirectDeviceSource.Open(volumePath, "G: FCTEST")))
        {
            var selectedVolume = Assert.Single(RecoveryScanner.Scan(selected, TestContext.Current.CancellationToken),
                item => item.FileSystem == "NTFS");
            var selectedFiles = All(selectedVolume.Root).Where(item => item.IsDeleted && !item.IsDirectory).ToList();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Selected {volumePath}: {selectedFiles.Count} deleted files; " +
                $"root children: {string.Join(" | ", selectedVolume.Root.Children.Select(item => item.Name))}");
            Assert.Contains(selectedFiles, item => item.Name == "recover-me.bin");
        }
        var rawFirst = Assert.Single(fixture.Extents);
        Assert.Equal(fixture.Size, rawFirst.Length);
        var raw = new byte[fixture.Size];
        int rawRead = source.Read(rawFirst.Offset, raw);
        Assert.Equal(raw.Length, rawRead);
        TestContext.Current.TestOutputHelper?.WriteLine($"Raw extent: {rawFirst.State}@{rawFirst.Offset}, " +
            $"SHA-256 {Convert.ToHexString(SHA256.HashData(raw))}, " +
            $"first bytes {Convert.ToHexString(raw.AsSpan(0, 32))}");

        string target = Path.Combine(output, fixture.Name);
        using (var content = new RecoveryContent(new WindowSource(source, volume.Offset, volume.Length, "G: FCTEST"), fixture))
        using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (fixture.State == RecoveryState.Uncertain)
                Assert.Contains("valid data", content.Caveat ?? string.Empty, StringComparison.Ordinal);
            var buffer = new byte[81920];
            for (long offset = 0; offset < content.Length;)
            {
                int read = content.Read(offset, buffer);
                Assert.True(read > 0, $"The source stopped serving byte {offset:N0}.");
                file.Write(buffer, 0, read);
                offset += read;
            }
            file.Flush(flushToDisk: true);
        }
        Assert.Equal(fixture.Size, new FileInfo(target).Length);
        byte[] recovered = File.ReadAllBytes(target);
        Assert.Equal(raw, recovered);
        string actual = Convert.ToHexString(SHA256.HashData(recovered));
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Target: {target}; actual SHA-256: {actual}; first bytes: " +
            Convert.ToHexString(recovered.AsSpan(0, Math.Min(32, recovered.Length))));
        TestContext.Current.TestOutputHelper?.WriteLine(
            "Extents: " + string.Join(" | ", fixture.Extents.Select(e => $"{e.State}@{e.Offset}+{e.Length}")));
        TestContext.Current.TestOutputHelper?.WriteLine("Evidence: " + string.Join(" | ", fixture.Evidence));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            Assert.Equal(RecoveryState.Uncertain, fixture.State);
            Assert.Contains("valid data", fixture.Evidence[0], StringComparison.Ordinal);
            TestContext.Current.TestOutputHelper?.WriteLine(
                "The disposable VHD returned different raw bytes after deletion; exact recovery remains unproven on this medium.");
        }
        Assert.False(File.Exists(@"G:\recover-me.bin"), "Recovery must leave the source deleted.");
        TestContext.Current.TestOutputHelper?.WriteLine($"Off-source output: {target}; SHA-256 {actual}; exact={expected.Equals(actual, StringComparison.OrdinalIgnoreCase)}");
    }
}
