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
        ArgumentOutOfRangeException.ThrowIfNegative(max);
        return ReadBoundAttribute(path, item, max);
    }

    public Stream Open(string path, HiddenItem item)
    {
        return new MemoryStream(Read(path, item, MaxValue), writable: false);
    }

    public void Delete(string path, HiddenItem item)
    {
        using var file = OpenActionFile(path, item);
        var inventory = CollectInventory(path, (buffer, size) => LinuxFdList(file, buffer, size), name => LinuxFdGet(file, name, null, 0), default);
        if (inventory.State != HiddenInventoryState.Complete) throw new IOException("The attribute category could not be completely resolved; refresh required.");
        var current = inventory.Items.SingleOrDefault(i => i.Kind == item.Kind && i.Name == item.Name)
            ?? throw new FileNotFoundException("The exact attribute is missing; refresh required.");
        if (HiddenMetadataRoles.DeleteRefusal(current) is { } reason) throw new NotSupportedException(reason);
        if (LinuxFdRemove(file, current.Name) != 0) throw Error(path);
    }

    private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenActionFile(string path, HiddenItem item)
    {
        if (!OperatingSystem.IsLinux()) throw new NotSupportedException("A held-object attribute value/action adapter is not qualified on this platform.");
        if (item.Kind != HiddenKind.Attribute || item.Name.Length == 0 || item.Name.Contains('\0') ||
            !new UTF8Encoding(false, true).GetString(new UTF8Encoding(false, true).GetBytes(item.Name)).Equals(item.Name, StringComparison.Ordinal))
            throw new NotSupportedException("The attribute category or native name is invalid; refresh required.");
        return OpenInventoryFile(path, default);
    }

    private static byte[] ReadBoundAttribute(string path, HiddenItem item, int max)
    {
        using var file = OpenActionFile(path, item);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            nint size = LinuxFdGet(file, item.Name, null, 0);
            if (size < 0) throw Error(path);
            if (size > MaxValue) throw new NotSupportedException("The attribute exceeds the 16 MiB explicit-value acquisition limit.");
            if (max == 0) return [];
            byte[] value = new byte[(int)size];
            try
            {
                nint got = LinuxFdGet(file, item.Name, value, value.Length);
                if (got < 0 && Marshal.GetLastPInvokeError() == 34 && attempt < 2) continue;
                if (got < 0) throw Error(path);
                if (got > value.Length) throw new IOException("The attribute query returned an invalid length.");
                return value.AsSpan(0, (int)Math.Min(got, max)).ToArray();
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(value); }
        }
        throw new IOException("The attribute kept growing beyond two retries; refresh required.");
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

    [LibraryImport("libc", EntryPoint = "fremovexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxFdRemove(Microsoft.Win32.SafeHandles.SafeFileHandle file, string name);

    [LibraryImport("libc", EntryPoint = "listxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint MacList(string path, byte[]? list, nint size, int options);

    [LibraryImport("libc", EntryPoint = "getxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint MacGet(string path, string name, byte[]? value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "removexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacRemove(string path, string name, int options);
}
