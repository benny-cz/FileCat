using System.Buffers.Binary;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

// A DACL stored without SE_DACL_AUTO_INHERITED (see HasLegacyDacl) reads differently in Windows tools: Explorer and
// GetSecurityInfo convert it against the parent with ConvertToAutoInheritPrivateObjectSecurity. FileCat shows that reading
// beside the stored entries and offers it as an explicit, reviewed conversion to automatic form, which is the only way to
// reach native propagation on a legacy folder. The conversion stores the reviewed reading as a descriptor, so the result is
// exactly what the review showed, and it requires the parent to still produce that reading.
internal sealed unsafe partial class WindowsPermissionService
{
    internal bool CanReviewInheritanceConversion(PermissionSnapshot snapshot) =>
        snapshot.WindowsReading is not null && snapshot.Inheritance.Value is { IsAutoInherited: false } &&
        snapshot.Access.Value?.Acl is WindowsDacl { Form: WindowsDaclForm.Entries } && snapshot.Binding?.Identity.Kind switch
        {
            PermissionObjectKind.File => EditRefusal(snapshot, QualifiedWrites) is null,
            PermissionObjectKind.Directory => _qualifyFolderOnly && IsReviewableNtfsFolder(snapshot),
            _ => false
        };

    internal (ReviewedPermissionOperation Review, PermissionSnapshot Expected) PrepareInheritanceConversion(PermissionInspection inspection,
        ItemRef source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var original = inspection.Snapshot;
        if (!CanReviewInheritanceConversion(original) || original.Subject.RawLocator != source.FileSystemPath)
            throw new NotSupportedException("Converting to automatic inheritance requires a reviewed legacy DACL on the original file or folder.");
        var reviewed = original.WindowsReading!;
        // The parent must still produce the reading the review shows.
        var (root, names) = SplitLocalPath(original.Subject.RawLocator);
        var parent = ReadVerifiedParent(original, root, names, ct);
        if (parent.Identity != reviewed.ParentIdentity || parent.Revision != reviewed.ParentRevision ||
            !ReadingBytes(ConvertToAutomatic(original, parent)).SequenceEqual(ReadingBytes(reviewed)))
            throw new PermissionBindingConflictException("The parent folder's rules changed. Inspect the item again.");

        string authority = original.Subject.Authority.Id;
        var control = original.Inheritance.Value!.Value;
        ushort dacl = (ushort)(reviewed.Control.Flags & (DaclProtected | DaclAutoInherited));
        var expectedControl = control with
        {
            Flags = (ushort)((control.Flags & ~(DaclProtected | DaclAutoInherited | DaclAutoInheritRequest | DaclDefaulted)) | dacl | DaclAutoInherited)
        };
        var aclBytes = AclBytes(reviewed.Acl);
        var expectedDacl = WindowsPermissionDescriptor.ProjectDacl(expectedControl, aclBytes, authority);
        var expected = original with
        {
            Access = original.Access with { Value = new PermissionAclValue(expectedDacl) },
            Inheritance = original.Inheritance with { Value = expectedControl }
        };
        var before = RequireRevision(original);
        var after = RequireRevision(expected);
        if (before == after) throw new NotSupportedException("The stored DACL already reads this way.");
        var change = new ConvertWindowsInheritance();
        var component = new ReviewedPermissionComponent(change, before, after, "Windows NtSetSecurityObject inheritance conversion");
        var mutation = BindDiscretionaryReview(inspection, before, WindowsPermissionToken.CurrentStamp(), ct, WriteDac, change, DaclInformation,
            ObjectDaclDescriptor(expectedControl, aclBytes, authority), descriptorWrite: true);
        try { return (new(PermissionScope.Objects, [new(source, original.Subject, original.Binding!, [component], after, mutation)]), expected); }
        catch { mutation.Dispose(); throw; }
    }

    // Inspection: the reading of a legacy DACL on a file or folder below a readable parent, or none.
    private static PermissionSnapshot WithInheritedReading(PermissionSnapshot snapshot, string root, string[] names, CancellationToken ct)
    {
        if (names.Length == 0 || snapshot.Inheritance.Value is not { IsAutoInherited: false } ||
            snapshot.Access.Value?.Acl is not WindowsDacl { Form: WindowsDaclForm.Entries } || snapshot.Owner.Value is null || snapshot.Group.Value is null)
            return snapshot;
        try
        {
            var parent = ReadVerifiedParent(snapshot, root, names, ct);
            return snapshot with { WindowsReading = ConvertToAutomatic(snapshot, parent) };
        }
        catch (Exception error) when (error is ReadFailure or IOException or Win32Exception or UnauthorizedAccessException or NotSupportedException or
            PermissionBindingConflictException or InvalidDataException)
        {
            return snapshot; // Without a verified parent there is no reading to show or convert to.
        }
    }

