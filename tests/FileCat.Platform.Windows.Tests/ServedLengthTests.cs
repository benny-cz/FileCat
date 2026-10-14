using FileCat.Platform.Windows.Recovery;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// A recovery source must report the bytes it can actually serve. A volume reports its partition's length, but
/// NTFS keeps a backup boot sector outside its own extent, so the last sectors are refused: the authorized USB
/// volume serves 7,795,109,888 of the 7,795,113,984 bytes the partition claims, which is exactly the size Windows
/// reports for the file system. Telling a scan those 4,096 bytes exist also lets a direct and a helper read return
/// two equally short reads there and be called equal (I110).
/// </summary>
public sealed class ServedLengthTests
{
    [Fact]
    public void A_source_that_serves_every_sector_keeps_its_length()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows device lengths.");
        string path = Path.Combine(Path.GetTempPath(), "filecat-served-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllBytes(path, new byte[8192]);
        try
        {
            using var handle = File.OpenHandle(path);
            Assert.Equal(8192, DeviceTopology.ServedLength(handle, 8192, 512));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_length_that_is_not_a_whole_number_of_sectors_reports_the_sectors_it_can_read()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows device lengths.");
        string path = Path.Combine(Path.GetTempPath(), "filecat-served-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllBytes(path, new byte[5000]);
        try
        {
            using var handle = File.OpenHandle(path);
            // Raw devices read whole sectors, so the trailing 392 bytes are not a sector a scan could read.
            Assert.Equal(4608, DeviceTopology.ServedLength(handle, 5000, 512));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_source_whose_end_cannot_be_read_never_reports_more_than_it_claims()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows device lengths.");
        string path = Path.Combine(Path.GetTempPath(), "filecat-served-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllBytes(path, new byte[2048]);
        try
        {
            using var handle = File.OpenHandle(path);
            // Claiming more than the source holds: the reported length is trimmed to what reads, never raised.
            long served = DeviceTopology.ServedLength(handle, 16384, 512);
            Assert.Equal(2048, served);
            Assert.True(served <= 16384);
        }
        finally { File.Delete(path); }
    }
}
