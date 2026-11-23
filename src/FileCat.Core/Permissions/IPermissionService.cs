using FileCat.Core.FileSystem;

namespace FileCat.Core.Permissions;

public interface IPermissionService
{
    /// <summary>May block in a native call. Schedule off the UI thread and dispose the result after the last consumer.</summary>
    PermissionInspection Inspect(PermissionSubject subject, PermissionSections sections, CancellationToken cancellationToken);
}

/// <summary>A snapshot and, on bound routes, the lease preventing reuse of the captured object's identity.</summary>
public sealed class PermissionInspection : IDisposable
{
    internal PermissionInspection(PermissionSnapshot snapshot, IDisposable? lease = null)
    {
        Snapshot = snapshot;
        _lease = lease;
    }

    private IDisposable? _lease;
    public PermissionSnapshot Snapshot { get; }

    internal T? TransferLease<T>() where T : class, IDisposable
    {
        var lease = Volatile.Read(ref _lease) as T;
        return lease is not null && ReferenceEquals(Interlocked.CompareExchange(ref _lease, null, lease), lease) ? lease : null;
    }

    public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();
}

public sealed class NoPermissionService : IPermissionService
{
    public PermissionInspection Inspect(PermissionSubject subject, PermissionSections sections, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Unsupported,
            "No permissions reader is available for this object.");
    }
}

internal static class PermissionSnapshots
{
    internal const int MaxSectionBytes = 1 << 20, MaxAclEntries = 4096, MaxPathDepth = 256;

    internal static bool IsFileSystemSubject(PermissionSubject subject) =>
        subject.OriginalLocation is { IsFileSystem: true, Session: null, Container: null } &&
        subject.Kind is not (PermissionObjectKind.Virtual or PermissionObjectKind.Link);

    internal static bool IsLocal(PermissionSubject subject) =>
        IsFileSystemSubject(subject) && subject.Authority.Kind == PermissionAuthorityKind.Local;

    internal static bool Matches(PermissionSubject subject, PermissionIdentity identity) =>
        (subject.Kind == PermissionObjectKind.Unknown || subject.Kind == identity.Kind ||
            (subject.Kind == PermissionObjectKind.Directory && identity.Kind == PermissionObjectKind.VolumeRoot)) &&
        subject.Authority == identity.Authority && (subject.ExpectedIdentity is null || subject.ExpectedIdentity == identity) &&
        (subject.ExpectedObject is null || subject.ExpectedObject == identity.Object);

    internal static bool MatchesListing(PermissionSubject subject, long? size, long? modifiedUtcTicks) =>
        (subject.ListedSize is null || subject.ListedSize == size) &&
        (subject.ListedModifiedUtcTicks is null || subject.ListedModifiedUtcTicks == modifiedUtcTicks);

    internal static PermissionInspection Unavailable(PermissionSubject subject, PermissionSections sections, PermissionReadStatus status,
        string reason, string source = "FileCat", int? error = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var snapshot = Create(subject, PermissionModel.Unknown, null, sections, now, source, status, reason, error) with { BindingProblem = reason };
        return new(WithCapabilities(snapshot, sections));
    }

    internal static PermissionSnapshot Create(PermissionSubject subject, PermissionModel model, PermissionBinding? binding,
        PermissionSections sections, DateTimeOffset now, string source, PermissionReadStatus missingStatus = PermissionReadStatus.Unsupported,
        string missingReason = "This section is unavailable on the active permissions route.", int? error = null)
    {
        PermissionObservation<T> Missing<T>(PermissionSections section) where T : struct => (sections & section) == 0
            ? new(PermissionReadStatus.Unloaded, source, now)
            : new(missingStatus, source, now, Reason: missingReason, NativeError: error);

        return new(subject, model, binding, Missing<Principal>(PermissionSections.Owner),
            Missing<Principal>(PermissionSections.Group), Missing<ushort>(PermissionSections.Mode),
            Missing<PermissionAclValue>(PermissionSections.Access), Missing<PermissionAclValue>(PermissionSections.Defaults),
            Missing<WindowsAclControl>(PermissionSections.Inheritance), Missing<PermissionAclValue>(PermissionSections.Audit),
            Missing<PermissionFlagsValue>(PermissionSections.Flags), Missing<PermissionVolumePolicy>(PermissionSections.VolumePolicy), [])
            { Times = Missing<PermissionTimesValue>(PermissionSections.Times) };
    }

    internal static PermissionSnapshot WithCapabilities(PermissionSnapshot snapshot, PermissionSections sections)
    {
        var capabilities = System.Collections.Immutable.ImmutableArray.CreateBuilder<PermissionCapability>();
        Add(PermissionSections.Owner, snapshot.Owner);
        Add(PermissionSections.Group, snapshot.Group);
        Add(PermissionSections.Mode, snapshot.Mode);
        Add(PermissionSections.Access, snapshot.Access);
        Add(PermissionSections.Defaults, snapshot.Defaults, PermissionScope.DefaultsForNewChildren);
        Add(PermissionSections.Inheritance, snapshot.Inheritance);
        Add(PermissionSections.Audit, snapshot.Audit);
        Add(PermissionSections.Flags, snapshot.Flags);
        if (snapshot.Times is { } times) Add(PermissionSections.Times, times);
        Add(PermissionSections.VolumePolicy, snapshot.VolumePolicy, PermissionScope.VolumePolicy);
        return snapshot with { Capabilities = capabilities.ToImmutable() };

        void Add<T>(PermissionSections section, PermissionObservation<T> observation, PermissionScope scope = PermissionScope.Objects) where T : struct
        {
            if ((sections & section) == 0) return;
            var availability = observation.Status switch
            {
                PermissionReadStatus.Present or PermissionReadStatus.Absent => PermissionAvailability.Available,
                PermissionReadStatus.Denied => PermissionAvailability.Denied,
                PermissionReadStatus.Unsupported => PermissionAvailability.Unsupported,
                PermissionReadStatus.Stale or PermissionReadStatus.Gone => PermissionAvailability.Stale,
                _ => PermissionAvailability.Unavailable
            };
            capabilities.Add(new(section, PermissionAction.Inspect, scope, availability, observation.Reason));
            var change = availability != PermissionAvailability.Available ? availability : snapshot.Binding?.ReadOnly == true
                ? PermissionAvailability.ReadOnly : PermissionAvailability.NotQualified;
            capabilities.Add(new(section, PermissionAction.Change, scope, change, change switch
            {
                PermissionAvailability.ReadOnly => "The mounted filesystem is read-only.",
                PermissionAvailability.NotQualified => snapshot.Mount?.ChangeRefusal ?? "Editing is unavailable on this permissions route.",
                _ => observation.Reason
            }));
        }
    }
}
