using System.Collections.Immutable;
using System.Globalization;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Permissions;

/// <summary>Edits individual access entries while keeping their canonical keys and the reviewed group-class limit.</summary>
internal sealed class PosixAccessDraft(PermissionSnapshot snapshot, bool isDefault = false)
{
    private readonly string _authority = snapshot.Subject.Authority.Id;

    internal ImmutableArray<PosixAclEntry> Rows { get; private set; } = isDefault ? DefaultRows(snapshot) : LinuxPermissionAcl.AccessRows(snapshot);

    internal static ImmutableArray<PosixAclEntry> DefaultRows(PermissionSnapshot snapshot) =>
        snapshot.Defaults.Value?.Acl is PosixAcl acl ? acl.Entries : [];

    internal void CreateDefault()
    {
        if (isDefault && Rows.IsEmpty) Rows = BaseAccess(LinuxPermissionAcl.AccessRows(snapshot));
    }

    internal void RemoveDefault()
    {
        if (isDefault) Rows = [];
    }

    internal static byte Mask(ImmutableArray<PosixAclEntry> rows) =>
        rows.FirstOrDefault(row => row.Tag == PosixAclTag.Mask, new(PosixAclTag.Mask, 7, null)).Rights;

    internal static bool IsNamed(PosixAclEntry row) => row.Tag is PosixAclTag.NamedUser or PosixAclTag.NamedGroup;

    internal static byte EffectiveRights(PosixAclEntry row, byte mask) =>
        row.Tag is PosixAclTag.NamedUser or PosixAclTag.NamedGroup or PosixAclTag.OwningGroup ? (byte)(row.Rights & mask) : row.Rights;

    internal string? ValidateNewPrincipal(bool group, string? text, out Principal principal)
    {
        principal = default;
        if (Rows.IsEmpty) return "Create a default ACL before adding named entries.";
        if (text is not { Length: > 0 and <= 10 } || !text.All(char.IsAsciiDigit) ||
            !uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) || id == uint.MaxValue)
            return "Enter a numeric ID from 0 to 4294967294.";

        principal = new(group ? PrincipalKind.UnixGroup : PrincipalKind.UnixUser, id.ToString(CultureInfo.InvariantCulture), _authority);
        var proposed = principal;
        if (Rows.Any(row => row.Principal == proposed))
            return "This account already has an entry.";
        int needed = Rows.Any(row => row.Tag == PosixAclTag.Mask) ? 1 : 2;
        return Rows.Length + needed > PermissionSnapshots.MaxAclEntries ? "The ACL has reached the entry limit." : null;
    }

    internal bool TryAdd(bool group, string? text, byte rights, out int index)
    {
        index = -1;
        if (rights > 7 || ValidateNewPrincipal(group, text, out var principal) is not null) return false;
        var added = new PosixAclEntry(group ? PosixAclTag.NamedGroup : PosixAclTag.NamedUser, rights, principal);
        var rows = Rows.Add(added);
        if (!Rows.Any(row => row.Tag == PosixAclTag.Mask))
        {
            byte owning = Rows.First(row => row.Tag == PosixAclTag.OwningGroup).Rights;
            rows = rows.Add(new(PosixAclTag.Mask, (byte)(owning | rights), null));
        }
        Rows = rows.OrderBy(row => row.Tag).ThenBy(row => row.Principal).ToImmutableArray();
        index = Rows.IndexOf(added);
        return true;
    }

    internal void SetRights(int index, byte rights)
    {
        if (rights > 7) throw new ArgumentOutOfRangeException(nameof(rights));
        Rows = Rows.SetItem(index, Rows[index] with { Rights = rights });
    }

    internal bool Remove(int index)
    {
        if (!IsNamed(Rows[index])) return false;
        Rows = Rows.RemoveAt(index);
        return true;
    }

    internal void Collapse() => Rows = BaseAccess(Rows);

    private static ImmutableArray<PosixAclEntry> BaseAccess(ImmutableArray<PosixAclEntry> rows)
    {
        byte mask = Mask(rows);
        return rows.Where(row => row.Tag is PosixAclTag.Owner or PosixAclTag.OwningGroup or PosixAclTag.Other)
            .Select(row => row with { Rights = EffectiveRights(row, mask) }).ToImmutableArray();
    }
}
