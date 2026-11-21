using System.Security.AccessControl;

namespace FileCat.Platform.Windows.Permissions;

internal enum WindowsFileAccessPreset { Read, ReadAndExecute, Modify, FullControl }

/// <summary>Maps the four generic access bits to the file rights that actually participate in a DACL check.</summary>
internal static class WindowsFileAccessRights
{
    internal const uint Known = (uint)FileSystemRights.FullControl;
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, GenericExecute = 0x20000000, GenericAll = 0x10000000;
    private const uint Synchronize = (uint)FileSystemRights.Synchronize;
    private const uint ReadControl = (uint)FileSystemRights.ReadPermissions;

    internal static uint MapGeneric(uint rights)
    {
        uint result = rights & ~(GenericRead | GenericWrite | GenericExecute | GenericAll);
        if ((rights & GenericRead) != 0) result |= (uint)FileSystemRights.Read | Synchronize;
        if ((rights & GenericWrite) != 0) result |= (uint)FileSystemRights.Write | ReadControl | Synchronize;
        if ((rights & GenericExecute) != 0) result |= (uint)(FileSystemRights.ExecuteFile | FileSystemRights.ReadAttributes) | ReadControl | Synchronize;
        if ((rights & GenericAll) != 0) result |= Known;
        return result;
    }

    internal static uint PresetMask(WindowsFileAccessPreset preset) => preset switch
    {
        WindowsFileAccessPreset.Read => (uint)FileSystemRights.Read | Synchronize,
        WindowsFileAccessPreset.ReadAndExecute => (uint)FileSystemRights.ReadAndExecute | Synchronize,
        WindowsFileAccessPreset.Modify => (uint)FileSystemRights.Modify | Synchronize,
        WindowsFileAccessPreset.FullControl => Known,
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };
}
