using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Recovery.Unix;

/// <summary>An authorized metadata review over one retained local object. Apply needs its own approval.</summary>
internal sealed class LinuxAuthorizedPermissionEdit : IDisposable
{
    private const string Source = "Authorized Linux security observation";
    private readonly LinuxPermissionBroker _broker;
    private readonly LinuxBrokerSnapshot _reviewed;
    private LinuxBrokerTarget? _target;

    private LinuxAuthorizedPermissionEdit(LinuxPermissionBroker broker, LinuxBrokerTarget target,
        PermissionSnapshot ordinary, LinuxBrokerSnapshot reviewed)
    {
        _broker = broker;
        _target = target;
        _reviewed = reviewed;
        Snapshot = FromBroker(ordinary, reviewed);
    }

    internal PermissionSnapshot Snapshot { get; }

    internal static LinuxAuthorizedPermissionEdit Inspect(LinuxPermissionBroker broker, PermissionInspection inspection,
        CancellationToken cancellation)
    {
        var target = LinuxPermissionService.TakeBrokerTarget(inspection, cancellation);
        try
        {
            var sections = LinuxBrokerSections.Access | LinuxBrokerSections.Flags |
                (target.Identity.Kind == 0x8000 ? LinuxBrokerSections.Capabilities : LinuxBrokerSections.Defaults);
            var review = broker.Inspect(target, sections, cancellation);
            CheckStamp(target, review);
            return new(broker, target, inspection.Snapshot, review);
        }
        catch { target.Dispose(); throw; }
    }

    internal bool CanEdit(string command)
    {
        if (Snapshot.Binding is not { ReadOnly: false, Identity.Kind: PermissionObjectKind.File or PermissionObjectKind.Directory } binding ||
            Snapshot.Flags.Value is not { } flags || (flags.KnownBits & LinuxPermissionService.InodeProtection) != LinuxPermissionService.InodeProtection)
            return false;
        if (command == "protection") return flags.KnownBits == uint.MaxValue;
        if ((flags.Bits & LinuxPermissionService.InodeProtection) != 0 ||
            Snapshot.Access.Status != PermissionReadStatus.Absent && Snapshot.Access.Value?.Acl is not PosixAcl ||
            Snapshot.Defaults.Value?.Acl is OpaquePermissionAcl)
            return false;
        return command switch
        {
            "mode" or "acl" => true,
            "defaults" => binding.Identity.Kind == PermissionObjectKind.Directory &&
                (Snapshot.Defaults.Status == PermissionReadStatus.Absent || Snapshot.Defaults.Value?.Acl is PosixAcl),
            "owner" or "group" => binding.Identity.Kind == PermissionObjectKind.Directory || Known(Snapshot.FileCapabilities),
            _ => false
        };
    }

    internal PermissionSnapshot Preview(PermissionDelta change, bool acceptSetIdClear = true) =>
        LinuxPermissionService.PreviewBrokerChange(Snapshot, change, acceptSetIdClear);

    internal PermissionSections SectionsFor(PermissionDelta change)
    {
        var sections = PermissionSections.Owner | PermissionSections.Group | PermissionSections.Mode |
            PermissionSections.Flags | change.Section;
        if (change is not ChangePermissionFlags || Known(Snapshot.Access)) sections |= PermissionSections.Access;
        if (Known(Snapshot.Defaults)) sections |= PermissionSections.Defaults;
        if (Known(Snapshot.FileCapabilities)) sections |= PermissionSections.FileCapabilities;
        return sections;
    }

