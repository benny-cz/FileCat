using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Ftp;

internal sealed partial class FtpChannel
{
    internal PermissionSnapshot[] InspectPermissions(string folder, IReadOnlyList<PermissionSubject> subjects, string protocol,
        PermissionAuthority authority, CancellationToken cancellation)
    {
        Safe(folder);
        if (!folder.StartsWith('/') || folder.Contains('\uFFFD') || folder.Contains("//", StringComparison.Ordinal) ||
            folder.Split('/').Any(part => part is "." or ".."))
            throw new IOException("The server folder does not have an unambiguous absolute path.");
        var names = subjects.Where(subject => CanInspectName(subject.RawLocator)).Select(subject => subject.RawLocator).ToHashSet(StringComparer.Ordinal);
        var found = names.Count == 0 ? null : List(folder, cancellation, job: false, permissionNames: names).Cast<FtpEntry>()
            .Where(entry => names.Contains(entry.Name)).ToLookup(entry => entry.Name, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var results = new PermissionSnapshot[subjects.Count];
        for (int i = 0; i < results.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var subject = subjects[i];
            if (!names.Contains(subject.RawLocator))
            {
                results[i] = Unavailable(subject, PermissionReadStatus.Unsupported, "FTP cannot inspect this exact name.");
                continue;
            }
            var current = subject with { Authority = authority, DisplayPath = RemotePath.Combine(folder, subject.RawLocator) };
            var entries = found![subject.RawLocator].Take(2).ToArray();
            if (entries.Length != 1)
            {
                results[i] = Unavailable(current, entries.Length == 0 ? PermissionReadStatus.Gone : PermissionReadStatus.Stale, entries.Length == 0
                    ? "The selected name is missing from the server listing." : "The server listed the selected name more than once.");
                continue;
            }
            var entry = entries[0];
            if (entry.IsLink || entry.IsDirectory != (subject.Kind == PermissionObjectKind.Directory) ||
                !PermissionSnapshots.MatchesListing(subject, entry.Size, entry.ModifiedUtc.Ticks))
            {
                results[i] = Unavailable(current, PermissionReadStatus.Stale, "The selected entry could not be corroborated in the current server listing.");
                continue;
            }
            if (entry.PermissionFacts is not { } facts)
            {
                results[i] = Unavailable(current, PermissionReadStatus.Unsupported,
                    "The server listing did not provide qualified permission information for this name.");
                continue;
            }

            var snapshot = PermissionSnapshots.Create(current, PermissionModel.ServerAttributes, null, PermissionSections.Ordinary, now, facts.Source,
                missingReason: "The server did not report this field.") with
            {
                Ftp = new(protocol, facts.Source, now, facts.TransferRights, facts.Unique, facts.OwnerName, facts.GroupName)
            };
            if (facts.Mode is ushort mode) snapshot = snapshot with { Mode = new(PermissionReadStatus.Present, facts.Source, now, mode) };
            if (facts.ListedAccessRules)
                snapshot = snapshot with { Access = new(PermissionReadStatus.Incomplete, facts.Source, now,
                    Reason: "The server listing marks access rules beyond the mode that FTP does not describe.") };
            else if (facts.InvalidMode) snapshot = snapshot with { Mode = new(PermissionReadStatus.Incomplete, facts.Source, now, Reason: "The server supplied an invalid or repeated mode fact.") };
            if (facts.Owner is uint uid) snapshot = snapshot with { Owner = PrincipalObservation(PrincipalKind.UnixUser, uid, facts.Source) };
            if (facts.Group is uint gid) snapshot = snapshot with { Group = PrincipalObservation(PrincipalKind.UnixGroup, gid, facts.Source) };
            results[i] = PermissionSnapshots.WithCapabilities(snapshot, PermissionSections.Ordinary);
        }
        return results;

        PermissionObservation<Principal> PrincipalObservation(PrincipalKind kind, uint id, string source) =>
            new(PermissionReadStatus.Present, source, now, new Principal(kind, id.ToString(CultureInfo.InvariantCulture), authority.Id));
    }

    internal static PermissionSnapshot Unavailable(PermissionSubject subject, PermissionReadStatus status, string reason)
    {
        using var unavailable = PermissionSnapshots.Unavailable(subject, PermissionSections.Ordinary, status, reason);
        return unavailable.Snapshot;
    }

    internal static bool CanInspectName(string name) => RemotePath.ProblemWithName(name) is null && !name.Contains('\uFFFD') &&
        name.IndexOfAny(['\r', '\n', '\\']) < 0 && !char.IsWhiteSpace(name[0]) && !char.IsWhiteSpace(name[^1]);

    internal static FtpPermissionFacts? ReadPermissionFacts(string? raw, string name, bool directory, bool machine)
    {
        if (machine) return FtpPermissionFacts.FromMlsd(raw, name, directory);
        if (string.IsNullOrEmpty(raw) || raw[0] != (directory ? 'd' : '-') || UnixListingPrefix().Match(raw) is not { Success: true } prefix ||
            !raw.AsSpan(prefix.Length).SequenceEqual(name.AsSpan()))
            return null;
        return FtpPermissionFacts.ListedMode(raw.AsSpan(1, 9)) is ushort mode ? new("Unix LIST", mode) { ListedAccessRules = raw[10] == '+' } : null;
    }
}
