using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed record DarwinAccessReport(PermissionIdentity Identity, DateTimeOffset CheckedAt, uint RealUid, uint RealGid,
    uint EffectiveUid, uint EffectiveGid, ImmutableArray<uint> Groups, bool ReadOnly, bool NoExecute, bool NoSetId,
    bool HonorsOwnership, uint FileFlags, ImmutableArray<PermissionAccessCheck> Checks);

internal sealed unsafe partial class DarwinPermissionService
{
    private const uint MountNoExecute = 4, MountNoSetId = 8;
    private const int AtEffectiveAccess = 0x10;
    private const int CheckRead = 4, CheckWrite = 2, CheckExecute = 1, CheckDelete = 1 << 12;

    internal bool CanCheckAccess(PermissionSnapshot snapshot) => qualifyAccessChecks &&
        snapshot.Subject.LinkPolicy == PermissionLinkPolicy.NoFollow &&
        snapshot.Binding is { Identity.Kind: PermissionObjectKind.File or PermissionObjectKind.Directory } &&
        snapshot.Flags.Value is { } flags && (flags.Bits & MacMaterializationPolicy.DatalessFlag) == 0;

    internal DarwinAccessReport CheckEffectiveAccess(PermissionSubject subject, CancellationToken cancellation)
    {
        using var inspection = Inspect(subject, PermissionSections.Flags, cancellation);
        var snapshot = inspection.Snapshot;
        if (snapshot.Flags.Status is PermissionReadStatus.Stale or PermissionReadStatus.Gone)
            throw new PermissionBindingConflictException(snapshot.BindingProblem ?? "The original access subject changed.");
        if (!CanCheckAccess(snapshot))
            throw new NotSupportedException(snapshot.BindingProblem ?? "The current-process access check is unavailable for this entry.");
        using var pin = inspection.TransferLease<InspectionPin>() ?? throw new IOException("The original Darwin access binding is unavailable.");
        var identity = snapshot.Binding!.Identity;
        try
        {
            return MacMaterializationPolicy.Run(cancellation, CheckBound, (_, reason) => throw new NotSupportedException(reason));
        }
        catch (ReadFailure error) when (error.Status is PermissionReadStatus.Gone or PermissionReadStatus.Stale)
        { throw new PermissionBindingConflictException(error.Message); }
        catch (ReadFailure error) { throw new IOException(error.Message, error); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        { throw new NotSupportedException("The Darwin effective-access call is unavailable.", error); }

        DarwinAccessReport CheckBound()
        {
            using var watch = new DirectoryWatch(pin.Handle);
            var parent = ReadEntry(pin.Handle, ".");
            if ((parent.Flags & MacMaterializationPolicy.DatalessFlag) != 0)
                throw new NotSupportedException(MacMaterializationPolicy.RequiredReason);
            CheckBinding(parent);
            var credentials = ReadAccessIds();
            var groups = ReadGroupIds().Distinct().Order().ToImmutableArray();
            bool directory = pin.Entry.Kind == PermissionObjectKind.Directory;
            var checkedAt = DateTimeOffset.UtcNow;
            var checks = ImmutableArray.CreateBuilder<PermissionAccessCheck>(directory ? 5 : 4);
            checks.Add(CheckRight(directory ? "List names" : "Read content", CheckRead));
            checks.Add(CheckRight(directory ? "Add file permission" : "Write content", CheckWrite));
            checks.Add(CheckRight(directory ? "Search known names" : "Execute permission", CheckExecute));
            if (directory) checks.Add(CheckRight("Add file and search together", CheckWrite | CheckExecute));
            checks.Add(CheckRight("Delete this entry from its parent", CheckDelete));

            if (credentials != ReadAccessIds() || !ReadGroupIds().Distinct().Order().SequenceEqual(groups))
                throw new PermissionBindingConflictException("The macOS access credential changed during the check.");
            CheckBinding(parent);
            if (watch.TakeChanges()) throw new PermissionBindingConflictException("The original parent changed during the access check.");
            uint flags = pin.Mount.Flags;
            return new(identity, checkedAt, credentials.RealUid, credentials.RealGid, credentials.EffectiveUid, credentials.EffectiveGid,
                groups, (flags & MountReadOnly) != 0, (flags & MountNoExecute) != 0, (flags & MountNoSetId) != 0,
                (flags & MountIgnoreOwnership) == 0, pin.Entry.Flags, checks.MoveToImmutable());
        }

        void CheckBinding(Entry parent)
        {
            cancellation.ThrowIfCancellationRequested();
            using var current = OpenDirectory(pin.Directory);
            if (parent.Object != pin.Identity || ReadMount(pin.Handle) != pin.ParentMount || ReadMount(current) != pin.ParentMount ||
                ReadEntry(current, ".") != parent || ReadEntry(pin.Handle, pin.Leaf) != pin.Entry || ReadEntry(current, pin.Leaf) != pin.Entry)
                throw new PermissionBindingConflictException("The original entry, parent or mount changed during the access check.");
        }

        PermissionAccessCheck CheckRight(string right, int mode)
        {
            cancellation.ThrowIfCancellationRequested();
            if (AccessAt(pin.Handle, pin.Leaf, mode, AtEffectiveAccess | AtNoFollowAny) == 0)
                return new(right, PermissionReadStatus.Present, null);
            int errno = Marshal.GetLastPInvokeError();
            if (errno is 2 or 20 or 62) throw new PermissionBindingConflictException("The original entry changed during the access check.");
            if (errno == MacMaterializationPolicy.Required) throw new IOException(MacMaterializationPolicy.RequiredReason, errno);
            return new(right, errno switch
            {
                1 or 13 or 30 => PermissionReadStatus.Denied,
                22 or 45 or 78 or 102 => PermissionReadStatus.Unsupported,
                _ => PermissionReadStatus.Failed
            }, errno);
        }
    }

    private static (uint RealUid, uint RealGid, uint EffectiveUid, uint EffectiveGid) ReadAccessIds() =>
        (RealUser(), RealGroup(), EffectiveUser(), EffectiveGroup());

    [LibraryImport("libc", EntryPoint = "faccessat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int AccessAt(SafeFileHandle parent, string name, int mode, int flags);
}
