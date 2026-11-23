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
        // A SACL write keeps the object's label, resource-attribute, scoped-policy and trust-label entries under their own
        // security information classes. Windows stores them after the audit entries whatever their position in the written
        // ACL, so only a baseline that already has them last can be written and read back byte for byte.
        int firstPolicy = PolicyStart(entries);
        if (entries.Take(firstPolicy).Any(IsPolicyEntry))
            throw new NotSupportedException("Label or policy entries precede audit entries in this system ACL, so Windows may reorder it.");
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
            int insertion = firstPolicy;
            for (int position = 0; position < firstPolicy; position++)
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

    // SYSTEM_MANDATORY_LABEL, SYSTEM_RESOURCE_ATTRIBUTE, SYSTEM_SCOPED_POLICY_ID and SYSTEM_PROCESS_TRUST_LABEL entries.
    private static bool IsPolicyEntry(WindowsAuditAce entry) => entry.Type is 0x11 or 0x12 or 0x13 or 0x14;

    private static int PolicyStart(ImmutableArray<WindowsAuditAce> entries)
    {
        int start = entries.Length;
        while (start > 0 && IsPolicyEntry(entries[start - 1])) start--;
        return start;
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
