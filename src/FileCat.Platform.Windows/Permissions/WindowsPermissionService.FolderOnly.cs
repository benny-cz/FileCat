using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal sealed unsafe partial class WindowsPermissionService
{
    internal bool CanReviewFolderOnly(PermissionSnapshot snapshot) => _qualifyFolderOnly &&
        IsReviewableNtfsFolder(snapshot) && snapshot.Binding!.Identity.Kind == PermissionObjectKind.Directory &&
        TryGetUnprotectedDacl(snapshot, out _, out _);

    internal static PermissionSnapshot PreviewFolderOnly(PermissionSnapshot original, PermissionDelta change) =>
        ProjectFolderOnly(original, change).Expected;

    internal (ReviewedPermissionOperation Review, PermissionSnapshot Expected) PrepareFolderOnly(PermissionInspection inspection,
        ItemRef source, PermissionDraft draft, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!CanReviewFolderOnly(inspection.Snapshot) || draft.Scope != PermissionScope.Objects ||
            draft.Changes is not [AddWindowsAce or ChangeWindowsAce] || source.Kind != EntryKind.Directory ||
            source.FileSystemPath != inspection.Snapshot.Subject.RawLocator)
            throw new NotSupportedException("Review one access rule on the original unprotected local NTFS folder.");
        return PrepareChange(inspection, source, draft, null, ct, folderOnly: true);
    }

    private static (PermissionSnapshot Expected, uint Section, ImmutableArray<byte> Bytes) ProjectFolderOnly(
        PermissionSnapshot original, PermissionDelta change, bool ensureRule = false)
    {
        if (change is not (AddWindowsAce or ChangeWindowsAce) ||
            !TryGetUnprotectedDacl(original, out _, out var control))
            throw new NotSupportedException("This folder-only route requires an unprotected DACL with automatic inheritance.");
        var (expected, section, bytes) = Project(original, change, directory: true, ensureRule: ensureRule);
        if (expected.Access.Value?.Acl is not WindowsDacl { Revision: 2 } projected || !HasSimpleFolderDacl(projected) ||
            WindowsPermissionAclPatch.HasRepeatedExplicitRules(projected))
            throw new NotSupportedException("The folder-only setter requires a simple DACL whose explicit rules can keep their reviewed order.");
        return (expected, section, ObjectDaclDescriptor(control, bytes, original.Subject.Authority.Id));
    }

    private static bool TryGetUnprotectedDacl(PermissionSnapshot snapshot, out WindowsDacl acl, out WindowsAclControl control)
    {
        acl = null!;
        control = default;
        if (snapshot.Inheritance.Value is not { IsProtected: false, IsAutoInherited: true } known ||
            (known.Flags & (DaclAutoInheritRequest | DaclDefaulted)) != 0 ||
            snapshot.Access.Value?.Acl is not WindowsDacl { Form: WindowsDaclForm.Entries, Revision: 2 } baseline ||
            !HasSimpleFolderDacl(baseline)) return false;
        acl = baseline;
        control = known;
        return true;
    }

    private static ImmutableArray<byte> ObjectDaclDescriptor(WindowsAclControl control, ImmutableArray<byte> bytes, string authority)
    {
        // NTFS otherwise clears the stored SE_DACL_AUTO_INHERITED bit. This input bit is not part of the expected result.
        return WindowsPermissionDescriptor.ProjectDacl(control with { Flags = (ushort)(control.Flags | DaclAutoInheritRequest) },
            bytes, authority).SecurityDescriptor;
    }

    private static bool HasSimpleFolderDacl(WindowsDacl dacl)
    {
        int lastBlock = -1;
        foreach (var entry in dacl.Entries)
        {
            if (entry.Kind == WindowsAceKind.Opaque || (entry.Flags & ~0x1f) != 0 ||
                entry.Mask is not uint mask || (mask & ~WindowsFileAccessRights.Known) != 0) return false;
            int block = (entry.Flags & InheritedAce) != 0 ? 2 : entry.Kind == WindowsAceKind.Deny ? 0 : 1;
            if (block < lastBlock) return false;
            lastBlock = block;
        }
        return true;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetSecurityObject(SafeFileHandle handle, uint section, byte* descriptor);
}
