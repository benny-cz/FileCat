using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Recovery;
using FileCat.Recovery;

namespace FileCat.Platform.Windows.Tests;

/// <summary>Actual recovery jobs and pipe readers, controlled helper identities, owned file images; no device reader opens.</summary>
public sealed class HeldDeviceAdmissionTests
{
    [Theory]
    [InlineData("same")]
    [InlineData("unknown")]
    [InlineData("changed")]
    [InlineData("became-unknown")]
    [InlineData("closed")]
    [InlineData("separate")]
    public async Task Opened_reader_identity_takes_precedence_over_the_selected_path(string route)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows helper and destination metadata require Windows.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-held-device", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var recovery = new RecoveryProvider();
        try
        {
            byte[] image;
            using (var input = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "fat16.img.gz")), CompressionMode.Decompress))
            using (var output = new MemoryStream()) { input.CopyTo(output); image = output.ToArray(); }
            string imagePath = Path.Combine(root, "source.img");
            File.WriteAllBytes(imagePath, image);
            byte[] expected;
            using (var original = new ImageFileSource(imagePath))
            {
                var volume = Assert.Single(RecoveryScanner.Scan(original, TestContext.Current.CancellationToken));
                var report = volume.Root.Children.Single(x => x.Name == "docs").Children.Single(x => x.Name == "report.txt");
                using var content = new RecoveryContent(new WindowSource(original, volume.Offset, volume.Length, "owned file"), report);
                expected = new byte[report.Size];
                Assert.Equal(expected.Length, content.Read(0, expected));
            }
            string destination = Path.Combine(root, "destination");
            Directory.CreateDirectory(destination);
            byte[] marker = "preserved destination bytes"u8.ToArray();
            File.WriteAllBytes(Path.Combine(destination, "keep.bin"), marker);
            var target = DeviceTopology.DisksOf(destination);
            Assert.NotNull(target);
            Assert.NotEmpty(target);
            // These are controlled helper answers. They do not assert a real device changed or a fabricated disk exists.
            int[]? heldDisks = route == "same" ? target.ToArray() : route == "unknown" ? null : [int.MaxValue];
            var pipe = new HeldDeviceScriptPipe(image, () => heldDisks);
            var held = new PipeDeviceSource(pipe, "owned helper fixture");
            int pathChecks = 0;
            recovery.OpenDevice = (_, _, _) => held;
            recovery.SharesDisk = (_, _) => { pathChecks++; return false; };
            var location = recovery.ForDevice(@"\\.\PhysicalDrive2147483646", "controlled held identity", 1);
            var sink = new Sink();
            await recovery.EnumerateAsync(location.WithPath("docs"), sink, TestContext.Current.CancellationToken);
            var entry = Assert.Single(sink.Entries, x => x.Name == "report.txt");
            if (route is "changed" or "became-unknown")
            {
                Assert.Null(recovery.CheckTransferDestination(location, destination));
                heldDisks = route == "changed" ? target.ToArray() : null;
            }
            if (route == "closed") held.Dispose();
            var admission = recovery.CheckTransferDestination(location, destination);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider()); providers.Register(recovery);
            var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(root, "journal"));
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [new ItemRef(location.WithPath("docs"), entry.Name, EntryKind.File, entry.Size)],
                Destination = Location.FileSystem(destination), Options = new TransferOptions { Conflicts = ConflictPolicy.Ask },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned recovery copy did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            var files = Directory.GetFiles(destination).Order(StringComparer.Ordinal).Select(p => new
            {
                Name = Path.GetFileName(p), Bytes = new FileInfo(p).Length,
                SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),
            }).ToArray();
            TestContext.Current.TestOutputHelper?.WriteLine("HELD_DEVICE_ADMISSION " + JsonSerializer.Serialize(new
            {
                route, target, heldDisks, admission, pathChecks, pipe.TopologyQueries, job.State, job.BytesDone,
                SourceSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath))),
                ExpectedSHA256 = Convert.ToHexString(SHA256.HashData(expected)), Files = files, ScanIssues = sink.Issues,
                Issues = job.Issues.Select(x => x.Message),
            }));
            Assert.Equal(image, File.ReadAllBytes(imagePath));
            Assert.Equal(marker, File.ReadAllBytes(Path.Combine(destination, "keep.bin")));
            if (route == "separate")
            {
                Assert.Null(admission); Assert.Equal(JobState.Completed, job.State);
                Assert.Equal(expected, File.ReadAllBytes(Path.Combine(destination, entry.Name)));
            }
            else
            {
                Assert.NotNull(admission); Assert.Equal(JobState.Failed, job.State);
                Assert.Equal(0, pathChecks);
                Assert.Equal(0, job.BytesDone); Assert.Equal(["keep.bin"], files.Select(x => x.Name));
                Assert.Contains(route is "same" or "changed" ? "is on the same" : "cannot tell", admission, StringComparison.Ordinal);
            }
        }
        finally { recovery.CloseAll(); Directory.Delete(root, recursive: true); }
    }

    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public List<string> Issues { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());
        public void ReportIssue(string message) => Issues.Add(message);
    }
}

internal sealed class HeldDeviceScriptPipe(byte[] image, Func<int[]?> disks) : Stream
{
    private MemoryStream _response = new();
    public bool Disposed { get; private set; }
    public bool FailReadHeader { get; set; }
    public bool FailWrite { get; set; }
    public int TopologyQueries { get; private set; }
    public byte[]? TopologyReply { get; set; }
    public override bool CanRead => !Disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => !Disposed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => _response.Read(buffer, offset, count);
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> request)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (FailWrite) throw new IOException("Controlled helper write failure.");
        Assert.Equal(RawReadProtocol.RequestSize, request.Length);
        _response.Dispose(); _response = new MemoryStream();
        if (request[0] == RawReadProtocol.Info)
        {
            _response.Write(BitConverter.GetBytes(0)); _response.Write(BitConverter.GetBytes((long)image.Length));
            _response.Write(BitConverter.GetBytes(512));
        }
        else if (request[0] == RawReadProtocol.Read && !FailReadHeader)
        {
            long at = BinaryPrimitives.ReadInt64LittleEndian(request[1..]);
            int want = BinaryPrimitives.ReadInt32LittleEndian(request[9..]);
            int n = (int)Math.Max(0, Math.Min(want, image.Length - at));
            _response.Write(BitConverter.GetBytes(0)); _response.Write(BitConverter.GetBytes(n));
            _response.Write(image.AsSpan((int)at, n));
        }
        else if (request[0] == 4)
        {
            TopologyQueries++;
            Assert.Equal(new byte[12], request[1..].ToArray());
            if (TopologyReply is { } answer) _response.Write(answer);
            else
            {
                var source = disks(); _response.Write(BitConverter.GetBytes(source is null ? 50 : 0));
                _response.Write(BitConverter.GetBytes(source?.Length ?? 0));
                if (source is not null) foreach (int disk in source) _response.Write(BitConverter.GetBytes(disk));
            }
        }
        _response.Position = 0;
    }
    protected override void Dispose(bool disposing)
    {
        Disposed = true; _response.Dispose(); base.Dispose(disposing);
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
