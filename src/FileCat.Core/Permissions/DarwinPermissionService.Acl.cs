using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class DarwinPermissionService
{
    private const int ExtendedAclType = 0x100, FileSecurityAclProperty = 5;
    private const int FirstAclEntry = 0, NextAclEntry = -1;

    internal static bool CanEditAcl(PermissionSnapshot snapshot) => snapshot.Capabilities.Any(capability => capability is
        { Section: PermissionSections.Access, Action: PermissionAction.Change, Availability: PermissionAvailability.Available });

    internal static PermissionSnapshot PreviewAcl(PermissionSnapshot snapshot, DarwinAclDelta change)
        => ProjectAcl(snapshot, change).Snapshot;

    private static (PermissionSnapshot Snapshot, byte[] Payload) ProjectAcl(PermissionSnapshot snapshot, DarwinAclDelta change, bool verifyExchange = true)
    {
        if (!CanEditAcl(snapshot)) throw new NotSupportedException("An ACL edit requires a qualified Darwin binding.");
        if ((snapshot.Flags.Value.GetValueOrDefault().Bits & (UserProtection | SystemProtection)) != 0)
            throw new NotSupportedException(ActiveProtectionRefusal);
        bool directory = snapshot.Binding!.Identity.Kind is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot;
        var baseline = snapshot.Access.Value?.Acl as DarwinAcl;
        string authority = snapshot.Subject.Authority.Id;
        var payload = DarwinPermissionAcl.Change(baseline, change, authority, directory);
        var expected = ProjectAclPayload(snapshot, payload, verifyExchange);
        return (expected, payload);
    }

    private static PermissionSnapshot ProjectAclPayload(PermissionSnapshot snapshot, ReadOnlySpan<byte> payload, bool verifyExchange = true)
    {
        var acl = DarwinPermissionAcl.Decode(payload, snapshot.Subject.Authority.Id);
        var expected = snapshot with
        {
            Access = snapshot.Access with
            {
                Status = acl is null ? PermissionReadStatus.Absent : PermissionReadStatus.Present,
                Value = acl is null ? null : new PermissionAclValue(acl)
            }
        };
        if (verifyExchange && snapshot.Binding!.DarwinRoute == DarwinBindingRoute.Descriptor) CheckExchangeAdmission(expected);
        return expected;
    }

    private static ApplyDarwinAcl PrepareAcl(PermissionSnapshot original, DarwinAclDelta change, byte[] projected)
    {
        if (change is RemoveDarwinAcl) return new(ImmutableArray.Create(projected), Remove: true);
        var bytes = original.Access.Value?.Acl is DarwinAcl acl ? acl.FileSecurity.AsSpan() : DarwinPermissionAcl.Header(0);
        nint clone = CloneAcl(bytes);
        try
        {
            switch (change)
            {
                case AddDarwinAce add:
                    CheckAclCall(CreateAclEntry(ref clone, out nint inserted, add.Index));
                    CopyProjectedEntry(inserted, projected, add.Index);
                    break;
                case ChangeDarwinAce edit:
                    nint selected = FindAclEntry(clone, edit.Index);
                    if (edit.Replacement is null) CheckAclCall(DeleteAclEntry(clone, selected));
                    else CopyProjectedEntry(selected, projected, edit.Index);
                    break;
                default:
                    throw new InvalidOperationException("The ACL edit needs one reviewed occurrence.");
            }
            return VerifyNativeAcl(clone, projected);
        }
        finally { ReleaseAcl(clone); }
    }

    private static ApplyDarwinAcl VerifyNativeAcl(nint clone, byte[] projected)
    {
        var roundTrip = new byte[projected.Length];
        fixed (byte* buffer = roundTrip)
            if (NativeCopyExternalAcl(buffer, clone, roundTrip.Length) != roundTrip.Length || !roundTrip.AsSpan().SequenceEqual(projected))
                throw new NotSupportedException("Darwin's native ACL clone differs from the reviewed ACL.");
        return new(ImmutableArray.Create(roundTrip), Remove: false);
    }

    private static string DescribeAclCall(DarwinAclDelta acl, bool descriptor)
    {
        string action = acl is RemoveDarwinAcl ? "remove extended ACL" : "edit ordered ACL";
        return descriptor
            ? acl is RemoveDarwinAcl ? $"fchmodx_np {action} (retained descriptor)" : $"acl_set_fd_np {action} (retained descriptor)"
            : $"setattrlistat {action} (checked parent and name)";
    }

    private static nint CloneAcl(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* buffer = bytes)
        {
            nint acl = NativeCopyInternalAcl(buffer);
            if (acl == 0) CheckAclCall(-1);
            return acl;
        }
    }

    private static void CopyProjectedEntry(nint entry, byte[] projected, int index)
    {
        nint source = CloneAcl(DarwinPermissionAcl.SingleEntryPayload(projected, index));
        try { CheckAclCall(CopyAclEntry(entry, FindAclEntry(source, 0))); }
        finally { ReleaseAcl(source); }
    }

    private static nint FindAclEntry(nint acl, int index)
    {
        CheckAclCall(GetAclEntry(acl, FirstAclEntry, out nint entry));
        for (int i = 0; i < index; i++) CheckAclCall(GetAclEntry(acl, NextAclEntry, out entry));
        return entry;
    }

    private static void CheckAclCall(int result)
    {
        if (result != 0) throw new IOException($"The native Darwin ACL could not be prepared (errno {Marshal.GetLastPInvokeError()}).");
    }

    private static int SetAcl(SafeFileHandle handle, string? leaf, ApplyDarwinAcl change)
    {
        if (change.FileSecurity.IsDefaultOrEmpty || change.FileSecurity.Length > DarwinPermissionAcl.NativePayloadLimit)
            throw new InvalidOperationException("The reviewed Darwin ACL payload exceeds the native bounds.");
        if (leaf is not null)
        {
            Span<byte> values = stackalloc byte[8 + change.FileSecurity.Length];
            BinaryPrimitives.WriteInt32LittleEndian(values, 8);
            BinaryPrimitives.WriteInt32LittleEndian(values[4..], change.FileSecurity.Length);
            change.FileSecurity.AsSpan().CopyTo(values[8..]);
            var attributes = new AttrList { Count = 5, Common = ExtendedSecurity };
            fixed (byte* data = values) return SetAttributesAt(handle, leaf, &attributes, data, (nuint)values.Length, FsNoFollowAny);
        }

        if (change.Remove)
        {
            nint security = NewFileSecurity();
            if (security == 0) return -1;
            try
            {
                const int removeAclSentinel = 1; // _FILESEC_REMOVE_ACL is passed in place of the property pointer.
                if (SetFileSecurityProperty(security, FileSecurityAclProperty, removeAclSentinel) != 0) return -1;
                return ChangeFileSecurity(handle, security);
            }
            finally { FreeFileSecurity(security); }
        }

        fixed (byte* buffer = change.FileSecurity.AsSpan())
        {
            nint acl = NativeCopyInternalAcl(buffer);
            if (acl == 0) return -1;
            try { return SetNativeAcl(handle, acl, ExtendedAclType); }
            finally { ReleaseAcl(acl); }
        }
    }

    [LibraryImport("libc", EntryPoint = "acl_copy_int_native", SetLastError = true)]
    private static partial nint NativeCopyInternalAcl(void* bytes);
    [LibraryImport("libc", EntryPoint = "acl_copy_ext_native", SetLastError = true)]
    private static partial nint NativeCopyExternalAcl(void* bytes, nint acl, nint size);
    [LibraryImport("libc", EntryPoint = "acl_get_entry", SetLastError = true)]
    private static partial int GetAclEntry(nint acl, int index, out nint entry);
    [LibraryImport("libc", EntryPoint = "acl_create_entry_np", SetLastError = true)]
    private static partial int CreateAclEntry(ref nint acl, out nint entry, int index);
    [LibraryImport("libc", EntryPoint = "acl_delete_entry", SetLastError = true)]
    private static partial int DeleteAclEntry(nint acl, nint entry);
    [LibraryImport("libc", EntryPoint = "acl_copy_entry", SetLastError = true)]
    private static partial int CopyAclEntry(nint destination, nint source);
    [LibraryImport("libc", EntryPoint = "acl_free")]
    private static partial int ReleaseAcl(nint acl);
    [LibraryImport("libc", EntryPoint = "acl_set_fd_np", SetLastError = true)]
    private static partial int SetNativeAcl(SafeFileHandle handle, nint acl, int type);
    [LibraryImport("libc", EntryPoint = "filesec_init", SetLastError = true)]
    private static partial nint NewFileSecurity();
    [LibraryImport("libc", EntryPoint = "filesec_set_property", SetLastError = true)]
    private static partial int SetFileSecurityProperty(nint security, int property, nint value);
    [LibraryImport("libc", EntryPoint = "filesec_free")]
    private static partial void FreeFileSecurity(nint security);
    [LibraryImport("libc", EntryPoint = "fchmodx_np", SetLastError = true)]
    private static partial int ChangeFileSecurity(SafeFileHandle handle, nint security);
}
