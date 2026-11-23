using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Permissions;

internal static class LinuxPermissionAcl
{
    internal static ImmutableArray<PosixAclEntry> AccessRows(PermissionSnapshot snapshot)
    {
        if (snapshot.Access.Value?.Acl is PosixAcl acl) return acl.Entries;
        bool modeOnly = snapshot.Model == PermissionModel.UnixMode && snapshot.Binding?.PosixAclDisabled == true;
        if ((!modeOnly && snapshot.Access.Status != PermissionReadStatus.Absent) || snapshot.Mode.Value is not { } mode)
            throw new NotSupportedException("Current access rules could not be established.");
        return [new(PosixAclTag.Owner, (byte)((mode >> 6) & 7), null),
            new(PosixAclTag.OwningGroup, (byte)((mode >> 3) & 7), null),
            new(PosixAclTag.Other, (byte)(mode & 7), null)];
    }

    internal static PosixAcl Encode(ImmutableArray<PosixAclEntry> entries, string authority)
    {
        if (entries.IsDefault || entries.Length > PermissionSnapshots.MaxAclEntries)
            throw new InvalidDataException("The POSIX ACL exceeds the entry limit.");
        var bytes = new byte[4 + 8 * entries.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 2);
        for (int index = 0; index < entries.Length; index++)
        {
            var row = entries[index];
            var entry = bytes.AsSpan(4 + 8 * index, 8);
            uint id = uint.MaxValue;
            if (row.Tag is PosixAclTag.NamedUser or PosixAclTag.NamedGroup)
            {
                var kind = row.Tag == PosixAclTag.NamedUser ? PrincipalKind.UnixUser : PrincipalKind.UnixGroup;
                if (row.Principal is not { } principal || principal.Kind != kind || principal.Authority != authority ||
                    !uint.TryParse(principal.Id, NumberStyles.None, CultureInfo.InvariantCulture, out id) || id == uint.MaxValue)
                    throw new InvalidDataException("The POSIX ACL principal is invalid for this authority.");
            }
            else if (row.Principal is not null)
                throw new InvalidDataException("This POSIX ACL row does not take a principal.");

            BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)row.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], row.Rights);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], id);
        }
        return Decode(bytes, authority) as PosixAcl ?? throw new InvalidDataException("The POSIX ACL contains an unsupported row.");
    }

    // chmod changes the group-class limit when the ACL has a mask.
    internal static PosixAcl WithMode(PosixAcl acl, ushort mode, string authority)
    {
        bool hasMask = acl.Entries.Any(entry => entry.Tag == PosixAclTag.Mask);
        var entries = acl.Entries.Select(entry => entry with
        {
            Rights = entry.Tag switch
            {
                PosixAclTag.Owner => (byte)((mode >> 6) & 7),
                PosixAclTag.Mask => (byte)((mode >> 3) & 7),
                PosixAclTag.OwningGroup when !hasMask => (byte)((mode >> 3) & 7),
                PosixAclTag.Other => (byte)(mode & 7),
                _ => entry.Rights
            }
        }).ToImmutableArray();
        return Encode(entries, authority);
    }

    internal static ushort ProjectMode(PosixAcl acl, ushort specialBits)
    {
        byte owner = 0, group = 0, other = 0;
        foreach (var row in acl.Entries)
        {
            // Canonical rows put the mask after OwningGroup, so the mask supplies the group class when present.
            switch (row.Tag)
            {
                case PosixAclTag.Owner: owner = row.Rights; break;
                case PosixAclTag.OwningGroup:
                case PosixAclTag.Mask: group = row.Rights; break;
                case PosixAclTag.Other: other = row.Rights; break;
            }
        }
        return (ushort)((specialBits & 0xe00) | (owner << 6) | (group << 3) | other);
    }

    internal static PermissionAcl Decode(ReadOnlySpan<byte> bytes, string authority)
    {
        if (bytes.Length < 4 || (bytes.Length - 4) % 8 != 0)
            throw new InvalidDataException("The POSIX ACL attribute has an invalid length.");
        int count = (bytes.Length - 4) / 8;
        if (count > PermissionSnapshots.MaxAclEntries)
            throw new OverflowException("The POSIX ACL exceeds the entry limit.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 2)
            return new OpaquePermissionAcl(ImmutableArray.Create(bytes));

        var entries = ImmutableArray.CreateBuilder<PosixAclEntry>(count);
        PosixAclTag previous = 0;
        uint previousId = 0;
        bool hasNamedEntries = false, hasMask = false, hasGroup = false;
        for (int index = 0; index < count; index++)
        {
            var entry = bytes[(4 + index * 8)..];
            var tag = (PosixAclTag)BinaryPrimitives.ReadUInt16LittleEndian(entry);
            if (tag is not (PosixAclTag.Owner or PosixAclTag.NamedUser or PosixAclTag.OwningGroup or
                PosixAclTag.NamedGroup or PosixAclTag.Mask or PosixAclTag.Other))
                return new OpaquePermissionAcl(ImmutableArray.Create(bytes));
            ushort rights = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            bool named = tag is PosixAclTag.NamedUser or PosixAclTag.NamedGroup;
            if (rights > 7 || named == (id == uint.MaxValue) || tag < previous ||
                (tag == previous && (!named || id <= previousId)))
                throw new InvalidDataException("The POSIX ACL has an invalid qualifier, rights mask, or entry order.");
            Principal? principal = named
                ? new Principal(tag == PosixAclTag.NamedUser ? PrincipalKind.UnixUser : PrincipalKind.UnixGroup,
                    id.ToString(CultureInfo.InvariantCulture), authority)
                : null;
            entries.Add(new(tag, (byte)rights, principal));
            previous = tag;
            previousId = id;
            hasNamedEntries |= named;
            hasGroup |= tag == PosixAclTag.OwningGroup;
            hasMask |= tag == PosixAclTag.Mask;
        }
        if (count < 3 || entries[0].Tag != PosixAclTag.Owner || !hasGroup || previous != PosixAclTag.Other || (hasNamedEntries && !hasMask))
            throw new InvalidDataException("The POSIX ACL is missing a required entry.");
        return new PosixAcl(entries.MoveToImmutable(), ImmutableArray.Create(bytes));
    }
}
