using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Renci.SshNet.Sftp;

namespace FileCat.Remote.Sftp;

public sealed partial class SftpProvider
{
    internal ReviewedPermissionOperation PrepareSftpPermissionOperation(PermissionSubject subject, ItemRef source, PermissionDraft draft,
        CancellationToken cancellation)
    {
        var review = PrepareSftpPermissionPathReview(subject, cancellation);
        try
        {
            var plan = SftpPermissionMutation.Preview(review, subject.Authority, draft);
            return SftpPermissionMutation.Prepare(review, subject, source, plan, GetDeviceKey(source.Parent));
        }
        catch { review.Dispose(); throw; }
    }

    internal SftpPermissionEdit BeginSftpPermissionEdit(PermissionSnapshot snapshot, ItemRef selected, CancellationToken ct)
    {
        var subject = snapshot.Subject;
        if (snapshot.Sftp is not { ChangeRefusal: null } || snapshot.ServerState is not { } expected ||
            selected.Parent != subject.OriginalLocation || selected.Name != subject.RawLocator || selected.Flags.HasFlag(EntryFlags.Link) ||
            selected.Kind != (subject.Kind == PermissionObjectKind.File ? EntryKind.File : EntryKind.Directory))
            throw new RefusedOperationException("Inspect the original SFTP file or folder before reviewing changes.");
        string device = GetDeviceKey(selected.Parent);
        var review = PrepareSftpPermissionPathReview(subject, ct);
        try
        {
            if (!review.EditorQualified)
                throw new RefusedOperationException(SftpServerQualification.EditorRefusal);
            var leaf = review.Evidence.Leaf;
            var target = SftpPermissionMutation.Target(review.Authority, subject, review.Evidence.Path, device);
            if (expected != SftpPermissionMutation.ServerState(target, leaf) ||
                snapshot.Mode.Value != (ushort)(leaf.Permissions!.Value & 0xfff) ||
                snapshot.Owner.Value?.Id != leaf.UserId?.ToString(CultureInfo.InvariantCulture) ||
                snapshot.Group.Value?.Id != leaf.GroupId?.ToString(CultureInfo.InvariantCulture))
                throw new SftpPermissionPathConflictException("The original server path, connection or permissions changed. Inspect the item again.");
            return new(review, subject, selected, device);
        }
        catch { review.Dispose(); throw; }
    }

    internal SftpPermissionPathReview PrepareSftpPermissionPathReview(PermissionSubject subject, CancellationToken cancellation)
    {
        if (ReviewedPermissionOperation.ProcessRefusal() is { } processRefusal)
            throw new RefusedOperationException(processRefusal);
        var location = subject.OriginalLocation;
        if (location.Scheme != Schemes.Sftp || Profile(location) is not { Protocol: RemoteProtocols.Sftp } profile ||
            subject.Authority.Kind != PermissionAuthorityKind.RemoteSession || subject.Authority.Id != profile.Id ||
            subject.LinkPolicy != PermissionLinkPolicy.NoFollow ||
            subject.Kind is not (PermissionObjectKind.File or PermissionObjectKind.Directory) ||
            RemotePath.ProblemWithName(subject.RawLocator) is not null)
            throw new RefusedOperationException("SFTP permission review requires a selected file or folder and its connection.");

        var lease = Lease(location, cancellation);
        try
        {
            var folder = Resolve(location, lease.Channel);
            if (!folder.StartsWith('/'))
                throw new RefusedOperationException("This SFTP review requires the selected folder's absolute server path.");
            return new(lease, profile, () => Profile(location), ct => Lease(location, ct), subject,
                RemotePath.Combine(folder, subject.RawLocator), cancellation);
        }
        catch (Exception error)
        {
            if (error is RemoteDisconnectedException) lease.Broken = true;
            lease.Dispose();
            throw;
        }
    }

    public PermissionSnapshot[] InspectSftpPermissions(Location location, IReadOnlyList<PermissionSubject> subjects, CancellationToken ct)
    {
        if (Profile(location) is not { Protocol: RemoteProtocols.Sftp } profile ||
            subjects.Any(subject => subject.OriginalLocation != location || subject.Authority.Kind != PermissionAuthorityKind.RemoteSession ||
                subject.Authority.Id != profile.Id || subject.Kind is not (PermissionObjectKind.File or PermissionObjectKind.Directory or PermissionObjectKind.Link)))
            return AllUnavailable(PermissionReadStatus.Unsupported, "These entries lack a configured SFTP authority.");

        try
        {
            using var lease = Lease(location, ct);
            if (lease.Channel is not SshNetChannel channel)
                return AllUnavailable(PermissionReadStatus.Unsupported, "This connection has no exact-path SFTP permissions reader.");
            try
            {
                var authority = channel.CaptureAuthority(profile, lease.Generation);
                var extensions = channel.SecurityExtensions();
                string? changeRefusal = SftpServerQualification.Refusal(channel.ServerVersion, extensions)
                    ?? (SftpServerQualification.SupportsUnchangedUid(channel.ServerVersion) ? null : SftpServerQualification.EditorRefusal);
                string? modeRefusal = lease.ModeWritesRefused ? "The server has refused mode changes in this session." : null;
                string folder = Resolve(location, channel);
                var names = new Dictionary<(uint User, uint Group), SftpAccountNames?>();
                var observations = subjects.Select(subject => Read(subject, folder, channel, authority, GetDeviceKey(location),
                    changeRefusal, modeRefusal, names, ct)).ToArray();
                if (!lease.IsCurrent || Profile(location) != profile || channel.CaptureAuthority(profile, lease.Generation) != authority)
                    return AllUnavailable(PermissionReadStatus.Stale, "The SFTP connection changed during inspection. Refresh the folder and try again.");
                return observations;
            }
            catch (RemoteDisconnectedException) { lease.Broken = true; throw; }
        }
        catch (NotSupportedException error) { return AllUnavailable(PermissionReadStatus.Unsupported, error.Message); }
        catch (UnauthorizedAccessException) { return AllUnavailable(PermissionReadStatus.Denied, "The server denied the permissions read."); }
        catch (IOException error) { return AllUnavailable(PermissionReadStatus.Failed, error.Message); }

        PermissionSnapshot[] AllUnavailable(PermissionReadStatus status, string reason) =>
            subjects.Select(subject => Unavailable(subject, status, reason)).ToArray();
    }

