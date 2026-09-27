using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public sealed record RegistryAclInfo(string? OwnerSid, string? GroupSid, int AceCount, string Sddl);

/// <summary>Read-only owner, group, and DACL inspection. SACL is omitted because it requires extra privilege.</summary>
public static partial class RegistryAcl
{
    private const uint OwnerGroupDacl = 0x00000001 | 0x00000002 | 0x00000004;
    private const int InsufficientBuffer = 122;
    private const int MaxDescriptor = 1024 * 1024;

    public static RegistryAclInfo Inspect(Location location)
    {
        using var key = WindowsRegistryProvider.Open(location, false);
        uint length = 0;
        int code = RegGetKeySecurity(key.Handle, OwnerGroupDacl, null, ref length);
        if (code != InsufficientBuffer && code != 0) throw new Win32Exception(code);
        if (length == 0 || length > MaxDescriptor) throw new IOException("Registry security descriptor is unavailable or too large.");
        var bytes = new byte[length];
        code = RegGetKeySecurity(key.Handle, OwnerGroupDacl, bytes, ref length);
        if (code != 0) throw new Win32Exception(code);
        var descriptor = new RawSecurityDescriptor(bytes, 0);
        return new RegistryAclInfo(descriptor.Owner?.Value, descriptor.Group?.Value,
            descriptor.DiscretionaryAcl?.Count ?? 0,
            descriptor.GetSddlForm(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access));
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegGetKeySecurity")]
    private static partial int RegGetKeySecurity(SafeRegistryHandle key, uint information, byte[]? descriptor, ref uint length);
}
