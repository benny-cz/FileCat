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

    internal static (ReviewedPermissionOperation Review, PermissionSnapshot Expected) Prepare(PermissionInspection inspection, ItemRef source,
        PermissionDraft draft, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var original = inspection.Snapshot;
        if (EditRefusal(original) is { } refusal) throw new NotSupportedException(refusal);
        if (original.Subject.RawLocator != source.FileSystemPath || draft.Scope != PermissionScope.Objects || draft.Changes.Length != 1)
            throw new NotSupportedException("Review one owner or DACL change for the original Windows file.");
        var binding = original.Binding!;
        var before = RequireRevision(original);
        var change = draft.Changes[0];
        var projection = Project(original, change);
        var expected = RequireRevision(projection.Expected);
        WindowsPermissionToken.Stamp token;
        if (change is AssignPermissionPrincipal owner)
        {
            var assignable = WindowsPermissionToken.OwnerChoices(original.Subject.Authority.Id);
            if (!assignable.Choices.Contains(owner.Principal))
                throw new UnauthorizedAccessException("Select an owner SID assignable by FileCat's ordinary token.");
            token = assignable.Stamp;
        }
        else token = WindowsPermissionToken.CurrentStamp();

        using var pin = inspection.TransferLease<WindowsInspectionPin>() ??
            throw new InvalidOperationException("This Windows permission binding has already been released.");
        WindowsFileMutation? mutation = null;
        try
        {
            using var current = Walk(pin.Root, pin.Names, original.Subject.Authority, ct, out var parent, out var chain, out var now);
            using (parent)
            {
                if (parent is null || now != binding || !SameChain(pin.Chain, chain))
                    throw new PermissionBindingConflictException("The original object, mount or folder changed.");
                var retained = Screen(pin.File, binding.Identity.MountInstance, root: false, original.Subject.Authority, ct);
                if (retained != chain[^1])
                    throw new PermissionBindingConflictException("The original path no longer identifies the held file.");
                uint access = projection.Section == OwnerInformation ? WriteOwner : WriteDac;
                var admitted = Open(parent, pin.Names[^1], security: true, ct, access);
                mutation = new WindowsFileMutation(original, pin.Root, pin.Names, pin.Chain, admitted, token, change, projection.Section, projection.Bytes);
                var actual = mutation.CheckCurrent(DiscretionarySections, ct);
                if (actual.Binding != binding || RequireRevision(actual) != before)
                    throw new PermissionBindingConflictException("Windows security changed after inspection.");
            }
            var component = new ReviewedPermissionComponent(change, before, expected,
                projection.Section == OwnerInformation ? "Windows SetSecurityInfo owner" : "Windows SetSecurityInfo DACL");
            var operation = new ReviewedPermissionOperation(PermissionScope.Objects,
                [new(source, original.Subject, binding, [component], expected, mutation)]);
            mutation = null;
            return (operation, projection.Expected);
        }
        catch (ReadFailure error) { throw ReadError(error); }
        catch (Win32Exception error) { throw new IOException("The reviewed Windows file could not be verified.", error); }
        finally { mutation?.Dispose(); }
    }

    private static (PermissionSnapshot Expected, uint Section, ImmutableArray<byte> Bytes) Project(PermissionSnapshot original,
        PermissionDelta change)
    {
        string authority = original.Subject.Authority.Id;
        if (change is AssignPermissionPrincipal { IsOwner: true } owner)
        {
            var sid = WindowsPermissionDescriptor.SidBytes(owner.Principal, authority);
            return (original with { Owner = original.Owner with { Status = PermissionReadStatus.Present, Value = owner.Principal } },
                OwnerInformation, ImmutableArray.Create(sid));
        }
        if (original.Access.Value?.Acl is not WindowsDacl baseline || original.Inheritance.Value is not { } control)
            throw new NotSupportedException("A readable file DACL and control are required.");
        var bytes = change switch
        {
            ChangeWindowsAce replacement => WindowsPermissionAclPatch.Replace(baseline, replacement, authority),
            AddWindowsAce addition => WindowsPermissionAclPatch.InsertCanonical(baseline, addition.Entry, authority),
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

    private sealed class WindowsFileMutation(PermissionSnapshot original, string root, string[] names, FileStamp[] inspectedChain, SafeFileHandle admitted,
        WindowsPermissionToken.Stamp token, PermissionDelta reviewedChange, uint section, ImmutableArray<byte> bytes) : IBoundPermissionMutation
    {
        private FileStamp? _checked;

        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation)
        {
            _checked = null;
            if (WindowsPermissionToken.CurrentStamp() != token)
                throw new PermissionBindingConflictException("The ordinary Windows token changed before the write.");
            var snapshot = ObserveHeld(sections, cancellation, out var held);
            if (CheckNamespace(held, cancellation) != true)
                throw new PermissionBindingConflictException("The reviewed file or its original namespace could not be verified.");
            _checked = held;
            return snapshot;
        }

        public PermissionMutationReturn Apply(PermissionDelta change)
        {
            var checkedStamp = _checked;
            _checked = null;
            if (!ReferenceEquals(reviewedChange, change) || checkedStamp is null)
                throw new InvalidOperationException("Windows permission writes require the rechecked reviewed component.");
            try
            {
                if (WindowsPermissionToken.CurrentStamp() != token || ReadStamp(CancellationToken.None) != checkedStamp)
                    return new(RejectedBeforeChange: true);
            }
            catch (Exception error) when (error is ReadFailure or Win32Exception or IOException or UnauthorizedAccessException)
            {
                return new(RejectedBeforeChange: true);
            }
            uint errorCode;
            fixed (byte* payload = bytes.AsSpan())
                errorCode = SetSecurityInfo(admitted, 1 /* SE_FILE_OBJECT */, section,
                    section == OwnerInformation ? payload : null, null, section == DaclInformation ? payload : null, null);
            return errorCode == 0 ? new() : new((int)errorCode, RejectedBeforeChange: errorCode is 5 or 87 or 1307 or 1314 or 1336);
        }

        public PermissionReadBack Observe(PermissionSections sections)
        {
            var snapshot = ObserveHeld(sections, CancellationToken.None, out var held);
            var name = CheckNamespace(held, CancellationToken.None);
            return new(snapshot, PermissionVerification.SameObject, name == false, name switch
            {
                false => "Security was read back on the original file. Its name or folder changed during the call.",
                null => "Security was read back on the held file. Its original name could not be rechecked.",
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
                if (ReadStamp(ct) != held) throw new PermissionBindingConflictException("The held Windows file changed while reading security.");
                return snapshot;
            }
            catch (ReadFailure error) { throw ReadError(error); }
            catch (Win32Exception error) { throw new IOException("The held Windows file could not be verified.", error); }
        }

        private FileStamp ReadStamp(CancellationToken ct)
        {
            var expected = original.Binding!.Identity;
            var held = Screen(admitted, expected.MountInstance, root: false, original.Subject.Authority, ct);
            if (held.Identity != expected)
                throw new PermissionBindingConflictException("The security handle did not identify the reviewed file.");
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

    [LibraryImport("advapi32.dll")]
    private static partial uint SetSecurityInfo(SafeFileHandle handle, int objectType, uint flags, byte* owner, byte* group, byte* dacl, byte* sacl);
}
