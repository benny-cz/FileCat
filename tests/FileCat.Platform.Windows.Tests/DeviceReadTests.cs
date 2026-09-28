using System.IO.Compression;
using System.IO.Pipes;
using System.Security.Principal;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Recovery;
using FileCat.Recovery;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Reading drives for recovery (P10, ADR-08) without elevation: the helper's read protocol runs over a real named pipe
/// with a file standing in for the device, and the plan and disk checks run as they do for FileCat.
/// </summary>
public sealed class DeviceReadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-device-tests", Guid.NewGuid().ToString("N")[..10]);

    public DeviceReadTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>A pipe session serving <paramref name="file"/> as a device with the given sector size, and FileCat's end of it.</summary>
    private static (PipeDeviceSource Client, Task<string> Server) Session(string file, int sector)
    {
        string name = "FileCat-test-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serving = Task.Run(() =>
        {
            using (server)
            using (var handle = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                server.WaitForConnection();
                return RawReadProtocol.Serve(server, handle, new FileInfo(file).Length, sector);
            }
        });
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        client.Connect(5000);
        return (new PipeDeviceSource(client, "test drive"), serving);
    }

    [Fact]
    public async Task Reads_come_back_exact_at_any_offset_and_size_and_stop_at_the_end()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The read helper is part of FileCat for Windows.");
        var bytes = new byte[5 * 1024 * 1024 + 777];
        new Random(42).NextBytes(bytes);
        string file = Path.Combine(_dir, "device.bin");
        File.WriteAllBytes(file, bytes);
        var (source, server) = Session(file, 4096);
        using (source)
        {
            Assert.Equal(bytes.Length, source.Length);
            Assert.Equal(4096, source.SectorSize);
            foreach (var (offset, length) in new[] { (0L, 1), (1L, 511), (4095L, 2), (100_000L, 70_001), (0L, bytes.Length), (bytes.Length - 10L, 100) })
            {
                var buffer = new byte[length];
                int n = source.Read(offset, buffer);
                int expected = (int)Math.Min(length, bytes.Length - offset);
                Assert.Equal(expected, n);
                Assert.True(bytes.AsSpan((int)offset, expected).SequenceEqual(buffer.AsSpan(0, n)), $"{offset}+{length}");
            }
            Assert.Equal(0, source.Read(bytes.Length + 5L, new byte[10]));
        }
        Assert.Equal("FileCat closed the session.", await server);
    }

    [Fact]
    public void A_disk_image_scans_through_the_helper_session_like_the_file_itself()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The read helper is part of FileCat for Windows.");
        string image = Path.Combine(_dir, "fat16.img");
        using (var input = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "fat16.img.gz")), CompressionMode.Decompress))
        using (var output = File.Create(image))
            input.CopyTo(output);
        var (source, _) = Session(image, 512);
        using (source)
        {
            var volume = Assert.Single(RecoveryScanner.Scan(source, TestContext.Current.CancellationToken));
            var report = volume.Root.Children.Single(c => c.Name == "docs").Children.Single(c => c.Name == "report.txt");
            Assert.Equal(RecoveryState.Recoverable, report.State);
            using var content = new RecoveryContent(new WindowSource(source, volume.Offset, volume.Length, "volume"), report);
            var data = new byte[report.Size];
            Assert.Equal(data.Length, content.Read(0, data));
            Assert.StartsWith("report.txt:00000000\n", System.Text.Encoding.UTF8.GetString(data), StringComparison.Ordinal);
        }
    }

    /// <summary>TV-09: what the helper's pipe costs a scan and a whole-file read, compared with reading the image directly.</summary>
    [Fact]
    public void Reading_through_the_helper_costs_a_small_factor()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The read helper is part of FileCat for Windows.");
        string image = Path.Combine(_dir, "fat16.img");
        using (var input = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "fat16.img.gz")), CompressionMode.Decompress))
        using (var output = File.Create(image))
            input.CopyTo(output);
        TimeSpan Measure(IBlockSource source)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int round = 0; round < 5; round++)
            {
                var volume = RecoveryScanner.Scan(source, TestContext.Current.CancellationToken)[0];
                var photo = volume.Root.Children.Single(c => c.Name == "photos").Children.Single(c => c.Name == "a.jpg");
                using var content = new RecoveryContent(new WindowSource(source, volume.Offset, volume.Length, "volume"), photo);
                var buffer = new byte[photo.Size];
                content.Read(0, buffer);
            }
            return clock.Elapsed;
        }
        // Each path is warmed up (the first scan also compiles the code), then the best of three runs counts: a shared CI
        // machine can stall any single run for seconds.
        TimeSpan Best(IBlockSource source)
        {
            Measure(source);
            return Enumerable.Range(0, 3).Select(_ => Measure(source)).Min();
        }
        TimeSpan direct;
        using (var file = new ImageFileSource(image)) direct = Best(file);
        var (piped, _) = Session(image, 512);
        TimeSpan through;
        using (piped) through = Best(piped);
        TestContext.Current.TestOutputHelper?.WriteLine($"TV-09: five scans and reads: direct {direct.TotalMilliseconds:F0} ms, through the helper's pipe {through.TotalMilliseconds:F0} ms.");
        Assert.True(through < direct * 20 + TimeSpan.FromSeconds(2), $"The pipe costs too much: {through} against {direct}.");
    }

    [Fact]
    public void A_request_the_protocol_does_not_know_ends_the_session()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The read helper is part of FileCat for Windows.");
        string file = Path.Combine(_dir, "small.bin");
        File.WriteAllBytes(file, new byte[4096]);
        using var pipe = new MemoryPipe([(byte)9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        using var handle = File.OpenHandle(file);
        Assert.Contains("does not know", RawReadProtocol.Serve(pipe, handle, 4096, 512), StringComparison.Ordinal);
        // Negative offsets and oversized reads are refused with an error code, never served.
        var bad = new byte[26];
        bad[0] = RawReadProtocol.Read;
        BitConverter.TryWriteBytes(bad.AsSpan(1), -1L);
        bad[13] = RawReadProtocol.Read;
        BitConverter.TryWriteBytes(bad.AsSpan(22), RawReadProtocol.MaxRead + 1);
        using var refused = new MemoryPipe(bad);
        RawReadProtocol.Serve(refused, handle, 4096, 512);
        Assert.Equal(87, BitConverter.ToInt32(refused.Written, 0));
        Assert.Equal(0, BitConverter.ToInt32(refused.Written, 4));
        Assert.Equal(87, BitConverter.ToInt32(refused.Written, 8));
    }

    /// <summary>A stream that plays given request bytes and records what the server writes.</summary>
    private sealed class MemoryPipe(byte[] requests) : Stream
    {
        private readonly MemoryStream _in = new(requests), _out = new();
        public byte[] Written => _out.ToArray();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _in.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _out.Write(buffer, offset, count);
    }

    private static ElevationPlan Plan(params ElevatedStep[] steps) => new()
    {
        Nonce = ElevationPlanCodec.NewNonce(),
        CreatedUtc = DateTime.UtcNow,
        UserSid = WindowsIdentity.GetCurrent().User!.Value,
        UserName = "tester",
        RequesterProcessId = Environment.ProcessId,
        Title = "Read drive E: to find deleted files",
        Steps = steps,
    };

    [Fact]
    public void A_read_session_plan_names_one_device_and_nothing_else()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The read helper is part of FileCat for Windows.");
        const string volume = @"\\?\Volume{12345678-1234-1234-1234-123456789abc}";
        string session = BrokeredDeviceSource.PipeName(ElevationPlanCodec.NewNonce());
        var good = Plan(new ElevatedStep(ElevatedVerb.ReadDevice) { Path = volume, Name = session });
        Assert.Empty(ElevationPlanCodec.Validate(ElevationPlanCodec.Parse(ElevationPlanCodec.Serialize(good)), DateTime.UtcNow));
        Assert.Empty(ElevationPlanCodec.Validate(Plan(new ElevatedStep(ElevatedVerb.ReadDevice) { Path = @"\\.\PhysicalDrive1", Name = session }), DateTime.UtcNow));
        Assert.Contains("Nothing on it", ElevationPlanCodec.Describe(good.Steps[0]).Replace("nothing on it", "Nothing on it"), StringComparison.Ordinal);

        void Refused(ElevationPlan plan) => Assert.NotEmpty(ElevationPlanCodec.Validate(plan, DateTime.UtcNow));
        Refused(Plan(new ElevatedStep(ElevatedVerb.ReadDevice) { Path = volume + @"\Windows", Name = session }));
        Refused(Plan(new ElevatedStep(ElevatedVerb.ReadDevice) { Path = @"C:\Windows\System32\config\SAM", Name = session }));
        Refused(Plan(new ElevatedStep(ElevatedVerb.ReadDevice) { Path = @"\\.\C:", Name = session }));
        Refused(Plan(new ElevatedStep(ElevatedVerb.ReadDevice) { Path = volume, Name = @"..\pipe\other" }));
        Refused(Plan(new ElevatedStep(ElevatedVerb.ReadDevice) { Path = volume, Name = session, Destination = volume + @"\x" }));
        Refused(Plan(new ElevatedStep(ElevatedVerb.ReadDevice) { Path = volume, Name = session },
            new ElevatedStep(ElevatedVerb.DeleteTree) { Path = volume + @"\a" }));
    }

    [Fact]
    public void Destinations_are_judged_by_the_physical_disks_they_share()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Disk topology is read from Windows.");
        string root = Path.GetPathRoot(_dir)!;
        string? device = DeviceTopology.VolumeDevice(root);
        Assert.NotNull(device);
        Assert.StartsWith(@"\\?\Volume{", device, StringComparison.OrdinalIgnoreCase);
        var disks = DeviceTopology.DisksOf(device);
        Assert.NotNull(disks);
        Assert.NotEmpty(disks);
        Assert.Equal(disks, DeviceTopology.DisksOf(_dir));
        Assert.True(DeviceTopology.SharesDisk(device, _dir));
        Assert.False(DeviceTopology.SharesDisk(device, @"\\server\share\recovered"));
        Assert.Equal([3], DeviceTopology.DisksOf(@"\\.\PhysicalDrive3"));
    }
}
