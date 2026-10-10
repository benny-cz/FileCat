using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.Platform.Windows.Recovery;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class HeldDeviceProtocolTests
{
    [Theory]
    [InlineData("unknown")]
    [InlineData("empty")]
    [InlineData("negative-count")]
    [InlineData("too-many")]
    [InlineData("short-header")]
    [InlineData("short-body")]
    [InlineData("negative-disk")]
    [InlineData("error-with-data")]
    [InlineData("duplicate")]
    public void Source_identity_answers_are_bounded_complete_and_fail_closed(string answer)
    {
        byte[] Reply(int status, int count, params int[] disks) => BitConverter.GetBytes(status).Concat(BitConverter.GetBytes(count)).Concat(disks.SelectMany(BitConverter.GetBytes)).ToArray();
        byte[] bytes = answer switch
        {
            "unknown" => Reply(50, 0), "empty" => Reply(0, 0), "negative-count" => Reply(0, -1),
            "too-many" => Reply(0, 33), "short-header" => new byte[7], "short-body" => Reply(0, 2, 7),
            "negative-disk" => Reply(0, 1, -1), "error-with-data" => Reply(5, 1, 7), _ => Reply(0, 2, 7, 7),
        };
        var pipe = new HeldDeviceScriptPipe(new byte[4096], () => null) { TopologyReply = bytes };
        var source = new PipeDeviceSource(pipe, "controlled helper reply");
        var actual = source.QuerySourceDisks();
        TestContext.Current.TestOutputHelper?.WriteLine("HELD_DEVICE_REPLY " + JsonSerializer.Serialize(new { answer, Actual = actual, pipe.TopologyQueries }));
        if (answer == "duplicate") Assert.Equal([7], actual!); else Assert.Null(actual);
        if (answer is "unknown" or "duplicate") Assert.Equal(16, source.Read(0, new byte[16]));
        else Assert.Throws<IOException>(() => source.Read(0, new byte[16]));
        source.Dispose(); source.Dispose(); Assert.True(pipe.Disposed);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("empty")]
    [InlineData("short-header")]
    [InlineData("short-extents")]
    [InlineData("too-many")]
    [InlineData("negative-disk")]
    [InlineData("negative-start")]
    [InlineData("zero-length")]
    public void Incomplete_native_extent_answers_never_become_partial_known_topology(string answer)
    {
        var data = new byte[8 + 24 * 32];
        BinaryPrimitives.WriteInt32LittleEndian(data, answer == "empty" ? 0 : answer == "too-many" ? 33 : 2);
        for (int i = 0; i < 2; i++)
        {
            var extent = data.AsSpan(8 + i * 24, 24);
            BinaryPrimitives.WriteInt32LittleEndian(extent, answer == "negative-disk" ? -1 : i + 3);
            BinaryPrimitives.WriteInt64LittleEndian(extent[8..], answer == "negative-start" ? -1 : 0);
            BinaryPrimitives.WriteInt64LittleEndian(extent[16..], answer == "zero-length" ? 0 : 4096);
        }
        uint returned = answer == "short-header" ? 7u : answer == "short-extents" ? 32u : 56u;
        var actual = DeviceTopology.ParseDiskExtents(data, returned);
        TestContext.Current.TestOutputHelper?.WriteLine("HELD_DEVICE_EXTENTS " + JsonSerializer.Serialize(new { answer, returned, Actual = actual }));
        if (answer == "complete") Assert.Equal([3, 4], actual!); else Assert.Null(actual);
    }

    [Fact]
    public void Held_volume_metadata_uses_a_query_only_handle_and_disposal_becomes_unknown()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Native volume metadata requires Windows.");
        string folder = Path.GetTempPath();
        string volume = DeviceTopology.VolumeDevice(Path.GetPathRoot(folder)!)!;
        using var handle = CreateFile(volume, 0, 3, 0, 3, 0, 0);
        Assert.False(handle.IsInvalid);
        var expected = DeviceTopology.DisksOf(folder);
        var actual = DeviceTopology.DisksOf(handle, wholeDisk: false);
        TestContext.Current.TestOutputHelper?.WriteLine("HELD_VOLUME_METADATA " + JsonSerializer.Serialize(new { volume, Access = 0, Expected = expected, Actual = actual }));
        Assert.NotNull(expected); Assert.NotEmpty(expected); Assert.Equal(expected, actual);
        Assert.True(DeviceTopology.SharesDisk(actual, folder));
        handle.Dispose(); Assert.Null(DeviceTopology.DisksOf(handle, wholeDisk: false));
        Assert.Null(DeviceTopology.SharesDisk((IReadOnlyList<int>?)null, @"\\server.invalid\share"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Helper_queries_only_its_held_file_and_preserves_following_reads(bool badArguments)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The native held-handle query requires Windows.");
        string path = Path.Combine(Path.GetTempPath(), "filecat-held-query-" + Guid.NewGuid().ToString("N"));
        byte[] bytes = Enumerable.Range(0, 4096).Select(x => (byte)x).ToArray();
        File.WriteAllBytes(path, bytes);
        try
        {
            var request = new byte[RawReadProtocol.RequestSize * 3];
            request[0] = RawReadProtocol.SourceDisks;
            if (badArguments) BinaryPrimitives.WriteInt64LittleEndian(request.AsSpan(1), 1);
            request[13] = RawReadProtocol.Read; BinaryPrimitives.WriteInt64LittleEndian(request.AsSpan(14), 123);
            BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(22), 17); request[26] = RawReadProtocol.Close;
            using var pipe = new Replay(request);
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Assert.Equal("FileCat closed the session.", RawReadProtocol.Serve(pipe, handle, bytes.Length, 512));
            var output = pipe.Output;
            Assert.Equal(badArguments ? 87 : 50, BinaryPrimitives.ReadInt32LittleEndian(output));
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(4)));
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(8)));
            Assert.Equal(17, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(12)));
            Assert.Equal(bytes.AsSpan(123, 17).ToArray(), output[16..]);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    private sealed class Replay(byte[] request) : Stream
    {
        private readonly MemoryStream _input = new(request), _output = new();
        public byte[] Output => _output.ToArray();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
