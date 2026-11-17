using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>Disposable NTFS behavior fixtures. These operations do not exercise FileCat release admission.</summary>
public sealed unsafe partial class WindowsSparseTransformTests
{
    private const int Length = 4 * 1024 * 1024;
    private const string NamedStream = ":fixture";
    private const uint SetSparse = 0x000900C4;
    private const uint QueryAllocatedRanges = 0x000940CF;

    [Theory]
    [InlineData("all-hole")]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Efs_native_actions_preserve_identity_and_logical_bytes_in_bounded_sparse_fixtures(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            var encrypted = Report("encrypt", TryEfs(() => File.Encrypt(path)), path, first);
            Assert.True(encrypted.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, encrypted.Main.Allocation);
            Assert.Equal(first.Named.Allocation, encrypted.Named.Allocation);
            var decrypted = Report("decrypt", TryEfs(() => File.Decrypt(path)), path, first);
            Assert.False(decrypted.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, decrypted.Main.Allocation);
            Assert.Equal(first.Named.Allocation, decrypted.Named.Allocation);
        });
    }

    [Theory]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Compression_native_actions_preserve_identity_and_logical_bytes_in_bounded_sparse_fixtures(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            var compressed = Report("compress-main", InvokeCompression(path, StorageAction.Compress), path, first);
            Assert.Equal(WindowsCompression.Lznt1, compressed.Main.Compression);
            Assert.Equal(first.Named.Compression, compressed.Named.Compression);
            Assert.Equal(first.Named.Allocation, compressed.Named.Allocation);
            var decompressed = Report("remove-main-compression", InvokeCompression(path, StorageAction.RemoveCompression), path, first);
            Assert.Equal(WindowsCompression.None, decompressed.Main.Compression);
            Assert.Equal(first.Main.Allocation, decompressed.Main.Allocation);
            Assert.Equal(first.Named.Compression, decompressed.Named.Compression);
            Assert.Equal(first.Named.Allocation, decompressed.Named.Allocation);
        });
    }

    private static void RequireFixture()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost &&
            Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_TRANSFORMS") == "1",
            "Requires an explicitly prepared disposable NTFS volume in the Windows 11 VM.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }

    private static void Run(string shape, Action<string, Observation> transform)
    {
        string directory = Directory.CreateTempSubdirectory("filecat-sparse-transforms-").FullName;
        string path = Path.Join(directory, "fixture.bin");
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(directory)!);
            Assert.Equal("NTFS", drive.DriveFormat);
            File.WriteAllBytes(path, []);
            WriteStream(path, shape != "named-holes", shape is "all-hole" or "main-holes" or "both-holes",
                allHole: shape == "all-hole", seed: 17);
            WriteStream(path + NamedStream, shape is "named-holes" or "both-holes", shape is "named-holes" or "both-holes",
                allHole: false, seed: 29);
            var first = Observe(path);
            if (shape != "named-holes") Assert.True(first.Attributes.HasFlag(FileAttributes.SparseFile));
            Assert.Equal(Length, first.Main.Length);
            Assert.Equal(Length, first.Named.Length);
            if (shape == "all-hole") Assert.Empty(first.Main.Ranges);
            if (shape is "main-holes" or "both-holes") Assert.True(first.Main.Ranges.Sum(range => range.Length) < Length / 2);
            if (shape is "named-holes" or "both-holes") Assert.True(first.Named.Ranges.Sum(range => range.Length) < Length / 2);
            if (shape == "sparse-no-holes") Assert.Equal(Length, first.Main.Ranges.Sum(range => range.Length));
            Assert.True(GetDiskFreeSpace(drive.RootDirectory.FullName, out uint sectors, out uint bytes, out _, out _));
            TestContext.Current.TestOutputHelper!.WriteLine($"{shape} NTFS {RuntimeInformation.ProcessArchitecture} allocationUnit={checked(sectors * bytes)}");
            Write("before", "observed", first);

            transform(path, first);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void WriteStream(string streamPath, bool sparse, bool holes, bool allHole, int seed)
    {
        using var stream = new FileStream(streamPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (sparse) Assert.True(Control(stream.SafeFileHandle, SetSparse, null, 0, null, 0, out _, 0),
            $"Setting the fixture sparse failed with {Marshal.GetLastPInvokeError()}.");
        stream.SetLength(Length);
        if (holes)
        {
            if (!allHole)
            {
                stream.Position = 64 * 1024;
                stream.WriteByte((byte)seed);
                stream.Position = 2 * 1024 * 1024;
                stream.WriteByte((byte)(seed + 1));
            }
        }
        else
        {
            byte[] block = new byte[64 * 1024];
            new Random(seed).NextBytes(block);
            while (stream.Position < Length) stream.Write(block);
        }
        stream.Flush(flushToDisk: true);
    }

    private static string TryEfs(Action call)
    {
        try { call(); return "success"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return $"failure 0x{error.HResult:X8} {error.Message}"; }
    }

    private static string InvokeCompression(string path, StorageAction action)
    {
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var result = WindowsCompression.InvokeAdmitted(file, action);
        return $"success={result.NativeSucceeded} error={result.NativeError} format={result.After.Native}";
    }

    private static Observation Report(string phase, string result, string path, Observation first)
    {
        var observed = Observe(path);
        Write(phase, result, observed);
        Assert.Equal(first.Identity, observed.Identity);
        Assert.Equal(first.Main.Digest, observed.Main.Digest);
        Assert.Equal(first.Named.Digest, observed.Named.Digest);
        Assert.Equal(first.Main.Length, observed.Main.Length);
        Assert.Equal(first.Named.Length, observed.Named.Length);
        Assert.Equal(first.Main.Ranges, observed.Main.Ranges);
        Assert.Equal(first.Named.Ranges, observed.Named.Ranges);
        Assert.Equal(first.Main.Attributes & FileAttributes.SparseFile, observed.Main.Attributes & FileAttributes.SparseFile);
        Assert.Equal(first.Named.Attributes & FileAttributes.SparseFile, observed.Named.Attributes & FileAttributes.SparseFile);
        return observed;
    }

    private static void Write(string phase, string result, Observation observation)
    {
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{phase} {result} attributes={observation.Attributes} id={observation.Identity}");
        WriteStreamObservation("main", observation.Main);
        WriteStreamObservation("named", observation.Named);
    }

    private static void WriteStreamObservation(string name, StreamObservation observation) =>
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"  {name} eof={observation.Length} allocation={observation.Allocation} attributes={observation.Attributes} compression={observation.Compression} " +
            $"ranges={string.Join(",", observation.Ranges.Select(r => $"{r.Offset}+{r.Length}"))} sha256={observation.Digest}");

    private static Observation Observe(string path)
    {
        using var main = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var named = new FileStream(path + NamedStream, FileMode.Open, FileAccess.Read, FileShare.Read);
        var identity = ProtectedHexFile.Identity(main.SafeFileHandle);
        Assert.Equal(identity, ProtectedHexFile.Identity(named.SafeFileHandle));
        return new(identity, File.GetAttributes(main.SafeFileHandle), ObserveStream(main), ObserveStream(named));
    }

    private static StreamObservation ObserveStream(FileStream stream)
    {
        var input = new Range(0, stream.Length);
        var output = stackalloc Range[32];
        uint rangeSize = (uint)sizeof(Range);
        Assert.True(Control(stream.SafeFileHandle, QueryAllocatedRanges, &input, rangeSize, output, 32 * rangeSize, out uint bytes, 0),
            $"Allocated-range query failed with {Marshal.GetLastPInvokeError()}.");
        Assert.True(bytes <= 32 * sizeof(Range) && bytes % sizeof(Range) == 0);
        var ranges = new ReadOnlySpan<Range>(output, (int)(bytes / sizeof(Range))).ToArray();
        long last = 0;
        foreach (var range in ranges)
        {
            Assert.True(range.Offset >= last && range.Length > 0 && checked(range.Offset + range.Length) <= input.Length);
            last = range.Offset + range.Length;
        }
        Assert.True(GetInfo(stream.SafeFileHandle, 1, out StandardInfo standard, (uint)sizeof(StandardInfo)));
        Assert.Equal(stream.Length, standard.EndOfFile);
        return new(stream.Length, standard.AllocationSize, File.GetAttributes(stream.SafeFileHandle), WindowsCompression.Query(stream.SafeFileHandle).Native,
            ranges, Convert.ToHexString(SHA256.HashData(stream)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Range(long Offset, long Length);

    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInfo
    {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending, Directory;
    }

    private sealed record StreamObservation(long Length, long Allocation, FileAttributes Attributes, ushort? Compression, Range[] Ranges, string Digest);
    private sealed record Observation(FileIdentity Identity, FileAttributes Attributes, StreamObservation Main, StreamObservation Named);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Control(SafeFileHandle file, uint code, void* input, uint inputBytes, void* output,
        uint outputBytes, out uint returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetInfo(SafeFileHandle file, int informationClass, out StandardInfo result, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector,
        out uint freeClusters, out uint totalClusters);
}
