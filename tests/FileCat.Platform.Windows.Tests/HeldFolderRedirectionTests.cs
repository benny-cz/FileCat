using System.Runtime.InteropServices;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// A held destination folder cannot be renamed or replaced, but while it is empty a standard user can make it a mount
/// point in place, which keeps its name (review finding F-H8 of f134ba37). The adapter tells, through the held handle, that
/// it became one; and once anything is in a folder, it cannot become one at all.
/// </summary>
public sealed partial class HeldFolderRedirectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "filecat-held-redirect-" + Guid.NewGuid().ToString("N"));

    public HeldFolderRedirectionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // The mount point first, as a link, so that deleting the folder never goes through it.
        string held = Path.Combine(_root, "held");
        if (Directory.Exists(held)) Directory.Delete(held);
        Directory.Delete(_root, recursive: true);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize, void* output,
        uint outputSize, out uint returned, nint overlapped);

    // Makes an existing folder a mount point to the target in place, sharing it as another process could; the error, or 0.
    private static unsafe int MakeMountPoint(string folder, string target)
    {
        using var handle = CreateFile(folder, 0x40000000 /* GENERIC_WRITE */, 7, 0, 3 /* OPEN_EXISTING */,
            0x02000000 | 0x00200000 /* backup semantics, open reparse point */, 0);
        if (handle.IsInvalid) return Marshal.GetLastPInvokeError();
        string substitute = @"\??\" + target.TrimEnd('\\'), print = target.TrimEnd('\\');
        int subBytes = substitute.Length * 2, printBytes = print.Length * 2, dataLength = 8 + subBytes + 2 + printBytes + 2;
        var buffer = new byte[8 + dataLength];
        fixed (byte* b = buffer)
        {
            *(uint*)b = 0xA0000003; // IO_REPARSE_TAG_MOUNT_POINT
            *(ushort*)(b + 4) = (ushort)dataLength;
            *(ushort*)(b + 10) = (ushort)subBytes;
            *(ushort*)(b + 12) = (ushort)(subBytes + 2);
            *(ushort*)(b + 14) = (ushort)printBytes;
            fixed (char* s = substitute) Buffer.MemoryCopy(s, b + 16, subBytes, subBytes);
            fixed (char* p = print) Buffer.MemoryCopy(p, b + 16 + subBytes + 2, printBytes, printBytes);
            return DeviceIoControl(handle, 0x000900A4 /* FSCTL_SET_REPARSE_POINT */, b, (uint)buffer.Length, null, 0, out _, 0)
                ? 0 : Marshal.GetLastPInvokeError();
        }
    }

    [Fact]
    public void A_held_folder_made_a_mount_point_in_place_is_told_and_one_with_a_file_in_it_cannot_be_made_one()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Mount points are Windows'.");
        string target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        string held = Directory.CreateDirectory(Path.Combine(_root, "held")).FullName;
        var ops = new WindowsFileOperations();
        using (var lease = LocalIconFileLease.TryOpenDirectory(held, LeaseLinks.Refuse))
        {
            Assert.NotNull(lease);
            Assert.False(ops.HeldDirectoryRedirected(lease));
            Assert.Equal(0, MakeMountPoint(held, target));
            Assert.True(ops.HeldDirectoryRedirected(lease));
        }

        string full = Directory.CreateDirectory(Path.Combine(_root, "full")).FullName;
        File.WriteAllText(Path.Combine(full, "staged.tmp"), "staged");
        Assert.Equal(145 /* ERROR_DIR_NOT_EMPTY */, MakeMountPoint(full, target));
    }
}