    internal ReviewedPermissionOperation Prepare(ItemRef source, PermissionDraft draft, bool acceptClear,
        PermissionRevision accepted, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (source.FileSystemPath != Snapshot.Subject.RawLocator || draft.Changes is not [var change] ||
            draft.Scope != (change is ChangePosixAcl { IsDefault: true } ? PermissionScope.DefaultsForNewChildren : PermissionScope.Objects))
            throw new NotSupportedException("Review one authorized change on the original object.");
        var expected = Preview(change, acceptClear);
        var sections = SectionsFor(change);
        var before = PermissionRevision.Read(Snapshot, sections) ??
            throw new NotSupportedException("The requested change still depends on unread security metadata.");
        if (before == accepted || PermissionRevision.Read(expected, sections) != accepted)
            throw new PermissionBindingConflictException("The authorized change no longer matches the review.");
        var native = ToNative(change, expected);
        var target = Interlocked.Exchange(ref _target, null) ??
            throw new InvalidOperationException("The reviewed object's binding has already been used.");
        var mutation = new Mutation(_broker, target, _reviewed, Snapshot, change, native);
        try
        {
            mutation.CheckCurrent(sections, cancellation);
            return new(draft.Scope, [new(source, Snapshot.Subject, Snapshot.Binding,
                [new(change, before, accepted, "Linux permissions broker")], accepted, mutation)]);
        }
        catch { mutation.Dispose(); throw; }
    }

    public void Dispose() => Interlocked.Exchange(ref _target, null)?.Dispose();

    private static bool Known<T>(PermissionObservation<T>? observation) where T : struct =>
        observation?.Status is PermissionReadStatus.Present or PermissionReadStatus.Absent;

    private static LinuxBrokerChange ToNative(PermissionDelta change, PermissionSnapshot expected) => change switch
    {
        ChangePermissionMode => new LinuxBrokerChange.Mode(expected.Mode.Value!.Value),
        AssignPermissionPrincipal owner => owner.IsOwner ?
            new LinuxBrokerChange.Ownership(uint.Parse(owner.Principal.Id, CultureInfo.InvariantCulture), null) :
            new LinuxBrokerChange.Ownership(null, uint.Parse(owner.Principal.Id, CultureInfo.InvariantCulture)),
        ChangePosixAcl { IsDefault: true, Entries.IsEmpty: true } => new LinuxBrokerChange.Acl(true, []),
        ChangePosixAcl acl => new LinuxBrokerChange.Acl(acl.IsDefault,
            LinuxPermissionAcl.Encode(acl.Entries, expected.Subject.Authority.Id).Bytes),
        ChangePermissionFlags flags => new LinuxBrokerChange.Protection((uint)flags.Bits, (uint)(flags.TouchedBits & ~flags.Bits)),
        _ => throw new NotSupportedException("This change has no Linux broker operation.")
    };

    private static PermissionSnapshot FromBroker(PermissionSnapshot ordinary, LinuxBrokerSnapshot result)
    {
        var now = DateTimeOffset.UtcNow;
        var authority = ordinary.Subject.Authority.Id;
        bool readOnly = (result.MountAttributes & 1) != 0;
        return ordinary with
        {
            Binding = ordinary.Binding! with { LinkCount = result.LinkCount, ReadOnly = readOnly },
            Owner = new(PermissionReadStatus.Present, Source, now,
                new Principal(PrincipalKind.UnixUser, result.Uid.ToString(CultureInfo.InvariantCulture), authority)),
            Group = new(PermissionReadStatus.Present, Source, now,
                new Principal(PrincipalKind.UnixGroup, result.Gid.ToString(CultureInfo.InvariantCulture), authority)),
            Mode = new(PermissionReadStatus.Present, Source, now, (ushort)(result.Mode & 0xfff)),
            Access = Acl(result.Access),
            Defaults = Acl(result.Defaults),
            Flags = new(Status(result.Flags.Status), Source, now, result.Flags.Status == LinuxBrokerReadStatus.Present
                ? new PermissionFlagsValue(result.Flags.Bits, result.Flags.Known,
                    !readOnly && result.Flags.Known == uint.MaxValue ? LinuxPermissionService.InodeProtection : 0) : null,
                NativeError: Error(result.Flags.Error)),
            FileCapabilities = new(Status(result.Capabilities.Status), Source, now, result.Capabilities.Status == LinuxBrokerReadStatus.Present
                ? new PermissionFileCapabilities(result.Capabilities.Bytes) : null, NativeError: Error(result.Capabilities.Error)),
            Capabilities = []
        };

        PermissionObservation<PermissionAclValue> Acl(LinuxBrokerAttribute value)
        {
            if (value.Status == LinuxBrokerReadStatus.Present)
                return new(PermissionReadStatus.Present, Source, now, new(LinuxPermissionAcl.Decode(value.Bytes.AsSpan(), authority)));
            return new(Status(value.Status), Source, now,
                value.Status == LinuxBrokerReadStatus.Opaque ? new(new OpaquePermissionAcl(value.Bytes)) : null,
                NativeError: Error(value.Error));
        }
    }

