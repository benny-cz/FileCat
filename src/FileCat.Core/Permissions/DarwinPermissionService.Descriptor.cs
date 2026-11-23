using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class DarwinPermissionService
{
    private const int SearchDirectory = 0x40100000, ExecuteFile = 0x40000000, NoFollowCloseOnExec = 0x01000100;
    private const uint GenericRead = 1u << 24, GenericWrite = 1u << 23, GenericExecute = 1u << 22, GenericAll = 1u << 21;
    private const uint ReadData = 1u << 1, WriteData = 1u << 2, ExecuteRight = 1u << 3, InheritOnly = 1u << 8;
    internal const string HeldDescriptorReviewNotice = "FileCat keeps the selected object open. If its name changes during the call, the result identifies the original object.";

    private static bool CanOpenMetadataDescriptor(Entry entry, PermissionObservation<PermissionAclValue> access) =>
        entry.Kind == PermissionObjectKind.Directory || entry.Kind == PermissionObjectKind.File &&
        ((entry.Mode & 0x49) != 0 || access.Value?.Acl is DarwinAcl acl && acl.Entries.Any(rule =>
            rule.Kind == DarwinAceKind.Allow && (rule.Flags & InheritOnly) == 0 && (rule.Rights & (ExecuteRight | GenericAll | GenericExecute)) != 0));

    private static SafeFileHandle OpenMetadataDescriptor(SafeFileHandle parent, string leaf, PermissionObjectKind kind)
    {
        int flags = NoFollowCloseOnExec | (kind switch
        {
            PermissionObjectKind.Directory => SearchDirectory,
            PermissionObjectKind.File => ExecuteFile,
            _ => throw new NotSupportedException("Only a directory or an executable regular file can be opened for reviewed metadata.")
        });
        // Both opens let the kernel reject a special node before its filesystem open operation.
        int fd = OpenAt(parent, leaf, flags);
        if (fd < 0) throw NativeFailure("Open the reviewed Darwin metadata descriptor", Marshal.GetLastPInvokeError());
        return new(fd, ownsHandle: true);
    }

    private static void CheckExchangeAdmission(PermissionSnapshot snapshot)
    {
        if (snapshot.Binding is not { FileSystem: "hfs", Identity.Kind: PermissionObjectKind.File }) return;
        bool outsideRead = (snapshot.Mode.Value.GetValueOrDefault() & 0x24) != 0;
        bool outsideWrite = (snapshot.Mode.Value.GetValueOrDefault() & GroupOrOtherWrite) != 0;
        if (outsideRead && outsideWrite) throw ExchangeRefusal();
        if (snapshot.Access.Status == PermissionReadStatus.Absent) return;
        if (snapshot.Access.Value?.Acl is not DarwinAcl acl) throw ExchangeRefusal();
        Principal? requester = null, root = null;
        bool resolved = false;
        foreach (var rule in acl.Entries)
        {
            if ((rule.Flags & InheritOnly) != 0 || rule.Kind == DarwinAceKind.Deny) continue;
            if (rule.Kind != DarwinAceKind.Allow) throw ExchangeRefusal();
            const uint exchangeRights = ReadData | WriteData | WriteSecurity | ChangeOwner | GenericRead | GenericWrite | GenericAll;
            if ((rule.Rights & exchangeRights) == 0) continue;
            if (!resolved)
            {
                requester = DarwinPrincipalNames.UserUuid(EffectiveUser(), snapshot.Subject.Authority.Id);
                root = DarwinPrincipalNames.UserUuid(0, snapshot.Subject.Authority.Id);
                resolved = true;
            }
            if (rule.Principal == requester || rule.Principal == root) continue;
            if ((rule.Rights & (WriteSecurity | ChangeOwner | GenericWrite | GenericAll)) != 0) throw ExchangeRefusal();
            outsideRead |= (rule.Rights & (ReadData | GenericRead)) != 0;
            outsideWrite |= (rule.Rights & WriteData) != 0;
            // An account may belong to multiple groups and unresolved UUIDs may denote groups.
            if (outsideRead && outsideWrite) throw ExchangeRefusal();
        }
    }

    private static NotSupportedException ExchangeRefusal() =>
        new("An HFS+ regular-file descriptor requires proof that other accounts cannot obtain both read-data and write-data access.");

    private sealed class DescriptorMutation(PermissionSubject subject, PermissionBinding binding, InspectionPin pin, SafeFileHandle descriptor,
        uint user, ImmutableArray<ReviewedPermissionComponent> components, DarwinGroupAuthority? groupAuthority) : IBoundPermissionMutation
    {
        private LaneObservation? _checked, _previous;
        private bool _dispatched, _folderChanged;

        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation)
        {
            _checked = null;
            try
            {
                _checked = MacMaterializationPolicy.Run(cancellation, () =>
                {
                    var observation = ReadDescriptor(sections);
                    if (!GroupAuthorityMatches(groupAuthority, cancellation) || !NameMatches(observation.Entry) ||
                        _previous is { } previous && !SameObservation(previous, observation, sections))
                        throw new PermissionBindingConflictException("The original name or retained descriptor changed before the call.");
                    return observation;
                }, (_, reason) => throw new IOException(reason));
                return _checked.Snapshot;
            }
            catch (Exception error) when (error is ReadFailure or IOException or NotSupportedException or UnauthorizedAccessException)
            { throw new PermissionBindingConflictException(error.Message); }
        }

        public PermissionMutationReturn Apply(PermissionDelta change)
        {
            var prior = _checked ?? throw new InvalidOperationException("The Darwin descriptor must be rechecked before dispatch.");
            _checked = null;
            var component = components.FirstOrDefault(item => ReferenceEquals(item.Change, change)) ??
                throw new InvalidOperationException("Darwin requires the original reviewed component.");
            return MacMaterializationPolicy.Run(CancellationToken.None, () =>
            {
                LaneObservation current;
                try
                {
                    if (!GroupAuthorityMatches(groupAuthority)) return new PermissionMutationReturn(BindingConflict: true);
                    current = ReadDescriptor(component.Before.Sections);
                    if (!SameObservation(prior, current, component.Before.Sections) || !NameMatches(current.Entry) ||
                        change is ChangePermissionMode { Bits: var mode } && (mode & SetGroupId) != 0 && !InFileGroup(current.Snapshot))
                        return new PermissionMutationReturn(BindingConflict: true);
                    if (change is ChangePermissionMode projected && binding.FileSystem == "hfs")
                        CheckExchangeAdmission(current.Snapshot with { Mode = current.Snapshot.Mode with { Value = projected.Bits } });
                }
                catch (Exception error) when (error is ReadFailure or IOException or NotSupportedException or UnauthorizedAccessException)
                { return new PermissionMutationReturn(BindingConflict: true); }

                _dispatched = true;
                return NativeResult(change switch
                {
                    ChangePermissionMode mode => Fchmod(descriptor, mode.Bits),
                    ChangePermissionTimes times => SetTimes(descriptor, null, times),
                    ChangePermissionFlags { TouchedBits: FinderHidden } hidden => SetHidden(descriptor, current.Entry.Flags, hidden.Bits != 0),
                    ChangePermissionFlags flags => SetProtectedFlags(descriptor, PatchFlags(current.Entry.Flags, flags)),
                    ApplyDarwinAcl acl => SetAcl(descriptor, null, acl),
                    AssignPermissionPrincipal group => SetGroup(descriptor, null, subject, group),
                    _ => throw new InvalidOperationException("The component is outside the Darwin descriptor route.")
                });
            }, (_, _) => _dispatched ? new PermissionMutationReturn() : new PermissionMutationReturn(BindingConflict: true));
        }

        public PermissionReadBack Observe(PermissionSections sections)
        {
            try
            {
                return MacMaterializationPolicy.Run(CancellationToken.None, () =>
                {
                    string? unflushed = FlushRefusal(descriptor, subject.RawLocator);
                    var observed = ReadDescriptor(sections);
                    _folderChanged |= !NameMatches(observed.Entry);
                    if (_dispatched) _previous = observed;
                    _dispatched = false;
                    if (unflushed is not null)
                        return new PermissionReadBack(observed.Snapshot, PermissionVerification.Unavailable, _folderChanged, unflushed);
                    return new PermissionReadBack(observed.Snapshot, PermissionVerification.SameObject, _folderChanged, _folderChanged
                        ? "The retained object was verified. Its original name or folder changed." : null);
                }, (_, reason) => Unverified(sections, reason));
            }
            catch (Exception error) when (error is ReadFailure or IOException or NotSupportedException or UnauthorizedAccessException)
            { return Unverified(sections, error.Message); }
        }

        private LaneObservation ReadDescriptor(PermissionSections sections)
        {
            var mount = ReadMount(descriptor);
            if (EffectiveUser() != user || mount != pin.Mount)
                throw new PermissionBindingConflictException("The retained object's mount or acting user changed.");
            var before = ReadEntry(descriptor, null);
            if (!SameIdentity(before, pin.Entry) || before.Owner != user)
                throw new PermissionBindingConflictException("The retained descriptor no longer corroborates the reviewed identity or owner.");
            var currentBinding = binding with { LinkCount = before.Links };
            var snapshot = ReadSnapshot(subject, currentBinding, sections, descriptor, null, before, mount);
            CheckExchangeAdmission(snapshot);
            if (ReadEntry(descriptor, null) != before || ReadMount(descriptor) != pin.Mount)
                throw new PermissionBindingConflictException("The retained descriptor changed while its security was read.");
            return new(before, snapshot);
        }

        private bool NameMatches(Entry entry)
        {
            try
            {
                using var parent = OpenDirectory(pin.Directory);
                return UnixFiles.Stat(parent)?.Identity == pin.Identity && ReadMount(parent) == pin.ParentMount &&
                    SameIdentity(ReadEntry(parent, pin.Leaf), entry);
            }
            catch (Exception error) when (error is ReadFailure or IOException) { return false; }
        }

        private static bool SameIdentity(Entry first, Entry second) =>
            first.Object == second.Object && first.FileSystem == second.FileSystem && first.Kind == second.Kind;

        private PermissionReadBack Unverified(PermissionSections sections, string reason) => new(
            Unavailable(subject, sections, PermissionReadStatus.Stale, reason), PermissionVerification.Unavailable,
            Detail: "The original Darwin descriptor could not be verified after the call. " + reason);

        public void Dispose() => pin.Dispose();
    }

    private static int SetHidden(SafeFileHandle handle, uint before, bool hidden)
    {
        var attributes = new AttrList { Count = 5, Common = 0x40000 }; // ATTR_CMN_FLAGS
        uint flags = MacFinderHidden.WithFlag(before, hidden);
        return SetHeldAttributes(handle, &attributes, &flags, sizeof(uint), 0);
    }

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAt(SafeFileHandle parent, string name, int flags);
    [LibraryImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static partial int Fchmod(SafeFileHandle handle, ushort mode);
    [LibraryImport("libc", EntryPoint = "fsetattrlist", SetLastError = true)]
    private static partial int SetHeldAttributes(SafeFileHandle handle, AttrList* attributes, void* data, nuint length, uint options);
}
