using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using Renci.SshNet.Sftp;

namespace FileCat.Remote.Sftp;

internal sealed record SftpPermissionStage(PermissionDelta Change, RemoteSecurityAttributes Before, RemoteSecurityAttributes After);
internal sealed record SftpPermissionPlan(ImmutableArray<SftpPermissionStage> Stages, bool NeedsSetIdConsent);

internal sealed class SftpPermissionMutation(SftpPermissionPathReview review, PermissionSubject subject,
    PermissionServerPath target, bool includesModeWrite)
    : IBoundPermissionMutation
{
    internal const PermissionSections Sections = PermissionSections.Owner | PermissionSections.Group | PermissionSections.Mode;
    internal const string GroupIdRefusal = "Choose a numerical group ID from this SFTP authority.";
    private const ushort PermissionBits = 0xfff, SetGroupId = 0x400, SetIdBits = 0xc00;
    private StatusCode? _status;
    private bool _lastWriteWasMode;
    private RemoteSecurityAttributes? _before;

    internal static ReviewedPermissionOperation Prepare(SftpPermissionPathReview review, PermissionSubject original, ItemRef source,
        SftpPermissionPlan plan, string device)
    {
        var subject = original with { RawLocator = review.Evidence.Path, DisplayPath = review.Evidence.Path };
        var target = Target(review.Authority, subject, subject.RawLocator, device);
        bool includesModeWrite = plan.Stages.Any(stage => stage.Change is ChangePermissionMode && stage.Before != stage.After);
        var mutation = new SftpPermissionMutation(review, subject, target, includesModeWrite);
        var components = plan.Stages.Select(stage =>
        {
            bool mode = stage.Change is ChangePermissionMode;
            return new ReviewedPermissionComponent(stage.Change, Revision(stage.Before), Revision(stage.After),
                mode ? "SFTP lsetstat@openssh.com (mode)" : "SFTP lsetstat@openssh.com (group)");
        }).ToImmutableArray();
        return new(PermissionScope.Objects, [new(source, subject, null, components, components[^1].Expected, mutation, target)]);

        PermissionRevision Revision(RemoteSecurityAttributes state) => PermissionRevision.Read(mutation.Snapshot(state), Sections)!.Value;
    }

    internal static SftpPermissionPlan Preview(SftpPermissionPathReview review, PermissionAuthority authority, PermissionDraft draft)
    {
        if (draft.Scope != PermissionScope.Objects || draft.Changes.IsDefaultOrEmpty || draft.Changes.Length > 2 ||
            draft.Changes.Any(change => change is not (AssignPermissionPrincipal { IsOwner: false } or
                ChangePermissionMode { Bits: <= PermissionBits, TouchedBits: > 0 and <= PermissionBits })) ||
            draft.Changes.Select(change => change.Section).Distinct().Count() != draft.Changes.Length)
            throw new RefusedOperationException("Review one SFTP group assignment, one mode change, or both.");
        var current = review.Evidence.Leaf;
        var stages = ImmutableArray.CreateBuilder<SftpPermissionStage>(4);
        bool setIdConsent = false;
        var mode = draft.Changes.OfType<ChangePermissionMode>().SingleOrDefault();
        var group = draft.Changes.OfType<AssignPermissionPrincipal>().SingleOrDefault();
        uint originalMode = current.Permissions!.Value;
        uint finalMode = mode is null ? originalMode : Patch(originalMode, mode.Bits, mode.TouchedBits);
        uint? groupId = group is null ? null : GroupId(group, authority);
        if (group is not null && groupId != current.GroupId)
        {
            if ((originalMode & SetIdBits & ~(uint)(mode?.TouchedBits ?? 0)) != 0)
                throw new RefusedOperationException("Review the existing set-ID bits explicitly before changing the SFTP group. The server may clear them during a group change.");
            // Both sides of the group switch see a subset of their respective reviewed mode.
            AddMode(originalMode & finalMode & ~(uint)SetIdBits);
            Add(group, current with { GroupId = groupId });
            // Set-ID grants are explicit, and run after the ordinary mode change.
            AddMode(finalMode & ~(uint)SetIdBits);
            AddMode(finalMode);
        }
        else
        {
            if (group is not null) Add(group, current);
            if (mode is not null && originalMode != finalMode && (originalMode & finalMode & SetGroupId) != 0)
            {
                if ((mode.TouchedBits & SetGroupId) == 0)
                    throw new RefusedOperationException("Review the existing set-group-ID bit explicitly. The server may clear it during a mode change.");
                setIdConsent = true;
            }
            if (mode is not null) AddMode(finalMode, mode.TouchedBits);
        }
        return new(stages.ToImmutable(), setIdConsent);

        void Add(PermissionDelta change, RemoteSecurityAttributes next)
        {
            setIdConsent |= ((current.Permissions!.Value ^ next.Permissions!.Value) & SetIdBits) != 0;
            stages.Add(new(change, current, next));
            current = next;
        }

        void AddMode(uint nativeMode, ushort? requestedBits = null)
        {
            ushort changed = (ushort)(current.Permissions!.Value ^ nativeMode);
            if (changed == 0 && requestedBits is null) return;
            if (changed != 0 && review.ModeWritesRefused)
                throw new RefusedOperationException("The server has refused mode changes in this session.");
            Add(new ChangePermissionMode((ushort)(nativeMode & PermissionBits), requestedBits ?? changed),
                current with { Permissions = nativeMode });
        }
    }

    private static uint Patch(uint baseline, ushort bits, ushort touched) => (baseline & ~(uint)touched) | (uint)(bits & touched);

    private static uint GroupId(AssignPermissionPrincipal assignment, PermissionAuthority authority)
    {
        var principal = assignment.Principal;
        if (assignment.IsOwner || principal.Kind != PrincipalKind.UnixGroup || principal.Authority != authority.Id ||
            !TryGroupId(principal.Id, out uint id))
            throw new RefusedOperationException(GroupIdRefusal);
        return id;
    }

    internal static bool TryGroupId(string text, out uint id) =>
        uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
        id < uint.MaxValue && text == id.ToString(CultureInfo.InvariantCulture);

    PermissionSnapshot IBoundPermissionMutation.CheckCurrent(PermissionSections sections, CancellationToken cancellation)
    {
        try
        {
            if (includesModeWrite && review.ModeWritesRefused)
                throw new SftpPermissionPathConflictException("The server has refused mode changes in this session.");
            var observed = review.ReadReviewedLeaf(cancellation);
            _before = observed;
            return Snapshot(observed);
        }
        catch (SftpPermissionPathConflictException error) { throw new PermissionBindingConflictException(error.Message); }
    }

    PermissionMutationReturn IBoundPermissionMutation.Apply(PermissionDelta change)
    {
        _lastWriteWasMode = change is ChangePermissionMode;
        _status = null;
        var before = _before ?? throw new InvalidOperationException("Review the current SFTP state before dispatch.");
        var wireChange = change switch
        {
            ChangePermissionMode mode => SftpSecurityChange.Mode(before.Permissions!.Value, mode.Bits),
            AssignPermissionPrincipal group => SftpSecurityChange.Group(review.UnchangedUser(before.UserId!.Value), GroupId(group, subject.Authority)),
            _ => throw new InvalidOperationException("The reviewed SFTP change requires a group or mode.")
        };
        try { _status = review.Write(wireChange, _lastWriteWasMode); }
        catch (SftpPermissionPathConflictException error) { return new(BindingConflict: true, Detail: error.Message); }
        catch (RemoteDisconnectedException) { return new(Detail: "The reply was lost. The change may have reached the server."); }
        return _status is { } errorStatus
            ? new((int)errorStatus, RejectedBeforeChange: errorStatus is StatusCode.Failure or StatusCode.PermissionDenied or StatusCode.OperationUnsupported,
                Detail: _lastWriteWasMode ? "The server could not apply this mode change." : "The server could not apply this group change.")
            : new();
    }

    PermissionReadBack IBoundPermissionMutation.Observe(PermissionSections sections)
    {
        var observed = review.ReadAfterDispatch();
        if (_lastWriteWasMode && _status == StatusCode.Failure && observed == _before && !review.LostReply)
            review.RefuseModeWrites();
        return new(Snapshot(observed), PermissionVerification.ServerPath,
            Detail: review.LostReply ? "The server path was observed again after the connection was lost." : null,
            Reconnected: review.LostReply);
    }

    private PermissionSnapshot Snapshot(RemoteSecurityAttributes observed)
    {
        const string source = "SFTP LSTAT";
        var now = DateTimeOffset.UtcNow;
        return PermissionSnapshots.Create(subject, PermissionModel.ServerAttributes, null, Sections, now, source) with
        {
            Owner = PrincipalObservation(PrincipalKind.UnixUser, observed.UserId!.Value),
            Group = PrincipalObservation(PrincipalKind.UnixGroup, observed.GroupId!.Value),
            Mode = new(PermissionReadStatus.Present, source, now, (ushort)(observed.Permissions!.Value & PermissionBits)),
            ServerState = ServerState(target, observed)
        };

        PermissionObservation<Principal> PrincipalObservation(PrincipalKind kind, uint id) =>
            new(PermissionReadStatus.Present, source, now, new Principal(kind, id.ToString(CultureInfo.InvariantCulture), subject.Authority.Id));
    }

    internal static PermissionServerState ServerState(PermissionServerPath target, RemoteSecurityAttributes observed) =>
        new(target, observed.Flags, observed.Permissions!.Value, observed.Size!.Value, observed.ModifiedUtc!.Value.Ticks);

    internal static PermissionServerPath Target(SftpServerAuthority authority, PermissionSubject subject, string path, string device) =>
        new(subject.Authority, path, TargetDigest(authority, path), device);

    private static string TargetDigest(SftpServerAuthority authority, string path)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        writer.Write(authority.ProfileId);
        writer.Write(authority.Host);
        writer.Write(authority.Port);
        writer.Write(authority.User);
        writer.Write(authority.Auth);
        writer.Write(authority.KeyFile ?? "");
        writer.Write(authority.InitialPath ?? "");
        writer.Write(authority.HostKeySha256);
        writer.Write(authority.Generation);
        writer.Write(path);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length)));
    }

    public void Dispose() => review.Dispose();
}
