using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsHiddenData
{
    private static void RefuseManagedObject(SafeFileHandle file)
    {
        uint attributes = NtFile.GetBasic(file).FileAttributes;
        if ((attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0 || (attributes & 0x400000) != 0)
            throw new NotSupportedException("Supplementary values/actions are not qualified for this reparse/offline/provider object.");
        var backing = WindowsStorageBacking.Query(file);
        if (backing.External != false) throw new NotSupportedException(backing.External == true
            ? "This object has filesystem-managed external backing; generic supplementary actions are unavailable."
            : "External-backing state is unavailable; the supplementary action is not qualified. " + backing.Reason);
    }

    private static SafeFileHandle OpenExactStream(string path, HiddenItem requested, uint access, bool deleting = false)
    {
        if (requested.Name.Length == 0 || requested.Name.Length > 32760 || requested.Name.IndexOfAny(['\0', ':', '/', '\\']) >= 0)
            throw new ArgumentException("Invalid native stream name.", nameof(requested));
        using var root = OpenForEas(path, 0x80 | 0x100000);
        RefuseManagedObject(root);
        var inventory = ReadStreamInventory(root, default);
        if (inventory.State != HiddenInventoryState.Complete) throw new IOException("The stream category could not be completely resolved; refresh required.");
        var current = inventory.Items.SingleOrDefault(i => i.Name == requested.Name) ?? throw new FileNotFoundException("The exact stream is missing; refresh required.");
        if (deleting && HiddenMetadataRoles.DeleteRefusal(current) is { } reason) throw new NotSupportedException(reason);
        // Relative ADS acquisition uses the held file object, not a second traversal of its mutable pathname.
        int status = NtFile.TryOpen(root, ":" + current.Name + ":$DATA", access | NtFile.ReadAttributes | NtFile.Synchronize,
            NtFile.ShareAll, NtFile.Open, NtFile.OpenReparsePoint, out var opened, caseSensitive: true);
        if (status < 0) { opened.Dispose(); throw NtFile.Error(status, "Open the held object's exact stream"); }
        try
        {
            if (ProtectedHexFile.Identity(opened) != ProtectedHexFile.Identity(root)) throw new IOException("The stream did not bind to the held source object.");
            RefuseManagedObject(opened);
            return opened;
        }
        catch { opened.Dispose(); throw; }
    }

    private static byte[] EaName(string name)
    {
        if (name.Length is < 1 or > 255 || name.Any(c => c == 0 || c > 255))
            throw new ArgumentException("The EA name cannot round-trip to its native bytes.", nameof(name));
        return Encoding.Latin1.GetBytes(name);
    }

    private static byte[] ReadExactEa(string path, HiddenItem item, int max, OutputEfsObservation? observation = null)
    {
        byte[] name = EaName(item.Name);
        using var file = OpenForEas(path, FileReadEa | 0x80 | 0x100000);
        RefuseManagedObject(file);
        if (observation is not null)
        {
            observation.ObserveOpened(file);
            IHiddenValueOutput.RequireClear(observation);
        }
        byte[] filter = new byte[6 + name.Length];
        filter[4] = (byte)name.Length;
        name.CopyTo(filter, 5);
        byte[] buffer = new byte[128 * 1024];
        try
        {
            int status; IoStatusBlock io;
            fixed (byte* output = buffer)
            fixed (byte* wanted = filter) status = NtQueryEaFile(file, out io, output, (uint)buffer.Length, 1, wanted, (uint)filter.Length, null, 1);
            if (observation is not null)
            {
                observation.Observe(file);
                IHiddenValueOutput.RequireClear(observation);
            }
            if (status < 0) throw Error(RtlNtStatusToDosError(status), path);
            long got = io.Information;
            if (got < 9 || got > buffer.Length) throw new IOException("The EA value query returned an invalid length.");
            var parsed = ParseEaInventory(buffer.AsSpan(0, (int)got));
            if (parsed.State != HiddenInventoryState.Complete || parsed.Items.Length != 1 || parsed.Items[0].Name != item.Name)
                throw new IOException("The EA changed or could not be resolved exactly; refresh required.");
            int start = 9 + buffer[5], length = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(6));
            return buffer.AsSpan(start, Math.Min(length, max)).ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInventoryInfo(SafeFileHandle file, int infoClass, void* buffer, uint size);
}
