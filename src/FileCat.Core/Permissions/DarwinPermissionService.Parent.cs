using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class DarwinPermissionService
{
    private const uint MountIgnoreOwnership = 0x00200000;
    private const ushort GroupOrOtherWrite = 0x12;
    private const uint AddFile = 1u << 2, AddDirectory = 1u << 5, DeleteChild = 1u << 6;
    private const uint WriteSecurity = 1u << 12, ChangeOwner = 1u << 13, GenericWriteOrAll = (1u << 23) | (1u << 21);
    private const uint EntryChangeRights = AddFile | AddDirectory | WriteSecurity | ChangeOwner;

    internal static bool HasOutsideNamespaceGrant(DarwinAcl acl, bool parent, Principal? requester, Principal? root)
    {
        uint relevant = EntryChangeRights | GenericWriteOrAll | (parent ? DeleteChild : 0);
        foreach (var rule in acl.Entries)
        {
            if ((rule.Flags & 0x100) != 0 || rule.Kind == DarwinAceKind.Deny) continue;
            if (rule.Kind != DarwinAceKind.Allow) return true;
            if ((rule.Rights & relevant) != 0 && rule.Principal != requester && rule.Principal != root) return true;
        }
        return false;
    }

    private static void AdmitParent(Mount mount, uint user, Entry folder,
        PermissionObservation<PermissionAclValue> access, string authority)
    {
        if (user == 0 || (mount.Flags & (MountReadOnly | MountIgnoreOwnership)) != 0 || (mount.Owner != 0 && mount.Owner != user))
            throw new NotSupportedException("This route requires a writable local volume that honors ownership and is mounted by this user or root.");
        if (folder.Kind != PermissionObjectKind.Directory || folder.FileSystem != mount.Id || (folder.Owner != user && folder.Owner != 0) ||
            (folder.Mode & GroupOrOtherWrite) != 0)
            throw new NotSupportedException("Another account could change this parent directory.");
        AdmitDirectoryAcl(access, parent: true, user, authority);
    }

    private static Entry ReadAdmittedParent(SafeFileHandle handle, FileIdentity identity, Mount mount, uint user, string authority)
    {
        var parent = ReadEntry(handle, ".");
        if (parent.Object != identity) throw new PermissionBindingConflictException("The held Darwin parent changed.");
        var access = ReadAcl(handle, ".", parent, authority, DateTimeOffset.UtcNow);
        AdmitParent(mount, user, parent, access, authority);
        return parent;
    }

    private static void AdmitEntry(PermissionSnapshot snapshot, Entry entry, uint user, ushort? expectedWritableDirectory, bool reviewedAcl = false)
    {
        if (entry.Kind != PermissionObjectKind.Directory) return;
        if ((entry.Mode & GroupOrOtherWrite) != 0 && entry.Mode != expectedWritableDirectory)
            throw new NotSupportedException("Another account could change this directory.");
        if (!reviewedAcl) AdmitDirectoryAcl(snapshot.Access, parent: false, user, snapshot.Subject.Authority.Id);
    }

    private static void AdmitDirectoryAcl(PermissionObservation<PermissionAclValue> access, bool parent, uint user, string authority)
    {
        if (access.Status == PermissionReadStatus.Absent) return;
        if (access.Value?.Acl is not DarwinAcl acl)
            throw new NotSupportedException("The directory's extended permissions could not be verified.");
        if (!HasOutsideNamespaceGrant(acl, parent, null, null)) return;
        var requester = DarwinPrincipalNames.UserUuid(user, authority);
        var root = DarwinPrincipalNames.UserUuid(0, authority);
        if (HasOutsideNamespaceGrant(acl, parent, requester, root))
            throw new NotSupportedException(parent ? "The parent ACL could allow another account to change its entries."
                : "The selected directory ACL could allow another account to change its namespace.");
    }

    private sealed class DirectoryWatch : IDisposable
    {
        private const short VnodeFilter = -4;
        private const ushort AddAndClear = 0x21, ErrorOrEof = 0xc000;
        private const uint Watched = 0x6b; // delete, write, attributes, rename, revoke
        private const uint Invalidated = 0x61; // delete, rename, revoke
        private readonly SafeFileHandle _queue;
        private readonly nuint _identifier;

        internal DirectoryWatch(SafeFileHandle parent, bool namespaceOnly = false)
        {
            int fd = Kqueue();
            if (fd < 0) throw WatchFailure();
            _queue = new(fd, ownsHandle: true);
            _identifier = (nuint)parent.DangerousGetHandle(); // The caller retains the directory for the lifetime of the watch.
            try
            {
                if (SetDescriptorFlags(_queue, 2 /* F_SETFD */, 0, 0, 0, 0, 0, 0, 1 /* FD_CLOEXEC */) != 0) throw WatchFailure();
                VnodeEvent change = new() { Identifier = _identifier, Filter = VnodeFilter, Flags = AddAndClear,
                    Note = namespaceOnly ? Watched & ~8u /* NOTE_ATTRIB */ : Watched };
                if (Kevent(_queue, &change, 1, null, 0, null) != 0) throw WatchFailure();
            }
            catch { _queue.Dispose(); throw; }
        }

        internal bool TakeChanges()
        {
            Timespec immediate = default;
            VnodeEvent current;
            int count = Kevent(_queue, null, 0, &current, 1, &immediate);
            if (count < 0) throw WatchFailure();
            if (count == 0) return false;
            if (current.Identifier != _identifier || current.Filter != VnodeFilter || (current.Flags & ErrorOrEof) != 0 ||
                (current.Note & Invalidated) != 0)
                throw new PermissionBindingConflictException("The original parent directory watch was invalidated.");
            return true;
        }

        public void Dispose() => _queue.Dispose();
        private static IOException WatchFailure() => new($"The Darwin directory watch failed (errno {Marshal.GetLastPInvokeError()}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VnodeEvent { public nuint Identifier; public short Filter; public ushort Flags; public uint Note; public nint Data, UserData; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec { public long Seconds, Nanoseconds; }

    [LibraryImport("libc", EntryPoint = "kqueue", SetLastError = true)]
    private static partial int Kqueue();
    // Darwin arm64 puts fcntl's variadic argument on the stack, after eight fixed register slots.
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int SetDescriptorFlags(SafeFileHandle handle, int command, long p2, long p3, long p4, long p5, long p6, long p7, long flags);
    [LibraryImport("libc", EntryPoint = "kevent", SetLastError = true)]
    private static partial int Kevent(SafeFileHandle queue, VnodeEvent* changes, int changeCount, VnodeEvent* events, int eventCount, Timespec* timeout);
}
