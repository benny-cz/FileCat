using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

internal static unsafe partial class UnixArchiveNative
{
    internal static SafeFileHandle CreateEditDirectory(SafeFileHandle parent, string leaf, uint user, CancellationToken ct)
    {
        Leaf(leaf); ct.ThrowIfCancellationRequested();
        if (EditMkdirAt(Fd(parent), leaf, 0x1c0) != 0) throw Unavailable();
        // This new directory is empty: no working bytes are written until its inherited ACL is removed and reviewed.
        var child = ChildDirectory(parent, leaf);
        try
        {
            if (Mac)
            {
                nint acl = AclInit(0);
                if (acl == 0) throw Unavailable();
                try
                {
                    // https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/acl_set_fd_np.3.html
                    if (EditSetAcl(Fd(child), acl, 0x100 /* ACL_TYPE_EXTENDED */) != 0) throw Unavailable();
                }
                finally { AclFree(acl); }
            }
            if (EditChmod(Fd(child), 0x1c0) != 0) throw Unavailable();
            RequireEditDirectory(child, user);
            EnumerateSelection(child, _ => throw Unavailable(), ct);
            ct.ThrowIfCancellationRequested(); return child;
        }
        catch { child.Dispose(); throw; } // An uncertain created namespace is retained, never path-deleted.
    }
    internal static void RequireEditDirectory(SafeFileHandle directory, uint user)
    {
        if (UnixFiles.Stat(directory) is not { IsDirectory: true, Owner: var owner, Mode: 0x1c0 } || owner != user)
            throw Unavailable();
        RequireCreationDirectory(directory, user); // Includes native extended-ACL review on Darwin.
    }
    internal static SafeFileHandle CreateEditFile(SafeFileHandle parent, string leaf)
    {
        Leaf(leaf);
        // The held parent was reviewed as private with no inherited Darwin ACL entries. No path-taking open is used.
        return Handle(OpenAt(Fd(parent), leaf, SafeOpen | 2 | (Mac ? 0x200 | 0x800 : 0x40 | 0x80), 0x180));
    }
    [LibraryImport("libc", EntryPoint="mkdirat", StringMarshalling=StringMarshalling.Utf8, SetLastError=true)]
    private static partial int EditMkdirAt(int parent, string leaf, uint mode);
    [LibraryImport("libc", EntryPoint="fchmod", SetLastError=true)]
    private static partial int EditChmod(int fd, uint mode);
    [LibraryImport("libc", EntryPoint="acl_set_fd_np", SetLastError=true)]
    private static partial int EditSetAcl(int fd, nint acl, int type);
}
