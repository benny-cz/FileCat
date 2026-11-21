using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;

namespace FileCat.Platform.Windows.Permissions;

internal static class WindowsPermissionAuditPatch
{
    internal const byte Success = 0x40, Failure = 0x80;
    private const byte Inherited = 0x10, AuditType = 2;

    internal static bool CanEditFileEntry(WindowsAuditAce entry) =>
        entry.Type == AuditType && (entry.Flags & (Success | Failure)) != 0 &&
        (entry.Flags & ~(Success | Failure)) == 0 && entry.Mask is uint mask && mask != 0 &&
        (WindowsFileAccessRights.MapGeneric(mask) & ~WindowsFileAccessRights.Known) == 0 && entry.Principal is not null;

    internal static WindowsAuditAce FileEntry(Principal principal, uint mask, byte flags)
    {
        if (mask == 0 || (mask & ~WindowsFileAccessRights.Known) != 0 || flags == 0 || (flags & ~(Success | Failure)) != 0)
            throw new ArgumentException("Choose supported file rights and at least one of success or failure.");
        return new(AuditType, flags, mask, principal, WindowsPermissionAclPatch.SimpleEntryBytes(AuditType, flags, mask, principal));
    }

    internal static WindowsSacl ProjectFile(WindowsSacl baseline, ChangeWindowsAuditAce change, string authority)
    {
        var observed = WindowsPermissionDescriptor.Sacl(baseline.SecurityDescriptor.AsSpan(), authority);
        if (observed.Revision is not (null or 2 or 4))
            throw new NotSupportedException("This system ACL revision is unavailable for editing.");
        var entries = observed.Entries;
        var replacement = change.Replacement is { } proposed ? ValidReplacement(proposed, authority) : null;
        if (change.Occurrence is int index)
        {
            if ((uint)index >= entries.Length || change.Expected is null ||
                !entries[index].Bytes.AsSpan().SequenceEqual(change.Expected.Bytes.AsSpan()))
                throw new PermissionBindingConflictException("The reviewed audit entry occurrence changed.");
            if (!CanEditFileEntry(entries[index]))
                throw new NotSupportedException("Select a known explicit file audit entry to change it.");
            entries = replacement is null ? entries.RemoveAt(index) : entries.SetItem(index, replacement);
        }
        else
        {
            if (change.Expected is not null || replacement is null)
                throw new InvalidDataException("Adding an audit entry requires one explicit new rule.");
            int insertion = entries.Length;
            for (int position = 0; position < entries.Length; position++)
            {
                var entry = entries[position];
                // Preserve all native order, including non-audit ACEs and callback or object audit ACEs.
                if ((entry.Flags & Inherited) != 0) insertion = Math.Min(insertion, position);
                else if (position >= insertion && entry.Type is 2 or 7 or 13 or 15)
                    throw new NotSupportedException("The audit ACL has explicit entries after inherited entries.");
            }
            entries = entries.Insert(insertion, replacement);
        }
        ReadOnlySpan<byte> header = observed.Revision is null ? [2, 0, 8, 0, 0, 0, 0, 0] : observed.Bytes.AsSpan();
        var bytes = WindowsPermissionAclPatch.Serialize(header, 8 + observed.Entries.Sum(entry => entry.Bytes.Length),
            entries.Select(entry => entry.Bytes).ToImmutableArray());
        return WindowsPermissionDescriptor.ProjectSacl(observed.Control, bytes, authority);
    }

    private static WindowsAuditAce ValidReplacement(WindowsAuditAce proposed, string authority)
    {
        if (!CanEditFileEntry(proposed) || proposed.Principal is not { } principal || principal.Authority != authority)
            throw new NotSupportedException("The replacement requires a known explicit audit entry for this authority.");
        var canonical = FileEntry(principal, proposed.Mask!.Value, proposed.Flags);
        if (!canonical.Bytes.AsSpan().SequenceEqual(proposed.Bytes.AsSpan()))
            throw new InvalidDataException("The typed audit entry differs from its native bytes.");
        return canonical;
    }
}
