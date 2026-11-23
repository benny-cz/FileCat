using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed record DarwinGroupAuthority(uint User, uint PrimaryGroup, ImmutableArray<uint> Groups)
{
    internal bool Includes(uint group) => Groups.Contains(group);

    internal bool Matches(DarwinGroupAuthority other) =>
        User == other.User && PrimaryGroup == other.PrimaryGroup && Groups.AsSpan().SequenceEqual(other.Groups.AsSpan());

    internal Principal[] Choices(PermissionAuthority authority) => Groups
        .Select(id => new Principal(PrincipalKind.UnixGroup, id.ToString(CultureInfo.InvariantCulture), authority.Id)).ToArray();
}

internal sealed unsafe partial class DarwinPermissionService
{
    private const ushort AccessBits = 0x1ff, StickyBit = 0x200;
    private const uint PreserveOwner = uint.MaxValue;

    internal static bool CanEditGroup(PermissionSnapshot snapshot) => snapshot.Capabilities.Any(capability => capability is
        { Section: PermissionSections.Group, Action: PermissionAction.Change, Availability: PermissionAvailability.Available });

    internal const PermissionSections AccessChangeSections = WriteSecuritySections;

    internal static DarwinGroupAuthority ReadGroupAuthority(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        uint user = EffectiveUser(), group = EffectiveGroup();
        if (user == 0 || user != RealUser() || group != RealGroup())
            throw new UnauthorizedAccessException("This group edit requires an ordinary macOS user with matching real and effective IDs.");
        var groups = ReadGroupIds().Append(group).Distinct().Order().ToImmutableArray();
        return new(user, group, groups);
    }

    private static uint[] ReadGroupIds()
    {
        int count = SupplementaryGroups(0, null);
        if (count is < 0 or > 65536) throw new IOException("The supplementary groups could not be verified.");
        var groups = new uint[count];
        fixed (uint* ids = groups)
            if (SupplementaryGroups(count, ids) != count) throw new IOException("The supplementary groups changed during review.");
        return groups;
    }

    private static (uint Prior, uint Requested) ReviewGroup(PermissionSnapshot original, AssignPermissionPrincipal assignment, DarwinGroupAuthority? authority)
    {
        if (!CanEditGroup(original)) throw new NotSupportedException("This group change requires a qualified Darwin file or directory binding.");
        if (authority is null || original.Owner.Value?.Id != authority.User.ToString(CultureInfo.InvariantCulture))
            throw new UnauthorizedAccessException("The original object is no longer owned by the ordinary macOS user.");
        if (!TryGroupId(assignment.Principal, original.Subject.Authority, out uint requested) || !authority.Includes(requested))
            throw new UnauthorizedAccessException("Choose a group in FileCat's ordinary macOS credential.");
        if (original.Group.Value is not { } prior || !TryGroupId(prior, original.Subject.Authority, out uint current))
            throw new NotSupportedException("The original Darwin group could not be verified.");
        return (current, requested);
    }

    private static bool TryGroupId(Principal principal, PermissionAuthority authority, out uint group)
    {
        group = 0;
        return principal.Kind == PrincipalKind.UnixGroup && principal.Authority == authority.Id &&
            uint.TryParse(principal.Id, NumberStyles.None, CultureInfo.InvariantCulture, out group) && group != PreserveOwner;
    }

    private static bool GroupAuthorityMatches(DarwinGroupAuthority? authority, CancellationToken cancellation = default) =>
        authority is null || authority.Matches(ReadGroupAuthority(cancellation));

    private static int SetGroup(SafeFileHandle handle, string? leaf, PermissionSubject subject, AssignPermissionPrincipal change)
    {
        if (change.IsOwner || !TryGroupId(change.Principal, subject.Authority, out uint group))
            throw new InvalidOperationException("The reviewed Darwin mutation requires a group ID.");
        return leaf is null ? SetHeldGroup(handle, PreserveOwner, group)
            : SetNamedGroup(handle, leaf, PreserveOwner, group, AtNoFollowAny);
    }

    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint RealUser();
    [LibraryImport("libc", EntryPoint = "getgid")]
    private static partial uint RealGroup();
    [LibraryImport("libc", EntryPoint = "fchown", SetLastError = true)]
    private static partial int SetHeldGroup(SafeFileHandle handle, uint owner, uint group);
    [LibraryImport("libc", EntryPoint = "fchownat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int SetNamedGroup(SafeFileHandle handle, string name, uint owner, uint group, int flags);
}
