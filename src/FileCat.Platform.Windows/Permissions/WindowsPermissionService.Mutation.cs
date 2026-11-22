using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal sealed unsafe partial class WindowsPermissionService
{
    private const uint WriteDac = 0x40000, WriteOwner = 0x80000;
    private const uint OwnerInformation = 1, DaclInformation = 4;

    internal static string? EditRefusal(PermissionSnapshot snapshot)
    {
        if (ReviewedPermissionOperation.ProcessRefusal() is { } refusal) return refusal;
        if (snapshot.BindingProblem is { } problem) return problem;
        if (snapshot.Mount is { IsServerProjection: true } mount) return mount.ChangeRefusal;
        if (snapshot.Binding is { FileSystem: "ReFS" }) return "ReFS permissions are inspection-only in FileCat.";
        if (snapshot.Binding is { RemovableMedia: true }) return "Permissions on removable NTFS volumes are inspection-only in FileCat.";
        if (snapshot.Binding is not { FileSystem: "NTFS", ReadOnly: false, LinkCount: > 0, Identity.Kind: PermissionObjectKind.File } ||
            snapshot.Model != PermissionModel.WindowsDescriptor || snapshot.Subject.LinkPolicy != PermissionLinkPolicy.NoFollow)
            return "This permissions writer requires an ordinary file on a writable local NTFS volume.";
        if (PermissionRevision.Read(snapshot, DiscretionarySections) is null)
            return "The owner's SID, primary group, DACL and control could not all be verified.";
        return null;
    }

    internal static ImmutableArray<Principal> OwnerChoices(PermissionAuthority authority) =>
        WindowsPermissionToken.OwnerChoices(authority.Id).Choices;

    internal static PermissionSnapshot PreviewChange(PermissionSnapshot original, PermissionDelta change) => Project(original, change).Expected;

    // Native propagation runs through SetSecurityInfo, which first rewrites a DACL stored without SE_DACL_AUTO_INHERITED in
    // automatic form (see HasLegacyDacl). Such a folder offers no propagation or inheritance change.
    internal bool CanReviewNativePropagation(PermissionSnapshot snapshot) =>
        _qualifyNativePropagation && IsReviewableNtfsFolder(snapshot) && snapshot.Inheritance.Value is { IsAutoInherited: true };

    // A DACL stored without SE_DACL_AUTO_INHERITED, as format, older tools and folders below such a DACL leave it. SetSecurityInfo
    // would set that bit and, unless the DACL is protected, mark and move the entries matching the parent's inheritable rules:
    // changes the review never showed. FileCat writes such a DACL as a descriptor, which stores the reviewed entries as they are.
    private static bool HasLegacyDacl(PermissionSnapshot snapshot) => snapshot.Inheritance.Value is { IsAutoInherited: false };

    private static bool IsReviewableNtfsFolder(PermissionSnapshot snapshot) => ReviewedPermissionOperation.ProcessRefusal() is null &&
        snapshot is { BindingProblem: null, Mount: null, Model: PermissionModel.WindowsDescriptor,
            Binding: { FileSystem: "NTFS", ReadOnly: false, LinkCount: > 0,
                Identity.Kind: PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot },
            Access.Value.Acl: WindowsDacl { Form: WindowsDaclForm.Entries, Revision: 2 or 4 } } &&
        PermissionSnapshots.IsLocal(snapshot.Subject) && snapshot.Subject.LinkPolicy == PermissionLinkPolicy.NoFollow &&
        PermissionRevision.Read(snapshot, DiscretionarySections) is not null;

    internal static PermissionSnapshot PreviewPropagation(PermissionSnapshot original, PermissionDelta change, InheritanceParent? parent = null) =>
        Project(original, change, directory: true, parent).Expected;

    internal (ReviewedPermissionOperation Review, PermissionSnapshot Expected) PrepareNativePropagation(PermissionInspection inspection,
        ItemRef source, PermissionDraft draft, PropagationBoundary boundary, CancellationToken ct, InheritanceParent? parent = null)
    {
        ct.ThrowIfCancellationRequested();
        if (!CanReviewNativePropagation(inspection.Snapshot) || draft.Scope != PermissionScope.NativePropagation ||
            draft.Changes is not [AddWindowsAce or ChangeWindowsAce or ChangeWindowsInheritance] || source.Kind != EntryKind.Directory ||
            source.FileSystemPath != inspection.Snapshot.Subject.RawLocator)
            throw new NotSupportedException("Native inheritance requires one reviewed access-rule change on the original local NTFS folder.");
        var snapshot = inspection.Snapshot;
        if (draft.Changes[0] is ChangeWindowsInheritance && !CanReviewInheritanceChange(snapshot))
            throw new NotSupportedException("Inheritance protection changes are unavailable on this route.");
        RequireUnchangedPropagationBoundary(snapshot, boundary, ct);
        if (draft.Changes[0] is ChangeWindowsInheritance { Action: WindowsInheritanceAction.Enable })
            RequireUnchangedInheritanceParent(snapshot, boundary, parent ??
                throw new NotSupportedException("A verified parent DACL is required to enable inheritance."), ct);
        else parent = null;
        return PrepareChange(inspection, source, draft, boundary, ct, parent);
    }

    internal static (ReviewedPermissionOperation Review, PermissionSnapshot Expected) Prepare(PermissionInspection inspection, ItemRef source,
        PermissionDraft draft, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var original = inspection.Snapshot;
        if (EditRefusal(original) is { } refusal) throw new NotSupportedException(refusal);
        if (original.Subject.RawLocator != source.FileSystemPath || draft.Scope != PermissionScope.Objects ||
            draft.Changes is not [AddWindowsAce or ChangeWindowsAce or AssignPermissionPrincipal { IsOwner: true }])
            throw new NotSupportedException("Review one owner or DACL change for the original Windows file.");
        return PrepareChange(inspection, source, draft, null, ct);
    }

    private static (ReviewedPermissionOperation Review, PermissionSnapshot Expected) PrepareChange(PermissionInspection inspection, ItemRef source,
        PermissionDraft draft, PropagationBoundary? boundary, CancellationToken ct, InheritanceParent? inheritanceParent = null,
        bool folderOnly = false)
    {
        var prepared = PrepareChangeTarget(inspection, source, draft.Changes[0], boundary, ct, inheritanceParent, folderOnly: folderOnly);
        try { return (new(draft.Scope, [prepared.Target]), prepared.Expected); }
        catch { prepared.Target.Mutation.Dispose(); throw; }
    }

    private static (ReviewedPermissionTarget Target, PermissionSnapshot Expected) PrepareChangeTarget(PermissionInspection inspection,
        ItemRef source, PermissionDelta change, PropagationBoundary? boundary, CancellationToken ct, InheritanceParent? inheritanceParent = null,
        ImmutableArray<PermissionIdentity> treeAncestry = default, bool folderOnly = false)
    {
        var original = inspection.Snapshot;
        bool propagates = boundary is not null;
        var binding = original.Binding!;
        var before = RequireRevision(original);
        bool reset = change is ResetWindowsDacl;
        var projection = change is ResetWindowsDacl replacement ? ProjectTreeReset(original, replacement) :
            folderOnly ? ProjectFolderOnly(original, change, ensureRule: !treeAncestry.IsDefault) :
            Project(original, change, directory: propagates, inheritanceParent, ensureRule: !treeAncestry.IsDefault);
        var expected = RequireRevision(projection.Expected);
        if (propagates && before == expected)
            throw new NotSupportedException("This native inheritance route needs a change to the selected folder's access rules.");
        WindowsPermissionToken.Stamp token;
        if (change is AssignPermissionPrincipal owner)
        {
            var assignable = WindowsPermissionToken.OwnerChoices(original.Subject.Authority.Id);
            if (!assignable.Choices.Contains(owner.Principal))
                throw new UnauthorizedAccessException("Select an owner SID assignable by FileCat's ordinary token.");
            token = assignable.Stamp;
        }
        else token = WindowsPermissionToken.CurrentStamp();

        uint access = projection.Section == OwnerInformation ? WriteOwner : WriteDac;
        bool legacy = !propagates && !folderOnly && !reset && projection.Section == DaclInformation && HasLegacyDacl(original);
        var payload = legacy ? ObjectDaclDescriptor(original.Inheritance.Value!.Value, projection.Bytes, original.Subject.Authority.Id)
            : projection.Bytes;
        var component = new ReviewedPermissionComponent(change, before, expected,
            propagates ? "Windows SetSecurityInfo native propagation" : reset ? "Windows NtSetSecurityObject reset DACL" :
            folderOnly && binding.Identity.Kind == PermissionObjectKind.VolumeRoot ? "Windows NtSetSecurityObject volume-root DACL" :
            folderOnly ? "Windows NtSetSecurityObject folder DACL" : legacy ? "Windows NtSetSecurityObject legacy DACL" :
            projection.Section == OwnerInformation ? "Windows SetSecurityInfo owner" : "Windows SetSecurityInfo DACL");
        var mutation = BindDiscretionaryReview(inspection, before, token, ct, access, change, projection.Section, payload,
            boundary, inheritanceParent, treeAncestry, descriptorWrite: folderOnly || reset || legacy);
        return (new(source, original.Subject, binding, [component], expected, mutation), projection.Expected);
    }

    private static WindowsDiscretionaryMutation BindDiscretionaryReview(PermissionInspection inspection, PermissionRevision before,
        WindowsPermissionToken.Stamp token, CancellationToken ct, uint writeAccess = 0, PermissionDelta? change = null, uint section = 0,
        ImmutableArray<byte> bytes = default, PropagationBoundary? boundary = null, InheritanceParent? inheritanceParent = null,
        ImmutableArray<PermissionIdentity> treeAncestry = default, bool descriptorWrite = false)
    {
        var original = inspection.Snapshot;
        var binding = original.Binding!;
        var kind = binding.Identity.Kind;
        bool volumeRoot = kind == PermissionObjectKind.VolumeRoot;
        bool captured = !treeAncestry.IsDefault;
        using var pin = inspection.TransferLease<WindowsInspectionPin>() ??
            throw new InvalidOperationException("This Windows permission binding has already been released.");
        WindowsDiscretionaryMutation? mutation = null;
        try
        {
            using var current = Walk(pin.Root, pin.Names, original.Subject.Authority, ct, out var parent, out var chain, out var now);
            using (parent)
            {
                if ((!volumeRoot && parent is null) || now != binding || !SameChain(pin.Chain, chain) ||
                    captured && (kind == PermissionObjectKind.File && binding.LinkCount != 1 ||
                        !chain.Select(stamp => stamp.Identity).SequenceEqual(treeAncestry)))
                    throw new PermissionBindingConflictException("The original object, mount or folder changed.");
                var retained = Screen(pin.File, binding.Identity.MountInstance, volumeRoot, original.Subject.Authority, ct);
                if (retained != chain[^1])
                    throw new PermissionBindingConflictException("The original path no longer identifies the held object.");
                string leaf = volumeRoot ? @"\??\" + pin.Root : pin.Names[^1];
                var admitted = Open(parent, leaf, security: true, ct, writeAccess, allowDirectoryWrite: kind != PermissionObjectKind.File);
                mutation = new WindowsDiscretionaryMutation(original, pin.Root, pin.Names, pin.Chain, admitted, token, change,
                    section, bytes, boundary, inheritanceParent, captured, descriptorWrite);
                var actual = mutation.CheckCurrent(DiscretionarySections, ct);
                if (actual.Binding != binding || RequireRevision(actual) != before)
                    throw new PermissionBindingConflictException("Windows security changed after inspection.");
            }
            var bound = mutation;
            mutation = null;
            return bound;
        }
        catch (ReadFailure error) { throw ReadError(error); }
        catch (Win32Exception error) { throw new IOException("The reviewed Windows object could not be verified.", error); }
        finally { mutation?.Dispose(); }
    }

    private static (PermissionSnapshot Expected, uint Section, ImmutableArray<byte> Bytes) Project(PermissionSnapshot original,
        PermissionDelta change, bool directory = false, InheritanceParent? parent = null, bool ensureRule = false)
    {
        string authority = original.Subject.Authority.Id;
        if (change is AssignPermissionPrincipal { IsOwner: true } owner)
        {
            var sid = WindowsPermissionDescriptor.SidBytes(owner.Principal, authority);
            return (original with { Owner = original.Owner with { Status = PermissionReadStatus.Present, Value = owner.Principal } },
                OwnerInformation, ImmutableArray.Create(sid));
        }
        if (original.Access.Value?.Acl is not WindowsDacl baseline || original.Inheritance.Value is not { } control)
            throw new NotSupportedException("A readable Windows DACL and control are required.");
        if (directory && change is ChangeWindowsInheritance inheritance)
            return ProjectInheritance(original, baseline, control, inheritance, parent);
        var bytes = change switch
        {
            ChangeWindowsAce replacement => WindowsPermissionAclPatch.Replace(baseline, replacement, authority),
            AddWindowsAce addition => WindowsPermissionAclPatch.InsertCanonical(baseline, addition.Entry, authority, directory, ensureRule),
            _ => throw new NotSupportedException("This Windows route edits one explicit access entry or assigns an owner.")
        };
        var projected = WindowsPermissionDescriptor.ProjectDacl(control, bytes, authority);
        return (original with { Access = original.Access with { Value = new PermissionAclValue(projected) } }, DaclInformation, bytes);
    }

    private static PermissionRevision RequireRevision(PermissionSnapshot snapshot) => PermissionRevision.Read(snapshot, DiscretionarySections) ??
        throw new IOException("The security sections needed to verify the Windows change are unavailable.");

    private static bool SameChain(FileStamp[] first, FileStamp[] second) =>
        first.Select(stamp => stamp.Identity).SequenceEqual(second.Select(stamp => stamp.Identity));

    private static Exception ReadError(ReadFailure failure) => failure.Status switch
    {
        PermissionReadStatus.Stale or PermissionReadStatus.Gone => new PermissionBindingConflictException(failure.Message),
        PermissionReadStatus.Denied => new UnauthorizedAccessException(failure.Message),
        _ => new IOException(failure.Message)
    };

    private sealed class WindowsDiscretionaryMutation(PermissionSnapshot original, string root, string[] names, FileStamp[] inspectedChain,
        SafeFileHandle admitted, WindowsPermissionToken.Stamp token, PermissionDelta? reviewedChange, uint section, ImmutableArray<byte> bytes,
        PropagationBoundary? boundary, InheritanceParent? inheritanceParent, bool captured, bool descriptorWrite) : IBoundPermissionMutation
    {
        private FileStamp? _checked;

        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation)
        {
            _checked = null;
            if (WindowsPermissionToken.CurrentStamp() != token)
                throw new PermissionBindingConflictException("The ordinary Windows token changed before the write.");
            var snapshot = ObserveHeld(sections, cancellation, out var held);
            if (CheckNamespace(held, cancellation) != true)
                throw new PermissionBindingConflictException("The reviewed object or its original namespace could not be verified.");
            _checked = held;
            return snapshot;
        }

        public PermissionMutationReturn Apply(PermissionDelta change) => Apply(change, CancellationToken.None);

        public PermissionMutationReturn Apply(PermissionDelta change, CancellationToken cancellation)
        {
            bool recheckName = captured || descriptorWrite;
            var checkedStamp = _checked;
            _checked = null;
            if (reviewedChange is null || !ReferenceEquals(reviewedChange, change) || checkedStamp is null)
                throw new InvalidOperationException("Windows permission writes require the rechecked reviewed component.");
            try
            {
                if (boundary is not null)
                {
                    RequireUnchangedPropagationBoundary(original, boundary, cancellation);
                    if (inheritanceParent is not null) RequireUnchangedInheritanceParent(original, boundary, inheritanceParent, cancellation);
                }
                if (WindowsPermissionToken.CurrentStamp() != token || ReadStamp(cancellation) != checkedStamp ||
                    recheckName && CheckNamespace(checkedStamp.Value, cancellation) != true)
                    return new(RejectedBeforeChange: true, BindingConflict: boundary is not null || recheckName);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return new(CanceledBeforeChange: true); }
            catch (PermissionBindingConflictException error) when (boundary is not null || recheckName)
            {
                return new(RejectedBeforeChange: true, BindingConflict: true, Detail: error.Message);
            }
            catch (Exception error) when (error is ReadFailure or Win32Exception or IOException or UnauthorizedAccessException)
            {
                return new(RejectedBeforeChange: true, Detail: error.Message, FailedWithoutDispatch: boundary is not null || recheckName);
            }
            uint errorCode;
            fixed (byte* payload = bytes.AsSpan())
            {
                if (descriptorWrite)
                {
                    int status = NtSetSecurityObject(admitted, section, payload);
                    errorCode = status < 0 ? (uint)StatusToDosError(status) : 0;
                }
                else errorCode = SetSecurityInfo(admitted, 1 /* SE_FILE_OBJECT */, section,
                    section == OwnerInformation ? payload : null, null, (section & DaclInformation) != 0 ? payload : null, null);
            }
            return errorCode == 0 ? new() : new((int)errorCode,
                RejectedBeforeChange: boundary is null && (errorCode is 5 or 87 or 1307 or 1314 or 1336));
        }

        public PermissionReadBack Observe(PermissionSections sections)
        {
            string? unflushed = FlushRefusal(root, names, original.Subject.Authority, CancellationToken.None);
            var snapshot = ObserveHeld(sections, CancellationToken.None, out var held);
            var name = CheckNamespace(held, CancellationToken.None);
            if (unflushed is not null) return new(snapshot, PermissionVerification.Unavailable, name == false, unflushed);
            return new(snapshot, PermissionVerification.SameObject, name == false, name switch
            {
                false => "Security was read back on the original object. Its name or folder changed during the call.",
                null => "Security was read back on the held object. Its original name could not be rechecked.",
                _ => null
            });
        }

        private PermissionSnapshot ObserveHeld(PermissionSections sections, CancellationToken ct, out FileStamp held)
        {
            try
            {
                held = ReadStamp(ct);
                var binding = original.Binding! with { LinkCount = held.Links };
                var candidate = PermissionSnapshots.Create(original.Subject, original.Model, binding, sections, DateTimeOffset.UtcNow, Source);
                var snapshot = ReadHeldDescriptor(candidate, sections, admitted, ct);
                if (ReadStamp(ct) != held) throw new PermissionBindingConflictException("The held Windows object changed while reading security.");
                return snapshot;
            }
            catch (ReadFailure error) { throw ReadError(error); }
            catch (Win32Exception error) { throw new IOException("The held Windows object could not be verified.", error); }
        }

        private FileStamp ReadStamp(CancellationToken ct)
        {
            var expected = original.Binding!.Identity;
            var held = Screen(admitted, expected.MountInstance, expected.Kind == PermissionObjectKind.VolumeRoot, original.Subject.Authority, ct);
            if (held.Identity != expected)
                throw new PermissionBindingConflictException("The security handle did not identify the reviewed object.");
            return held;
        }

        private bool? CheckNamespace(FileStamp held, CancellationToken ct)
        {
            try
            {
                using var observed = Walk(root, names, original.Subject.Authority, ct,
                    out var parent, out var chain, out var binding);
                using (parent)
                    return SameChain(inspectedChain, chain) && binding == original.Binding &&
                        chain[^1] == held;
            }
            catch (ReadFailure failure) when (failure.Status is PermissionReadStatus.Gone or PermissionReadStatus.Stale) { return false; }
            catch (Exception error) when (error is ReadFailure or Win32Exception or IOException or UnauthorizedAccessException) { return null; }
        }

        public void Dispose() => admitted.Dispose();
    }

    // NTFS answers a read-back from its cache, and an ordinary token cannot flush an object it may only re-permission
    // (FlushFileBuffers needs write-data access; measured on Windows 11 26200). A handle to the containing folder with
    // add-file access can flush, as can the volume root for a root. An error then leaves the result unverified, as on Linux
    // after a failed syncfs; without that access the read-back stays the file system's view.
    // A test may fail the flush for objects in chosen folders with a Win32 error.
    internal static Func<string, int>? FlushFaultForTests;

    private static string? FlushRefusal(string root, string[] names, PermissionAuthority authority, CancellationToken ct)
    {
        string folderPath = names.Length <= 1 ? root : root + string.Join(Path.DirectorySeparatorChar, names[..^1]);
        SafeFileHandle folder;
        try
        {
            folder = Walk(root, names.Length == 0 ? names : names[..^1], authority, ct, out var parent, out _, out _, leafReadAccess: 0x2 /* FILE_ADD_FILE */);
            parent?.Dispose();
        }
        catch (Exception error) when (error is ReadFailure or Win32Exception or IOException or UnauthorizedAccessException) { return null; }
        using (folder)
        {
            int error = FlushFaultForTests?.Invoke(folderPath) ?? 0;
            if (error == 0 && FlushFileBuffers(folder)) return null;
            if (error == 0) error = Marshal.GetLastPInvokeError();
            return $"The file system could not write its pending changes to the device (error {error}). " +
                "The change may not persist; inspect the object again once its device is available.";
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushFileBuffers(SafeFileHandle handle);

    [LibraryImport("advapi32.dll")]
    private static partial uint SetSecurityInfo(SafeFileHandle handle, int objectType, uint flags, byte* owner, byte* group, byte* dacl, byte* sacl);
}
