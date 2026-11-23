using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Permissions;

internal static class DarwinPermissionAcl
{
    // Host-order kauth_filesec returned by the Darwin attribute calls. The UUID octets retain network order.
    private const uint Magic = 0x012cc16d, NoAcl = uint.MaxValue;
    private const int FileSecurityHeader = 44, EntrySize = 24;
    internal const int NativeEntryLimit = 128;
    internal const int NativePayloadLimit = FileSecurityHeader + NativeEntryLimit * EntrySize;
    internal const uint Inherited = 0x10, FileInherit = 0x20, DirectoryInherit = 0x40, LimitInherit = 0x80, InheritOnly = 0x100;
    internal const uint InheritanceFlags = FileInherit | DirectoryInherit | LimitInherit | InheritOnly;
    internal const uint DeferredInheritance = 1u << 16, NoInheritance = 1u << 17;
    internal const uint EditableRights = 0x103ffe;

    internal static DarwinAcl? Decode(ReadOnlySpan<byte> bytes, string authority)
    {
        if (bytes.Length < FileSecurityHeader || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
            throw new InvalidDataException("The Darwin file security header is invalid.");

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[36..]);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes[40..]);
        if (count != NoAcl && count > PermissionSnapshots.MaxAclEntries)
            throw new OverflowException("The Darwin ACL exceeds the entry limit.");
        int entries = count == NoAcl ? 0 : (int)count;
        if (bytes.Length != FileSecurityHeader + entries * EntrySize)
            throw new InvalidDataException("The Darwin ACL entry count and payload size do not match.");
        if (count == NoAcl) return null;

