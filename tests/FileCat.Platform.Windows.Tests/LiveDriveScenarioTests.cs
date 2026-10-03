using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using FileCat.Platform.Windows.Recovery;
using FileCat.Recovery;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Recovery with known ground truth on a real drive, destructively: FILECAT_RECOVERY_LIVE_DESTRUCTIVE=1 on top of the
/// USB-and-serial guard of <see cref="LiveDriveRecoveryTests"/>. The drive is formatted, filled with generated files, and
/// Windows deletes most of them; FileCat then scans the drive through the helper's read protocol, and every byte it calls
/// recovered is compared with what was written. One run per file system Windows formats.
/// </summary>
[Collection("Live USB")]
public sealed class LiveDriveScenarioTests
{
    private static LiveUsbGuard DestructiveDrive()
    {
        var usb = LiveUsbGuard.Capture();
        if (Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE_DESTRUCTIVE") != "1")
            Assert.Skip("Formats the drive: set FILECAT_RECOVERY_LIVE_DESTRUCTIVE=1 as well.");
        if (!Environment.IsPrivilegedProcess) Assert.Skip("Formatting the disposable USB fixture requires administrator rights.");
        return usb;
    }

    private static string PowerShell(string command)
    {
        var psi = new ProcessStartInfo("powershell", ["-NoProfile", "-NonInteractive", "-Command", command])
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && stderr.Result.Trim().Length == 0, $"{command} failed: {stderr.Result}");
        return output;
    }

    /// <summary>Deterministic content for a name and size.</summary>
    private static byte[] Content(string name, int size)
    {
        var bytes = new byte[size];
        ulong x = (ulong)name.GetHashCode(StringComparison.Ordinal) * 0x9E3779B97F4A7C15UL | 1;
        for (int i = 0; i + 8 <= size; i += 8)
        {
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            BitConverter.TryWriteBytes(bytes.AsSpan(i), x);
        }
        return bytes;
    }

    private sealed record Written(string Path, byte[] Content, string Kind);
    private sealed record MissingBytes(long Offset, long Length);
    private sealed record Observed(string Path, string Kind, bool Found, string? State, long Bytes, long ReadBytes,
        string? SHA256, MissingBytes[] MissingRanges);