    // The parent of the inspected object, read with READ_CONTROL through a fresh walk that must pass the same ancestors.
    private static InheritanceParent ReadVerifiedParent(PermissionSnapshot snapshot, string root, string[] names, CancellationToken ct)
    {
        var subject = snapshot.Subject;
        var binding = snapshot.Binding ?? throw new NotSupportedException("The inspected object has no binding.");
        string? locator = Path.GetDirectoryName(subject.RawLocator.TrimEnd('\\'));
        if (names.Length == 0 || string.IsNullOrEmpty(locator)) throw new NotSupportedException("The original parent directory path is unavailable.");
        using var parent = Walk(root, names[..^1], subject.Authority, ct, out var grandparent, out var chain, out var parentBinding,
            leafReadAccess: ReadControl, allowRefs: binding.FileSystem == "ReFS", allowRemovable: binding.RemovableMedia);
        grandparent?.Dispose();
        if (parentBinding.Identity.MountInstance != binding.Identity.MountInstance)
            throw new PermissionBindingConflictException("The parent folder is on another volume.");
        var snapshotParent = PermissionSnapshots.Create(new(Location.FileSystem(locator), locator, locator, subject.Authority,
            parentBinding.Identity.Kind, PermissionLinkPolicy.NoFollow), PermissionModel.WindowsDescriptor, parentBinding, DiscretionarySections,
            DateTimeOffset.UtcNow, Source);
        snapshotParent = ReadHeldDescriptor(snapshotParent, DiscretionarySections, parent, ct);
        if (Screen(parent, parentBinding.Identity.MountInstance, parentBinding.Identity.Kind == PermissionObjectKind.VolumeRoot,
                subject.Authority, ct) != chain[^1])
            throw new PermissionBindingConflictException("The parent folder changed during its ACL read.");
        if (snapshotParent.Access.Value?.Acl is not WindowsDacl { Form: WindowsDaclForm.Entries } acl ||
            PermissionRevision.Read(snapshotParent, DiscretionarySections) is not { } revision)
            throw new NotSupportedException("A complete parent DACL is required to read the legacy DACL as Windows tools do.");
        return new(parentBinding.Identity, revision, acl, locator);
    }

    private static WindowsInheritedReading ConvertToAutomatic(PermissionSnapshot snapshot, InheritanceParent parent)
    {
        var current = (WindowsDacl)snapshot.Access.Value!.Value.Acl;
        string authority = snapshot.Subject.Authority.Id;
        bool directory = snapshot.Binding?.Identity.Kind == PermissionObjectKind.Directory;
        var mapping = new FileGenericMapping { Read = 0x00120089, Write = 0x00120116, Execute = 0x001200a0, All = 0x001f01ff };
        nint converted = 0;
        try
        {
            // An in-memory conversion of two descriptors; no object is opened or changed.
            fixed (byte* parentDescriptor = parent.Acl.SecurityDescriptor.AsSpan())
            fixed (byte* currentDescriptor = current.SecurityDescriptor.AsSpan())
                if (!ConvertToAutoInheritPrivateObjectSecurity(parentDescriptor, currentDescriptor, out converted, null, directory, &mapping))
                    throw new IOException("Windows could not read the legacy DACL against its parent.", new Win32Exception(Marshal.GetLastPInvokeError()));
            if (converted == 0 || !IsValidSecurityDescriptor(converted))
                throw new IOException("Windows returned an invalid reading of the legacy DACL.");
            uint length = GetSecurityDescriptorLength(converted);
            if (length is < 20 or > PermissionSnapshots.MaxSectionBytes)
                throw new IOException("The reading exceeds the descriptor limit.");
            var raw = new ReadOnlySpan<byte>((void*)converted, (int)length);
            var acl = WindowsPermissionDescriptor.Dacl(raw, authority);
            if (acl.Form != WindowsDaclForm.Entries) throw new NotSupportedException("Windows read the legacy DACL without entries.");
            return new(parent.Identity, parent.Revision, WindowsPermissionDescriptor.Control(raw), acl);
        }
        finally { if (converted != 0) _ = DestroyPrivateObjectSecurity(ref converted); }
    }

    private static ImmutableArray<byte> AclBytes(WindowsDacl acl)
    {
        var descriptor = acl.SecurityDescriptor.AsSpan();
        int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(descriptor[16..]));
        if (offset == 0) throw new InvalidDataException("The reading has no DACL.");
        return ImmutableArray.Create(descriptor.Slice(offset, BinaryPrimitives.ReadUInt16LittleEndian(descriptor[(offset + 2)..])).ToArray());
    }

    private static byte[] ReadingBytes(WindowsInheritedReading reading) =>
        [.. BitConverter.GetBytes((ushort)(reading.Control.Flags & (DaclProtected | DaclAutoInherited))), .. AclBytes(reading.Acl)];

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertToAutoInheritPrivateObjectSecurity(byte* parent, byte* current, out nint converted, Guid* objectType,
        [MarshalAs(UnmanagedType.U1)] bool isDirectory, FileGenericMapping* mapping);
}
