using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class LinuxPermissionService
{
    private const ushort PermissionBits = 0xfff, SetGroupId = 0x400, SetIdBits = 0xc00;
    private const string AccessAttribute = "system.posix_acl_access";
    private const string DefaultAttribute = "system.posix_acl_default";
    private const PermissionSections FileSecurity = PermissionSections.Owner | PermissionSections.Group | PermissionSections.Mode | PermissionSections.Access;

    internal static (ReviewedPermissionOperation Review, PermissionSnapshot Expected) Prepare(PermissionInspection inspection, ItemRef source,
        PermissionDraft draft, bool acceptSetIdClear, CancellationToken ct, LinuxGroupAuthority? groupAuthority = null)
    {
        var prepared = !draft.Changes.IsDefaultOrEmpty && draft.Changes.Any(change => change is ChangePermissionFlags)
            ? PrepareProtectionTarget(inspection, source, draft, groupAuthority, acceptSetIdClear, ct)
            : groupAuthority is null ? PrepareTarget(inspection, source, draft, acceptSetIdClear, ct)
            : PrepareGroupTarget(inspection, source, draft, groupAuthority, acceptSetIdClear, ct);
        try { return (new(draft.Scope, [prepared.Target]), prepared.Expected); }
        catch { prepared.Target.Mutation.Dispose(); throw; }
    }

    internal static string? ModeReviewRefusal(PermissionSnapshot snapshot)
    {
        if (ReviewedPermissionOperation.ProcessRefusal() is { } refusal) return refusal;
        if (snapshot.BindingProblem is { } problem) return problem;
        if (snapshot.Mount is { } projected) return projected.ChangeRefusal;
        if (!IsWritableObject(snapshot))
            return "Changing mode requires a bound file or directory on a qualified writable Linux filesystem.";
        if (!IsEffectiveOwner(snapshot)) return "The original object is not owned by this FileCat user.";
        var sections = SecuritySections(snapshot.Binding!);
        try
        {
            ValidateKnownAcl(snapshot);
            _ = RequireRevision(snapshot, sections);
            return null;
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException) { return error.Message; }
    }

    internal static ushort PreviewMode(PermissionSnapshot snapshot, ChangePermissionMode change) =>
        ProjectMode(snapshot, change, acceptSetIdClear: true, out _);

    internal static PermissionSnapshot PreviewAccessChange(PermissionSnapshot snapshot, PermissionDelta change) => change switch
    {
        ChangePermissionMode mode => ForecastMode(snapshot, mode, acceptSetIdClear: true, out _),
        ChangePosixAcl { IsDefault: false } access => ForecastAccess(snapshot, access, acceptSetIdClear: true),
        ChangePosixAcl defaults => ForecastDefaults(snapshot, defaults),
        _ => throw new NotSupportedException("Select a supported access or mode change.")
    };

    internal static (ReviewedPermissionTarget Target, PermissionSnapshot Expected) PrepareTarget(PermissionInspection inspection, ItemRef source,
        PermissionDraft draft, bool acceptSetIdClear, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ReviewedPermissionOperation.ProcessRefusal() is { } refusal) throw new UnauthorizedAccessException(refusal);
        var original = inspection.Snapshot;
        RequireUnprotected(original);
        if (!IsWritableObject(original) || original.Subject.RawLocator != source.FileSystemPath)
            throw new NotSupportedException("This change requires a retained file or directory on a qualified writable Linux filesystem.");
        if (!IsEffectiveOwner(original)) throw new UnauthorizedAccessException("The original object is not owned by this FileCat user.");
        var binding = original.Binding!;
        bool editDefaults = draft.Scope == PermissionScope.DefaultsForNewChildren;
        if (editDefaults)
        {
            if (binding.Identity.Kind != PermissionObjectKind.Directory || draft.Changes.IsDefaultOrEmpty ||
                draft.Changes is not [ChangePosixAcl { IsDefault: true }])
                throw new NotSupportedException("Review one default ACL change on the selected directory.");
        }
        else if (draft.Scope != PermissionScope.Objects || draft.Changes.IsDefaultOrEmpty || draft.Changes.Length > 2 ||
            draft.Changes.Any(change => change is not (ChangePermissionMode or ChangePosixAcl { IsDefault: false } or ChangePermissionTimes)) ||
            draft.Changes.Count(change => change is not ChangePermissionTimes) > 1 || draft.Changes.Count(change => change is ChangePermissionTimes) > 1)
            throw new NotSupportedException("Review a single access or mode change and an optional modification time.");

        bool security = draft.Changes.Any(change => change is ChangePermissionMode or ChangePosixAcl);
        var sections = security ? SecuritySections(binding) : PermissionSections.Owner;
        if (draft.Changes.Any(change => change is ChangePermissionTimes)) sections |= PermissionSections.Times;
        var startingRevision = RequireRevision(original, sections);
        var expected = original;
        var components = ImmutableArray.CreateBuilder<ReviewedPermissionComponent>(draft.Changes.Length);
        foreach (var change in draft.Changes.OrderBy(change => change is ChangePermissionTimes))
        {
            var before = RequireRevision(expected, sections);
            var stage = ProjectObjectChange(expected, change, acceptSetIdClear);
            expected = stage.Expected;
            components.Add(new(stage.Change, before, RequireRevision(expected, sections), stage.NativeCall));
        }
        return BindPreparedTarget(inspection, source, expected, components.MoveToImmutable(), startingRevision, ct, editDefaults);
    }

    private static PermissionChangeStage ProjectObjectChange(PermissionSnapshot snapshot, PermissionDelta change, bool acceptSetIdClear)
    {
        switch (change)
        {
            case ChangePermissionMode { Bits: <= PermissionBits, TouchedBits: <= PermissionBits } mode:
                var expected = ForecastMode(snapshot, mode, acceptSetIdClear, out ushort requested);
                return new(new ChangePermissionMode(requested, PermissionBits), expected, "fchmodat2 (held descriptor)");
            case ChangePosixAcl { IsDefault: false } access:
                return new(access, ForecastAccess(snapshot, access, acceptSetIdClear), "setxattr (held descriptor)");
            case ChangePosixAcl defaults:
                return new(defaults, ForecastDefaults(snapshot, defaults), defaults.Entries.IsEmpty
                    ? "removexattr default ACL (held descriptor)" : "setxattr default ACL (held descriptor)");
            case ChangePermissionTimes { Modified: { } modified, Created: null } when CanWriteModifiedTime(modified):
                return new(change, snapshot with { Times = snapshot.Times! with { Value = new(modified.ToUniversalTime(), null) } },
                    "utimensat (held descriptor)");
            default:
                throw new NotSupportedException("This component is unavailable for the reviewed Linux object.");
        }
    }

    private static (ReviewedPermissionTarget Target, PermissionSnapshot Expected) BindPreparedTarget(PermissionInspection inspection,
        ItemRef source, PermissionSnapshot expected, ImmutableArray<ReviewedPermissionComponent> components, PermissionRevision startingRevision,
        CancellationToken ct, bool editDefaults = false, LinuxGroupAuthority? groupAuthority = null, bool reviewProtection = false)
    {
        var original = inspection.Snapshot;
        var binding = original.Binding!;
        var sections = startingRevision.Sections;
        var final = RequireRevision(expected, sections);
        var mutation = inspection.TransferLease<LinuxBoundPermission>() ??
            throw new InvalidOperationException("This Linux permission binding has already been released.");
        try
        {
            if (editDefaults && !mutation.DefaultWritesEnabled)
                throw new NotSupportedException("The Linux default ACL writer has not been qualified for release.");
            if (groupAuthority is not null) mutation.AdmitGroupReview(groupAuthority);
            if (reviewProtection) mutation.AdmitProtectionReview(ct);
            var current = mutation.CheckCurrent(sections, ct);
            if (binding != current.Binding || startingRevision != RequireRevision(current, sections))
                throw new PermissionBindingConflictException("The current state changed since inspection.");
            return (new(source, original.Subject, binding, components, final, mutation), expected);
        }
        catch
        {
            mutation.Dispose();
            throw;
        }
    }

    internal static bool CanWriteModifiedTime(DateTimeOffset modified) => modified.UtcDateTime.Year is >= 1970 and <= 2445;

    private static PermissionSections SecuritySections(PermissionBinding binding) => binding.PosixAclDisabled
        ? FileSecurity & ~PermissionSections.Access
        : binding.Identity.Kind == PermissionObjectKind.Directory ? FileSecurity | PermissionSections.Defaults : FileSecurity;

    private static bool IsWritableObject(PermissionSnapshot snapshot) => OperatingSystem.IsLinux() &&
        snapshot.Binding is { ReadOnly: false, LinkCount: > 0, Identity.Kind: PermissionObjectKind.File or PermissionObjectKind.Directory } binding &&
        SupportsMount(binding.FileSystem, additionalFileSystems: true) &&
        snapshot.Subject.LinkPolicy == PermissionLinkPolicy.NoFollow;

    private static bool IsEffectiveOwner(PermissionSnapshot snapshot) => snapshot.Owner.Value is { } owner &&
        owner.Id == GetEffectiveUid().ToString(CultureInfo.InvariantCulture);

    private static PermissionRevision RequireRevision(PermissionSnapshot snapshot, PermissionSections sections) =>
        PermissionRevision.Read(snapshot, sections) ??
        throw new NotSupportedException("The reviewed change depends on security or time information that could not be read.");

    private static PermissionSnapshot ForecastMode(PermissionSnapshot snapshot, ChangePermissionMode change, bool acceptSetIdClear, out ushort requested,
        bool? mayRetainSetGroupId = null)
    {
        ValidateKnownAcl(snapshot);
        ushort projected = ProjectMode(snapshot, change, acceptSetIdClear, out requested, mayRetainSetGroupId);
        var access = snapshot.Access;
        if (access.Value?.Acl is PosixAcl acl)
            access = access with { Value = new(LinuxPermissionAcl.WithMode(acl, projected, snapshot.Subject.Authority.Id)) };
        return snapshot with { Mode = snapshot.Mode with { Value = projected }, Access = access };
    }

    private static ushort ProjectMode(PermissionSnapshot snapshot, ChangePermissionMode change, bool acceptSetIdClear, out ushort requested,
        bool? mayRetainSetGroupId = null)
    {
        ushort oldMode = snapshot.Mode.Value!.Value;
        requested = (ushort)((oldMode & ~change.TouchedBits) | (change.Bits & change.TouchedBits));
        return requested == oldMode ? oldMode : ForecastSetId(snapshot, requested, change.TouchedBits, acceptSetIdClear, mayRetainSetGroupId);
    }

    private static PermissionSnapshot ForecastAccess(PermissionSnapshot snapshot, ChangePosixAcl change, bool acceptSetIdClear,
        bool? mayRetainSetGroupId = null)
    {
        if (snapshot.Binding?.PosixAclDisabled == true) throw new NotSupportedException("This filesystem has POSIX ACLs disabled.");
        ValidateKnownAcl(snapshot);
        ushort oldMode = snapshot.Mode.Value!.Value;
        var acl = LinuxPermissionAcl.Encode(change.Entries, snapshot.Subject.Authority.Id);
        ushort projected = LinuxPermissionAcl.ProjectMode(acl, oldMode);
        var access = acl.Entries.Length == 3
            ? snapshot.Access with { Status = PermissionReadStatus.Absent, Value = null }
            : snapshot.Access with { Status = PermissionReadStatus.Present, Value = new(acl) };
        var current = snapshot with { Mode = snapshot.Mode with { Value = projected }, Access = access };
        var sections = PermissionSections.Mode | PermissionSections.Access;
        if (RequireRevision(current, sections) != RequireRevision(snapshot, sections))
            current = current with { Mode = snapshot.Mode with { Value = ForecastSetId(snapshot, projected, 0, acceptSetIdClear, mayRetainSetGroupId) } };
        return current;
    }

    private static PermissionSnapshot ForecastDefaults(PermissionSnapshot snapshot, ChangePosixAcl change)
    {
        if (snapshot.Binding?.PosixAclDisabled == true) throw new NotSupportedException("This filesystem has POSIX ACLs disabled.");
        ValidateKnownAcl(snapshot);
        if (snapshot.Binding?.Identity.Kind != PermissionObjectKind.Directory ||
            snapshot.Defaults.Status != PermissionReadStatus.Absent && snapshot.Defaults.Value?.Acl is not PosixAcl)
            throw new NotSupportedException("The selected directory's current default ACL could not be established.");
        if (change.Entries.IsDefault)
            throw new InvalidDataException("A default ACL edit requires a supplied rule list.");
        if (change.Entries.IsEmpty)
            return snapshot with { Defaults = snapshot.Defaults with { Status = PermissionReadStatus.Absent, Value = null } };
        var acl = LinuxPermissionAcl.Encode(change.Entries, snapshot.Subject.Authority.Id);
        return snapshot with { Defaults = snapshot.Defaults with { Status = PermissionReadStatus.Present, Value = new(acl) } };
    }

    private static void ValidateKnownAcl(PermissionSnapshot snapshot)
    {
        if (snapshot.Binding?.PosixAclDisabled == true && snapshot.Model == PermissionModel.UnixMode) return;
        if ((snapshot.Access.Status != PermissionReadStatus.Absent && snapshot.Access.Value?.Acl is not PosixAcl) ||
            snapshot.Defaults.Value?.Acl is OpaquePermissionAcl)
            throw new NotSupportedException("This change depends on a POSIX ACL with unsupported rules.");
    }

    private static ushort ForecastSetId(PermissionSnapshot snapshot, ushort requested, ushort touched, bool acceptClear,
        bool? mayRetainSetGroupId = null)
    {
        if ((requested & SetGroupId) != 0 && !(mayRetainSetGroupId ?? MayRetainSetGroupId(snapshot)))
        {
            if ((touched & SetGroupId) != 0)
                throw new UnauthorizedAccessException("The kernel would clear the requested set-group-ID bit for this user.");
            requested &= unchecked((ushort)~SetGroupId);
        }
        if (!acceptClear && (snapshot.Mode.Value!.Value & SetIdBits & ~requested) != 0)
            throw new InvalidOperationException("Review and accept clearing the existing set-ID bits before applying.");
        return requested;
    }

    private static bool MayRetainSetGroupId(PermissionSnapshot snapshot)
    {
        if (!uint.TryParse(snapshot.Group.Value?.Id, NumberStyles.None, CultureInfo.InvariantCulture, out uint group))
            throw new NotSupportedException("The file group could not be verified.");
        if (GetEffectiveGid() == group) return true;
        int count = GetGroups(0, null);
        if (count is < 0 or > 65536) throw new IOException("The current supplementary groups could not be verified.");
        var groups = new uint[count];
        fixed (uint* ids = groups)
            if (GetGroups(groups.Length, ids) != count) throw new IOException("The current supplementary groups changed during review.");
        if (groups.Contains(group)) return true;
        int* header = stackalloc int[2] { 0x20080522, 0 };
        uint* capabilities = stackalloc uint[6];
        if (CapGet(header, capabilities) != 0) throw new IOException("The current file set-ID capability could not be verified.");
        return (capabilities[0] & (1u << 4)) != 0; // CAP_FSETID
    }

    private sealed class LinuxBoundPermission(PermissionSubject subject, PermissionBinding binding, SafeFileHandle pin,
        string[] names, Stamp[] ancestry, bool qualifyDefaultWrites, bool qualifyGroupWrites, bool additionalFileSystems,
        bool qualifyProtectionWrites, Stamp[]? treeParent) : IBoundPermissionMutation
    {
        private Stamp? _checked;
        private LinuxGroupAuthority? _groupAuthority;
        private LinuxGroupAuthority? _flagAuthority;
        private SafeFileHandle? _flagsHandle;
        private uint? _checkedFlags;
        private int? _resetFromDepth;
        internal bool DefaultWritesEnabled => qualifyDefaultWrites;
        internal Stamp[] TreeAncestry => ancestry;
        internal void PreserveTreeResetProtection(int sourceDepth) => _resetFromDepth = sourceDepth;

        internal Stamp EnumerateTree(Action<string?, ulong> visit, CancellationToken ct)
        {
            var before = ReadStamp(pin);
            UnixArchiveNative.EnumerateSelectionNames(pin, visit, ct);
            if (ReadStamp(pin) != before)
                throw new ReadFailure(PermissionReadStatus.Stale, "The directory changed while capturing its entries.");
            return before;
        }

        internal void ConfirmTreeListing(Stamp listing)
        {
            try
            {
                if (ReadStamp(pin) == listing) return;
            }
            catch (ReadFailure) { }
            throw new PermissionBindingConflictException("The directory changed while capturing its entries. Review the scope again.");
        }

        internal Stamp ReadTreeChild(string name, ulong inode)
        {
            using var child = OpenSameMountChild(pin, name, PinFlags, "Bind a captured directory entry");
            var facts = ReadStamp(child);
            if (facts.Mount != ancestry[^1].Mount || facts.Device != ancestry[^1].Device)
                throw new ReadFailure(PermissionReadStatus.Unsupported, "The entry crosses a mount or Btrfs subvolume boundary.", 18); // EXDEV
            if (facts.Inode != inode)
                throw new ReadFailure(PermissionReadStatus.Stale, "The directory entry changed during review.");
            return facts;
        }

        internal LinuxAccessReport CheckEffectiveAccess(CancellationToken cancellation) =>
            CheckBoundAccess(pin, binding, ancestry[^1], additionalFileSystems, NamespaceMatches, cancellation);

        internal LinuxBrokerTarget DuplicateBrokerTarget(CancellationToken cancellation)
        {
            var original = ancestry[^1];
            using var proc = OpenOwnProc(out string pid, cancellation);
            var mounts = ReadMounts(proc, pid, cancellation);
            if (!NamespaceMatches(mounts, cancellation, original) || ReadStamp(pin) != original)
                throw new PermissionBindingConflictException("The original item changed before it could be handed to the permissions broker.");

            // The broker compares the non-recycled mount ID. The ordinary inspector still uses mountinfo's ID.
            const uint required = Type | Inode | UniqueMountId;
            byte* facts = stackalloc byte[256];
            new Span<byte>(facts, 256).Clear();
            if (Statx(pin, "", AtEmptyPath, required, facts) != 0)
                throw Failure("Read the unique held mount identity", Marshal.GetLastPInvokeError());
            if ((*(uint*)facts & required) != required || *(ulong*)(facts + 144) == 0)
                throw new NotSupportedException("The kernel did not return a unique mount identity for the broker.");
            var identity = new LinuxBrokerIdentity(*(ulong*)(facts + 144), *(ulong*)(facts + 32),
                *(uint*)(facts + 136), *(uint*)(facts + 140), (ushort)(*(ushort*)(facts + 28) & 0xf000));
            if (identity.Inode != original.Inode || identity.Kind != original.FileKind ||
                ((ulong)identity.DeviceMajor << 32 | identity.DeviceMinor) != original.Device || ReadStamp(pin) != original)
                throw new PermissionBindingConflictException("The held item changed while reading its broker identity.");

            cancellation.ThrowIfCancellationRequested();
            int descriptor = DuplicateBrokerPin(pin, 1030 /* F_DUPFD_CLOEXEC */, 3);
            if (descriptor < 0) throw Failure("Duplicate the held descriptor for the broker", Marshal.GetLastPInvokeError());
            return new(subject.RawLocator == "/" ? "/" : subject.RawLocator.TrimEnd('/'),
                identity, new SafeFileHandle((nint)descriptor, ownsHandle: true));
        }

        internal void AdmitGroupReview(LinuxGroupAuthority authority)
        {
            if (!qualifyGroupWrites) throw new NotSupportedException("The Linux group writer awaits release qualification.");
            _groupAuthority = authority;
        }

        internal void AdmitProtectionReview(CancellationToken ct)
        {
            if (!qualifyProtectionWrites || binding.FileSystem != "ext4" || binding.ReadOnly != false)
                throw new NotSupportedException("The Linux inode protection setter awaits release qualification.");
            try
            {
                using var proc = OpenOwnProc(out string pid, ct);
                _flagAuthority = ReadGroupAuthority(proc, pid);
                if ((_flagAuthority.Capabilities & ImmutableCapability) == 0)
                    throw new UnauthorizedAccessException("Changing inode protection requires CAP_LINUX_IMMUTABLE.");
                _flagsHandle = OpenFlagHandle(proc, pid, pin, ancestry[^1], ct);
            }
            catch (ReadFailure failure) when (failure.Status is PermissionReadStatus.Stale or PermissionReadStatus.Gone)
            {
                throw new PermissionBindingConflictException(failure.Message);
            }
            catch (ReadFailure failure) { throw new IOException(failure.Message); }
        }

        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation)
        {
            _checked = null;
            _checkedFlags = null;
            try
            {
                using var proc = OpenOwnProc(out string pid, cancellation);
                if (_groupAuthority is { } credentials && ReadGroupAuthority(proc, pid) != credentials)
                    throw new PermissionBindingConflictException("FileCat's group credentials changed since review.");
                if (_flagAuthority is { } flagCredentials && ReadGroupAuthority(proc, pid) != flagCredentials)
                    throw new PermissionBindingConflictException("FileCat's inode protection credentials changed since review.");
                var mounts = ReadMounts(proc, pid, cancellation);
                var observed = ReadBound(sections, proc, pid, mounts, cancellation, out var stamp);
                if (!NamespaceMatches(mounts, cancellation, stamp))
                    throw new PermissionBindingConflictException("The original namespace changed after inspection.");
                if ((stamp.Valid & Uid) == 0 || GetEffectiveUid() == 0 || stamp.User != GetEffectiveUid())
                    throw new UnauthorizedAccessException("The original object is not owned by this FileCat user.");
                if (_flagsHandle is not null && observed.Flags.Value is { KnownBits: uint.MaxValue } flags)
                    _checkedFlags = (uint)flags.Bits;
                _checked = stamp;
                return observed;
            }
            catch (ReadFailure failure) when (failure.Status is PermissionReadStatus.Stale or PermissionReadStatus.Gone)
            {
                throw new PermissionBindingConflictException(failure.Message);
            }
            catch (ReadFailure failure) { throw new IOException(failure.Message); }
        }

        public PermissionMutationReturn Apply(PermissionDelta change)
        {
            var checkedStamp = _checked;
            _checked = null;
            var checkedFlags = _checkedFlags;
            _checkedFlags = null;
            if (checkedStamp is null) throw new InvalidOperationException("The original permissions must be rechecked before a call.");
            try
            {
                if (ReadStamp(pin) != checkedStamp) return new(RejectedBeforeChange: true);
                if (_flagsHandle is not null)
                {
                    if (!HasImmutableCapability()) return new(1, RejectedBeforeChange: true);
                    if (ReadInodeFlags(_flagsHandle) != checkedFlags || ReadStamp(pin) != checkedStamp)
                        return new(RejectedBeforeChange: true, BindingConflict: true, Detail: "The reviewed inode flags or security state changed.");
                }
                if (checkedStamp.Value.Protection != 0 && change is not ChangePermissionFlags)
                    return new(1, RejectedBeforeChange: true); // EPERM
                return change switch
                {
                    ChangePermissionFlags flags when _flagsHandle is not null && checkedFlags is { } current =>
                        ApplyInodeFlags(_flagsHandle, current, flags),
                    ChangePermissionMode { Bits: <= PermissionBits, TouchedBits: PermissionBits } mode =>
                        NativeResult(FchmodAt2(452, pin, "", mode.Bits, AtEmptyPath)),
                    ChangePosixAcl acl when !acl.IsDefault || DefaultWritesEnabled => ApplyAcl(acl, checkedStamp.Value),
                    AssignPermissionPrincipal { IsOwner: false } group when _groupAuthority is { } credentials &&
                        TryGroupId(group.Principal, subject.Authority.Id, out uint gid) && credentials.Includes(gid) =>
                        NativeResult(FchownAt(pin, "", uint.MaxValue, gid, AtEmptyPath)),
                    ChangePermissionTimes { Modified: { } modified, Created: null } => ApplyTime(modified),
                    _ => throw new InvalidOperationException("This change was not prepared for the held Linux descriptor.")
                };
            }
            catch (ReadFailure failure) { return new(failure.Error, RejectedBeforeChange: true); }
        }

        private static PermissionMutationReturn ApplyInodeFlags(SafeFileHandle handle, uint current, ChangePermissionFlags change)
        {
            if (change.TouchedBits == 0 || (change.TouchedBits & ~(ulong)InodeProtection) != 0 ||
                (change.Bits & ~change.TouchedBits) != 0)
                throw new InvalidOperationException("Only reviewed immutable and append-only bits can be written.");
            uint flags = (current & ~(uint)change.TouchedBits) | (uint)change.Bits;
            return NativeResult(InodeFlagIoctl(handle, SetInodeFlags, &flags));
        }

        private PermissionMutationReturn ApplyAcl(ChangePosixAcl change, Stamp checkedStamp)
        {
            var result = new PermissionMutationReturn(RejectedBeforeChange: true);
            bool remove = change.IsDefault && change.Entries.IsEmpty;
            var attribute = change.IsDefault ? DefaultAttribute : AccessAttribute;
            var encoded = remove ? null : LinuxPermissionAcl.Encode(change.Entries, subject.Authority.Id);
            using var proc = OpenOwnProc(out string pid, CancellationToken.None);
            WithProcPin(proc, pid, pin, checkedStamp, CancellationToken.None, entry =>
            {
                if (ReadStamp(pin) != checkedStamp) return;
                if (encoded is null)
                    result = NativeResult(RemoveXattr(entry, attribute));
                else
                    fixed (byte* bytes = encoded.Bytes.AsSpan())
                        result = NativeResult(SetXattr(entry, attribute, bytes, (nuint)encoded.Bytes.Length, 0));
            });
            return result;
        }

        private PermissionMutationReturn ApplyTime(DateTimeOffset modified)
        {
            long seconds = modified.ToUnixTimeSeconds();
            var times = stackalloc TimeSpec[2];
            times[0] = new() { Nanoseconds = 0x3ffffffe }; // UTIME_OMIT preserves atime.
            times[1] = new() { Seconds = seconds, Nanoseconds = modified.UtcTicks % TimeSpan.TicksPerSecond * 100 };
            return NativeResult(UtimensAt(pin, "", times, AtEmptyPath));
        }

        private static PermissionMutationReturn NativeResult(long result)
        {
            if (result == 0) return new();
            int error = Marshal.GetLastPInvokeError();
            return new(error, RejectedBeforeChange: error is 1 or 13 or 22 or 30 or 38 or 95);
        }

        public PermissionReadBack Observe(PermissionSections sections)
        {
            using var proc = OpenOwnProc(out string pid, CancellationToken.None);
            var mounts = ReadMounts(proc, pid, CancellationToken.None);
            var observed = ReadBound(sections, proc, pid, mounts, CancellationToken.None, out _);
            bool changed = !NamespaceMatches(mounts, CancellationToken.None);
            return new(observed, PermissionVerification.SameObject, changed, changed
                ? "The original object was verified on its held descriptor. Its name or folder changed during the call." : null);
        }

        private PermissionSnapshot ReadBound(PermissionSections sections, SafeFileHandle proc, string pid, Dictionary<ulong, Mount> mounts,
            CancellationToken ct, out Stamp stamp)
        {
            stamp = ReadStamp(pin);
            if (stamp.Identity != ancestry[^1].Identity || (stamp.Valid & ChangeTime) == 0)
                throw new PermissionBindingConflictException("The held object could not corroborate its reviewed identity or security revision.");
            var mount = AdmitMount(pin, stamp, mounts, binding.MountRoot, additionalFileSystems);
            if (mount.FileSystem != binding.FileSystem || mount.PosixAclDisabled != binding.PosixAclDisabled)
                throw new PermissionBindingConflictException("The mounted filesystem or its ACL policy changed after review.");
            var observed = ReadSnapshot(subject, binding with { ReadOnly = mount.ReadOnly }, pin, stamp, proc, pid, sections, ct,
                _flagsHandle, qualifyProtectionWrites);
            if (ReadStamp(pin) != stamp)
                throw new PermissionBindingConflictException("The held object changed while reading the requested section.");
            return observed;
        }

        private bool NamespaceMatches(Dictionary<ulong, Mount> mounts, CancellationToken ct, Stamp? stamp = null)
        {
            try
            {
                using var fresh = Walk(names, mounts, additionalFileSystems, ct, out var current, out _, treeParent);
                return (!stamp.HasValue || current[^1] == stamp) && ancestry.Select(value => value.Identity).SequenceEqual(current.Select(value => value.Identity)) &&
                    (_resetFromDepth is not { } depth || current.Skip(depth).All(value => value.ResetUnprotected == true));
            }
            catch (ReadFailure) { return false; }
        }

        public void Dispose()
        {
            _flagsHandle?.Dispose();
            pin.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec { public long Seconds, Nanoseconds; }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUid();
    [LibraryImport("libc", EntryPoint = "getegid")]
    private static partial uint GetEffectiveGid();
    [LibraryImport("libc", EntryPoint = "getgroups", SetLastError = true)]
    private static partial int GetGroups(int count, uint* groups);
    [LibraryImport("libc", EntryPoint = "capget", SetLastError = true)]
    private static partial int CapGet(int* header, uint* capabilities);
    [LibraryImport("libc", EntryPoint = "syscall", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial long FchmodAt2(long syscall, SafeFileHandle pin, string name, uint mode, int flags);
    [LibraryImport("libc", EntryPoint = "utimensat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int UtimensAt(SafeFileHandle pin, string name, TimeSpec* times, int flags);
    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int SetXattr(string name, string attribute, byte* buffer, nuint length, int flags);
    [LibraryImport("libc", EntryPoint = "removexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RemoveXattr(string name, string attribute);
}