    [Theory]
    [InlineData("FAT32")]
    [InlineData("exFAT")]
    [InlineData("NTFS")]
    public async Task Files_Windows_deleted_come_back_as_written(string fileSystem)
    {
        var usb = DestructiveDrive();
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        // Recheck before formatting; address the pinned volume GUID throughout, including every file mutation.
        usb.Recheck();
        PowerShell($"Format-Volume -Path '{usb.Root}' -FileSystem {fileSystem} -NewFileSystemLabel FCTEST -Force -Confirm:$false | Out-Null");
        usb.Recheck();
        string root = usb.Root;
        var written = new List<Written>();
        void Write(string path, int size, string kind)
        {
            var content = Content(path, size);
            File.WriteAllBytes(Path.Combine(root, path), content);
            written.Add(new Written(path, content, kind));
        }

        // Kept: pushes what follows past cluster 65,535 (FAT32 with 4 KiB clusters).
        Write("filler.bin", 300 * 1024 * 1024, "kept");
        // A folder with enough files that its listing takes several clusters, among their data.
        Directory.CreateDirectory(Path.Combine(root, "set"));
        for (int i = 0; i < 300; i++) Write($"set\\file-{i:D3} with a long name.bin", 1024 + i * 211 % 65536, "deleted");
        Directory.CreateDirectory(Path.Combine(root, "set\\inner"));
        for (int i = 0; i < 20; i++) Write($"set\\inner\\note-{i:D2}.txt", 3000 + i * 97, "deleted");
        foreach (int mib in new[] { 5, 12, 40 }) Write($"large-{mib}.bin", mib * 1024 * 1024, "deleted");
        // A file stored in pieces: delta.bin fills the gap bravo.bin left, then goes on after charlie.bin. (Names that
        // differ after their first letter: a deleted FAT short name loses it.)
        Write("alpha.bin", 1024 * 1024, "deleted");
        Write("bravo.bin", 1024 * 1024, "gone");
        Write("charlie.bin", 1024 * 1024, "deleted");
        usb.Recheck();
        File.Delete(Path.Combine(root, "bravo.bin"));
        PowerShell($"Write-VolumeCache -Path '{usb.Root}'");
        usb.Recheck();
        Write("delta.bin", 3 * 1024 * 1024, "fragmented");
        // Space reused: old.bin's clusters go to new.bin.
        Write("old.bin", 2 * 1024 * 1024, "overwritten");
        usb.Recheck();
        File.Delete(Path.Combine(root, "old.bin"));
        PowerShell($"Write-VolumeCache -Path '{usb.Root}'");
        usb.Recheck();
        Write("new.bin", 2 * 1024 * 1024, "kept");

        usb.Recheck();
        string groundTruth = Path.Combine(usb.Evidence, $"{fileSystem}-written-{Guid.NewGuid():N}.json");
        File.WriteAllText(groundTruth, System.Text.Json.JsonSerializer.Serialize(written.Select(w => new
        {
            w.Path, Bytes = w.Content.Length, w.Kind, SHA256 = Convert.ToHexString(SHA256.HashData(w.Content)).ToLowerInvariant(),
        })));
        log?.WriteLine("Written fixture manifest: " + groundTruth);
        Directory.Delete(Path.Combine(root, "set"), recursive: true);
        foreach (var w in written.Where(w => w.Kind is "deleted" or "fragmented" && !w.Path.StartsWith("set\\", StringComparison.Ordinal)))
            File.Delete(Path.Combine(root, w.Path));
        PowerShell($"Write-VolumeCache -Path '{usb.Root}'");

        usb.Recheck();
        string device = usb.Device;
        log?.WriteLine($"{DateTime.UtcNow:O} fixture mutations complete; beginning read-only recovery");
        string pipeName = "FileCat-scenario-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serving = Task.Run(() =>
        {
            using (server)
            using (var handle = File.OpenHandle(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                server.WaitForConnection();
                return RawReadProtocol.Serve(server, handle, DeviceTopology.Length(handle), DeviceTopology.SectorSize(handle));
            }
        }, ct);
        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await client.ConnectAsync(5000, ct);
        using (var source = new PipeDeviceSource(client, usb.Drive))
        {
            var clock = Stopwatch.StartNew();
            var volume = Assert.Single(RecoveryScanner.Scan(source, ct), v => v.FileSystem != "Unknown");
            log?.WriteLine($"{fileSystem}: scanned in {clock.Elapsed.TotalSeconds:F1} s ({volume.FileSystem}, {volume.ClusterSize} bytes per cluster).");
            if (volume.OpenListings > 0)
            {
                clock.Restart();
                volume = Assert.Single(RecoveryScanner.Scan(source, ct, new RecoveryScanOptions { SearchFreeSpace = true }), v => v.FileSystem != "Unknown");
                log?.WriteLine($"  {volume.OpenListings} listing(s) left open: free space searched in {clock.Elapsed.TotalSeconds:F0} s.");
            }
            var window = new WindowSource(source, volume.Offset, volume.Length, "volume");
            var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var observations = new List<Observed>();
            int wrongButClaimed = 0;
            foreach (var w in written.Where(w => w.Kind is not "kept"))
            {
                var item = Find(volume.Root, w.Path.Split('\\'));
                string outcome;
                if (item is null)
                {
                    outcome = "not found";
                    observations.Add(new(w.Path, w.Kind, false, null, 0, 0, null, []));
                }
                else
                {
                    using var content = new RecoveryContent(window, item);
                    var data = new byte[content.Length];
                    long readBytes = 0;
                    while (readBytes < data.Length)
                    {
                        int n = content.Read(readBytes, data.AsSpan((int)readBytes));
                        if (n <= 0) break;
                        readBytes += n;
                    }
                    var lost = content.MissingRanges;
                    observations.Add(new(w.Path, w.Kind, true, item.State.ToString(), data.LongLength, readBytes,
                        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                        lost.Select(m => new MissingBytes(m.Offset, m.Length)).ToArray()));
                    bool exact = data.AsSpan().SequenceEqual(w.Content);
                    bool keptBytesRight = RecoveryFixtureOracle.KnownBytesMatch(data, w.Content, lost);
                    outcome = $"{item.State}{(exact ? ", exact" : keptBytesRight ? ", the rest exact" : ", differs")}";
                    if (item.State is RecoveryState.Recoverable or RecoveryState.Partial && readBytes != data.LongLength ||
                        !RecoveryFixtureOracle.ClaimIsTruthful(item.State, data, w.Content, lost))
                    {
                        wrongButClaimed++;
                        log?.WriteLine($"  WRONG: {w.Path} ({item.State}): {string.Join(" ", item.Evidence)}");
                    }
                }
                string key = $"{w.Kind}: {outcome}";
                tally[key] = tally.GetValueOrDefault(key) + 1;
            }
            usb.Recheck();
            string recoveredManifest = Path.Combine(usb.Evidence, $"{fileSystem}-observed-{Guid.NewGuid():N}.json");
            File.WriteAllText(recoveredManifest, System.Text.Json.JsonSerializer.Serialize(observations));
            log?.WriteLine("Recovered observations: " + recoveredManifest);
            foreach (var (key, count) in tally) log?.WriteLine($"  {count,4} × {key}");
            // The truthfulness rule on real hardware: nothing FileCat calls recoverable is anything but the file's own bytes.
            Assert.Equal(0, wrongButClaimed);
            int deleted = written.Count(w => w.Kind == "deleted");
            int exactDeleted = tally.Where(t => t.Key.StartsWith("deleted: Recoverable, exact", StringComparison.Ordinal)).Sum(t => t.Value);
            Assert.True(exactDeleted >= deleted * 9 / 10, $"Only {exactDeleted} of {deleted} deleted files came back exactly.");
        }
        log?.WriteLine("Helper session: " + await serving);
    }

    /// <summary>A deleted item by its path; a FAT short name may show its lost first letter as "_".</summary>
    private static RecoveryItem? Find(RecoveryItem folder, string[] parts)
    {
        var node = folder;
        foreach (var part in parts)
        {
            node = node.Children.FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase))
                   ?? node.Children.FirstOrDefault(c => c.NameUncertain && string.Equals(c.Name[1..], part[1..], StringComparison.OrdinalIgnoreCase));
            if (node is null) return null;
        }
        return node;
    }
}