    private static PermissionSnapshot Read(PermissionSubject subject, string folder, SshNetChannel channel, SftpServerAuthority authority,
        string device, string? changeRefusal, string? modeRefusal, Dictionary<(uint User, uint Group), SftpAccountNames?> names, CancellationToken ct)
    {
        if (RemotePath.ProblemWithName(subject.RawLocator) is not null)
            return Unavailable(subject, PermissionReadStatus.Unsupported, "The selected name cannot be addressed exactly.");
        string path = RemotePath.Combine(folder, subject.RawLocator);
        var current = subject with { DisplayPath = path, Authority = subject.Authority with { Generation = authority.Generation } };
        RemoteSecurityAttributes observed;
        try { observed = channel.ReadExactSecurity(path, ct); }
        catch (ArgumentException) { return Unavailable(current, PermissionReadStatus.Unsupported, "The selected SFTP path cannot be encoded exactly."); }
        catch (FileNotFoundException) { return Unavailable(current, PermissionReadStatus.Gone, "The selected SFTP path is gone."); }
        catch (UnauthorizedAccessException) { return Unavailable(current, PermissionReadStatus.Denied, "The server denied the permissions read."); }
        catch (IOException error) when (error is not RemoteDisconnectedException)
        { return Unavailable(current, PermissionReadStatus.Failed, error.Message); }

        uint? type = observed.Permissions & 0xf000u;
        bool typeMatches = type switch
        {
            null or 0 => true,
            0xa000 => subject.Kind == PermissionObjectKind.Link,
            0x4000 => subject.Kind == PermissionObjectKind.Directory,
            _ => subject.Kind == PermissionObjectKind.File
        };
        if (!typeMatches ||
            subject.ListedSize is long expectedSize && observed.Size is ulong size && (ulong)expectedSize != size ||
            subject.ListedModifiedUtcTicks is long expectedTime && observed.ModifiedUtc is DateTime mtime && expectedTime != mtime.Ticks)
            return Unavailable(current, PermissionReadStatus.Stale, "The current SFTP attributes differ from the selected listing.");

        const string source = "SFTP LSTAT";
        var now = DateTimeOffset.UtcNow;
        bool ordinary = (type is 0x4000 or 0x8000) && (subject.Kind is PermissionObjectKind.File or PermissionObjectKind.Directory);
        bool complete = (observed.Flags & 0xfu) == 0xfu && observed.Size.HasValue && observed.ModifiedUtc.HasValue &&
            observed.UserId is < uint.MaxValue && observed.GroupId is < uint.MaxValue;
        changeRefusal ??= !ordinary ? "SFTP changes require an ordinary file or folder."
            : !complete ? "The server must report the complete mode, owner, group, size and modification time."
            : authority.User == "root" || observed.UserId == 0 ? "SFTP permission changes require a non-root owner and account."
            : null;
        var snapshot = PermissionSnapshots.Create(current, PermissionModel.ServerAttributes, null, PermissionSections.Ordinary, now, source,
            missingReason: "The server did not report this field.") with
        {
            Sftp = new(now, type switch
            {
                0x4000 => "Folder",
                0x8000 => "File",
                0xa000 => "Symbolic link",
                0x1000 => "FIFO",
                null or 0 => "Type unreported",
                _ => "Special object"
            }, changeRefusal, Connection: authority.Display, ModeChangeRefusal: modeRefusal),
            ServerState = ordinary && complete
                ? SftpPermissionMutation.ServerState(SftpPermissionMutation.Target(authority, current, path, device), observed) : null
        };
        if (observed.Permissions is uint permissions)
            snapshot = snapshot with { Mode = new(PermissionReadStatus.Present, source, now, (ushort)(permissions & 0xfff)) };
        if (observed.UserId is uint uid && observed.GroupId is uint gid)
        {
            if (!names.TryGetValue((uid, gid), out var labels))
            {
                try { labels = channel.LookupSecurityNames(uid, gid, ct); }
                catch (IOException error) when (error is not RemoteDisconnectedException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Text.DecoderFallbackException) { }
                names.Add((uid, gid), labels);
            }
            snapshot = snapshot with
            {
                Owner = Principal(PrincipalKind.UnixUser, uid), Group = Principal(PrincipalKind.UnixGroup, gid),
                Sftp = snapshot.Sftp! with { UserName = labels?.User, GroupName = labels?.Group }
            };
        }
        return PermissionSnapshots.WithCapabilities(snapshot, PermissionSections.Ordinary);

        PermissionObservation<Principal> Principal(PrincipalKind kind, uint id) => new(PermissionReadStatus.Present, source, now,
            new Principal(kind, id.ToString(CultureInfo.InvariantCulture), authority.ProfileId));
    }

    private static PermissionSnapshot Unavailable(PermissionSubject subject, PermissionReadStatus status, string reason)
    {
        using var inspection = PermissionSnapshots.Unavailable(subject, PermissionSections.Ordinary, status, reason, "SFTP LSTAT");
        return inspection.Snapshot;
    }
}
