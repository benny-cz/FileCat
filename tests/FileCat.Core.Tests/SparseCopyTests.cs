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
