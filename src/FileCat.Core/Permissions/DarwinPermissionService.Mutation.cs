using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class DarwinPermissionService
{
    private const ushort PermissionBits = 0xfff, SetGroupId = 0x400, SetId = 0xc00;
    private const uint FinderHidden = MacFinderHidden.Hidden, CreateTime = 0x200, ModifyTime = 0x400;
    private const int AtNoFollowAny = 0x800;
    private const uint FsNoFollowAny = 0x800;
    private const PermissionSections WriteSecuritySections = PermissionSections.Owner | PermissionSections.Group |
        PermissionSections.Mode | PermissionSections.Access | PermissionSections.Flags;
    internal const string ParentRelativeReviewNotice = "Another process running as you or root can replace the entry during a call. A replacement restored before verification may go unreported.";

    internal static string? ModeRefusal(PermissionSnapshot snapshot) => snapshot.BindingProblem ??
        (snapshot.Capabilities.FirstOrDefault(capability => capability is { Section: PermissionSections.Mode, Action: PermissionAction.Change }) is { } mode
            ? mode.Availability == PermissionAvailability.Available ? null : mode.Reason ?? "The Darwin mode change is unavailable."
            : "The Darwin mode change was not inspected.");

    private PermissionSnapshot PrepareWriteAvailability(PermissionSnapshot snapshot, PermissionSections sections, SafeFileHandle parent,
        FileIdentity parentIdentity, string leaf, Mount mount, Entry entry, ref SafeFileHandle? descriptor)
    {
        var route = DarwinBindingRoute.None;
        string? basicRefusal = CommonWriteRefusal(snapshot, mount, entry);
        string? refusal = basicRefusal;
        bool canTryParent = basicRefusal is null && qualifyParentWrites && snapshot.Binding?.Identity.Kind != PermissionObjectKind.VolumeRoot;
        // A checked parent remains valid if a reviewed mode grants both read and write access on HFS+.
        bool preferParent = canTryParent && mount.Type == "hfs" && entry.Kind == PermissionObjectKind.File;
        if (preferParent) TryParent();
        if (route == DarwinBindingRoute.None && basicRefusal is null && qualifyDescriptorWrites && CanOpenMetadataDescriptor(entry, snapshot.Access))
        {
            bool openedHere = descriptor is null;
            try
            {
                CheckExchangeAdmission(snapshot);
                descriptor ??= OpenMetadataDescriptor(parent, leaf, entry.Kind);
                if (ReadMount(descriptor) != mount || ReadEntry(descriptor, null) != entry)
                    throw new ReadFailure(PermissionReadStatus.Stale, "The opened descriptor does not identify the inspected entry.");
                var held = ReadSnapshot(snapshot.Subject, snapshot.Binding!, sections, descriptor, null, entry, mount);
                if (RequireRevision(snapshot, WriteSecuritySections) != RequireRevision(held, WriteSecuritySections))
                    throw new ReadFailure(PermissionReadStatus.Stale, "The descriptor security does not match the original observation.");
                route = DarwinBindingRoute.Descriptor;
                snapshot = held;
                refusal = null;
            }
            catch (ReadFailure error) when (error.Status is PermissionReadStatus.Denied or PermissionReadStatus.Unsupported)
            { refusal = error.Message; }
            catch (NotSupportedException error) { refusal = error.Message; }
            finally
            {
                if (openedHere && route != DarwinBindingRoute.Descriptor)
                { descriptor?.Dispose(); descriptor = null; }
            }
        }

        if (route == DarwinBindingRoute.None && canTryParent && !preferParent) TryParent();

        if (route == DarwinBindingRoute.None) refusal ??= WritesEnabled
            ? "No qualified Darwin writer can bind this entry." : "The Darwin writer awaits macOS release qualification.";
        snapshot = PermissionSnapshots.WithCapabilities(snapshot, sections);
        if (refusal is null && snapshot.Flags.Value is { } flags)
        {
            ulong writable = qualifyProtectionWrites ? FinderHidden | UserProtection : FinderHidden;
            snapshot = snapshot with { Flags = snapshot.Flags with { Value = flags with { WritableBits = writable } } };
        }
        return snapshot with { Binding = snapshot.Binding! with { DarwinRoute = route }, Capabilities = snapshot.Capabilities.Select(capability =>
        {
            if (capability.Action != PermissionAction.Change || capability.Availability != PermissionAvailability.NotQualified) return capability;
            if (capability.Section is PermissionSections.Mode or PermissionSections.Times or PermissionSections.Flags)
                return capability with { Availability = refusal is null ? PermissionAvailability.Available : PermissionAvailability.NotQualified, Reason = refusal };
            if (capability.Section == PermissionSections.Access && qualifyAclWrites && refusal is null &&
                ((entry.Flags & UserProtection) == 0 || qualifyProtectionWrites) &&
                (snapshot.Access.Value?.Acl is not DarwinAcl acl || acl.Entries.Length <= DarwinPermissionAcl.NativeEntryLimit))
                return capability with { Availability = PermissionAvailability.Available, Reason = null };
            if (capability.Section == PermissionSections.Group && qualifyGroupWrites && refusal is null)
                return capability with { Availability = PermissionAvailability.Available, Reason = null };
            return capability;
        }).ToImmutableArray() };

        void TryParent()
        {
            try
            {
                _ = ReadAdmittedParent(parent, parentIdentity, mount, EffectiveUser(), snapshot.Subject.Authority.Id);
                AdmitEntry(snapshot, entry, EffectiveUser(), expectedWritableDirectory: null);
                route = DarwinBindingRoute.ParentRelative;
                refusal = null;
            }
            catch (Exception error) when (error is IOException or ReadFailure or NotSupportedException or UnauthorizedAccessException)
            { refusal = error.Message; }
        }

    }

    private static string? CommonWriteRefusal(PermissionSnapshot snapshot, Mount mount, Entry entry)
    {
        if (ReviewedPermissionOperation.ProcessRefusal() is { } refusal) return refusal;
        if (snapshot.Subject.LinkPolicy != PermissionLinkPolicy.NoFollow || snapshot.Binding is not { FileSystem: "apfs" or "hfs", ReadOnly: false })
            return "This route requires an original entry on a writable local APFS or HFS+ volume.";
        uint user = EffectiveUser();
        if ((mount.Flags & MountIgnoreOwnership) != 0 || (mount.Owner != 0 && mount.Owner != user))
            return "This route requires a volume that honors ownership and is mounted by this user or root.";
        if (entry.Owner != user) return "This route requires the current user to own the original object.";
        if ((entry.Flags & SystemProtection) != 0) return "System file protection is read only in ordinary FileCat.";
        // A cloud placeholder: its provider owns the item until it is downloaded, as for trees and access checks.
        if ((entry.Flags & MacMaterializationPolicy.DatalessFlag) != 0)
            return "This item is a cloud placeholder that is not downloaded. Download it before changing its permissions.";
        return PermissionRevision.Read(snapshot, WriteSecuritySections) is null
            ? "This route requires the original mode, owner, group, flags and extended security." : null;
    }

    internal static PermissionSnapshot PreviewMode(PermissionSnapshot snapshot, ChangePermissionMode change) =>
        ProjectMode(snapshot, change, acceptSetIdClear: true);

    internal static string? TimeReviewRefusal(PermissionSnapshot snapshot, ChangePermissionTimes times)
    {
        try { _ = ReviewTimes(snapshot, times); return null; }
        catch (NotSupportedException error) { return error.Message; }
    }

    internal static (ReviewedPermissionOperation Review, PermissionSnapshot Expected) Prepare(PermissionInspection inspection,
        ItemRef source, PermissionDraft draft, bool acceptSetIdClear, CancellationToken ct, DarwinGroupAuthority? groupAuthority = null)
    {
        var prepared = PrepareTarget(inspection, source, draft, acceptSetIdClear, ct, groupAuthority);
        try { return (new(PermissionScope.Objects, [prepared.Target]), prepared.Expected); }
        catch { prepared.Target.Mutation.Dispose(); throw; }
    }

    internal static (ReviewedPermissionTarget Target, PermissionSnapshot Expected) PrepareTarget(PermissionInspection inspection,
        ItemRef source, PermissionDraft draft, bool acceptSetIdClear, CancellationToken ct, DarwinGroupAuthority? groupAuthority = null)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = inspection.Snapshot;
        if (ModeRefusal(snapshot) is { } refusal) throw new NotSupportedException(refusal);
        if (snapshot.Subject.RawLocator != source.FileSystemPath || draft.Scope != PermissionScope.Objects ||
            draft.Changes.IsDefaultOrEmpty || draft.Changes.Length > 4 || draft.Changes.Select(delta => delta.Section).Distinct().Count() != draft.Changes.Length ||
            draft.Changes.Any(delta => delta is not (ChangePermissionMode or ChangePermissionTimes or ChangePermissionFlags or DarwinAclDelta or AssignPermissionPrincipal { IsOwner: false })))
            throw new NotSupportedException("Review the original object's group, mode, times, file flags or extended ACL.");
        bool assignsGroup = draft.Changes.Any(change => change is AssignPermissionPrincipal);
        if (assignsGroup || draft.Changes.Any(change => change is DarwinAclDelta))
        {
            if (assignsGroup && (groupAuthority is null || !GroupAuthorityMatches(groupAuthority, ct)))
                throw new PermissionBindingConflictException("The ordinary macOS group credential changed. Review the group assignment again.");
            var plan = PreviewAccessChanges(snapshot, draft, groupAuthority);
            if (plan.NeedsSetIdConsent && !acceptSetIdClear)
                throw new InvalidOperationException("Accept the reviewed set-ID effects before applying these changes.");
            if (plan.Stages.IsEmpty) throw new InvalidOperationException("The requested permissions already match.");
            var baseline = RequireRevision(snapshot, WriteSecuritySections);
            var prior = baseline;
            var preceding = snapshot;
            var reviewed = ImmutableArray.CreateBuilder<ReviewedPermissionComponent>(plan.Stages.Length);
            foreach (var stage in plan.Stages)
            {
                var after = RequireRevision(stage.Expected, WriteSecuritySections);
                var change = stage.Change is DarwinAclDelta acl
                    ? PrepareAcl(preceding, acl, ProjectAcl(preceding, acl).Payload) : stage.Change;
                reviewed.Add(new(change, prior, after, stage.NativeCall));
                preceding = stage.Expected;
                prior = after;
            }
            return BindTarget(inspection, source, baseline, plan.Expected, reviewed.MoveToImmutable(), ct, assignsGroup ? groupAuthority : null);
        }

        PermissionBinding binding = snapshot.Binding!;
        bool descriptor = binding.DarwinRoute == DarwinBindingRoute.Descriptor;
        var sections = WriteSecuritySections;
        if (draft.Changes.Any(delta => delta is ChangePermissionTimes)) sections |= PermissionSections.Times;
        var original = RequireRevision(snapshot, sections);
        var expected = snapshot;
        var order = OrderDarwinChanges(snapshot, draft.Changes);
        var components = ImmutableArray.CreateBuilder<ReviewedPermissionComponent>(order.Length);
        foreach (var delta in order)
        {
            var before = RequireRevision(expected, sections);
            var change = delta;
            string nativeCall;
            switch (delta)
            {
                case ChangePermissionMode mode:
                    expected = ProjectMode(expected, mode, acceptSetIdClear);
                    change = new ChangePermissionMode(expected.Mode.Value!.Value, PermissionBits);
                    nativeCall = descriptor ? "fchmod (retained descriptor)" : "fchmodat (checked parent and name)";
                    break;
                case ChangePermissionTimes times when times.Modified is not null || times.Created is not null:
                    var requested = ReviewTimes(expected, times);
                    var observed = expected.Times!;
                    var current = observed.Value!.Value;
                    change = requested;
                    expected = expected with { Times = observed with { Value = new(requested.Modified ?? current.Modified, requested.Created ?? current.Created) } };
                    nativeCall = descriptor ? "fsetattrlist times (retained descriptor)" : "setattrlistat times (checked parent and name)";
                    break;
                case ChangePermissionFlags flags:
                    expected = PreviewFlags(expected, flags);
                    nativeCall = DescribeFlagCall(flags, descriptor);
                    break;
                default:
                    throw new NotSupportedException("This route changes only mode, selected timestamps and file flags.");
            }
            components.Add(new(change, before, RequireRevision(expected, sections), nativeCall));
        }
        if (binding.DarwinRoute == DarwinBindingRoute.ParentRelative && binding.Identity.Kind == PermissionObjectKind.Directory && draft.Changes.Length > 1 &&
            (expected.Mode.Value.GetValueOrDefault() & GroupOrOtherWrite) != 0)
            throw new NotSupportedException("Apply the permission change separately because it changes who can write the directory.");

        return BindTarget(inspection, source, original, expected, components.MoveToImmutable(), ct);
    }

    private static (ReviewedPermissionTarget Target, PermissionSnapshot Expected) BindTarget(PermissionInspection inspection,
        ItemRef source, PermissionRevision original, PermissionSnapshot expected, ImmutableArray<ReviewedPermissionComponent> reviewed,
        CancellationToken ct, DarwinGroupAuthority? groupAuthority = null)
    {
        var snapshot = inspection.Snapshot;
        var binding = snapshot.Binding!;
        var pin = inspection.TransferLease<InspectionPin>() ?? throw new InvalidOperationException("This Darwin permission binding has been released.");
        IBoundPermissionMutation? mutation = null;
        try
        {
            mutation = binding.DarwinRoute switch
            {
                DarwinBindingRoute.Descriptor when pin.Descriptor is { } handle => new DescriptorMutation(snapshot.Subject, binding, pin, handle, EffectiveUser(), reviewed, groupAuthority),
                DarwinBindingRoute.ParentRelative => new ParentMutation(snapshot.Subject, binding, pin, EffectiveUser(), reviewed, groupAuthority),
                _ => throw new InvalidOperationException("The reviewed Darwin write lacks its original binding.")
            };
            var current = mutation.CheckCurrent(original.Sections, ct);
            if (original != RequireRevision(current, original.Sections))
                throw new PermissionBindingConflictException("The original Darwin permissions changed since inspection.");
            return (new(source, snapshot.Subject, binding, reviewed, RequireRevision(expected, original.Sections), mutation), expected);
        }
        catch
        {
            if (mutation is null) pin.Dispose(); else mutation.Dispose();
            throw;
        }
    }

    private static PermissionSnapshot ProjectMode(PermissionSnapshot snapshot, ChangePermissionMode change, bool acceptSetIdClear, bool? groupMember = null)
    {
        if (snapshot.Mode.Value is not { } original || change.Bits > PermissionBits || change.TouchedBits > PermissionBits)
            throw new NotSupportedException("Enter a supported BSD mode.");
        ushort mode = (ushort)((original & ~change.TouchedBits) | (change.Bits & change.TouchedBits));
        if (mode != original && (snapshot.Flags.Value.GetValueOrDefault().Bits & (UserProtection | SystemProtection)) != 0)
            throw new NotSupportedException(ActiveProtectionRefusal);
        if (mode != original && (mode & SetGroupId) != 0 && !(groupMember ?? InFileGroup(snapshot)))
            throw new UnauthorizedAccessException("macOS requires membership in the file's group to retain or set set-group-ID.");
        if (!acceptSetIdClear && (original & SetId & ~mode) != 0)
            throw new InvalidOperationException("Review and accept clearing the existing set-ID bits before applying.");
        var projected = snapshot with { Mode = snapshot.Mode with { Value = mode } };
        if (snapshot.Binding?.DarwinRoute == DarwinBindingRoute.Descriptor) CheckExchangeAdmission(projected);
        return projected;
    }

    private static bool InFileGroup(PermissionSnapshot snapshot)
    {
        if (!uint.TryParse(snapshot.Group.Value?.Id, NumberStyles.None, CultureInfo.InvariantCulture, out uint group)) return false;
        return EffectiveGroup() == group || ReadGroupIds().AsSpan().Contains(group);
    }

    private static DateTimeOffset? ReviewTime(DateTimeOffset? time, string fileSystem)
    {
        if (time is not { } value) return null;
        long seconds = value.ToUnixTimeSeconds();
        if (fileSystem == "hfs")
        {
            const long hfsEpoch = -2082844800;
            if (seconds < hfsEpoch || seconds - hfsEpoch > uint.MaxValue)
                throw new NotSupportedException("The requested date cannot be stored on this HFS+ volume.");
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        long ticks = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        if (ticks < long.MinValue / 100 || ticks > long.MaxValue / 100)
            throw new NotSupportedException("The requested date cannot be stored on this APFS volume.");
        return value.ToUniversalTime();
    }

    private static ChangePermissionTimes ReviewTimes(PermissionSnapshot snapshot, ChangePermissionTimes times)
    {
        if (snapshot.Binding is not { } binding || snapshot.Times?.Value is not { Modified: not null, Created: { } originalCreation })
            throw new NotSupportedException("The original file times could not be verified.");
        var modified = ReviewTime(times.Modified, binding.FileSystem);
        var created = ReviewTime(times.Created, binding.FileSystem);
        // Darwin lowers ATTR_CMN_CRTIME when a new ATTR_CMN_MODTIME predates it.
        if (modified is { } change && change < (created ?? originalCreation))
            throw new NotSupportedException("This modification time would change the creation time. Review a creation time at or before the new modification time.");
        return new(modified, created);
    }

    private static PermissionRevision RequireRevision(PermissionSnapshot snapshot, PermissionSections sections) =>
        PermissionRevision.Read(snapshot, sections) ?? throw new NotSupportedException("The reviewed change depends on unavailable Darwin metadata.");

    private sealed class ParentMutation(PermissionSubject subject, PermissionBinding binding, InspectionPin pin, uint user,
        ImmutableArray<ReviewedPermissionComponent> components, DarwinGroupAuthority? groupAuthority) : IBoundPermissionMutation
    {
        private readonly DirectoryWatch _watch = new(pin.Handle);
        private LaneObservation? _checked, _previous;
        private ReviewedPermissionComponent? _pending;
        private ushort? _expectedWritableDirectory;
        private PermissionRevision? _expectedDirectoryAcl;
        private bool _folderChanged;

        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation)
        {
            _checked = null;
            try
            {
                _checked = ReadBefore(sections, cancellation);
                return _checked.Snapshot;
            }
            catch (Exception error) when (error is ReadFailure or IOException or NotSupportedException or UnauthorizedAccessException)
            { throw new PermissionBindingConflictException(error.Message); }
        }

        private LaneObservation ReadBefore(PermissionSections sections, CancellationToken ct) => MacMaterializationPolicy.Run(ct, () =>
        {
            if (!GroupAuthorityMatches(groupAuthority, ct)) throw new PermissionBindingConflictException("The ordinary macOS group credential changed.");
            _watch.TakeChanges();
            var observed = ReadLane(sections);
            if (_watch.TakeChanges() || _previous is { } previous && !SameObservation(previous, observed, sections))
                throw new PermissionBindingConflictException("The original entry or parent changed before the next call.");
            return observed;
        }, (_, reason) => throw new IOException(reason));

        public PermissionMutationReturn Apply(PermissionDelta change)
        {
            var prior = _checked ?? throw new InvalidOperationException("Darwin requires a current checked binding before dispatch.");
            _checked = null;
            var component = components.FirstOrDefault(item => ReferenceEquals(item.Change, change)) ??
                throw new InvalidOperationException("Darwin requires the original reviewed component.");
            return MacMaterializationPolicy.Run(CancellationToken.None, () =>
            {
                LaneObservation current;
                try
                {
                    if (!GroupAuthorityMatches(groupAuthority)) return new PermissionMutationReturn(BindingConflict: true);
                    current = ReadLane(component.Before.Sections);
                    if (!SameObservation(prior, current, component.Before.Sections) ||
                        change is ChangePermissionMode { Bits: var mode } && (mode & SetGroupId) != 0 && !InFileGroup(current.Snapshot) ||
                        _watch.TakeChanges()) return new PermissionMutationReturn(BindingConflict: true);
                }
                catch (Exception error) when (error is ReadFailure or IOException or NotSupportedException or UnauthorizedAccessException)
                { return new PermissionMutationReturn(BindingConflict: true); }

                _pending = component;
                int returned = change switch
                {
                    ChangePermissionMode mode => ChangeModeAt(pin.Handle, pin.Leaf, mode.Bits, AtNoFollowAny),
                    ChangePermissionTimes times => SetTimes(pin.Handle, pin.Leaf, times),
                    ChangePermissionFlags flags => MacFinderHidden.SetFlagsAt(pin.Handle, pin.Leaf, PatchFlags(current.Entry.Flags, flags)),
                    ApplyDarwinAcl acl => SetAcl(pin.Handle, pin.Leaf, acl),
                    AssignPermissionPrincipal group => SetGroup(pin.Handle, pin.Leaf, subject, group),
                    _ => throw new InvalidOperationException("The component is outside the Darwin parent route.")
                };
                return NativeResult(returned);
            }, (_, _) => _pending is null ? new PermissionMutationReturn(BindingConflict: true) : new PermissionMutationReturn());
        }

        public PermissionReadBack Observe(PermissionSections sections)
        {
            try { return MacMaterializationPolicy.Run(CancellationToken.None, () => ReadBack(sections), (_, reason) => Unverified(sections, reason)); }
            catch (Exception error) when (error is ReadFailure or IOException or NotSupportedException or UnauthorizedAccessException)
            { return Unverified(sections, error.Message); }
        }

        private PermissionReadBack ReadBack(PermissionSections sections)
        {
            bool eventBefore = _watch.TakeChanges();
            ushort? directoryMode = _pending?.Change is ChangePermissionMode mode ? mode.Bits : _expectedWritableDirectory;
            var directoryAcl = _pending?.Change is ApplyDarwinAcl ? _pending.Expected : _expectedDirectoryAcl;
            // The entry has no descriptor here; a full flush through its held folder commits the volume's pending metadata.
            string? unflushed = FlushRefusal(pin.Handle, subject.RawLocator);
            var observed = ReadLane(sections, directoryMode, directoryAcl);
            _folderChanged |= eventBefore | _watch.TakeChanges();
            if (_pending is not null)
            {
                _previous = observed;
                if (observed.Entry.Kind == PermissionObjectKind.Directory && directoryMode == observed.Entry.Mode &&
                    (observed.Entry.Mode & GroupOrOtherWrite) != 0 && RequireRevision(observed.Snapshot, _pending.Expected.Sections) == _pending.Expected)
                    _expectedWritableDirectory = directoryMode;
                if (observed.Entry.Kind == PermissionObjectKind.Directory && directoryAcl is { } acl &&
                    RequireRevision(observed.Snapshot, acl.Sections) == acl)
                    _expectedDirectoryAcl = acl;
                _pending = null;
            }
            if (unflushed is not null)
                return new PermissionReadBack(observed.Snapshot, PermissionVerification.Unavailable, _folderChanged, unflushed);
            return new PermissionReadBack(observed.Snapshot, PermissionVerification.SameObject, _folderChanged, _folderChanged
                ? "The reviewed entry matched after the call. The original parent directory changed during the operation." : null);
        }

        private PermissionReadBack Unverified(PermissionSections sections, string reason) => new(
            Unavailable(subject, sections, PermissionReadStatus.Stale, reason), PermissionVerification.Unavailable,
            Detail: "The change may have reached another object. The original Darwin binding could not be verified. " + reason);

        private LaneObservation ReadLane(PermissionSections sections, ushort? expectedWritableDirectory = null,
            PermissionRevision? expectedDirectoryAcl = null)
        {
            if (EffectiveUser() != user) throw new PermissionBindingConflictException("The ordinary Darwin user changed.");
            var mount = ReadMount(pin.Handle);
            if (mount != pin.Mount) throw new PermissionBindingConflictException("The original Darwin mount changed.");
            var parent = ReadAdmittedParent(pin.Handle, pin.Identity, mount, user, subject.Authority.Id);

            var entry = ReadEntry(pin.Handle, pin.Leaf);
            if (entry.Object != pin.Entry.Object || entry.FileSystem != mount.Id || entry.Kind != pin.Entry.Kind || entry.Owner != user ||
                entry.Links != pin.Entry.Links)
                throw new PermissionBindingConflictException("The original entry no longer matches its reviewed identity or authority.");
            var snapshot = ReadSnapshot(subject, binding, sections, pin.Handle, pin.Leaf, entry, mount);
            bool reviewedAcl = entry.Kind == PermissionObjectKind.Directory && expectedDirectoryAcl is { } revision &&
                RequireRevision(snapshot, revision.Sections) == revision;
            AdmitEntry(snapshot, entry, user, expectedWritableDirectory, reviewedAcl);
            using var currentParent = OpenDirectory(pin.Directory);
            if (UnixFiles.Stat(currentParent)?.Identity != pin.Identity || ReadMount(currentParent) != mount ||
                ReadEntry(pin.Handle, ".") != parent || ReadEntry(pin.Handle, pin.Leaf) != entry)
                throw new PermissionBindingConflictException("The original Darwin namespace changed while reading security.");
            return new(entry, snapshot);
        }

        public void Dispose() { _watch.Dispose(); pin.Dispose(); }
    }

    private sealed record LaneObservation(Entry Entry, PermissionSnapshot Snapshot);

    private static bool SameObservation(LaneObservation previous, LaneObservation current, PermissionSections sections) =>
        previous.Entry == current.Entry && RequireRevision(previous.Snapshot, sections) == RequireRevision(current.Snapshot, sections);

    private static int SetTimes(SafeFileHandle handle, string? leaf, ChangePermissionTimes times)
    {
        Timespec* values = stackalloc Timespec[2];
        int count = 0;
        var attributes = new AttrList { Count = 5 };
        if (times.Created is { } created) { attributes.Common |= CreateTime; values[count++] = NativeTime(created); }
        if (times.Modified is { } modified) { attributes.Common |= ModifyTime; values[count++] = NativeTime(modified); }
        var size = (nuint)(count * sizeof(Timespec));
        return leaf is null ? SetHeldAttributes(handle, &attributes, values, size, 0)
            : SetAttributesAt(handle, leaf, &attributes, values, size, FsNoFollowAny);
    }

    private static PermissionMutationReturn NativeResult(int returned)
    {
        if (returned == 0) return new();
        int error = Marshal.GetLastPInvokeError();
        return new(error, RejectedBeforeChange: error is 1 or 13 or 22 or 30 or 45 or 62 or 102);
    }

    private static Timespec NativeTime(DateTimeOffset value) => new()
    {
        Seconds = value.ToUnixTimeSeconds(),
        Nanoseconds = value.UtcTicks % TimeSpan.TicksPerSecond * 100
    };

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint EffectiveUser();
    [LibraryImport("libc", EntryPoint = "getegid")]
    private static partial uint EffectiveGroup();
    [LibraryImport("libc", EntryPoint = "getgroups", SetLastError = true)]
    private static partial int SupplementaryGroups(int count, uint* groups);
    [LibraryImport("libc", EntryPoint = "fchmodat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int ChangeModeAt(SafeFileHandle parent, string name, ushort mode, int flags);
    [LibraryImport("libc", EntryPoint = "setattrlistat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int SetAttributesAt(SafeFileHandle parent, string name, AttrList* attributes, void* data, nuint length, uint options);
}
