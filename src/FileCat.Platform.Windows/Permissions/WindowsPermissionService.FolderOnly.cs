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

    internal bool CanReviewVolumeRoot(PermissionSnapshot snapshot) => _qualifyVolumeRootWrites &&
        IsReviewableNtfsFolder(snapshot) && snapshot.Binding!.Identity.Kind == PermissionObjectKind.VolumeRoot &&
        TryGetVolumeRootDacl(snapshot, out _);

    internal static PermissionSnapshot PreviewFolderOnly(PermissionSnapshot original, PermissionDelta change) =>
        ProjectFolderOnly(original, change).Expected;

    internal (ReviewedPermissionOperation Review, PermissionSnapshot Expected) PrepareFolderOnly(PermissionInspection inspection,
        ItemRef source, PermissionDraft draft, CancellationToken ct, bool approveVolumeRoot = false)
    {
        ct.ThrowIfCancellationRequested();
        bool available = approveVolumeRoot ? CanReviewVolumeRoot(inspection.Snapshot) : CanReviewFolderOnly(inspection.Snapshot);
        if (!available || draft.Scope != PermissionScope.Objects ||
            draft.Changes is not [AddWindowsAce or ChangeWindowsAce] || source.Kind != EntryKind.Directory ||
            source.FileSystemPath != inspection.Snapshot.Subject.RawLocator)
            throw new NotSupportedException("Review one qualified access-rule change on the original NTFS directory. A volume root requires separate approval.");
        return PrepareChange(inspection, source, draft, null, ct, folderOnly: true);
    }

    private static (PermissionSnapshot Expected, uint Section, ImmutableArray<byte> Bytes) ProjectFolderOnly(
        PermissionSnapshot original, PermissionDelta change, bool ensureRule = false)
    {
        bool root = original.Binding?.Identity.Kind == PermissionObjectKind.VolumeRoot;
        bool available = root ? TryGetVolumeRootDacl(original, out var control) : TryGetUnprotectedDacl(original, out _, out control);
        if (change is not (AddWindowsAce or ChangeWindowsAce) || !available)
            throw new NotSupportedException("This directory requires a qualified DACL with automatic inheritance.");
        var (expected, section, bytes) = Project(original, change, directory: true, ensureRule: ensureRule);
        if (expected.Access.Value?.Acl is not WindowsDacl { Revision: 2 } projected || !HasSimpleFolderDacl(projected, volumeRoot: root) ||
            WindowsPermissionAclPatch.HasRepeatedExplicitRules(projected))
            throw new NotSupportedException("The folder-only setter requires a simple DACL whose explicit rules can keep their reviewed order.");
        return (expected, section, ObjectDaclDescriptor(control, bytes, original.Subject.Authority.Id));
    }

    private static bool TryGetVolumeRootDacl(PermissionSnapshot snapshot, out WindowsAclControl control) =>
        TryGetSimpleAutoInheritedDacl(snapshot, out var acl, out control, volumeRoot: true) &&
        acl.Entries.All(entry => (entry.Flags & InheritedAce) == 0);

    private static bool TryGetUnprotectedDacl(PermissionSnapshot snapshot, out WindowsDacl acl, out WindowsAclControl control) =>
        TryGetSimpleAutoInheritedDacl(snapshot, out acl, out control) && !control.IsProtected;

    private static bool TryGetSimpleAutoInheritedDacl(PermissionSnapshot snapshot, out WindowsDacl acl, out WindowsAclControl control,
        bool volumeRoot = false)
    {
        acl = null!;
        control = default;
        // A volume root has no parent to inherit from. Formatting leaves its DACL without SE_DACL_AUTO_INHERITED, and the
        // object-only write below keeps whichever state it has.
        if (snapshot.Inheritance.Value is not { } known || !known.IsAutoInherited && !volumeRoot ||
            (known.Flags & (DaclAutoInheritRequest | DaclDefaulted)) != 0 ||
            snapshot.Access.Value?.Acl is not WindowsDacl { Form: WindowsDaclForm.Entries, Revision: 2 } baseline ||
            !HasSimpleFolderDacl(baseline, volumeRoot)) return false;
        acl = baseline;
        control = known;
        return true;
    }

    private static ImmutableArray<byte> ObjectDaclDescriptor(WindowsAclControl control, ImmutableArray<byte> bytes, string authority)
    {
        // NTFS otherwise clears the stored SE_DACL_AUTO_INHERITED bit. This input bit is not part of the expected result.
        // A root without that bit keeps it clear: requesting auto-inheritance there would set it.
        return WindowsPermissionDescriptor.ProjectDacl(control.IsAutoInherited
                ? control with { Flags = (ushort)(control.Flags | DaclAutoInheritRequest) } : control,
            bytes, authority).SecurityDescriptor;
    }

    private static bool HasSimpleFolderDacl(WindowsDacl dacl, bool volumeRoot = false)
    {
        int lastBlock = -1;
        foreach (var entry in dacl.Entries)
        {
            if (entry.Kind == WindowsAceKind.Opaque || (entry.Flags & ~0x1f) != 0 || entry.Mask is not uint mask) return false;
            // NTFS format seeds root allow ACEs with generic masks for inheritance only.
            const byte inheritOnlyAce = 0x08;
            uint qualifiedMask = volumeRoot && entry.Kind == WindowsAceKind.Allow && entry.Flags == (InheritToChildren | inheritOnlyAce)
                ? WindowsFileAccessRights.MapGeneric(mask) : mask;
            if ((qualifiedMask & ~WindowsFileAccessRights.Known) != 0) return false;
            int block = (entry.Flags & InheritedAce) != 0 ? 2 : entry.Kind == WindowsAceKind.Deny ? 0 : 1;
            if (block < lastBlock) return false;
            lastBlock = block;
        }
        return true;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetSecurityObject(SafeFileHandle handle, uint section, byte* descriptor);
}