    private static PermissionReadStatus Status(LinuxBrokerReadStatus status) => status switch
    {
        LinuxBrokerReadStatus.Unrequested => PermissionReadStatus.Unloaded,
        LinuxBrokerReadStatus.Present => PermissionReadStatus.Present,
        LinuxBrokerReadStatus.Absent => PermissionReadStatus.Absent,
        LinuxBrokerReadStatus.NotApplicable or LinuxBrokerReadStatus.Unsupported or LinuxBrokerReadStatus.Opaque => PermissionReadStatus.Unsupported,
        LinuxBrokerReadStatus.Denied => PermissionReadStatus.Denied,
        LinuxBrokerReadStatus.Incomplete => PermissionReadStatus.Incomplete,
        _ => PermissionReadStatus.Failed
    };

    private static int? Error(int error) => error is < -4095 or 0 or > 4095 ? null : Math.Abs(error);

    private static void CheckStamp(LinuxBrokerTarget target, LinuxBrokerSnapshot snapshot)
    {
        var expected = new LinuxBrokerStamp(snapshot.Identity, snapshot.Mode, snapshot.Uid, snapshot.Gid, snapshot.LinkCount,
            snapshot.Size, snapshot.Attributes, snapshot.AttributeMask, snapshot.ChangeSeconds, snapshot.ChangeNanoseconds,
            snapshot.ModificationSeconds, snapshot.ModificationNanoseconds);
        if (LinuxPermissionService.ReadBrokerStamp(target) != expected)
            throw new PermissionBindingConflictException("The original item's security or filesystem state changed. Inspect it again.");
    }

    private sealed class Mutation(LinuxPermissionBroker broker, LinuxBrokerTarget target, LinuxBrokerSnapshot review,
        PermissionSnapshot original, PermissionDelta change, LinuxBrokerChange native) : IBoundPermissionMutation
    {
        private LinuxBrokerSnapshot? _observed;
        private string? _detail;

        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            CheckStamp(target, review);
            return original;
        }

        public PermissionMutationReturn Apply(PermissionDelta component) => Apply(component, CancellationToken.None);

        public PermissionMutationReturn Apply(PermissionDelta component, CancellationToken cancellation)
        {
            if (component != change) throw new InvalidOperationException("The native operation differs from the reviewed change.");
            _observed = null;
            try
            {
                var outcome = broker.Apply(target, review, native, cancellation);
                _detail = outcome.Detail;
                _observed = outcome.State switch
                {
                    LinuxBrokerWriteState.Verified or LinuxBrokerWriteState.Rejected => outcome.Observed,
                    LinuxBrokerWriteState.NotSent => review,
                    _ => null
                };
                return new(Error(outcome.SetterError != 0 ? outcome.SetterError : outcome.VerificationError),
                    RejectedBeforeChange: outcome.State is LinuxBrokerWriteState.NotSent or LinuxBrokerWriteState.Rejected,
                    BindingConflict: outcome.State == LinuxBrokerWriteState.NotSent && outcome.SetterError == -116, Detail: _detail);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return new(CanceledBeforeChange: true);
            }
            catch (DBusException refusal)
            {
                _observed = review;
                _detail = refusal.Message;
                return new(RejectedBeforeChange: true, BindingConflict: refusal.Name == "System.Error.ESTALE",
                    Detail: _detail, CanceledBeforeChange: refusal.Name == "System.Error.ECANCELED");
            }
        }

        public PermissionReadBack Observe(PermissionSections sections)
        {
            if (_observed is not { } observed) return new(original, PermissionVerification.Unavailable, Detail: _detail);
            CheckStamp(target, observed);
            return new(FromBroker(original, observed), PermissionVerification.SameObject, Detail: _detail);
        }

        public void Dispose() => target.Dispose();
    }
}
