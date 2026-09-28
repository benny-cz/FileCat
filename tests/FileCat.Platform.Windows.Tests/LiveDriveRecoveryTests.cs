using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using FileCat.Platform.Windows.Recovery;
using FileCat.Recovery;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Recovery on a real drive (P10's manual gate), read-only. FILECAT_RECOVERY_LIVE names the drive (G:) and
/// FILECAT_RECOVERY_LIVE_SERIAL its disk's serial number; the test refuses any drive that is not a USB disk with exactly
/// that serial. The volume is served by the helper's read protocol, as in the product. Signed programs are recovered to
/// the temporary folder on another disk and checked by their Authenticode signatures, which only an exact copy keeps.
/// </summary>
public sealed class LiveDriveRecoveryTests : IDisposable
{
    private readonly string _output = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-live-recovery", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE_KEEP") is null)
            try { Directory.Delete(_output, recursive: true); } catch (IOException) { }
    }

    /// <summary>The bus, serial number, and number of the disk that holds a drive letter.</summary>
    internal static (string Bus, string Serial, int Number) DiskOf(string drive)
    {
        string letter = drive.TrimEnd(':', '\\');
        var psi = new ProcessStartInfo("powershell", ["-NoProfile", "-Command",
            $"Get-Partition -DriveLetter {letter} | Get-Disk | ForEach-Object {{ \"$($_.BusType)|$($_.SerialNumber.Trim())|$($_.Number)\" }}"])
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        string line = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        var parts = line.Split('|');
        return parts.Length == 3 ? (parts[0], parts[1], int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)) : ("", "", -1);
    }

    /// <summary>The drive under test, only when it is the USB disk the environment names; otherwise the test is skipped.</summary>
    internal static string GuardedDrive()
    {
        string? drive = Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE");
        string? serial = Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE_SERIAL");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(drive) || string.IsNullOrEmpty(serial))
            Assert.Skip("Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number).");
        var (bus, actualSerial, number) = DiskOf(drive);
        Assert.True(bus == "USB" && actualSerial == serial,
            $"Refusing {drive}: it is on disk {number} ({bus}, serial {actualSerial}), not the USB disk with serial {serial}.");
        Assert.NotEqual(Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'), drive.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase);
        return drive.TrimEnd('\\');
    }

    [Fact]
    public async Task A_usb_drive_is_scanned_through_the_helper_protocol_and_signed_files_recover_exactly()
    {
        string drive = GuardedDrive();
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        string device = DeviceTopology.VolumeDevice(drive + "\\") ?? throw new InvalidOperationException("No volume device for " + drive);
        // The recovered files go to another disk than the one read (plan §17.2).
        Assert.False(DeviceTopology.SharesDisk(device, _output) ?? true);

        string pipeName = "FileCat-live-" + Guid.NewGuid().ToString("N");
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
        using (var source = new PipeDeviceSource(client, drive))
        {
            var clock = Stopwatch.StartNew();
            var volumes = RecoveryScanner.Scan(source, ct);
            var scan = clock.Elapsed;
            var volume = Assert.Single(volumes, v => v.FileSystem != "Unknown");
            var items = All(volume.Root).Where(i => i.IsDeleted && !i.IsDirectory).ToList();
            log?.WriteLine($"{drive} ({volume.FileSystem}, {source.Length / (1024 * 1024)} MiB, sector {source.SectorSize}): scanned in {scan.TotalSeconds:F1} s; " +
                           $"{items.Count:N0} deleted files ({items.Sum(i => i.Size) / (1024 * 1024)} MiB): " +
                           string.Join(", ", items.GroupBy(i => i.State).Select(g => $"{g.Count():N0} {g.Key}")));
            foreach (var top in volume.Root.Children.Take(20)) log?.WriteLine($"  {(top.IsDirectory ? "[" + top.Name + "]" : top.Name)} {(top.IsDeleted ? top.State.ToString() : "")}");
            foreach (var folder in All(volume.Root).Where(i => i.IsDirectory && i.IsDeleted))
                log?.WriteLine($"  folder {PathOf(folder)}: {folder.Children.Count} items. {folder.Evidence.LastOrDefault()}");
            foreach (var uncertain in items.Where(i => i.State != RecoveryState.Recoverable).Take(15))
                log?.WriteLine($"  {PathOf(uncertain)} ({uncertain.State}): {string.Join(" ", uncertain.Evidence)}");
            Assert.NotEmpty(items);

            // Every recoverable signed program (and a sample of the rest) is copied off the drive.
            var signed = items.Where(i => i.State == RecoveryState.Recoverable && Path.GetExtension(i.Name).ToLowerInvariant() is ".exe" or ".dll" or ".efi" or ".sys" or ".mui" && i.Size > 0 && i.Size < 64L * 1024 * 1024)
                .Take(400).ToList();
            var window = new WindowSource(source, volume.Offset, volume.Length, "volume");
            clock.Restart();
            long copied = 0;
            foreach (var item in signed)
            {
                using var content = new RecoveryContent(window, item);
                string target = Path.Combine(_output, $"{copied}-{item.Name}");
                using (var output = File.Create(target))
                {
                    var buffer = new byte[1024 * 1024];
                    for (long at = 0; at < content.Length;)
                    {
                        int n = content.Read(at, buffer);
                        if (n <= 0) break;
                        output.Write(buffer, 0, n);
                        at += n;
                    }
                }
                copied++;
            }
            var read = clock.Elapsed;
            long bytes = signed.Sum(i => i.Size);
            log?.WriteLine($"Recovered {signed.Count:N0} programs and libraries ({bytes / (1024 * 1024.0):F1} MiB) in {read.TotalSeconds:F1} s.");

            // Authenticode: a signature only verifies over the exact bytes that were signed.
            var results = Directory.GetFiles(_output).Select(f => (File: Path.GetFileName(f), Result: Authenticode.Verify(f))).ToList();
            var statuses = results.GroupBy(r => r.Result).ToDictionary(g => g.Key, g => g.Count());
            log?.WriteLine("Signatures of the recovered files: " + string.Join(", ", statuses.Select(s => $"{s.Key} {s.Value}")));
            foreach (var group in results.Where(r => r.Result != "valid").GroupBy(r => r.Result))
                log?.WriteLine($"  {group.Key}: {string.Join(", ", group.Take(8).Select(r => r.File))}");
            Assert.True(statuses.GetValueOrDefault("valid") > 0, "No recovered program had a valid signature.");
            Assert.Equal(0, statuses.GetValueOrDefault("altered"));
        }
        log?.WriteLine("Helper session: " + await serving);
    }

    private static IEnumerable<RecoveryItem> All(RecoveryItem node) => node.Children.SelectMany(c => c.IsDirectory ? All(c).Prepend(c) : [c]);

    private static string PathOf(RecoveryItem item) => item.Parent is { Name.Length: > 0 } parent ? PathOf(parent) + "/" + item.Name : item.Name;
}

