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
    [Fact]
    public void Explicit_whole_disk_reselection_forgets_a_previous_single_volume_classification()
    {
        var provider = new RecoveryProvider();
        const string path = "owned-classification-only-device";

        var oneVolume = provider.ForDevice(path, "selected partition", volume: 1);
        Assert.Equal(Schemes.Computer, provider.GetParent(oneVolume)?.Scheme);

        var wholeDisk = provider.ForDevice(path, "selected whole disk");
        var firstVolume = new Location(Schemes.Recovery, string.Empty, wholeDisk.Container, "0");
        Assert.Equal("Volume 0", provider.GetDisplayName(firstVolume));
        Assert.Equal(wholeDisk, provider.GetParent(firstVolume));

        provider.CloseAll();
    }

    [Fact]
    public async Task Evicted_device_scan_cannot_reopen_from_an_old_location_without_fresh_selection()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string a = Path.Combine(root, "a.img"), b = Path.Combine(root, "b.img");
        byte[] bytesA = Image(64, "FC_SOURCE_A"), bytesB = Image(64, "FC_SOURCE_B");
        File.WriteAllBytes(a, bytesA);
        File.WriteAllBytes(b, bytesB);
        var provider = new RecoveryProvider();
        var opens = new Dictionary<string, int>(StringComparer.Ordinal);
        var readers = new List<Reader>();
        provider.OpenDevice = (path, name, _) =>
        {
            opens[path] = opens.GetValueOrDefault(path) + 1;
            var reader = new Reader(path == "owned-device-0" && opens[path] > 1 ? b : a, name);
            readers.Add(reader);
            return reader;
        };

        try
        {
            var first = provider.ForDevice("owned-device-0", "approved A", length: bytesA.Length);
            var sink = new Sink();
            await provider.EnumerateAsync(first, sink, TestContext.Current.CancellationToken);
            Assert.Contains("FC_SOURCE_A", Assert.IsType<RecoveryVolumeTag>(Assert.Single(sink.Rows).Tag).Details);

            // The fifth owned source evicts the first scan from the four-session cache.
            for (int i = 1; i <= 4; i++)
            {
                var next = provider.ForDevice("owned-device-" + i, "approved " + i, length: bytesA.Length);
                await provider.EnumerateAsync(next, new Sink(), TestContext.Current.CancellationToken);
            }

            Assert.True(readers[0].Disposed);
            var oldTab = new Sink();
            UnauthorizedAccessException? refusal = null;
            try { await provider.EnumerateAsync(first, oldTab, TestContext.Current.CancellationToken); }
            catch (UnauthorizedAccessException ex) { refusal = ex; }
            string? reopenedDetails = oldTab.Rows.Count == 1
                ? Assert.IsType<RecoveryVolumeTag>(oldTab.Rows[0].Tag).Details : null;
            output.WriteLine("RECOVERY_EVICTED_DEVICE_REOPEN " + JsonSerializer.Serialize(new
            {
                refused = refusal is not null,
                reopenedDetails,
                opensOfFirstPath = opens["owned-device-0"],
                readers = readers.Select(reader => new { reader.Description, reader.Reads, reader.Disposed }),
                unchanged = File.ReadAllBytes(a).SequenceEqual(bytesA) && File.ReadAllBytes(b).SequenceEqual(bytesB),
            }));
            Assert.NotNull(refusal);
            Assert.Empty(oldTab.Rows);
            Assert.Equal(1, opens["owned-device-0"]);
            Assert.Equal(bytesA, File.ReadAllBytes(a));
            Assert.Equal(bytesB, File.ReadAllBytes(b));
        }
        finally
        {
            provider.CloseAll();
            Assert.All(readers, reader => Assert.True(reader.Disposed));
            File.Delete(a);
            File.Delete(b);
            Directory.Delete(root, recursive: false);
            Assert.False(Directory.Exists(root));
        }
    }

    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Rows { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Rows.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }

    private sealed class Reader(string image, string name, string? identity = null) : IBlockSource, IDeviceSelectionIdentity
    {
        private readonly ImageFileSource _source = new(image);
        public bool Disposed { get; private set; }
        public int Reads { get; private set; }
        public string Description => name;
        public string? SelectionIdentity => identity;
        public long Length => _source.Length;
        public int Read(long offset, Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Reads++;
            return _source.Read(offset, buffer);
        }
        public void Dispose() { Disposed = true; _source.Dispose(); }
    }

    private sealed class PathGuardReader(Reader reader) : IBlockSource, IDevicePathGuard
    {
        public bool Current { get; set; } = true;
        public bool InvalidateOnRead { get; set; }
        public string Description => reader.Description;
        public long Length => reader.Length;
        public int Read(long offset, Span<byte> buffer)
        {
            int count = reader.Read(offset, buffer);
            if (InvalidateOnRead) Current = false;
            return count;
        }
        public bool IsCurrentDevicePath(string path) => Current;
        public void Dispose() => reader.Dispose();
    }

    private sealed class CallbackSink(Action issue) : IEnumerationSink
    {
        public int Batches { get; private set; }
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Batches++;
        public void ReportIssue(string message) => issue();
    }

    [Fact]
    public async Task Cached_device_listing_refuses_a_changed_held_source_path()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-held-listing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string image = Path.Combine(root, "a.img");
        byte[] bytes = Image(64, "FC_SOURCE_A");
        File.WriteAllBytes(image, bytes);
        var provider = new RecoveryProvider();
        var source = new Reader(image, "selected A");
        var guard = new PathGuardReader(source);
        provider.OpenDevice = (_, _, _) => guard;
        try
        {
            var chosen = provider.ForDevice("owned-held-device", "selected A", length: bytes.Length);
            var initial = new Sink();
            await provider.EnumerateAsync(chosen, initial, TestContext.Current.CancellationToken);
            Assert.Contains("FC_SOURCE_A", Assert.IsType<RecoveryVolumeTag>(Assert.Single(initial.Rows).Tag).Details);

            guard.Current = false;
            var stale = new Sink();
            IOException? refusal = null;
            try { await provider.EnumerateAsync(chosen, stale, TestContext.Current.CancellationToken); }
            catch (IOException ex) { refusal = ex; }
            output.WriteLine("RECOVERY_HELD_PATH_LISTING " + JsonSerializer.Serialize(new
            {
                refused = refusal is not null, rows = stale.Rows.Count, source.Reads,
                sourceUnchanged = File.ReadAllBytes(image).SequenceEqual(bytes),
            }));
            Assert.NotNull(refusal);
            Assert.Empty(stale.Rows);
            Assert.Equal(bytes, File.ReadAllBytes(image));
        }
        finally
        {
            provider.CloseAll();
            Assert.True(source.Disposed);
            File.Delete(image);
            Directory.Delete(root, recursive: false);
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public async Task Device_listing_refuses_path_changed_by_issue_callback_before_publication()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-held-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string image = Path.Combine(root, "a.img");
        byte[] bytes = Image(64, "FC_SOURCE_A");
        File.WriteAllBytes(image, bytes);
        var provider = new RecoveryProvider();
        var source = new Reader(image, "selected A");
        var guard = new PathGuardReader(source);
        provider.OpenDevice = (_, _, _) => guard;
        try
        {
            var chosen = provider.ForDevice("owned-held-callback-device", "selected A", volume: 1, length: bytes.Length);
            int issues = 0;
            var sink = new CallbackSink(() => { issues++; guard.Current = false; });
            IOException? refusal = null;
            try { await provider.EnumerateAsync(chosen, sink, TestContext.Current.CancellationToken); }
            catch (IOException ex) { refusal = ex; }
            output.WriteLine("RECOVERY_HELD_PATH_CALLBACK " + JsonSerializer.Serialize(new
            {
                refused = refusal is not null, sink.Batches, issues, source.Reads,
                sourceUnchanged = File.ReadAllBytes(image).SequenceEqual(bytes),
            }));
            Assert.True(issues > 0);
            Assert.NotNull(refusal);
            Assert.Equal(0, sink.Batches);
            Assert.Equal(bytes, File.ReadAllBytes(image));
        }
        finally
        {
            provider.CloseAll();
            Assert.True(source.Disposed);
            File.Delete(image);
            Directory.Delete(root, recursive: false);
            Assert.False(Directory.Exists(root));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Held_device_content_refuses_a_changed_path_before_or_during_read(bool duringRead)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-held-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string image = Path.Combine(root, "a.img");
        byte[] bytes = Image(64, "FC_SOURCE_A");
        Encoding.ASCII.GetBytes("CONTENT BIN").CopyTo(bytes, 3 * 512);
        bytes[3 * 512] = 0xE5;
        bytes[3 * 512 + 11] = 0x20;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(3 * 512 + 26), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(3 * 512 + 28), 4096);
        for (int i = 0; i < 4096; i++) bytes[4 * 512 + i] = (byte)(i * 29 + 17);
        File.WriteAllBytes(image, bytes);
        var provider = new RecoveryProvider();
        var source = new Reader(image, "selected A");
        var guard = new PathGuardReader(source);
        provider.OpenDevice = (_, _, _) => guard;
        try
        {
            var chosen = provider.ForDevice("owned-held-content-device", "selected A", volume: 1, length: bytes.Length);
            var initial = new Sink();
            await provider.EnumerateAsync(chosen, initial, TestContext.Current.CancellationToken);
            var row = Assert.Single(initial.Rows);
            using var content = provider.OpenContent(provider.GetItemRef(chosen, row))!;
            var first = new byte[4096];
            Assert.Equal(4096, content.Read(0, first));
            Assert.Equal(bytes.AsSpan(4 * 512, 4096).ToArray(), first);
            int readsBeforeChange = source.Reads;
            if (duringRead) guard.InvalidateOnRead = true;
            else guard.Current = false;
            var later = new byte[4096];
            IOException? refusal = null;
            try { content.Read(0, later); }
            catch (IOException ex) { refusal = ex; }
            output.WriteLine("RECOVERY_HELD_CONTENT_PATH " + JsonSerializer.Serialize(new
            {
                duringRead, refused = refusal is not null, readsBeforeChange, source.Reads,
                returnedOriginalBytes = later.SequenceEqual(first),
                sourceUnchanged = File.ReadAllBytes(image).SequenceEqual(bytes),
            }));
            Assert.NotNull(refusal);
            if (!duringRead) Assert.Equal(readsBeforeChange, source.Reads);
            else Assert.True(source.Reads > readsBeforeChange);
            Assert.Equal(bytes, File.ReadAllBytes(image));
        }
        finally
        {
            provider.CloseAll();
            Assert.True(source.Disposed);
            File.Delete(image);
            Directory.Delete(root, recursive: false);
            Assert.False(Directory.Exists(root));
        }
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

    [Theory]
    [InlineData("B", true)]
    [InlineData(null, true)]
    [InlineData("A", false)]
    public async Task Same_size_replacement_is_refused_before_any_scan_read(string? openedIdentity, bool refused)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string a = Path.Combine(root, "a.img"), b = Path.Combine(root, "b.img");
        byte[] bytesA = Image(64, "FC_SOURCE_A"), bytesB = Image(64, "FC_SOURCE_B");
        File.WriteAllBytes(a, bytesA); File.WriteAllBytes(b, bytesB);
        var provider = new RecoveryProvider();
        Reader? opened = null;
        try
        {
            const string device = "owned-physical-drive-1";
            var selected = provider.ForDevice(device, "selected A", length: bytesA.Length, identity: "A");
            // The same path and length now open B (or return an unavailable identity).
            provider.OpenDevice = (_, name, _) => opened = new Reader(openedIdentity == "A" ? a : b, name, openedIdentity);
            var sink = new Sink();
            if (refused)
            {
                var error = await Assert.ThrowsAsync<IOException>(() => provider.EnumerateAsync(selected, sink, TestContext.Current.CancellationToken));
                Assert.Contains("identity changed or could not be verified", error.Message, StringComparison.Ordinal);
                Assert.Empty(sink.Rows);
                Assert.NotNull(opened);
                Assert.Equal(0, opened.Reads);
                Assert.True(opened.Disposed);
            }
            else
            {
                await provider.EnumerateAsync(selected, sink, TestContext.Current.CancellationToken);
                Assert.Contains("FC_SOURCE_A", Assert.IsType<RecoveryVolumeTag>(Assert.Single(sink.Rows).Tag).Details, StringComparison.Ordinal);
                Assert.True(opened!.Reads > 0);
            }
            Assert.Equal(bytesA, File.ReadAllBytes(a));
            Assert.Equal(bytesB, File.ReadAllBytes(b));
        }
        finally
        {
            provider.CloseAll();
            Assert.True(opened is null || opened.Disposed);
            File.Delete(a); File.Delete(b); Directory.Delete(root, recursive: false);
            Assert.False(Directory.Exists(root));
        }
    }
}
