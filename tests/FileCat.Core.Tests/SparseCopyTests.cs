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
        using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(length);
            if (!allHole)
            {
                byte[] data = RandomNumberGenerator.GetBytes(4096);
                stream.Write(data);
                stream.Position = length / 2;
                stream.Write(data);
            }
        }

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
