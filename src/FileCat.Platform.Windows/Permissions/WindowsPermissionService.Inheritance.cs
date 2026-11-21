using System.Buffers.Binary;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Permissions;

internal sealed unsafe partial class WindowsPermissionService
{
    private const ushort DaclProtected = 0x1000, DaclAutoInherited = 0x0400, DaclAutoInheritRequest = 0x0100, DaclDefaulted = 0x0008;
    private const uint ProtectDaclInformation = 0x80000000, UnprotectDaclInformation = 0x20000000;
    private const byte InheritedAce = 0x10, InheritToChildren = 0x03;

    internal sealed record InheritanceParent(PermissionIdentity Identity, PermissionRevision Revision, WindowsDacl Acl, string Locator);

    internal bool CanReviewInheritanceChange(PermissionSnapshot snapshot) =>
        _qualifyInheritanceChanges && CanReviewNativePropagation(snapshot) &&
        snapshot.Binding?.Identity.Kind == PermissionObjectKind.Directory;

    internal static InheritanceParent CaptureInheritanceParent(PermissionSnapshot selected, PropagationBoundary boundary, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var subject = selected.Subject;
        if (selected.Binding != boundary.Root || subject.RawLocator != boundary.Locator || boundary.Ancestors.Length < 2)
            throw new PermissionBindingConflictException("The reviewed folder has no verified inheritance parent.");
        var (root, names) = SplitLocalPath(subject.RawLocator);
        if (names.Length + 1 != boundary.Ancestors.Length)
            throw new PermissionBindingConflictException("The captured folder ancestry does not match its original path.");
        string? locator = Path.GetDirectoryName(subject.RawLocator.TrimEnd('\\'));
        if (string.IsNullOrEmpty(locator))
            throw new NotSupportedException("The original parent directory path is unavailable.");

        try
        {
            using var parent = Walk(root, names[..^1], subject.Authority, ct, out var grandparent, out var chain, out var binding,
                leafReadAccess: ReadControl);
            grandparent?.Dispose();
            if (!chain.Select(stamp => stamp.Identity).SequenceEqual(boundary.Ancestors.Take(boundary.Ancestors.Length - 1)))
                throw new PermissionBindingConflictException("The selected folder's inheritance parent or its ancestry changed.");
            bool volumeRoot = binding.Identity.Kind == PermissionObjectKind.VolumeRoot;
            var snapshot = PermissionSnapshots.Create(new(Location.FileSystem(locator), locator, locator, subject.Authority,
                binding.Identity.Kind, PermissionLinkPolicy.NoFollow), PermissionModel.WindowsDescriptor,
                binding, DiscretionarySections, DateTimeOffset.UtcNow, Source);
            snapshot = ReadHeldDescriptor(snapshot, DiscretionarySections, parent, ct);
            if (Screen(parent, binding.Identity.MountInstance, volumeRoot, subject.Authority, ct) != chain[^1])
                throw new PermissionBindingConflictException("The parent directory changed during its ACL read.");
            if (snapshot.Access.Value?.Acl is not WindowsDacl { Form: WindowsDaclForm.Entries } acl ||
                PermissionRevision.Read(snapshot, DiscretionarySections) is not { } revision)
                throw new NotSupportedException("A complete parent DACL is required to preview inherited rules.");
            return new(binding.Identity, revision, acl, locator);
        }
        catch (ReadFailure error) { throw ReadError(error); }
        catch (Win32Exception error) { throw new IOException("The inheritance parent could not be verified.", error); }
    }

    private static void RequireUnchangedInheritanceParent(PermissionSnapshot selected, PropagationBoundary boundary,
        InheritanceParent reviewed, CancellationToken ct)
    {
        var current = CaptureInheritanceParent(selected, boundary, ct);
        if (current.Identity != reviewed.Identity || current.Revision != reviewed.Revision)
            throw new PermissionBindingConflictException("The parent DACL changed. Review inheritance again.");
    }

