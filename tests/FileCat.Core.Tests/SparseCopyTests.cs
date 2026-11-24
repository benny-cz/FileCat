using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

public sealed class SparseCopyTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Copy_keeps_data_and_trailing_holes_without_allocating_them(bool allHole, bool disablePreallocation)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Skip("Unix extent reporting is required.");

        const int length = 16 * 1024 * 1024;
        string source = Path.Combine(_dir.Path, "source.bin");
        string destination = Path.Combine(_dir.Path, "copy.bin");
        WriteSparse(source, length, allHole ? [] : [(0, 4096), (length / 2, 4096)]);

        long sourceBlocks = Blocks(source);
        if (sourceBlocks * 512 >= length / 4)
            Assert.Skip("The test filesystem did not create a sparse source file.");

        long reported = -1;
        new PortableFileOperations().CopyFile(source, destination,
            new FileCopyOptions { DisablePreallocation = disablePreallocation },
            (done, total) => { Assert.Equal(length, total); reported = done; return CopyProgressAction.Continue; },
            TestContext.Current.CancellationToken);

        Assert.Equal(length, new FileInfo(destination).Length);
        Assert.Equal(length, reported);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(source)), SHA256.HashData(File.ReadAllBytes(destination)));
        Assert.True(Blocks(destination) * 512 < length / 4,
            "A sparse source copy must not consume nearly the full logical length on disk.");
    }

    /// <summary>
    /// I652: APFS gives a file written with gaps real zero blocks for its smaller gaps when the file is closed, so FileCat's
    /// copy of the Mac GUI harness's b26 source (32 MiB with two 4 KiB extents) was fully allocated although its bytes
    /// matched. The copy now punches the holes it skips, so it keeps the source's allocation. The other layouts add a
    /// leading hole with data ending inside an unaligned last block, and many small gaps.
    /// </summary>
    [Theory]
    [InlineData("b26")]
    [InlineData("leading hole, unaligned data end")]
    [InlineData("many small gaps")]
    public void Copy_keeps_the_holes_APFS_would_fill_when_the_copy_is_closed(string layout)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Skip("Unix extent reporting is required.");
        const long mib = 1024 * 1024;
        var (length, data) = layout switch
        {
            "b26" => (32 * mib, new (long, int)[] { (0, 4096), (16 * mib, 4096) }),
            "leading hole, unaligned data end" => (16 * mib + 100, new (long, int)[] { (8 * mib, 4096), (16 * mib, 100) }),
            _ => (16 * mib, Enumerable.Range(0, 8).Select(i => (i * 2 * mib, 4096)).ToArray()),
        };
        string source = Path.Combine(_dir.Path, "sparse.bin");
        string destination = Path.Combine(_dir.Path, "copy.bin");
        WriteSparse(source, length, data);
        long sourceBytes = Blocks(source) * 512;
        if (sourceBytes >= length / 4)
            Assert.Skip("The test filesystem did not keep the source sparse.");
        byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(source));

        long reported = -1;
        new PortableFileOperations().CopyFile(source, destination, new FileCopyOptions(),
            (done, total) => { reported = done; return CopyProgressAction.Continue; }, TestContext.Current.CancellationToken);

        Assert.Equal(length, new FileInfo(destination).Length);
        Assert.Equal(length, reported);
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(destination)));
        long copied = Blocks(destination) * 512;
        Assert.True(copied < length / 4, $"The copy allocated {copied:N0} bytes for a {length:N0}-byte file with {sourceBytes:N0} allocated.");
        if (OperatingSystem.IsMacOS())
            Assert.True(copied <= sourceBytes + 64 * 1024, $"The copy allocated {copied:N0} bytes for a source with {sourceBytes:N0} allocated.");
        Assert.Equal(sourceBytes, Blocks(source) * 512);
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(source)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Windows_copy_keeps_sparse_ranges_and_bytes(bool allHole, bool disablePreallocation)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sparse-file control codes are required.");
        const int length = 16 * 1024 * 1024;
        string source = Path.Combine(_dir.Path, "source.bin");
        string destination = Path.Combine(_dir.Path, "copy.bin");
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            if (!DeviceIoControl(stream.SafeFileHandle, 0x000900C4, 0, 0, 0, 0, out _, 0))
                Assert.Skip("The test volume cannot mark a file sparse.");
            stream.SetLength(length);
            if (!allHole)
            {
                byte[] data = RandomNumberGenerator.GetBytes(4096);
                stream.Write(data);
                stream.Position = length / 2;
                stream.Write(data);
            }
        }
        if (StorageBytes(source) >= length / 4)
            Assert.Skip("The test volume did not create a sparse source file.");

        long reported = -1;
        new PortableFileOperations().CopyFile(source, destination,
            new FileCopyOptions { DisablePreallocation = disablePreallocation },
            (done, total) => { Assert.Equal(length, total); reported = done; return CopyProgressAction.Continue; },
            TestContext.Current.CancellationToken);

        Assert.Equal(length, new FileInfo(destination).Length);
        Assert.Equal(length, reported);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(source)), SHA256.HashData(File.ReadAllBytes(destination)));
        Assert.True((File.GetAttributes(destination) & FileAttributes.SparseFile) != 0);
        Assert.True(StorageBytes(destination) < length / 4,
            "A sparse source copy must not consume nearly the full logical length on disk.");
    }

    [Fact]
    public void Windows_sparse_copy_handles_a_last_allocated_byte_beyond_a_rounding_boundary()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sparse-file control codes are required.");
        const int length = 16 * 1024 * 1024 + 1;
        string source = Path.Combine(_dir.Path, "source-unaligned.bin");
        string destination = Path.Combine(_dir.Path, "copy-unaligned.bin");
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            if (!DeviceIoControl(stream.SafeFileHandle, 0x000900C4, 0, 0, 0, 0, out _, 0))
                Assert.Skip("The test volume cannot mark a file sparse.");
            stream.SetLength(length);
            stream.Position = length - 1;
            stream.WriteByte(0x5A);
        }
        if (StorageBytes(source) >= length / 4)
            Assert.Skip("The test volume did not create a sparse source file.");

        new PortableFileOperations().CopyFile(source, destination, new FileCopyOptions(), null,
            TestContext.Current.CancellationToken);
        Assert.Equal(length, new FileInfo(destination).Length);
        Assert.Equal(0x5A, File.ReadAllBytes(destination)[^1]);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(source)), SHA256.HashData(File.ReadAllBytes(destination)));
        Assert.True(StorageBytes(destination) < length / 4);
    }

    [Fact]
    public void Windows_sparse_copy_refuses_a_newly_allocated_hole_before_publication()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sparse-file control codes are required.");
        const int length = 8 * 1024 * 1024;
        string source = Path.Combine(_dir.Path, "source-race.bin");
        string destination = Path.Combine(_dir.Path, "copy-race.bin");
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            if (!DeviceIoControl(stream.SafeFileHandle, 0x000900C4, 0, 0, 0, 0, out _, 0))
                Assert.Skip("The test volume cannot mark a file sparse.");
            stream.SetLength(length);
            stream.Write(RandomNumberGenerator.GetBytes(4096));
            stream.Position = length - 4096;
            stream.Write(RandomNumberGenerator.GetBytes(4096));
        }
        if (StorageBytes(source) >= length / 4)
            Assert.Skip("The test volume did not create a sparse source file.");

        bool wroteNewRange = false;
        var failure = Assert.Throws<IOException>(() => new PortableFileOperations().CopyFile(source, destination,
            new FileCopyOptions(), (done, _) =>
            {
                if (!wroteNewRange && done >= 4096)
                {
                    using var writer = new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    writer.Position = length / 2;
                    writer.Write(Enumerable.Repeat((byte)0x7B, 4096).ToArray());
                    writer.Flush(flushToDisk: true);
                    wroteNewRange = true;
                }
                return CopyProgressAction.Continue;
            }, TestContext.Current.CancellationToken));
        Assert.True(wroteNewRange);
        Assert.Contains("allocated ranges changed", failure.Message);
        Assert.False(File.Exists(destination));
        Assert.Equal(0x7B, File.ReadAllBytes(source)[length / 2]);
    }

    [Fact]
    public void Windows_sparse_copy_discards_output_when_a_copied_extent_is_rewritten()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sparse-file control codes are required.");
        const int length = 8 * 1024 * 1024;
        string source = Path.Combine(_dir.Path, "source-rewritten.bin");
        string destination = Path.Combine(_dir.Path, "copy-rewritten.bin");
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            if (!DeviceIoControl(stream.SafeFileHandle, 0x000900C4, 0, 0, 0, 0, out _, 0))
                Assert.Skip("The test volume cannot mark a file sparse.");
            stream.SetLength(length);
            stream.Write(RandomNumberGenerator.GetBytes(4096));
            stream.Position = length - 4096;
            stream.Write(RandomNumberGenerator.GetBytes(4096));
        }
        if (StorageBytes(source) >= length / 4)
            Assert.Skip("The test volume did not create a sparse source file.");

        byte[] originalDigest = SHA256.HashData(File.ReadAllBytes(source));
        bool rewrote = false;
        var failure = Assert.Throws<IOException>(() => new PortableFileOperations().CopyFile(source, destination,
            new FileCopyOptions(), (done, _) =>
            {
                if (!rewrote && done >= 4096)
                {
                    using var writer = new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    writer.Position = 0;
                    writer.Write(Enumerable.Repeat((byte)0x7B, 4096).ToArray());
                    writer.Flush(flushToDisk: true);
                    rewrote = true;
                }
                return CopyProgressAction.Continue;
            }, TestContext.Current.CancellationToken));
        Assert.True(rewrote);
        Assert.Contains("source file changed during its sparse copy", failure.Message);
        Assert.False(File.Exists(destination));
        Assert.NotEqual(originalDigest, SHA256.HashData(File.ReadAllBytes(source)));
    }

    [Fact]
    public void Windows_sparse_copy_verifies_allocated_ranges_across_query_batches()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sparse-file control codes are required.");
        const int rangeCount = 300, stride = 8192, length = 4 * 1024 * 1024;
        string source = Path.Combine(_dir.Path, "source-many-ranges.bin");
        string destination = Path.Combine(_dir.Path, "copy-many-ranges.bin");
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            if (!DeviceIoControl(stream.SafeFileHandle, 0x000900C4, 0, 0, 0, 0, out _, 0))
                Assert.Skip("The test volume cannot mark a file sparse.");
            stream.SetLength(length);
            for (int i = 0; i < rangeCount; i++)
            {
                stream.Position = i * stride;
                stream.WriteByte((byte)(i + 1));
            }
        }
        if (StorageBytes(source) >= length * 3L / 4)
            Assert.Skip("The test volume did not preserve separated sparse ranges.");

        new PortableFileOperations().CopyFile(source, destination, new FileCopyOptions(), null,
            TestContext.Current.CancellationToken);
        Assert.Equal(length, new FileInfo(destination).Length);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(source)), SHA256.HashData(File.ReadAllBytes(destination)));
        Assert.True((File.GetAttributes(destination) & FileAttributes.SparseFile) != 0);
        Assert.True(StorageBytes(destination) < length * 3L / 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Windows_sparse_copy_discards_output_when_source_eof_changes(bool grow)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sparse-file control codes are required.");
        const int length = 8 * 1024 * 1024;
        string source = Path.Combine(_dir.Path, "source-shrinking.bin");
        string destination = Path.Combine(_dir.Path, "copy-shrinking.bin");
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            if (!DeviceIoControl(stream.SafeFileHandle, 0x000900C4, 0, 0, 0, 0, out _, 0))
                Assert.Skip("The test volume cannot mark a file sparse.");
            stream.SetLength(length);
            stream.WriteByte(0x5A);
            stream.Position = length - 1;
            stream.WriteByte(0xA5);
        }
        if (StorageBytes(source) >= length / 4)
            Assert.Skip("The test volume did not create a sparse source file.");

        bool shrank = false;
        Assert.Throws<IOException>(() => new PortableFileOperations().CopyFile(source, destination,
            new FileCopyOptions(), (done, _) =>
            {
                if (!shrank && done > 0)
                {
                    using var writer = new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    writer.SetLength(grow ? length + 4096 : length / 2);
                    writer.Flush(flushToDisk: true);
                    shrank = true;
                }
                return CopyProgressAction.Continue;
            }, TestContext.Current.CancellationToken));
        Assert.True(shrank);
        Assert.Equal(grow ? length + 4096 : length / 2, new FileInfo(source).Length);
        Assert.False(File.Exists(destination));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Windows_sparse_copy_discards_output_on_cancellation(bool allHole)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sparse-file control codes are required.");
        const int length = 8 * 1024 * 1024;
        string source = Path.Combine(_dir.Path, "source-cancel.bin");
        string destination = Path.Combine(_dir.Path, "copy-cancel.bin");
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            if (!DeviceIoControl(stream.SafeFileHandle, 0x000900C4, 0, 0, 0, 0, out _, 0))
                Assert.Skip("The test volume cannot mark a file sparse.");
            stream.SetLength(length);
            if (!allHole) stream.WriteByte(0x5A);
        }
        if (StorageBytes(source) >= length / 4)
            Assert.Skip("The test volume did not create a sparse source file.");

        int callbacks = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => new PortableFileOperations().CopyFile(source, destination,
            new FileCopyOptions(), (_, _) => { callbacks++; return CopyProgressAction.Cancel; },
            TestContext.Current.CancellationToken));
        Assert.Equal(1, callbacks);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void Windows_sparse_copy_discards_partial_output_on_a_full_NTFS_volume()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_FULL_ROOT");
        Assert.SkipUnless(OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(root),
            "Requires a disposable, nearly full NTFS volume prepared for this native fixture.");
        var drive = new DriveInfo(root!);
        Assert.Equal(drive.RootDirectory.FullName, Path.GetFullPath(root!), ignoreCase: true);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.Equal("FCSPARSEFULL", drive.VolumeLabel);
        Assert.InRange(drive.TotalSize, 128L << 20, 320L << 20);
        Assert.InRange(drive.AvailableFreeSpace, 3L << 20, 7L << 20);

        const int length = 16 * 1024 * 1024, extent = 4 * 1024 * 1024;
        string source = Path.Combine(_dir.Path, "source-full-disk.bin");
        string destination = Path.Join(root!, "partial-copy.bin");
        Assert.False(File.Exists(destination));
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            Assert.True(DeviceIoControl(stream.SafeFileHandle, 0x000900C4, 0, 0, 0, 0, out _, 0));
            stream.SetLength(length);
            stream.Write(RandomNumberGenerator.GetBytes(extent));
            stream.Position = length - extent;
            stream.Write(RandomNumberGenerator.GetBytes(extent));
        }
        Assert.True(StorageBytes(source) < length);
        byte[] sourceDigest = SHA256.HashData(File.ReadAllBytes(source));
        long lastProgress = 0;
        bool observedSparseDestination = false;
        var failure = Assert.Throws<IOException>(() => new PortableFileOperations().CopyFile(source, destination,
            new FileCopyOptions { DisablePreallocation = true }, (done, _) =>
            {
                lastProgress = done;
                observedSparseDestination |= (File.GetAttributes(destination) & FileAttributes.SparseFile) != 0;
                return CopyProgressAction.Continue;
            }, TestContext.Current.CancellationToken));
        Assert.Contains(failure.HResult & 0xffff, new[] { 39, 112 });
        Assert.InRange(lastProgress, 1, length - 1);
        Assert.True(observedSparseDestination);
        Assert.False(File.Exists(destination));
        Assert.Equal(sourceDigest, SHA256.HashData(File.ReadAllBytes(source)));
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"disk-full HRESULT=0x{failure.HResult:X8}; progress={lastProgress}/{length}; source SHA256={Convert.ToHexString(sourceDigest)}");
    }

    [Fact]
    public void Windows_sparse_copy_reports_device_loss_after_a_partial_write()
    {
        string? control = Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_LOSS_CONTROL");
        string? volumeRoot = Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_LOSS_VOLUME");
        Assert.SkipUnless(OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(control) && !string.IsNullOrWhiteSpace(volumeRoot),
            "Requires an owned, detached-on-progress NTFS VHDX fixture.");
        Assert.StartsWith("sparse-device-loss-", Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(control!))),
            StringComparison.OrdinalIgnoreCase);
        var drive = new DriveInfo(volumeRoot!);
        Assert.Equal(drive.RootDirectory.FullName, Path.GetFullPath(volumeRoot!), ignoreCase: true);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.Equal("FCSPARSELOSS", drive.VolumeLabel);
        Assert.InRange(drive.TotalSize, 128L << 20, 320L << 20);

        RunDeviceLossCopy(control!, Path.Join(volumeRoot!, "partial-copy.bin"));
    }

    [Fact]
    public void Windows_sparse_copy_reports_physical_usb_loss_after_a_partial_write()
    {
        string? control = Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_LOSS_CONTROL");
        string? volumeRoot = Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_LOSS_VOLUME");
        string? fixtureRoot = Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_LOSS_USB_DESTINATION_ROOT");
        Assert.SkipUnless(OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(control) &&
            !string.IsNullOrWhiteSpace(volumeRoot) && !string.IsNullOrWhiteSpace(fixtureRoot),
            "Requires an explicitly owned physical removable NTFS device-loss fixture.");
        Assert.StartsWith("sparse-usb-device-loss-",
            Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(control!))),
            StringComparison.OrdinalIgnoreCase);
        var drive = new DriveInfo(volumeRoot!);
        string driveRoot = drive.RootDirectory.FullName;
        Assert.Equal(driveRoot, Path.GetFullPath(volumeRoot!), ignoreCase: true);
        Assert.Equal(DriveType.Removable, drive.DriveType);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.Equal("FCTEST", drive.VolumeLabel);
        Assert.InRange(drive.TotalSize, 7L << 30, 9L << 30);
        string owned = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fixtureRoot!));
        Assert.Equal(driveRoot, Path.GetDirectoryName(owned), ignoreCase: true);
        Assert.StartsWith("FileCatSparseUsbLoss-", Path.GetFileName(owned), StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(owned));
        Assert.Equal(FileAttributes.None, File.GetAttributes(owned) & FileAttributes.ReparsePoint);

        RunDeviceLossCopy(control!, Path.Join(owned, "partial-copy.bin"));
    }

    private void RunDeviceLossCopy(string control, string destination)
    {

        const int length = 16 * 1024 * 1024, extent = 4 * 1024 * 1024;
        string source = Path.Combine(_dir.Path, "source-device-loss.bin");
        Assert.False(File.Exists(destination));
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            Assert.True(DeviceIoControl(stream.SafeFileHandle, 0x000900C4, 0, 0, 0, 0, out _, 0));
            stream.SetLength(length);
            stream.Write(RandomNumberGenerator.GetBytes(extent));
            stream.Position = length - extent;
            stream.Write(RandomNumberGenerator.GetBytes(extent));
        }
        byte[] originalDigest = SHA256.HashData(File.ReadAllBytes(source));
        bool signaled = false;
        long progress = 0;
        Exception? failure = null;
        try
        {
            new PortableFileOperations().CopyFile(source, destination,
                new FileCopyOptions { DisablePreallocation = true }, (done, _) =>
                {
                    progress = done;
                    if (!signaled && done >= extent)
                    {
                        File.WriteAllText(Path.Join(control, "ready"), done.ToString(CultureInfo.InvariantCulture));
                        signaled = true;
                        WaitForFixtureMarker(Path.Join(control, "detached"), TestContext.Current.CancellationToken);
                    }
                    return CopyProgressAction.Continue;
                }, TestContext.Current.CancellationToken);
        }
        catch (Exception ex) { failure = ex; }
        finally { File.WriteAllText(Path.Join(control, "retired"), "1"); }
        Assert.True(signaled);
        WaitForFixtureMarker(Path.Join(control, "reattached"), TestContext.Current.CancellationToken);
        Assert.IsAssignableFrom<IOException>(failure);
        Assert.Equal(originalDigest, SHA256.HashData(File.ReadAllBytes(source)));
        bool partial = File.Exists(destination);
        long partialLength = partial ? new FileInfo(destination).Length : -1;
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"device-loss HRESULT=0x{failure!.HResult:X8}; progress={progress}/{length}; " +
            $"destination present={partial} length={partialLength}; source SHA256={Convert.ToHexString(originalDigest)}");
    }

    private static void WaitForFixtureMarker(string path, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(Path.Join(Path.GetDirectoryName(path)!, "aborted")))
                throw new IOException("The isolated device-loss controller aborted.");
            if (timer.Elapsed > TimeSpan.FromSeconds(45)) throw new TimeoutException($"Fixture did not create {Path.GetFileName(path)}.");
            Thread.Sleep(20);
        }
    }

    private static long Blocks(string path)
    {
        using var process = Process.Start(new ProcessStartInfo("stat")
        {
            ArgumentList = { OperatingSystem.IsMacOS() ? "-f" : "-c", "%b", path },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return long.Parse(output.Trim(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A new file of <paramref name="length"/> bytes with random data at <paramref name="data"/> and holes elsewhere. APFS
    /// gives a file written with gaps zero blocks for its smaller gaps when it is closed, so on macOS each gap's whole
    /// blocks are punched while the file is open, as the Mac GUI harness builds its b26 source.
    /// </summary>
    private static void WriteSparse(string path, long length, (long Offset, int Length)[] data)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        stream.SetLength(length);
        foreach (var (offset, count) in data)
        {
            stream.Position = offset;
            stream.Write(RandomNumberGenerator.GetBytes(count));
        }
        stream.Flush();
        if (!OperatingSystem.IsMacOS()) return;
        int fd = checked((int)stream.SafeFileHandle.DangerousGetHandle());
        long end = 0;
        foreach (var (offset, count) in data.OrderBy(range => range.Offset).Append((length, 0)))
        {
            long from = (end + 4095) / 4096 * 4096, to = offset / 4096 * 4096;
            if (to > from)
            {
                // struct fpunchhole: u32 fp_flags, u32 reserved, off_t fp_offset, off_t fp_length. fcntl is variadic:
                // Darwin ARM64 passes the variadic argument on the stack, after eight register slots.
                long[] punch = [0, from, to - from];
                int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? FcntlArm64(fd, 99 /* F_PUNCHHOLE */, 0, 0, 0, 0, 0, 0, punch) : Fcntl(fd, 99, punch);
                if (result != 0) Assert.Skip($"The test volume cannot punch holes (F_PUNCHHOLE errno {Marshal.GetLastPInvokeError()}).");
            }
            end = Math.Max(end, offset + count);
        }
    }

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int command, long[] argument);

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int FcntlArm64(int fd, int command, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, long[] argument);

    private static long StorageBytes(string path)
    {
        uint low = GetCompressedFileSizeW(path, out uint high);
        if (low == uint.MaxValue && Marshal.GetLastPInvokeError() != 0)
            throw new IOException($"Could not measure sparse allocation ({Marshal.GetLastPInvokeError()}).");
        return ((long)high << 32) | low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, nint input, uint inputSize,
        nint output, uint outputSize, out uint returned, nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string path, out uint high);
}
