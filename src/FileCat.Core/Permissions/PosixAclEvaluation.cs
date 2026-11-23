using System.Collections.Immutable;
using System.Globalization;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Permissions;

internal enum PosixAccessClass { Owner, NamedUser, MatchingGroup, Other, IncompleteGroups }
internal readonly record struct PosixAccessEstimate(bool? Granted, PosixAccessClass AccessClass);

internal sealed class PosixAclEvaluation
{
    private readonly uint _owner;
    private readonly byte _ownerRights;
    private readonly byte _otherRights;
    private readonly byte _mask;
    private readonly ImmutableDictionary<uint, byte> _users;
    private readonly ImmutableArray<(uint Id, byte Rights)> _groups;

    private PosixAclEvaluation(uint owner, uint group, ImmutableArray<PosixAclEntry> rows)
    {
        _owner = owner;
        _ownerRights = rows.Single(row => row.Tag == PosixAclTag.Owner).Rights;
        _otherRights = rows.Single(row => row.Tag == PosixAclTag.Other).Rights;
        _mask = PosixAccessDraft.Mask(rows);
        _users = rows.Where(row => row.Tag == PosixAclTag.NamedUser)
            .ToImmutableDictionary(row => ParseId(row.Principal!.Value.Id), row => row.Rights);
        _groups = [(group, rows.Single(row => row.Tag == PosixAclTag.OwningGroup).Rights),
            .. rows.Where(row => row.Tag == PosixAclTag.NamedGroup).Select(row => (ParseId(row.Principal!.Value.Id), row.Rights))];
    }

    internal static PosixAclEvaluation? FromSnapshot(PermissionSnapshot snapshot)
    {
        if (snapshot.Model != PermissionModel.PosixAcl ||
            snapshot.Owner is not { Status: PermissionReadStatus.Present, Value: { Kind: PrincipalKind.UnixUser } owner } ||
            snapshot.Group is not { Status: PermissionReadStatus.Present, Value: { Kind: PrincipalKind.UnixGroup } group } ||
            owner.Authority != snapshot.Subject.Authority.Id || group.Authority != snapshot.Subject.Authority.Id ||
            !TryParseId(owner.Id, out uint uid) || !TryParseId(group.Id, out uint gid))
            return null;
        bool currentAclKnown = snapshot.Access is { Status: PermissionReadStatus.Present, Value: { Acl: PosixAcl } } ||
            snapshot.Access.Status == PermissionReadStatus.Absent && snapshot.Mode is { Status: PermissionReadStatus.Present, Value: not null };
        return currentAclKnown ? new(uid, gid, LinuxPermissionAcl.AccessRows(snapshot)) : null;
    }

    internal PosixAccessEstimate Evaluate(byte requested, uint user, IReadOnlySet<uint> groups, bool groupsComplete)
    {
        if (requested is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(requested));
        if (user == _owner) return new(Allows(_ownerRights, requested), PosixAccessClass.Owner);
        if (_users.TryGetValue(user, out byte userRights)) return new(Allows(userRights & _mask, requested), PosixAccessClass.NamedUser);

        bool matchedGroup = false, unlistedGroupCouldAllow = false;
        foreach (var (id, rights) in _groups)
        {
            bool permits = Allows(rights & _mask, requested);
            if (groups.Contains(id))
            {
                matchedGroup = true;
                // A single matched group must grant the entire request.
                if (permits) return new(true, PosixAccessClass.MatchingGroup);
            }
            else unlistedGroupCouldAllow |= permits;
        }
        if (matchedGroup && (groupsComplete || !unlistedGroupCouldAllow)) return new(false, PosixAccessClass.MatchingGroup);
        if (groupsComplete) return new(Allows(_otherRights, requested), PosixAccessClass.Other);
        return new(null, PosixAccessClass.IncompleteGroups);
    }

    internal static bool TryParseId(string? text, out uint id)
    {
        id = 0;
        return text is { Length: > 0 and <= 10 } && text.All(char.IsAsciiDigit) &&
            uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id < uint.MaxValue;
    }

    private static uint ParseId(string text) => uint.Parse(text, CultureInfo.InvariantCulture);
    private static bool Allows(int configured, byte requested) => (configured & requested) == requested;
}
