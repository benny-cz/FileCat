using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Recovery;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

[CollectionDefinition("Recovery image handle ownership", DisableParallelization = true)]
public sealed class RecoveryImageHandleOwnershipCollection;

[Collection("Recovery image handle ownership")]
public sealed class RecoveryImageOpenFailureTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("dynamic-vhd")]
    [InlineData("differencing-vhd")]
    [InlineData("vhdx")]
    [InlineData("fixed-vhd")]
    [InlineData("raw")]
    public void Refused_images_close_their_source_before_collection_and_supported_images_still_read(string kind)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-image-open-ownership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "owned.img");
        var bytes = Enumerable.Range(0, 1536).Select(i => (byte)(i * 29 + 7)).ToArray();
        if (kind.EndsWith("vhd", StringComparison.Ordinal))
        {
            "conectix"u8.CopyTo(bytes.AsSpan(bytes.Length - 512));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(bytes.Length - 512 + 60),
                kind == "fixed-vhd" ? 2U : kind == "dynamic-vhd" ? 3U : 4U);
        }
        else if (kind == "vhdx") "vhdxfile"u8.CopyTo(bytes);
        File.WriteAllBytes(path, bytes);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        bool noGC = false;
        try
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            noGC = GC.TryStartNoGCRegion(32L << 20);
            Assert.True(noGC, "The isolated source-ownership control requires a bounded no-GC interval.");
            int[] beforeGC = Counts();
            int before = OpenHandles(path);
            var observed = Open(path);
            int after = OpenHandles(path);
            int[] afterGC = Counts();
            GC.EndNoGCRegion(); noGC = false;
            bool unchanged = File.ReadAllBytes(path).SequenceEqual(bytes);
            output.WriteLine("IMAGE_OPEN_OWNERSHIP " + JsonSerializer.Serialize(new
            {
                kind, before, after, during = observed.During, length = observed.Length,
                errorType = observed.Failure?.GetType().FullName, errorMessage = observed.Failure?.Message,
                readBytes = observed.Bytes, actualPayloadSHA256 = observed.Hash,
                expectedPayloadSHA256 = Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, kind == "fixed-vhd" ? 1024 : bytes.Length))),
                beforeGC, afterGC, unchanged, inputSHA256 = hash, inputBytes = bytes.Length,
                nativeWindowsSharing = OperatingSystem.IsWindows(), nativeUnixDescriptors = !OperatingSystem.IsWindows(),
                boundedNoGCObservation = true, ownedRegularImageOnly = true,
            }));
            Assert.Equal(beforeGC, afterGC);
            Assert.Equal(0, before); Assert.True(unchanged);
            bool supported = kind is "raw" or "fixed-vhd";
            if (supported)
            {
                Assert.Null(observed.Failure); Assert.Equal(1, observed.During);
                Assert.Equal(kind == "fixed-vhd" ? 1024 : bytes.Length, observed.Length);
                Assert.Equal(observed.Length, observed.Bytes);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, (int)observed.Length))), observed.Hash);
            }
            else Assert.IsType<InvalidDataException>(observed.Failure);
            Assert.Equal(0, after);
        }
        finally
        {
            if (noGC) GC.EndNoGCRegion();
            // Preserve the immediate observation above; finalizers only restore the original failed fixture here.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            int afterFixtureGC = OpenHandles(path);
            bool unchanged = File.ReadAllBytes(path).SequenceEqual(bytes);
            File.Delete(path); Directory.Delete(root, recursive: false);
            output.WriteLine("IMAGE_OPEN_OWNERSHIP_RESTORATION " + JsonSerializer.Serialize(new
            {
                root, afterFixtureGC, unchanged, ownedRootAbsent = !Directory.Exists(root),
                fixtureGCIsNotImmediateReleaseAcceptance = true,
            }));
        }
    }

    private static int[] Counts() => [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];

    private static int OpenHandles(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            try { using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None); return 0; }
            catch (IOException) { return 1; }
        }
        var selected = UnixFiles.Stat(path, followLinks: true) ?? throw new IOException("The owned input's identity is unavailable.");
        int count = 0;
        foreach (string entry in Directory.EnumerateFileSystemEntries(OperatingSystem.IsLinux() ? "/proc/self/fd" : "/dev/fd"))
        {
            if (!int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out int fd)) continue;
            using var handle = new SafeFileHandle((nint)fd, ownsHandle: false);
            if (UnixFiles.Stat(handle) is { } actual && actual.Device == selected.Device && actual.Inode == selected.Inode) count++;
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Exception? Failure, int During, long Length, int Bytes, string? Hash) Open(string path)
    {
        try
        {
            using var source = new ImageFileSource(path);
            int during = OpenHandles(path);
            var read = new byte[(int)source.Length];
            int bytes = source.Read(0, read);
            return (null, during, source.Length, bytes, Convert.ToHexString(SHA256.HashData(read)));
        }
        catch (Exception ex) { return (ex, -1, -1, 0, null); }
    }
}
