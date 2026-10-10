using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Recovery;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

[Collection("Recovery image handle ownership")]
public sealed class RecoveryImageSessionLifetimeTests(ITestOutputHelper output)
{
    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Rows { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Rows.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }

    private static byte[] Image(int seed)
    {
        var bytes = new byte[64 * 512];
        bytes[0] = 0xEB; bytes[1] = 0x3C; bytes[2] = 0x90;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(11), 512); bytes[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 1); bytes[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(17), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(19), 64); bytes[21] = 0xF8;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1); bytes[38] = 0x29;
        Encoding.ASCII.GetBytes("OWNED_LEASE").CopyTo(bytes, 43);
        bytes[510] = 0x55; bytes[511] = 0xAA;
        Encoding.ASCII.GetBytes("LEASE   BIN").CopyTo(bytes, 3 * 512); bytes[3 * 512] = 0xE5; bytes[3 * 512 + 11] = 0x20;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(3 * 512 + 26), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(3 * 512 + 28), 4096);
        for (int i = 0; i < 4096; i++) bytes[4 * 512 + i] = (byte)(i * 29 + seed);
        return bytes;
    }

    private static int Handles(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            try { using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None); return 0; }
            catch (IOException) { return 1; }
        }
        var selected = UnixFiles.Stat(path, followLinks: true) ?? throw new IOException("Owned image identity unavailable");
        int count = 0;
        foreach (string pathFd in Directory.EnumerateFileSystemEntries(OperatingSystem.IsLinux() ? "/proc/self/fd" : "/dev/fd"))
        {
            if (!int.TryParse(Path.GetFileName(pathFd), NumberStyles.None, CultureInfo.InvariantCulture, out int fd)) continue;
            using var handle = new SafeFileHandle((nint)fd, ownsHandle: false);
            if (UnixFiles.Stat(handle) is { } actual && actual.Device == selected.Device && actual.Inode == selected.Inode) count++;
        }
        return count;
    }

    private async Task Observe(string route, int consumers, bool device)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-image-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var expectedImages = Enumerable.Range(0, 6).Select(i => Image(17 + i)).ToArray();
        var paths = Enumerable.Range(0, 6).Select(i => Path.Combine(root, i + ".img")).ToArray();
        for (int i = 0; i < paths.Length; i++) File.WriteAllBytes(paths[i], expectedImages[i]);
        string retiredPath = Path.Combine(root, "retired.img");
        var provider = new RecoveryProvider();
        var readers = new List<IContentSource>();
        int opens = 0;
        provider.OpenDevice = (_, _, _) => new ImageFileSource(paths[Math.Min(opens++, paths.Length - 1)]);
        async Task<Sink> List(Location location)
        {
            var sink = new Sink(); await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken); return sink;
        }
        try
        {
            var location = device ? provider.ForDevice("owned-device-label", "owned regular image", volume: 1, length: expectedImages[0].Length)
                : RecoveryProvider.ForImage(paths[0], 1);
            var row = Assert.Single((await List(location)).Rows);
            Assert.Equal(EntryKind.File, row.Kind);
            for (int i = 0; i < consumers; i++) readers.Add(provider.OpenContent(provider.GetItemRef(location, row))!);
            var expected = expectedImages[0].AsSpan(4 * 512, 4096).ToArray();
            foreach (var reader in readers) { var bytes = new byte[4096]; Assert.Equal(4096, reader.Read(0, bytes)); Assert.Equal(expected, bytes); }
            Assert.Equal(1, Handles(paths[0]));
            if (route == "evict")
                for (int i = 1; i < paths.Length; i++) await List(RecoveryProvider.ForImage(paths[i], 1));
            else if (route == "forget") provider.Forget(location);
            else if (route == "close-all") provider.CloseAll();
            else if (route == "reselect") provider.ForDevice("owned-device-label", "new explicit owned image", volume: 1, length: expectedImages[1].Length);
            else if (route == "replace")
            {
                File.Move(paths[0], retiredPath); File.WriteAllBytes(paths[0], expectedImages[1]);
                File.SetLastWriteTimeUtc(paths[0], DateTime.UtcNow.AddMinutes(1));
                var newRow = Assert.Single((await List(location)).Rows);
                using var current = provider.OpenContent(provider.GetItemRef(location, newRow))!;
                var bytes = new byte[4096]; Assert.Equal(4096, current.Read(0, bytes)); Assert.Equal(expectedImages[1].AsSpan(4 * 512, 4096).ToArray(), bytes);
            }
            else throw new InvalidOperationException(route);
            string observedPath = route == "replace" ? retiredPath : paths[0];
            int afterRetire = Handles(observedPath);
            var readResults = new List<object>();
            foreach (var reader in readers)
            {
                var bytes = new byte[4096]; string? error = null; int count = -1;
                try { count = reader.Read(0, bytes); } catch (Exception ex) { error = ex.GetType().FullName; }
                readResults.Add(new { count, error, hash = Convert.ToHexString(SHA256.HashData(bytes)), expectedHash = Convert.ToHexString(SHA256.HashData(expected)) });
            }
            output.WriteLine("RECOVERY_IMAGE_LIFETIME " + JsonSerializer.Serialize(new { route, consumers, device, afterRetire, readResults, noDeviceOpened = true, ownedRegularImagesOnly = true }));
            if (device)
            {
                Assert.Equal(0, afterRetire);
                foreach (var reader in readers) Assert.Throws<ObjectDisposedException>(() => reader.Read(0, new byte[4096]));
            }
            else
            {
                Assert.Equal(1, afterRetire);
                foreach (var reader in readers) { var bytes = new byte[4096]; Assert.Equal(4096, reader.Read(0, bytes)); Assert.Equal(expected, bytes); Assert.Empty(((IPartialContent)reader).MissingRanges); }
            }
            readers[0].Dispose(); readers[0].Dispose();
            int afterFirst = Handles(observedPath);
            if (!device) Assert.Equal(consumers > 1 ? 1 : 0, afterFirst);
            foreach (var reader in readers.Skip(1)) reader.Dispose();
            Assert.Equal(0, Handles(observedPath));
            output.WriteLine("RECOVERY_IMAGE_LIFETIME_RELEASE " + JsonSerializer.Serialize(new { route, consumers, device, afterFirst, afterLast = Handles(observedPath), doubleCloseSafe = true, noCollectionForcedForAcceptance = true }));
            GC.KeepAlive(readers);
        }
        finally
        {
            foreach (var reader in readers) reader.Dispose(); provider.CloseAll();
            bool unchanged = Enumerable.Range(0, paths.Length).All(i => File.ReadAllBytes(paths[i]).SequenceEqual(expectedImages[i == 0 && File.Exists(retiredPath) ? 1 : i]))
                && (!File.Exists(retiredPath) || File.ReadAllBytes(retiredPath).SequenceEqual(expectedImages[0]));
            int handles = paths.Sum(Handles) + (File.Exists(retiredPath) ? Handles(retiredPath) : 0);
            foreach (string path in paths) File.Delete(path); if (File.Exists(retiredPath)) File.Delete(retiredPath); Directory.Delete(root, recursive: false);
            output.WriteLine("RECOVERY_IMAGE_LIFETIME_RESTORATION " + JsonSerializer.Serialize(new { root, unchanged, handles, ownedRootAbsent = !Directory.Exists(root) }));
            Assert.True(unchanged); Assert.Equal(0, handles); Assert.False(Directory.Exists(root));
        }
    }

    [Theory]
    [InlineData("evict", 1)] [InlineData("evict", 2)]
    [InlineData("forget", 1)] [InlineData("forget", 2)]
    [InlineData("close-all", 1)] [InlineData("close-all", 2)]
    [InlineData("replace", 1)] [InlineData("replace", 2)]
    public Task Active_image_consumers_keep_their_open_source_until_the_last_reader_closes(string route, int consumers) => Observe(route, consumers, false);

    [Theory]
    [InlineData("evict")] [InlineData("close-all")] [InlineData("reselect")]
    public Task Device_readers_still_close_on_retirement_or_explicit_reselection(string route) => Observe(route, 1, true);
}
