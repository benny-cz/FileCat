using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;

namespace FileCat.Platform.Windows.Permissions;

/// <summary>Lossless projection of the bounded self-relative descriptor returned by Windows.</summary>
internal static class WindowsPermissionDescriptor
{
    private const ushort SelfRelative = 0x8000, DaclPresent = 4, SaclPresent = 0x10, ResourceManagerControlValid = 0x4000;
    private const ushort SaclControlFlags = 0x6a30; // Present, defaulted, auto-inherit requested, auto-inherited, protected, resource manager.

    internal static WindowsAclControl Control(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < 20 || descriptor[0] != 1 || (Flags(descriptor) & SelfRelative) == 0)
            throw new InvalidDataException("Windows did not return a self-relative security descriptor.");
        return new(Flags(descriptor), descriptor[0]);
    }

    internal static Principal? PrincipalAt(ReadOnlySpan<byte> descriptor, int fieldOffset, string authority)
    {
        int offset = Offset(descriptor, fieldOffset);
        return offset == 0 ? null : ReadSid(descriptor[offset..], authority);
    }

    internal static WindowsDacl Dacl(ReadOnlySpan<byte> descriptor, string authority)
    {
        ushort control = Control(descriptor).Flags;
        var bytes = ImmutableArray.Create(descriptor);
        if ((control & DaclPresent) == 0) return new(WindowsDaclForm.Absent, null, [], bytes);
        int offset = Offset(descriptor, 16);
        if (offset == 0) return new(WindowsDaclForm.Null, null, [], bytes);

        var acl = ReadAcl(descriptor[offset..], out int count);
        var entries = ImmutableArray.CreateBuilder<WindowsAce>(count);
        for (int i = 0, cursor = 8; i < count; i++)
        {
            var entry = ReadAce(acl, ref cursor);
            WindowsAceKind kind = entry[0] switch { 0 => WindowsAceKind.Allow, 1 => WindowsAceKind.Deny, _ => WindowsAceKind.Opaque };
            uint? rights = null;
            Principal? principal = null;
            if (kind != WindowsAceKind.Opaque)
            {
                if (entry.Length < 16 || entry.Length != 16 + 4 * entry[9])
                    throw new InvalidDataException("A simple Windows ACE has an invalid SID length.");
                rights = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
                principal = ReadSid(entry[8..], authority);
            }
            entries.Add(new(entry[0], entry[1], kind, rights, principal, ImmutableArray.Create(entry)));
        }
        return new(WindowsDaclForm.Entries, acl[0], entries.MoveToImmutable(), bytes);
    }

    internal static WindowsSacl Sacl(ReadOnlySpan<byte> descriptor, string authority)
    {
        var control = Control(descriptor);
        var auditControl = new WindowsSaclControl((ushort)(control.Flags & SaclControlFlags), control.DescriptorRevision,
            (control.Flags & ResourceManagerControlValid) != 0 ? descriptor[1] : null);
        var bytes = ImmutableArray.Create(descriptor);
        if ((control.Flags & SaclPresent) == 0) return new(false, null, auditControl, [], [], bytes);
        int offset = Offset(descriptor, 12);
        if (offset == 0) return new(true, null, auditControl, [], [], bytes);

        var acl = ReadAcl(descriptor[offset..], out int count);
        var entries = ImmutableArray.CreateBuilder<WindowsAuditAce>(count);
        for (int i = 0, cursor = 8; i < count; i++)
        {
            var entry = ReadAce(acl, ref cursor);
            uint? rights = null;
            Principal? principal = null;
            if (entry[0] == 2) // SYSTEM_AUDIT_ACE_TYPE
            {
                if (entry.Length < 16 || entry.Length != 16 + 4 * entry[9])
                    throw new InvalidDataException("A simple Windows audit ACE has an invalid SID length.");
                rights = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
                principal = ReadSid(entry[8..], authority);
            }
            entries.Add(new(entry[0], entry[1], rights, principal, ImmutableArray.Create(entry)));
        }
        return new(true, acl[0], auditControl, entries.MoveToImmutable(), ImmutableArray.Create(acl), bytes);
    }

    private static ReadOnlySpan<byte> ReadAcl(ReadOnlySpan<byte> acl, out int count)
    {
        if (acl.Length < 8) throw new InvalidDataException("The Windows ACL header is incomplete.");
        int length = BinaryPrimitives.ReadUInt16LittleEndian(acl[2..]);
        count = BinaryPrimitives.ReadUInt16LittleEndian(acl[4..]);
        if (length < 8 || length > acl.Length || count > (length - 8) / 4)
            throw new InvalidDataException("The Windows ACL length or entry count is invalid.");
        if (count > PermissionSnapshots.MaxAclEntries) throw new OverflowException("The Windows ACL exceeds the entry limit.");
        return acl[..length];
    }

    private static ReadOnlySpan<byte> ReadAce(ReadOnlySpan<byte> acl, ref int cursor)
    {
        if (acl.Length - cursor < 4) throw new InvalidDataException("A Windows ACE header is incomplete.");
        int size = BinaryPrimitives.ReadUInt16LittleEndian(acl[(cursor + 2)..]);
        if (size < 4 || (size & 3) != 0 || size > acl.Length - cursor)
            throw new InvalidDataException("A Windows ACE length is invalid.");
        var entry = acl.Slice(cursor, size);
        cursor += size;
        return entry;
    }
    private static ushort Flags(ReadOnlySpan<byte> descriptor) => BinaryPrimitives.ReadUInt16LittleEndian(descriptor[2..]);

    private static int Offset(ReadOnlySpan<byte> descriptor, int field)
    {
        _ = Control(descriptor);
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[field..]);
        if (offset != 0 && (offset < 20 || (offset & 3) != 0 || offset >= descriptor.Length))
            throw new InvalidDataException("A Windows security descriptor offset is invalid.");
        return (int)offset;
    }

    internal static Principal ReadSid(ReadOnlySpan<byte> sid, string authority)
    {
        if (sid.Length < 8 || sid[0] != 1 || sid[1] > 15 || sid.Length < 8 + sid[1] * 4)
            throw new InvalidDataException("Windows returned an invalid security identifier.");
        ulong identifierAuthority = 0;
        foreach (byte value in sid.Slice(2, 6)) identifierAuthority = (identifierAuthority << 8) | value;
        var name = new StringBuilder("S-1-").Append(identifierAuthority.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < sid[1]; i++)
            name.Append('-').Append(BinaryPrimitives.ReadUInt32LittleEndian(sid[(8 + 4 * i)..]).ToString(CultureInfo.InvariantCulture));
        return new(PrincipalKind.WindowsSid, name.ToString(), authority);
    }

    internal static byte[] SidBytes(Principal principal, string authority)
    {
        if (principal.Kind != PrincipalKind.WindowsSid || principal.Authority != authority || string.IsNullOrEmpty(principal.Id))
            throw new ArgumentException("The SID belongs to a different permissions authority.", nameof(principal));
        string[] parts = principal.Id.Split('-');
        if (parts[1] != "1" || !ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong sidAuthority) ||
            sidAuthority > 0xffff_ffff_ffffUL)
            throw new ArgumentException("The SID has an invalid revision or identifier authority.", nameof(principal));
        var bytes = new byte[8 + 4 * (parts.Length - 3)];
        bytes[0] = 1;
        bytes[1] = (byte)(parts.Length - 3);
        for (int index = 7; index >= 2; index--)
        {
            bytes[index] = unchecked((byte)sidAuthority);
            sidAuthority >>= 8;
        }
        for (int index = 3; index < parts.Length; index++)
        {
            if (!uint.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out uint subAuthority))
                throw new ArgumentException("The SID has an out-of-range subauthority.", nameof(principal));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 + 4 * (index - 3)), subAuthority);
        }
        return bytes;
    }

    internal static WindowsDacl ProjectDacl(WindowsAclControl control, ImmutableArray<byte> acl, string authority)
    {
        var descriptor = new byte[20 + acl.Length];
        descriptor[0] = control.DescriptorRevision;
        const ushort daclControlFlags = 0x150c; // Present, defaulted, auto-inherit requested, auto-inherited, protected.
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(2), (ushort)((control.Flags & daclControlFlags) | SelfRelative | DaclPresent));
        descriptor[16] = 20;
        acl.AsSpan().CopyTo(descriptor.AsSpan(20));
        return Dacl(descriptor, authority);
    }

    internal static WindowsSacl ProjectSacl(WindowsSaclControl control, ImmutableArray<byte> acl, string authority) =>
        Sacl(SaclDescriptor(control with { Flags = (ushort)(control.Flags | SaclPresent) }, acl), authority);

    internal static byte[] AuditOnly(ReadOnlySpan<byte> descriptor, string authority)
    {
        var audit = Sacl(descriptor, authority);
        return SaclDescriptor(audit.Control, audit.Bytes);
    }

    // The descriptor an authorized SACL edit hands to NtSetSecurityObject, which stores it with no inheritance processing.
    // SetSecurityInfo instead re-derives inherited audit entries from the current parent, so on a file whose inherited
    // entries no longer match its parent (a file moved within the volume) it changed entries the review never showed.
    // NTFS clears SE_SACL_AUTO_INHERITED unless the descriptor also sets SE_SACL_AUTO_INHERIT_REQ; a SACL stored without
    // that bit keeps that form. Label, resource-attribute, scoped-policy and trust-label entries are left out: the
    // SACL_SECURITY_INFORMATION write keeps the stored ones under their own classes, after the audit entries.
    internal static ImmutableArray<byte> SaclWriteDescriptor(WindowsSacl projected)
    {
        const ushort autoInheritRequest = 0x0200, autoInherited = 0x0800, isProtected = 0x2000;
        var control = projected.Control;
        ushort flags = (ushort)(SelfRelative | SaclPresent | (control.Flags & (autoInherited | isProtected)) |
            (control.IsAutoInherited ? autoInheritRequest : 0));
        var kept = projected.Entries.Where(entry => entry.Type is not (0x11 or 0x12 or 0x13 or 0x14))
            .Select(entry => entry.Bytes).ToImmutableArray();
        var acl = WindowsPermissionAclPatch.Serialize(projected.Bytes.AsSpan(),
            8 + projected.Entries.Sum(entry => entry.Bytes.Length), kept);
        var descriptor = new byte[20 + acl.Length];
        descriptor[0] = control.DescriptorRevision;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(2), flags);
        descriptor[12] = 20;
        acl.AsSpan().CopyTo(descriptor.AsSpan(20));
        return ImmutableArray.Create(descriptor);
    }

    private static byte[] SaclDescriptor(WindowsSaclControl control, ImmutableArray<byte> acl)
    {
        var descriptor = new byte[20 + acl.Length];
        descriptor[0] = control.DescriptorRevision;
        descriptor[1] = control.ResourceManagerControl ?? 0;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(2), (ushort)((control.Flags & SaclControlFlags) | SelfRelative));
        if (!acl.IsEmpty)
        {
            descriptor[12] = 20;
            acl.AsSpan().CopyTo(descriptor.AsSpan(20));
        }
        return descriptor;
    }
}
