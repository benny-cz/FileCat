using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsEfsCopyTests
{
    private const uint SetSparse = 0x000900C4, QueryAllocatedRanges = 0x000940CF;

    [Fact]
    public async Task Native_copy_preserves_efs_and_sparse_holes_in_main_and_named_streams()
    {
        RequireEfs();
        const int mainLength = 8 * 1024 * 1024, namedLength = 4 * 1024 * 1024;
        string root = Directory.CreateTempSubdirectory("filecat-efs-sparse-copy-").FullName;
        try
        {
            Assert.Equal("NTFS", new DriveInfo(Path.GetPathRoot(root)!).DriveFormat);
            string destination = Directory.CreateDirectory(Path.Join(root, "destination")).FullName;
            string source = Path.Join(root, "sparse.bin");
            using (var main = new FileStream(source, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                MarkSparse(main);
                main.SetLength(mainLength);
                main.Position = 0;
                main.Write(RandomNumberGenerator.GetBytes(4096));
                main.Position = mainLength / 2;
                main.Write(RandomNumberGenerator.GetBytes(4096));
            }
            using (var named = new FileStream(source + ":proof", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                MarkSparse(named);
                named.SetLength(namedLength);
                named.Position = namedLength / 4;
                named.Write(RandomNumberGenerator.GetBytes(4096));
                named.Position = namedLength - 4096;
                named.Write(RandomNumberGenerator.GetBytes(4096));
            }
            File.Encrypt(source);
            var beforeMain = ObserveSparseStream(source);
            var beforeNamed = ObserveSparseStream(source + ":proof");
            Assert.True(beforeMain.Attributes.HasFlag(FileAttributes.SparseFile));
            Assert.True(beforeNamed.Attributes.HasFlag(FileAttributes.SparseFile));
            // This Windows EFS volume can report zero FileStandardInfo allocation despite nonempty ranges.
            Assert.True(beforeMain.Ranges.Sum(range => range.Length) < mainLength / 2);
            Assert.True(beforeNamed.Ranges.Sum(range => range.Length) < namedLength / 2);
            WriteSparse("source main", beforeMain);
            WriteSparse("source named", beforeNamed);

            var (job, questions) = await Run(root, new WindowsFileOperations(), JobKind.Copy, [source], destination,
                _ => new(DecisionAction.CancelJob));
            Assert.Empty(questions);
            Assert.Equal(JobState.Completed, job.State);
            string target = Path.Join(destination, "sparse.bin");
            Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            var afterMain = ObserveSparseStream(target);
            var afterNamed = ObserveSparseStream(target + ":proof");
            WriteSparse("copy main", afterMain);
            WriteSparse("copy named", afterNamed);
            AssertSparseCopy(beforeMain, afterMain);
            AssertSparseCopy(beforeNamed, afterNamed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(JobKind.Copy)]
    [InlineData(JobKind.Move)]
    public async Task Native_cross_volume_copy_and_move_to_removable_ntfs_preserve_efs_and_sparse_holes(JobKind kind)
    {
        RequireEfs();
        string? removableRoot = Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_USB_ROOT");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(removableRoot) && Directory.Exists(removableRoot),
            "Requires a prepared removable NTFS destination owned by the native fixture.");
        string removable = Path.GetFullPath(removableRoot!);
        var drive = new DriveInfo(Path.GetPathRoot(removable)!);
        Assert.Equal(DriveType.Removable, drive.DriveType);
        Assert.Equal("NTFS", drive.DriveFormat);
        string root = Directory.CreateTempSubdirectory("filecat-efs-sparse-usb-source-").FullName;
        string destination = Path.Combine(removable, "filecat-efs-sparse-usb-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(removable, Path.GetDirectoryName(destination));
        try
        {
            Directory.CreateDirectory(destination);
            const int mainLength = 8 * 1024 * 1024, namedLength = 4 * 1024 * 1024;
            string source = Path.Combine(root, "sparse-usb.bin");
            using (var main = new FileStream(source, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                MarkSparse(main);
                main.SetLength(mainLength);
                main.Position = 0;
                main.Write(RandomNumberGenerator.GetBytes(4096));
                main.Position = mainLength / 2;
                main.Write(RandomNumberGenerator.GetBytes(4096));
            }
            using (var named = new FileStream(source + ":proof", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                MarkSparse(named);
                named.SetLength(namedLength);
                named.Position = namedLength / 4;
                named.Write(RandomNumberGenerator.GetBytes(4096));
                named.Position = namedLength - 4096;
                named.Write(RandomNumberGenerator.GetBytes(4096));
            }
            File.Encrypt(source);
            string originalId = Assert.IsType<string>(new WindowsFileOperations().GetFileIdentity(source));
            var beforeMain = ObserveSparseStream(source);
            var beforeNamed = ObserveSparseStream(source + ":proof");
            Assert.True(beforeMain.Ranges.Sum(range => range.Length) < mainLength / 2);
            Assert.True(beforeNamed.Ranges.Sum(range => range.Length) < namedLength / 2);
            WriteSparse("usb source main", beforeMain);
            WriteSparse("usb source named", beforeNamed);

            var (job, questions) = await Run(root, new WindowsFileOperations(), kind, [source], destination,
                _ => new(DecisionAction.CancelJob));
            Assert.Empty(questions);
            Assert.Equal(JobState.Completed, job.State);
            string target = Path.Combine(destination, "sparse-usb.bin");
            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            var afterMain = ObserveSparseStream(target);
            var afterNamed = ObserveSparseStream(target + ":proof");
            WriteSparse("usb copy main", afterMain);
            WriteSparse("usb copy named", afterNamed);
            AssertSparseCopy(beforeMain, afterMain);
            AssertSparseCopy(beforeNamed, afterNamed);
            if (kind == JobKind.Move)
            {
                Assert.False(File.Exists(source));
                Assert.False(File.Exists(source + ":proof"));
            }
            else
            {
                Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
                Assert.Equal(originalId, new WindowsFileOperations().GetFileIdentity(source));
                AssertSparseCopy(beforeMain, ObserveSparseStream(source));
                AssertSparseCopy(beforeNamed, ObserveSparseStream(source + ":proof"));
            }
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertSparseCopy(SparseStream before, SparseStream after)
    {
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before.Digest, after.Digest);
        Assert.Equal(before.Ranges, after.Ranges);
        Assert.True(after.Attributes.HasFlag(FileAttributes.SparseFile));
        Assert.True(after.Ranges.Sum(range => range.Length) < after.Length / 2);
    }

    private static void WriteSparse(string label, SparseStream stream) => TestContext.Current.TestOutputHelper!.WriteLine(
        $"{label}: eof={stream.Length} allocated={stream.Allocation} attributes={stream.Attributes} " +
        $"ranges={string.Join(",", stream.Ranges.Select(range => $"{range.Offset}+{range.Length}"))} sha256={stream.Digest}");

    private static unsafe SparseStream ObserveSparseStream(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var query = new SparseRange(0, stream.Length);
        var output = stackalloc SparseRange[32];
        uint rangeSize = (uint)sizeof(SparseRange);
        Assert.True(Control(stream.SafeFileHandle, QueryAllocatedRanges, &query, rangeSize, output,
            32 * rangeSize, out uint bytes, 0), $"Allocated-range query failed with {Marshal.GetLastPInvokeError()}.");
        Assert.True(bytes <= 32 * rangeSize && bytes % rangeSize == 0);
        var ranges = new ReadOnlySpan<SparseRange>(output, (int)(bytes / rangeSize)).ToArray();
        long end = 0;
        foreach (var range in ranges)
        {
            Assert.True(range.Offset >= end && range.Length > 0 && checked(range.Offset + range.Length) <= stream.Length);
            end = range.Offset + range.Length;
        }
        Assert.True(GetInfo(stream.SafeFileHandle, 1, out SparseStandardInfo standard, (uint)sizeof(SparseStandardInfo)));
        Assert.Equal(stream.Length, standard.EndOfFile);
        stream.Position = 0;
        return new(stream.Length, standard.AllocationSize, File.GetAttributes(stream.SafeFileHandle), ranges,
            Convert.ToHexString(SHA256.HashData(stream)));
    }

    private static unsafe void MarkSparse(FileStream stream)
    {
        Assert.True(Control(stream.SafeFileHandle, SetSparse, null, 0, null, 0, out _, 0),
            $"The fixture could not mark its stream sparse ({Marshal.GetLastPInvokeError()}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct SparseRange(long Offset, long Length);

    [StructLayout(LayoutKind.Sequential)]
    private struct SparseStandardInfo
    {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending, Directory;
    }

    private sealed record SparseStream(long Length, long Allocation, FileAttributes Attributes, SparseRange[] Ranges, string Digest);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool Control(SafeFileHandle handle, uint code, void* input, uint inputSize,
        void* output, uint outputSize, out uint returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetInfo(SafeFileHandle handle, int infoClass, out SparseStandardInfo info, uint size);
}