        var rules = ImmutableArray.CreateBuilder<DarwinAce>(entries);
        for (int index = 0; index < entries; index++)
        {
            var entry = bytes.Slice(FileSecurityHeader + index * EntrySize, EntrySize);
            var uuid = new Guid(entry[..16], bigEndian: true);
            uint rawFlags = BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
            uint rights = BinaryPrimitives.ReadUInt32LittleEndian(entry[20..]);
            var kind = (rawFlags & 0xf) switch { 1 => DarwinAceKind.Allow, 2 => DarwinAceKind.Deny, _ => DarwinAceKind.Opaque };
            rules.Add(new(new Principal(PrincipalKind.DarwinUuid, uuid.ToString("D"), authority), kind, rawFlags, rights));
        }
        return new(flags, rules.MoveToImmutable(), ImmutableArray.Create(bytes));
    }

    internal static byte[] Change(DarwinAcl? baseline, DarwinAclDelta change, string authority, bool directory)
    {
        int count = baseline?.Entries.Length ?? 0;
        if (count > NativeEntryLimit) throw new NotSupportedException("The Darwin ACL exceeds the native write limit.");
        if (change is RemoveDarwinAcl) return Header(NoAcl);

        int index;
        DarwinAce? replacement;
        switch (change)
        {
            case AddDarwinAce add when add.Index >= 0 && add.Index <= count && count < NativeEntryLimit:
                index = add.Index;
                replacement = add.Entry;
                Validate(add.Entry, null, authority, directory);
                break;
            case ChangeDarwinAce edit when edit.Index >= 0 && edit.Index < count && baseline!.Entries[edit.Index] == edit.Expected:
                index = edit.Index;
                replacement = edit.Replacement;
                if (edit.Expected.Kind is not (DarwinAceKind.Allow or DarwinAceKind.Deny))
                    throw new NotSupportedException("Only a known allow or deny entry can be edited individually.");
                if (replacement is { } rule) Validate(rule, edit.Expected, authority, directory);
                break;
            default:
                throw new NotSupportedException("The reviewed ACL occurrence or insertion position is unavailable.");
        }
        bool insert = change is AddDarwinAce, remove = replacement is null;
        int newCount = count + (insert ? 1 : remove ? -1 : 0);
        byte[] bytes = Header((uint)newCount, baseline?.Flags ?? 0);
        if (baseline is not null) baseline.FileSecurity.AsSpan().Slice(4, 32).CopyTo(bytes.AsSpan(4));
        var entries = baseline is null ? ReadOnlySpan<byte>.Empty : baseline.FileSecurity.AsSpan()[FileSecurityHeader..];
        int start = index * EntrySize;
        entries[..start].CopyTo(bytes.AsSpan(FileSecurityHeader));
        if (replacement is { } value) WriteEntry(bytes.AsSpan(FileSecurityHeader + start, EntrySize), value);
        entries[(start + (insert ? 0 : EntrySize))..].CopyTo(bytes.AsSpan(FileSecurityHeader + start + (remove ? 0 : EntrySize)));
        return bytes;
    }

    internal static byte[] DeriveChild(DarwinAcl? parent, DarwinAcl? child, bool directory, bool reset)
    {
        var direct = child?.Entries ?? [];
        var source = parent?.Entries ?? [];
        uint inherit = directory ? DirectoryInherit : FileInherit;
        int count = (reset ? 0 : direct.Count(rule => (rule.Flags & Inherited) == 0)) +
            source.Count(rule => (rule.Flags & inherit) != 0);
        if (count > NativeEntryLimit) throw new NotSupportedException("The inherited ACL exceeds the native entry limit.");
        if (count == 0 && child is null) return Header(NoAcl);
        var bytes = Header((uint)count, child?.Flags ?? 0);
        int offset = FileSecurityHeader;
        if (child is not null)
        {
            child.FileSecurity.AsSpan().Slice(4, 32).CopyTo(bytes.AsSpan(4));
            for (int index = 0; !reset && index < direct.Length; index++)
            {
                if ((direct[index].Flags & Inherited) != 0) continue;
                child.FileSecurity.AsSpan().Slice(FileSecurityHeader + index * EntrySize, EntrySize).CopyTo(bytes.AsSpan(offset));
                offset += EntrySize;
            }
        }
        // XNU appends qualifying copies after the explicit block, in the parent's order.
        for (int index = 0; index < source.Length; index++)
        {
            uint flags = source[index].Flags;
            if ((flags & inherit) == 0) continue;
            var entry = bytes.AsSpan(offset, EntrySize);
            parent!.FileSecurity.AsSpan().Slice(FileSecurityHeader + index * EntrySize, EntrySize).CopyTo(entry);
            flags = (flags | Inherited) & ~InheritOnly;
            if (!directory || (flags & LimitInherit) != 0) flags &= ~InheritanceFlags;
            BinaryPrimitives.WriteUInt32LittleEndian(entry[16..], flags);
            offset += EntrySize;
        }
        return bytes;
    }

    private static void Validate(DarwinAce rule, DarwinAce? before, string authority, bool directory)
    {
        if (rule.Principal.Kind != PrincipalKind.DarwinUuid || rule.Principal.Authority != authority ||
            !Guid.TryParseExact(rule.Principal.Id, "D", out var uuid) || uuid == Guid.Empty ||
            rule.Kind is not (DarwinAceKind.Allow or DarwinAceKind.Deny) || rule.Rights == 0)
            throw new NotSupportedException("Choose a valid Darwin UUID, allow or deny, and at least one right.");
        uint previous = before?.Flags ?? 0;
        if ((rule.Rights & ~EditableRights) != ((before?.Rights ?? 0) & ~EditableRights) ||
            ((previous ^ rule.Flags) & ~(InheritanceFlags | 0xfu)) != 0)
            throw new NotSupportedException("The reviewed edit must preserve inherited markers and unrecognized ACL bits.");
        uint inheritance = rule.Flags & InheritanceFlags;
        if (((previous ^ rule.Flags) & InheritanceFlags) != 0 &&
            (!directory || ((inheritance & (LimitInherit | InheritOnly)) != 0 && (inheritance & (FileInherit | DirectoryInherit)) == 0)))
            throw new NotSupportedException("Inheritance modifiers require a directory and a file or directory inheritance target.");
    }

    internal static byte[] Header(uint count, uint flags = 0)
    {
        // ATTR_CMN_EXTENDED_SECURITY supplies and consumes only the ACL. Its UUID header fields are zero.
        var bytes = new byte[FileSecurityHeader + (count == NoAcl ? 0 : (int)count * EntrySize)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), count);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), flags);
        return bytes;
    }

    private static void WriteEntry(Span<byte> bytes, DarwinAce rule)
    {
        _ = Guid.ParseExact(rule.Principal.Id, "D").TryWriteBytes(bytes[..16], bigEndian: true, out _);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[16..], (rule.Flags & ~0xfu) | (rule.Kind == DarwinAceKind.Allow ? 1u : 2u));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[20..], rule.Rights);
    }

    internal static byte[] SingleEntryPayload(ReadOnlySpan<byte> payload, int index)
    {
        var bytes = Header(1);
        payload.Slice(FileSecurityHeader + index * EntrySize, EntrySize).CopyTo(bytes.AsSpan(FileSecurityHeader));
        return bytes;
    }
}

internal sealed record ApplyDarwinAcl(ImmutableArray<byte> FileSecurity, bool Remove) : PermissionDelta(PermissionSections.Access);
