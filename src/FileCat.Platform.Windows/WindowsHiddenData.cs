using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.HiddenData;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>
/// NTFS and ReFS alternate data streams, and NTFS extended attributes (EAs), of a file or folder (D-55). EAs are read
/// with NtQueryEaFile, which also returns those only the kernel may write ($KERNEL.*, kept by Smart App Control and
/// AppLocker) and WSL's ($LXUID, $LXGID, $LXMOD, LXATTRB).
/// </summary>
public sealed unsafe partial class WindowsHiddenData : IHiddenData
{
    private const uint FileReadEa = 0x0008, FileWriteEa = 0x0010;
    private const uint ShareAll = 0x7, OpenExisting = 3, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    private const int NoEas = unchecked((int)0xC0000052), NoMoreEas = unchecked((int)0x80000012), EaListInconsistent = unchecked((int)0x80000014);

    public bool IsSupported => true;

    public IReadOnlyList<HiddenItem> List(string path)
    {
        var items = new List<HiddenItem>();
        nint find = FindFirstStream(Long(path), 0, out var data, 0);
        if (find == -1)
        {
            int error = Marshal.GetLastPInvokeError();
            // 38 (end of file) means no streams; 5 is a folder or file the user cannot read.
            if (error is not (38 or 2 or 3)) throw Error(error, path);
        }
        else
        {
            try
            {
                do
                {
                    string name = new string(data.StreamName);
                    const string suffix = ":$DATA";
                    if (name.Length > suffix.Length + 1 && name[0] == ':' && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        items.Add(new HiddenItem(name[1..^suffix.Length], HiddenKind.Stream, data.StreamSize));
                } while (FindNextStream(find, out data));
            }
            finally { FindClose(find); }
        }
        foreach (var (name, value) in ReadEas(path)) items.Add(new HiddenItem(name, HiddenKind.NtfsAttribute, value.Length));
        return items;
    }

    public byte[] Read(string path, HiddenItem item, int max)
    {
        if (item.Kind == HiddenKind.NtfsAttribute)
            return ReadEas(path).FirstOrDefault(e => e.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)).Value is { } value
                ? value[..Math.Min(value.Length, max)]
                : throw new FileNotFoundException($"{path} has no attribute {item.Name}.");
        using var stream = Open(path, item);
        var buffer = new byte[(int)Math.Min(max, Math.Max(0, item.Size))];
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return buffer[..read];
    }

    public Stream Open(string path, HiddenItem item) => item.Kind == HiddenKind.NtfsAttribute
        ? new MemoryStream(Read(path, item, int.MaxValue), writable: false)
        : new FileStream(path + ":" + item.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);

    public void Delete(string path, HiddenItem item)
    {
        if (item.Kind == HiddenKind.Stream)
        {
            File.Delete(path + ":" + item.Name);
            return;
        }
        // An EA written with no value is removed; the kernel's own ($KERNEL.*) refuse.
        using var handle = OpenForEas(path, FileWriteEa);
        byte[] name = Encoding.ASCII.GetBytes(item.Name);
        var buffer = new byte[(9 + name.Length + 1 + 3) & ~3];
        buffer[5] = (byte)name.Length;
        name.CopyTo(buffer, 8);
        fixed (byte* start = buffer)
        {
            int status = NtSetEaFile(handle, out _, start, (uint)buffer.Length);
            if (status < 0) throw Error(RtlNtStatusToDosError(status), path);
        }
    }

    /// <summary>Every EA of the file: its name and its value.</summary>
    private static List<(string Name, byte[] Value)> ReadEas(string path)
    {
        var result = new List<(string, byte[])>();
        SafeFileHandle handle;
        try { handle = OpenForEas(path, FileReadEa); }
        catch (UnauthorizedAccessException) { return result; }
        using (handle)
        {
            var buffer = new byte[128 * 1024];
            fixed (byte* start = buffer)
            {
                int status = NtQueryEaFile(handle, out _, start, (uint)buffer.Length, 0, null, 0, null, 1);
                if (status is NoEas or NoMoreEas || status < 0 && status != EaListInconsistent) return result;
                int at = 0;
                while (at + 8 <= buffer.Length)
                {
                    int next = BitConverter.ToInt32(buffer, at);
                    int nameLength = buffer[at + 5];
                    int valueLength = BitConverter.ToUInt16(buffer, at + 6);
                    if (at + 8 + nameLength + 1 + valueLength > buffer.Length) break;
                    string name = Encoding.ASCII.GetString(buffer, at + 8, nameLength);
                    result.Add((name, buffer.AsSpan(at + 8 + nameLength + 1, valueLength).ToArray()));
                    if (next <= 0) break;
                    at += next;
                }
            }
        }
        return result;
    }

    private static SafeFileHandle OpenForEas(string path, uint access)
    {
        var handle = CreateFile(Long(path), access, ShareAll, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw Error(error, path);
        }
        return handle;
    }

    private static string Long(string path) =>
        path.Length >= 248 && !path.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path
            : path;

    private static Exception Error(int error, string path) => error switch
    {
        5 => new UnauthorizedAccessException($"{path}: {new Win32Exception(error).Message}"),
        2 or 3 => new FileNotFoundException(new Win32Exception(error).Message, path),
        _ => new IOException($"{path}: {new Win32Exception(error).Message}", error),
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindStreamData
    {
        public long StreamSize;
        public fixed char StreamName[296];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstStreamW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindFirstStream(string path, int level, out FindStreamData data, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "FindNextStreamW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindNextStream(nint handle, out FindStreamData data);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public nint Status;
        public nint Information;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryEaFile(SafeFileHandle file, out IoStatusBlock status, byte* buffer, uint length, byte returnSingleEntry,
        void* eaList, uint eaListLength, uint* eaIndex, byte restartScan);

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetEaFile(SafeFileHandle file, out IoStatusBlock status, byte* buffer, uint length);

    [LibraryImport("ntdll.dll")]
    private static partial int RtlNtStatusToDosError(int status);
}
