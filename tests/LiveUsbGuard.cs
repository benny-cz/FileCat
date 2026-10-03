using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Platform.Windows.Recovery;
using Xunit;

namespace FileCat.Tests;

[CollectionDefinition("Live USB", DisableParallelization = true)]
public sealed class LiveUsbCollection;

// Shared by the opt-in physical recovery tests. No source mutation is allowed until the identity and every
// protected destination have been checked. Recheck before each mutation phase; use the pinned volume GUID.
internal sealed class LiveUsbGuard : IDisposable
{
    internal sealed record Identity(string Drive, string Serial, long DiskBytes, int DiskNumber, string Bus,
        string Instance, string DiskPath, string Volume, int PartitionNumber, long Offset, long PartitionBytes,
        bool Boot, bool System);

    private readonly Identity _identity;
    private readonly string _evidence;
    private FileStream? _lease;
    private static string LeaseDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FileCat", "Validation", "LiveUsbLocks");
    internal string Drive => _identity.Drive;
    internal string Root => _identity.Volume;
    internal string Device => Root.TrimEnd('\\');
    internal string Evidence => _evidence;

    private LiveUsbGuard(Identity identity, string evidence) { _identity = identity; _evidence = evidence; }

    internal static LiveUsbGuard Capture()
    {
        string? drive = Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE");
        string? serial = Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE_SERIAL");
        string? bytes = Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE_BYTES");
        string? evidence = Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE_EVIDENCE");
        if (!OperatingSystem.IsWindows() || drive is null || serial is null || bytes is null || evidence is null)
            Assert.Skip("Set FILECAT_RECOVERY_LIVE, FILECAT_RECOVERY_LIVE_SERIAL, FILECAT_RECOVERY_LIVE_BYTES and FILECAT_RECOVERY_LIVE_EVIDENCE (an owned evidence folder on another disk).");
        string canonical = CanonicalDrive(drive);
        Assert.True(long.TryParse(bytes, NumberStyles.None, CultureInfo.InvariantCulture, out long capacity) && capacity > 0,
            "The expected physical disk capacity must be a positive byte count.");
        Assert.True(Path.IsPathFullyQualified(evidence) && File.Exists(Path.Combine(evidence, ".filecat-owned")),
            "The evidence folder must be absolute, marked .filecat-owned and outside the source disk.");
        var identity = ReadIdentity(canonical);
        Validate(identity, canonical, serial, capacity);
        var guard = new LiveUsbGuard(identity, evidence);
        guard.CheckProtectedFolders();
        // One non-waiting, cross-process lease per physical serial, independent of the drive letter or run folder.
        // File handles release on process death and can be disposed after an async continuation changes threads.
        guard._lease = AcquireLease(LeaseDirectory, serial);
        try { guard.Recheck(); }
        catch { guard.Dispose(); throw; }
        TestContext.Current.TestOutputHelper?.WriteLine("Pinned USB identity: " + JsonSerializer.Serialize(identity));
        return guard;
    }

    internal static string CanonicalDrive(string drive)
    {
        Assert.True(drive.Length is 2 or 3 && char.IsAsciiLetter(drive[0]) && drive[1] == ':' &&
            (drive.Length == 2 || drive[2] == '\\'), "Only one literal drive letter such as G: is allowed.");
        return char.ToUpperInvariant(drive[0]) + ":";
    }

    internal static void Validate(Identity identity, string drive, string serial, long capacity)
    {
        Assert.Equal(drive, identity.Drive);
        Assert.True(!string.IsNullOrWhiteSpace(serial) && identity.Serial == serial && identity.Bus == "USB",
            "The source must be the USB disk with the exact authorized serial.");
        Assert.Equal(capacity, identity.DiskBytes);
        Assert.InRange(identity.DiskBytes, 1, 64L * 1024 * 1024 * 1024);
        Assert.True(!identity.Boot && !identity.System, "Refusing a system or boot disk/partition.");
        Assert.True(identity.DiskNumber >= 0 && identity.PartitionNumber > 0 && identity.Offset > 0 &&
            identity.PartitionBytes > 0 && identity.PartitionBytes <= identity.DiskBytes - identity.Offset,
            "Invalid or ambiguous source partition bounds.");
        Assert.False(string.IsNullOrWhiteSpace(identity.Instance));
        Assert.False(string.IsNullOrWhiteSpace(identity.DiskPath));
        const string prefix = @"\\?\Volume{";
        Assert.True(identity.Volume.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            identity.Volume.EndsWith("}\\", StringComparison.Ordinal) &&
            Guid.TryParseExact(identity.Volume[prefix.Length..^2], "D", out _), "A stable volume GUID is required.");
    }

