using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using FileCat.Platform.Windows.Recovery;
using FileCat.Recovery;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Recovery on a real drive (P10's manual gate), read-only. FILECAT_RECOVERY_LIVE names the drive (G:) and
/// FILECAT_RECOVERY_LIVE_SERIAL its disk's serial number; the test refuses any drive that is not a USB disk with exactly
/// that serial. The volume is served by the helper's read protocol, as in the product. Signed programs are recovered to
/// the temporary folder on another disk and checked by their Authenticode signatures, which only an exact copy keeps.
/// FILECAT_RECOVERY_LIVE_IMAGE runs the same checks on an image of such a drive instead (docs/validation/P10-recovery.md).
/// </summary>
[Collection("Live USB")]
public sealed class LiveDriveRecoveryTests : IDisposable
{
    private readonly string _output = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-live-recovery", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE_KEEP") is null)
            try { Directory.Delete(_output, recursive: true); } catch (IOException) { }
    }

    /// <summary>The quick scan, and the one that also searches all free space for listings nothing points to (minutes).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_usb_drive_is_scanned_through_the_helper_protocol_and_signed_files_recover_exactly(bool searchFreeSpace)
    {
        // An image of the drive, made before it was used for something else, keeps its scenario for later runs.
        if (Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE_IMAGE") is { Length: > 0 } image)
        {
            using var file = new ImageFileSource(image);
            Check(file, Path.GetFileName(image), searchFreeSpace);
            return;
        }
        var usb = LiveUsbGuard.Capture();
        if (!Environment.IsPrivilegedProcess) Assert.Skip("Reading a drive without the installed helper needs administrator rights.");
        string drive = usb.Drive;
        var ct = TestContext.Current.CancellationToken;
        usb.Recheck();
        string device = usb.Device;
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
            Check(source, drive, searchFreeSpace);
        TestContext.Current.TestOutputHelper?.WriteLine("Helper session: " + await serving);
    }

    /// <summary>
    /// A FileCat that runs as administrator reads the drive itself: the bytes are the ones the helper serves, and the
    /// scan finds the same deleted files.
    /// </summary>
    [Fact]
    public async Task An_elevated_FileCat_reads_the_drive_itself_exactly_as_the_helper_serves_it()
    {
        var usb = LiveUsbGuard.Capture();
        string drive = usb.Drive;
        using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                Assert.Skip("Reading a drive directly needs the test to run as administrator.");
        var ct = TestContext.Current.CancellationToken;
        usb.Recheck();
        string device = usb.Device;
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
        using (var direct = DirectDeviceSource.Open(device, drive))
        using (var piped = new PipeDeviceSource(client, drive))
        {
            Assert.Equal(piped.Length, direct.Length);
            Assert.Equal(piped.SectorSize, direct.SectorSize);
            foreach (var (offset, length) in new[] { (0L, 512), (3L, 70_001), (direct.Length / 2 + 5, 9 * 1024 * 1024), (direct.Length - 5000, 5000) })
            {
                var a = new byte[length];
                var b = new byte[length];
                Assert.Equal(piped.Read(offset, b), direct.Read(offset, a));
                Assert.True(a.AsSpan().SequenceEqual(b), $"{offset}+{length}");
            }
            static int Deleted(IBlockSource source, CancellationToken ct) =>
                All(Assert.Single(RecoveryScanner.Scan(source, ct), v => v.FileSystem != "Unknown").Root).Count(i => i.IsDeleted);
            Assert.Equal(Deleted(piped, ct), Deleted(direct, ct));
        }
        await serving;
    }

    /// <summary>Scans a source, recovers every recoverable signed program to another disk, and checks the signatures.</summary>
    private void Check(IBlockSource source, string name, bool searchFreeSpace)
    {
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        var clock = Stopwatch.StartNew();
        long lastReport = 0;
        var volumes = RecoveryScanner.Scan(source, ct, new RecoveryScanOptions
        {
            SearchFreeSpace = searchFreeSpace,
            Progress = (done, total) =>
            {
                if (done - lastReport < total / 4 && done < total) return;
                lastReport = done;
                log?.WriteLine($"  free space searched: {done / (1024 * 1024)} of {total / (1024 * 1024)} MiB after {clock.Elapsed.TotalSeconds:F0} s");
            },
        });
        var scan = clock.Elapsed;
        var volume = Assert.Single(volumes, v => v.FileSystem != "Unknown");
        var items = All(volume.Root).Where(i => i.IsDeleted && !i.IsDirectory).ToList();
        log?.WriteLine($"{name} ({volume.FileSystem}, {source.Length / (1024 * 1024)} MiB): scanned in {scan.TotalSeconds:F1} s; " +
                       $"{items.Count:N0} deleted files ({items.Sum(i => i.Size) / (1024 * 1024)} MiB): " +
                       string.Join(", ", items.GroupBy(i => i.State).Select(g => $"{g.Count():N0} {g.Key}")));
        foreach (var top in volume.Root.Children.Take(20)) log?.WriteLine($"  {(top.IsDirectory ? "[" + top.Name + "]" : top.Name)} {(top.IsDeleted ? top.State.ToString() : "")}");
        foreach (var folder in All(volume.Root).Where(i => i.IsDirectory && i.IsDeleted))
            log?.WriteLine($"  folder {PathOf(folder)}: {folder.Children.Count} items. {folder.Evidence.LastOrDefault()}");
        foreach (var uncertain in items.Where(i => i.State != RecoveryState.Recoverable).Take(15))
            log?.WriteLine($"  {PathOf(uncertain)} ({uncertain.State}): {string.Join(" ", uncertain.Evidence)}");
        foreach (var large in items.Where(i => i.Size > 100L * 1024 * 1024))
            log?.WriteLine($"  large: {PathOf(large)} ({large.Size / (1024 * 1024)} MiB, {large.State}): {string.Join(" ", large.Evidence)}");
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