    private static (PermissionSnapshot Expected, uint Section, ImmutableArray<byte> Bytes) ProjectInheritance(
        PermissionSnapshot original, WindowsDacl baseline, WindowsAclControl control, ChangeWindowsInheritance change, InheritanceParent? parent)
    {
        bool enable = change.Action == WindowsInheritanceAction.Enable;
        if (control.IsProtected != enable)
            throw new NotSupportedException(enable ? "Windows inheritance is already enabled." : "The DACL is already protected.");
        string authority = original.Subject.Authority.Id;
        var explicitBytes = WindowsPermissionAclPatch.ConvertInheritance(baseline, authority, change.Action);
        var bytes = enable ? MergeParentInheritance(original, explicitBytes, parent ??
            throw new NotSupportedException("A verified parent DACL is required to enable inheritance.")) : explicitBytes;
        ushort retained = (ushort)(control.Flags & ~(DaclProtected | DaclAutoInheritRequest | DaclDefaulted));
        var expectedControl = control with
        {
            Flags = (ushort)(retained | (enable ? DaclAutoInherited : DaclProtected))
        };
        var expectedDacl = WindowsPermissionDescriptor.ProjectDacl(expectedControl, bytes, authority);
        // NTFS can collapse repeated explicit ACEs during a protection change. The review promises each occurrence.
        if (expectedDacl.Entries.Where(entry => entry.Kind != WindowsAceKind.Opaque && (entry.Flags & InheritedAce) == 0)
            .GroupBy(entry => (entry.Type, entry.Flags, entry.Principal)).Any(rules => rules.Skip(1).Any()))
            throw new NotSupportedException("Windows may merge repeated access rules during this inheritance change. FileCat cannot preserve each reviewed rule.");
        return (original with
        {
            Access = original.Access with { Value = new PermissionAclValue(expectedDacl) },
            Inheritance = original.Inheritance with { Value = expectedControl }
        }, DaclInformation | (enable ? UnprotectDaclInformation : ProtectDaclInformation), bytes);
    }

    private static ImmutableArray<byte> MergeParentInheritance(PermissionSnapshot original, ImmutableArray<byte> explicitBytes, InheritanceParent parent)
    {
        if (parent.Acl.Entries.Any(entry => entry.Kind == WindowsAceKind.Opaque && (entry.Flags & InheritToChildren) != 0))
            throw new NotSupportedException("An inheritable parent rule uses a format FileCat cannot project.");
        if (original.Owner.Value is not { } owner || original.Group.Value is not { } group)
            throw new NotSupportedException("The folder's owner and primary group are required to project inheritance.");

        var creator = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, new SecurityIdentifier(owner.Id),
            new SecurityIdentifier(group.Id), null, new RawAcl(explicitBytes.ToArray(), 0));
        byte[] creatorBytes = new byte[creator.BinaryLength];
        creator.GetBinaryForm(creatorBytes, 0);
        string authority = original.Subject.Authority.Id;
        var explicitAcl = WindowsPermissionDescriptor.ProjectDacl(original.Inheritance.Value!.Value, explicitBytes, authority);
        var mapping = new FileGenericMapping { Read = 0x00120089, Write = 0x00120116, Execute = 0x001200a0, All = 0x001f01ff };
        nint descriptor = 0;
        try
        {
            // This call only combines descriptors in memory. Authorization stays on the separately opened filesystem handle.
            const uint deriveDaclWithoutToken = 0x01 | 0x08 | 0x10;
            fixed (byte* source = parent.Acl.SecurityDescriptor.AsSpan())
            fixed (byte* child = creatorBytes)
                if (!CreatePrivateObjectSecurityEx(source, child, out descriptor, 0, true, deriveDaclWithoutToken, 0, &mapping))
                    throw new IOException("Windows could not project the inherited rules.", new Win32Exception(Marshal.GetLastPInvokeError()));
            if (descriptor == 0 || !IsValidSecurityDescriptor(descriptor))
                throw new IOException("Windows returned an invalid inheritance projection.");
            uint length = GetSecurityDescriptorLength(descriptor);
            if (length is < 20 or > PermissionSnapshots.MaxSectionBytes)
                throw new IOException("The inheritance projection exceeds the descriptor limit.");
            var raw = new ReadOnlySpan<byte>((void*)descriptor, (int)length);
            var merged = WindowsPermissionDescriptor.Dacl(raw, authority);
            if (merged.Form != WindowsDaclForm.Entries || merged.Entries.Length < explicitAcl.Entries.Length ||
                explicitAcl.Entries.Where((entry, index) => !entry.Bytes.AsSpan().SequenceEqual(merged.Entries[index].Bytes.AsSpan())).Any() ||
                merged.Entries.Skip(explicitAcl.Entries.Length).Any(entry => (entry.Flags & InheritedAce) == 0))
                throw new NotSupportedException("The Windows merge could not preserve every explicit rule in order.");

            int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(raw[16..]));
            return ImmutableArray.Create(raw.Slice(offset, BinaryPrimitives.ReadUInt16LittleEndian(raw[(offset + 2)..])).ToArray());
        }
        finally { if (descriptor != 0) _ = DestroyPrivateObjectSecurity(ref descriptor); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileGenericMapping { internal uint Read, Write, Execute, All; }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePrivateObjectSecurityEx(byte* parent, byte* creator, out nint descriptor, nint objectType,
        [MarshalAs(UnmanagedType.Bool)] bool container, uint flags, nint token, FileGenericMapping* mapping);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyPrivateObjectSecurity(ref nint descriptor);
}
