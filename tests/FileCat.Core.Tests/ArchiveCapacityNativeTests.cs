using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

public sealed class ArchiveCapacityNativeTests
{
    [Fact]
    public async Task Native_C_headers_independently_confirm_held_Unix_volume_units_layout_and_available_bytes()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Native Unix capacity observer.");
        string? executable = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_CAPACITY_OBSERVER");
        if (string.IsNullOrEmpty(executable)) Assert.Skip("Explicit independently compiled native observer required.");
        using var root = new TempDir(); string path = UnixFiles.RealPath(root.Path)!;
        using var directory = UnixArchiveDirectory.Open(path);
        var before = FileSystemCapacity.QueryHeldDirectory(directory.Handle, directory.Identity.VolumeSerial); Assert.NotNull(before);
        var start = new ProcessStartInfo(executable!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(path); using var process = Process.Start(start)!;
        string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken); Assert.True(process.ExitCode == 0, error);
        Assert.InRange(output.Length, 1, 4096); using var json = JsonDocument.Parse(output); var native = json.RootElement;
        Assert.Equal(8, native.GetProperty("pointerBytes").GetInt32()); Assert.InRange(native.GetProperty("structureBytes").GetInt32(), 88, 4096);
        Assert.Equal(OperatingSystem.IsMacOS() ? 0 : 72, native.GetProperty("unitOffset").GetInt32());
        Assert.Equal(OperatingSystem.IsMacOS() ? 24 : 32, native.GetProperty("unitsOffset").GetInt32());
        Assert.Equal(directory.Identity.VolumeSerial.ToString("X16"), native.GetProperty("device").GetString());
        long independentlyAvailable = checked((long)(native.GetProperty("unitBytes").GetUInt64() * native.GetProperty("availableUnits").GetUInt64()));
        var after = FileSystemCapacity.QueryHeldDirectory(directory.Handle, directory.Identity.VolumeSerial); Assert.NotNull(after);
        Assert.Equal("unix-device:" + directory.Identity.VolumeSerial.ToString("X16"), after.VolumeIdentity);
        // Concurrent filesystem allocations prevent exact equality across separate queries. The tolerance detects
        // layout/unit mistakes without claiming a reserved allocation or immutable capacity snapshot.
        Assert.InRange(independentlyAvailable, Math.Max(0, Math.Min(before.AvailableBytes, after.AvailableBytes) - (64L << 20)), Math.Max(before.AvailableBytes, after.AvailableBytes) + (64L << 20));
        TestContext.Current.TestOutputHelper?.WriteLine(output.Trim());
        Assert.Throws<ArchiveWriteException>(() => ((IArchiveDestinationSpace)directory).ReviewSpace("wrong-parent", new(0, 1024, 0, 0), TestContext.Current.CancellationToken));
    }
}