    internal void Recheck()
    {
        ObjectDisposedException.ThrowIf(_lease is null, this);
        var current = ReadIdentity(Drive);
        Validate(current, Drive, _identity.Serial, _identity.DiskBytes);
        Assert.Equal(_identity, current); // changed disk/instance/volume/bounds: stop before the next phase
        CheckProtectedFolders();
    }

    private void CheckProtectedFolders()
    {
        var disks = DeviceTopology.DisksOf(Device);
        Assert.NotNull(disks);
        Assert.Equal(_identity.DiskNumber, Assert.Single(disks));
        string[] folders = [Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppContext.BaseDirectory, Environment.CurrentDirectory, Path.GetTempPath(), _evidence, LeaseDirectory];
        foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            Assert.True(!string.IsNullOrEmpty(folder) && DeviceTopology.SharesDisk(Device, folder) == false,
                "Refusing a source sharing a protected folder's backing disk, or unknown topology: " + folder);
    }

    internal static string LeasePath(string directory, string serial) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serial))) + ".lock");

    internal static FileStream AcquireLease(string directory, string serial)
    {
        Directory.CreateDirectory(directory);
        // Keep the empty file after releasing its handle. Deleting it could let a contender open a new inode.
        return new FileStream(LeasePath(directory, serial), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public void Dispose()
    {
        _lease?.Dispose();
        _lease = null;
    }

    private static Identity ReadIdentity(string drive)
    {
        // CanonicalDrive rejects all PowerShell syntax before the sole interpolated drive-letter character.
        string command = """
            $ErrorActionPreference='Stop'
            $ProgressPreference='SilentlyContinue'
            $partitions=@(Get-Partition -DriveLetter LETTER)
            if($partitions.Count -ne 1){throw 'Ambiguous drive partition.'}
            $p=$partitions[0]
            $disks=@($p | Get-Disk)
            $volumes=@(Get-Volume -DriveLetter LETTER)
            if($disks.Count -ne 1 -or $volumes.Count -ne 1){throw 'Ambiguous drive disk or volume.'}
            $d=$disks[0]; $v=$volumes[0]
            $allPartitions=@(Get-Partition -DiskNumber $d.Number)
            $instances=@(Get-CimInstance Win32_DiskDrive | Where-Object Index -eq $d.Number)
            if($instances.Count -ne 1 -or !$instances[0].PNPDeviceID){throw 'No unique disk instance.'}
            [pscustomobject]@{Drive='LETTER:';Serial=$d.SerialNumber.Trim();DiskBytes=[long]$d.Size;DiskNumber=[int]$d.Number;
                Bus=$d.BusType.ToString();Instance=$instances[0].PNPDeviceID;DiskPath=$d.Path;Volume=$v.UniqueId;
                PartitionNumber=[int]$p.PartitionNumber;Offset=[long]$p.Offset;PartitionBytes=[long]$p.Size;
                Boot=[bool]($d.IsBoot -or @($allPartitions | Where-Object IsBoot).Count);
                System=[bool]($d.IsSystem -or @($allPartitions | Where-Object IsSystem).Count)} | ConvertTo-Json -Compress
            """.Replace("LETTER", CanonicalDrive(drive)[..1], StringComparison.Ordinal);
        var start = new ProcessStartInfo("powershell.exe")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(); Assert.Fail("USB identity query timed out."); }
        Assert.True(process.ExitCode == 0 && string.IsNullOrWhiteSpace(stderr.Result), "USB identity query failed: " + stderr.Result);
        Assert.InRange(stdout.Result.Length, 1, 16 * 1024);
        return JsonSerializer.Deserialize<Identity>(stdout.Result) ?? throw new InvalidOperationException("No USB identity.");
    }
}
