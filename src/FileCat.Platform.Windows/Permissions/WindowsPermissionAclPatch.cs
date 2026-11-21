using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;

namespace FileCat.Platform.Windows.Permissions;

/// <summary>Byte patches for one explicit Windows ACE occurrence. Bytes outside the chosen entry are carried through.</summary>
internal static class WindowsPermissionAclPatch
{
    private const byte Inherited = 0x10;

    internal static WindowsAce SimpleEntry(WindowsAceKind kind, Principal principal, uint mask, byte flags = 0)
    {
        byte type = kind switch
        {
            WindowsAceKind.Allow => 0,
            WindowsAceKind.Deny => 1,
            _ => throw new NotSupportedException("Create a known allow or deny access entry.")
        };
        return new(type, flags, kind, mask, principal, SimpleEntryBytes(type, flags, mask, principal));
    }

    internal static ImmutableArray<byte> SimpleEntryBytes(byte type, byte flags, uint mask, Principal principal)
    {
        byte[] sid = WindowsPermissionDescriptor.SidBytes(principal, principal.Authority);
        var bytes = new byte[8 + sid.Length];
        bytes[0] = type;
        bytes[1] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), mask);
        sid.CopyTo(bytes, 8);
        return ImmutableArray.Create(bytes);
    }

    internal static ImmutableArray<byte> Replace(WindowsDacl baseline, ChangeWindowsAce change, string authority)
    {
        var observed = RequireExplicitAcl(baseline, authority);
        if ((uint)change.Occurrence >= (uint)observed.Entries.Length)
            throw new PermissionBindingConflictException("The reviewed Windows ACL occurrence is no longer present.");
        var original = observed.Entries[change.Occurrence];
        if (!original.Bytes.AsSpan().SequenceEqual(change.Expected.Bytes.AsSpan()))
            throw new PermissionBindingConflictException("The bytes of the reviewed Windows ACL occurrence changed.");
        if (original.Kind == WindowsAceKind.Opaque || (original.Flags & Inherited) != 0)
            throw new NotSupportedException("Only a known explicit access entry can be changed here.");
        var entries = observed.Entries;
        if (change.Replacement is null) entries = entries.RemoveAt(change.Occurrence);
        else
        {
            var replacement = ValidateSimpleEntry(change.Replacement, authority);
            if (replacement.Flags != original.Flags)
                throw new NotSupportedException("Inheritance flags require their own review.");
            entries = entries.SetItem(change.Occurrence, replacement);
        }
        return Serialize(observed, entries);
    }

    internal static ImmutableArray<byte> InsertCanonical(WindowsDacl baseline, WindowsAce entry, string authority)
    {
        var observed = RequireExplicitAcl(baseline, authority);
        var addition = ValidateSimpleEntry(entry, authority);
        if (addition.Flags != 0) throw new NotSupportedException("New file access entries have no inheritance flags.");
        int insertion = observed.Entries.Length;
        int lastBlock = -1;
        for (int index = 0; index < observed.Entries.Length; index++)
        {
            var current = observed.Entries[index];
            if (current.Kind == WindowsAceKind.Opaque)
                throw new NotSupportedException("An opaque rule prevents choosing a canonical insertion position.");
            int block = (current.Flags & Inherited) != 0 ? 2 : current.Kind == WindowsAceKind.Deny ? 0 : 1;
            if (block < lastBlock) throw new NotSupportedException("Review the existing noncanonical order before adding a rule.");
            lastBlock = block;
            if (insertion == observed.Entries.Length && (block == 2 || block == 1 && addition.Kind == WindowsAceKind.Deny))
                insertion = index;
        }
        return Serialize(observed, observed.Entries.Insert(insertion, addition));
    }

    private static WindowsDacl RequireExplicitAcl(WindowsDacl baseline, string authority)
    {
        var observed = WindowsPermissionDescriptor.Dacl(baseline.SecurityDescriptor.AsSpan(), authority);
        if (observed.Form != WindowsDaclForm.Entries || observed.Revision is not (2 or 4))
            throw new NotSupportedException("Replacing an absent, NULL or unsupported DACL requires separate review.");
        return observed;
    }

    private static WindowsAce ValidateSimpleEntry(WindowsAce entry, string authority)
    {
        if (entry.Kind == WindowsAceKind.Opaque || (entry.Flags & Inherited) != 0 ||
            entry.Bytes.Length is < 16 or > 76)
            throw new NotSupportedException("A replacement must be a known explicit allow or deny entry.");
        var descriptor = new byte[28 + entry.Bytes.Length];
        descriptor[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(2), 0x8004);
        descriptor[16] = 20;
        descriptor[20] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(22), (ushort)(descriptor.Length - 20));
        descriptor[24] = 1;
        entry.Bytes.AsSpan().CopyTo(descriptor.AsSpan(28));
        var validated = WindowsPermissionDescriptor.Dacl(descriptor, authority).Entries[0];
        if (validated.Kind != entry.Kind || validated.Type != entry.Type || validated.Flags != entry.Flags ||
            validated.Mask != entry.Mask || validated.Principal != entry.Principal || validated.Bytes.Length != entry.Bytes.Length)
            throw new InvalidDataException("The typed Windows access entry differs from its native bytes.");
        return validated;
    }

    private static ImmutableArray<byte> Serialize(WindowsDacl observed, ImmutableArray<WindowsAce> entries)
    {
        var descriptor = observed.SecurityDescriptor.AsSpan();
        int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(descriptor[16..]);
        return Serialize(descriptor[offset..], 8 + observed.Entries.Sum(entry => entry.Bytes.Length),
            entries.Select(entry => entry.Bytes).ToImmutableArray());
    }

    internal static ImmutableArray<byte> Serialize(ReadOnlySpan<byte> acl, int previousEnd, ImmutableArray<ImmutableArray<byte>> entries)
    {
        int previousLength = BinaryPrimitives.ReadUInt16LittleEndian(acl[2..]);
        var trailing = acl.Slice(previousEnd, previousLength - previousEnd);
        int size = 8 + entries.Sum(entry => entry.Length) + trailing.Length;
        if (size > ushort.MaxValue || entries.Length > PermissionSnapshots.MaxAclEntries)
            throw new OverflowException("The patched Windows ACL exceeds its native size or entry limit.");
        var bytes = new byte[size];
        acl[..8].CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)size);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)entries.Length);
        int cursor = 8;
        foreach (var entry in entries)
        {
            entry.AsSpan().CopyTo(bytes.AsSpan(cursor));
            cursor += entry.Length;
        }
        trailing.CopyTo(bytes.AsSpan(cursor));
        return ImmutableArray.Create(bytes);
    }
}