/// <summary>A file's embedded Authenticode signature, checked by WinVerifyTrust without the network or any prompt.</summary>
internal static unsafe class Authenticode
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public int Size;
        public char* Path;
        public nint File;
        public Guid* KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public int Size;
        public nint PolicyCallbackData, SipClientData;
        public int UiChoice, RevocationChecks, UnionChoice;
        public FileInfo* File;
        public int StateAction;
        public nint StateData;
        public char* UrlReference;
        public int ProviderFlags, UiContext;
        public nint SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(nint window, Guid* action, TrustData* data);

    /// <summary>"valid", "unsigned", "altered" (the bytes differ from what was signed), or another result's code.</summary>
    public static string Verify(string path)
    {
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
        fixed (char* p = path)
        {
            var file = new FileInfo { Size = sizeof(FileInfo), Path = p };
            var data = new TrustData
            {
                Size = sizeof(TrustData),
                UiChoice = 2, // WTD_UI_NONE
                UnionChoice = 1, // WTD_CHOICE_FILE
                File = &file,
                StateAction = 1, // WTD_STATEACTION_VERIFY
                ProviderFlags = 0x1000, // WTD_CACHE_ONLY_URL_RETRIEVAL: nothing is fetched
            };
            int result = WinVerifyTrust(-1, &action, &data);
            data.StateAction = 2; // WTD_STATEACTION_CLOSE
            WinVerifyTrust(-1, &action, &data);
            return unchecked((uint)result) switch
            {
                0 => "valid",
                0x800B0100 => "unsigned", // TRUST_E_NOSIGNATURE (also files signed only in a catalog)
                0x80096010 => "altered", // TRUST_E_BAD_DIGEST: the bytes differ from what was signed
                0x800B0109 => "valid, test-signed", // CERT_E_UNTRUSTEDROOT: the bytes match; the signer is not trusted here
                var other => $"0x{other:X8}",
            };
        }
    }
}
