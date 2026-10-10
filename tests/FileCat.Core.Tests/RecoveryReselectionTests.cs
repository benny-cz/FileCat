using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>A fresh explicit device choice cannot inherit the previous choice's open reader and scan.</summary>
public sealed class RecoveryReselectionTests(ITestOutputHelper output)
{
    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Rows { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Rows.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }

    private sealed class Reader(string image, string name) : IBlockSource
    {
        private readonly ImageFileSource _source = new(image);
        public bool Disposed { get; private set; }
        public int Reads { get; private set; }
        public string Description => name;
        public long Length => _source.Length;
        public int Read(long offset, Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Reads++;
            return _source.Read(offset, buffer);
        }
        public void Dispose() { Disposed = true; _source.Dispose(); }
    }

    private static byte[] Image(int sectors, string label)
    {
        var bytes = new byte[sectors * 512];
        bytes[0] = 0xEB; bytes[1] = 0x3C; bytes[2] = 0x90;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(11), 512); bytes[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 1); bytes[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(17), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(19), (ushort)sectors); bytes[21] = 0xF8;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1); bytes[38] = 0x29;
        Encoding.ASCII.GetBytes(label.PadRight(11)).CopyTo(bytes, 43);
        bytes[510] = 0x55; bytes[511] = 0xAA;
        return bytes;
    }

    private async Task Observe(string mode, bool changedSize, bool sameName)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-reselection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string a = Path.Combine(root, "a.img"), b = Path.Combine(root, "b.img");
        var bytesA = Image(64, "FC_SOURCE_A");
        var bytesB = Image(changedSize ? 66 : 64, "FC_SOURCE_B");
        File.WriteAllBytes(a, bytesA); File.WriteAllBytes(b, bytesB);
        var provider = new RecoveryProvider();
        var readers = new List<Reader>();
        var names = new List<string>();
        provider.OpenDevice = (_, name, _) =>
        {
            names.Add(name);
            var reader = new Reader(readers.Count == 0 ? a : b, name);
            readers.Add(reader);
            return reader;
        };
        const string device = "/dev/mapper/fc_reselection_control";
        try
        {
            async Task<string> List(Location location)
            {
                var sink = new Sink();
                await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
                return Assert.IsType<RecoveryVolumeTag>(Assert.Single(sink.Rows).Tag).Details;
            }
            var first = provider.ForDevice(device, "owned source A", length: bytesA.Length);
            Assert.Contains("FC_SOURCE_A", await List(first), StringComparison.Ordinal);
            string? details = null;
            bool refused = false;
            if (mode == "repeat")
            {
                if (changedSize) provider.Forget(first);
                details = await List(first);
            }
            else
            {
                var selected = provider.ForDevice(device, sameName ? "owned source A" : "owned source B",
                    length: bytesB.Length + (mode == "mismatch" ? 512 : 0));
                try { details = await List(selected); }
                catch (IOException ex) when (ex.Message.Contains("is not the disk that was chosen", StringComparison.Ordinal)) { refused = true; }
            }
            bool unchanged = File.ReadAllBytes(a).SequenceEqual(bytesA) && File.ReadAllBytes(b).SequenceEqual(bytesB);
            output.WriteLine("RECOVERY_RESELECTION " + JsonSerializer.Serialize(new
            {
                mode, changedSize, sameName, details, refused, unchanged, names,
                readers = readers.Select(x => new { x.Description, x.Length, x.Reads, x.Disposed }).ToArray(),
                bytesA = bytesA.Length, bytesB = bytesB.Length,
                noDeviceOpened = true, twoOwnedRegularImagesOnly = true,
            }));
            Assert.True(unchanged);
            if (mode == "repeat")
            {
                Assert.False(refused); Assert.Contains("FC_SOURCE_A", details!, StringComparison.Ordinal);
                Assert.Single(readers); Assert.False(readers[0].Disposed);
            }
            else
            {
                Assert.Equal(2, readers.Count);
                Assert.True(readers[0].Disposed, "A fresh explicit choice must retire the old reader.");
                Assert.Equal(mode == "mismatch", refused);
                Assert.Equal(mode == "mismatch", readers[1].Disposed);
                if (mode != "mismatch") Assert.Contains("FC_SOURCE_B", details!, StringComparison.Ordinal);
            }
        }
        finally
        {
            provider.CloseAll();
            bool unchanged = File.ReadAllBytes(a).SequenceEqual(bytesA) && File.ReadAllBytes(b).SequenceEqual(bytesB);
            File.Delete(a); File.Delete(b); Directory.Delete(root, recursive: false);
            output.WriteLine("RECOVERY_RESELECTION_RESTORATION " + JsonSerializer.Serialize(new
            {
                root, ownedRootAbsent = !Directory.Exists(root), unchanged,
                everyReaderClosed = readers.All(x => x.Disposed),
            }));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Explicit_reselection_opens_the_new_source_even_when_its_path_is_reused(bool changedSize, bool sameName) =>
        Observe("reselect", changedSize, sameName);

    [Fact]
    public Task Reselection_cannot_bypass_the_current_chosen_size_check() => Observe("mismatch", true, false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Repeat_and_rescan_keep_the_existing_approved_reader(bool rescan) => Observe("repeat", rescan, true);
}
