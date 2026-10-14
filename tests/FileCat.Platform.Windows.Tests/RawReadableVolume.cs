using FileCat.Platform.Windows.Recovery;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Picks a fixture root on an NTFS volume whose raw sectors this computer actually lets FileCat read. Some systems
/// refuse direct reads of their system volume outright — the request is answered with ERROR_NOT_SUPPORTED, for
/// everyone, not only FileCat — and a record or device test whose fixtures live there proves nothing about the
/// product. The volume holding the temporary folder is preferred, so an ordinary machine behaves as it always did.
/// Set FILECAT_RAW_FIXTURE_ROOT to choose the folder yourself.
/// </summary>
internal static class RawReadableVolume
{
    private static readonly Lazy<(string? Root, string? Refusal)> Chosen = new(Choose);

    /// <summary>Creates a new empty fixture folder, or skips the test with the exact reason it cannot.</summary>
    public static string NewFolder(string prefix)
    {
        var (root, refusal) = Chosen.Value;
        if (root is null) Assert.Skip(refusal ?? "No NTFS volume on this computer allows a raw read.");
        string dir = Path.Combine(root!, prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A volume device FileCat can read directly, or a skip explaining why there is none.</summary>
    public static string VolumeDevice()
    {
        var (root, refusal) = Chosen.Value;
        if (root is null) Assert.Skip(refusal ?? "No NTFS volume on this computer allows a raw read.");
        return DeviceTopology.VolumeDevice(Path.GetPathRoot(root!)!)
               ?? throw new InvalidOperationException("The chosen fixture volume has no volume device.");
    }

    private static (string?, string?) Choose()
    {
        if (Environment.GetEnvironmentVariable("FILECAT_RAW_FIXTURE_ROOT") is { Length: > 0 } chosen)
            return (chosen, null);
        if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess) return (null, "Raw volume reads need Windows and administrator rights.");

        string? refusal = null;
        foreach (var root in Candidates())
        {
            string? device = DeviceTopology.VolumeDevice(root);
            if (device is null) continue;
            try
            {
                using var source = DirectDeviceSource.Open(device, "drive " + root);
                var sector = new byte[512];
                if (source.Read(0, sector) != sector.Length) continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep the first explanation: it is the one for the volume the tests would have used.
                refusal ??= $"This computer does not allow a raw read of {root}: {ex.Message}";
                continue;
            }
            // Fixtures stay in the temporary folder when its own volume can be read.
            string temporary = Path.GetTempPath();
            return (string.Equals(Path.GetPathRoot(temporary), root, StringComparison.OrdinalIgnoreCase)
                ? temporary
                : Directory.CreateDirectory(Path.Combine(root, "FileCatTests")).FullName, null);
        }
        return (null, refusal ?? "No NTFS volume on this computer allows a raw read.");
    }

    private static IEnumerable<string> Candidates()
    {
        string? temporary = Path.GetPathRoot(Path.GetTempPath());
        if (temporary is not null && Readable(temporary)) yield return temporary;
        foreach (var drive in DriveInfo.GetDrives())
            if (!string.Equals(drive.Name, temporary, StringComparison.OrdinalIgnoreCase) && Readable(drive.Name))
                yield return drive.Name;
    }

    private static bool Readable(string root)
    {
        try
        {
            var drive = new DriveInfo(root);
            return drive.IsReady && drive.DriveFormat == "NTFS"
                && drive.DriveType is DriveType.Fixed or DriveType.Removable
                && drive.AvailableFreeSpace > 64L * 1024 * 1024;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
