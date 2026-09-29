using System.Runtime.InteropServices;
using System.Text;

namespace FileCat.Core.HiddenData;

/// <summary>
/// Extended attributes on Linux (every namespace this user may read) and macOS (with the resource fork), through libc,
/// never following a link (D-55).
/// </summary>
public sealed partial class UnixHiddenData : IHiddenData
{
    private const int MacNoFollow = 0x0001;
    private const string ResourceForkName = "com.apple.ResourceFork";
    private const int MaxValue = 16 << 20;

    public bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public IReadOnlyList<HiddenItem> List(string path)
    {
        nint size = ListNames(path, null, 0);
        if (size < 0) throw Error(path);
        if (size == 0) return [];
        var buffer = new byte[size];
        size = ListNames(path, buffer, size);
        if (size < 0) throw Error(path);
        var items = new List<HiddenItem>();
        foreach (string name in Encoding.UTF8.GetString(buffer, 0, (int)size).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            nint length = GetValue(path, name, null, 0);
            var kind = OperatingSystem.IsMacOS() && name == ResourceForkName ? HiddenKind.ResourceFork : HiddenKind.Attribute;
            items.Add(new HiddenItem(name, kind, length));
        }
        return items;
    }

    public byte[] Read(string path, HiddenItem item, int max)
    {
        if (item.Kind == HiddenKind.ResourceFork && OperatingSystem.IsMacOS())
        {
            using var fork = Open(path, item);
            var head = new byte[Math.Min(max, (int)Math.Min(int.MaxValue, Math.Max(0, item.Size)))];
            int read = fork.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return head[..read];
        }
        nint size = GetValue(path, item.Name, null, 0);
        if (size < 0) throw Error(path);
        var buffer = new byte[Math.Min(size, MaxValue)];
        size = GetValue(path, item.Name, buffer, buffer.Length);
        if (size < 0) throw Error(path);
        return buffer.AsSpan(0, (int)Math.Min(size, max)).ToArray();
    }

    public Stream Open(string path, HiddenItem item)
    {
        // A resource fork is a stream of its own (it may be large): read through its path, not the attribute call.
        if (item.Kind == HiddenKind.ResourceFork && OperatingSystem.IsMacOS())
            return new FileStream(Path.Join(path, "..namedfork", "rsrc"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new MemoryStream(Read(path, item, MaxValue), writable: false);
    }

    public void Delete(string path, HiddenItem item)
    {
        int result = OperatingSystem.IsMacOS() ? MacRemove(path, item.Name, MacNoFollow) : LinuxRemove(path, item.Name);
        if (result != 0) throw Error(path);
    }

    private static nint ListNames(string path, byte[]? buffer, nint size) =>
        OperatingSystem.IsMacOS() ? MacList(path, buffer, size, MacNoFollow) : LinuxList(path, buffer, size);

    private static nint GetValue(string path, string name, byte[]? buffer, nint size) =>
        OperatingSystem.IsMacOS() ? MacGet(path, name, buffer, size, 0, MacNoFollow) : LinuxGet(path, name, buffer, size);

    private static Exception Error(string path)
    {
        int errno = Marshal.GetLastPInvokeError();
        string message = Marshal.GetPInvokeErrorMessage(errno);
        return errno switch
        {
            1 or 13 => new UnauthorizedAccessException($"{path}: {message}"),
            2 => new FileNotFoundException(message, path),
            _ => new IOException($"{path}: {message}", errno),
        };
    }

    [LibraryImport("libc", EntryPoint = "llistxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint LinuxList(string path, byte[]? list, nint size);

    [LibraryImport("libc", EntryPoint = "lgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint LinuxGet(string path, string name, byte[]? value, nint size);

    [LibraryImport("libc", EntryPoint = "lremovexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxRemove(string path, string name);

    [LibraryImport("libc", EntryPoint = "listxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint MacList(string path, byte[]? list, nint size, int options);

    [LibraryImport("libc", EntryPoint = "getxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint MacGet(string path, string name, byte[]? value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "removexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacRemove(string path, string name, int options);
}
