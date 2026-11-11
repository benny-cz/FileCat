using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Edit;

/// <summary>
/// Reviewed discard in FileCat's creator-private native edit namespace on Unix (the archive transactions' private cleanup
/// decision, UNIX_PRIVATE_CLEANUP_DECISION.md, applied to edit sessions). Only leases opened for a reviewed discard remove
/// anything, and nothing is removed by path:
/// <list type="bullet">
/// <item>A file's name is removed only while it still binds the held, reviewed object, and afterwards that object must
/// have no link left. Its content is not truncated first: an editor may still have it mapped, and truncation would fault
/// that editor.</item>
/// <item>On Linux a write lease first proves that no other program has the file open or mapped, the refusal Windows' file
/// sharing gives an open editor. macOS has no such check: an editor that still holds the working copy keeps its own
/// unlinked copy until it closes it.</item>
/// <item>A folder is removed only once empty and while its name still binds the reviewed folder.</item>
/// </list>
/// Anything that does not match is kept and reported.
/// </summary>
internal sealed partial class UnixEditDirectoryLease
{
    private bool discard;

    internal override bool CanDiscardReviewedSession => isPrivate;
    internal override bool CanRemoveOwnedDirectories => isPrivate && discard;

    internal override EditDirectoryLease OpenDiscardDirectory(string name, string? expected, CancellationToken ct)
    {
        lock (Gate)
        {
            RequireOpen(); Leaf(name); ReviewCore(ct);
            if (!isPrivate) throw new UnauthorizedAccessException("Discard requires a private native edit namespace.");
            var result = new UnixEditDirectoryLease(System.IO.Path.Combine(Path, name), UnixArchiveNative.ChildDirectory(directory, name), user, true, this, name)
            { discard = true };
            try
            {
                if (expected is not null && result.Identity != expected) throw new IOException("The reviewed edit folder was replaced. Its files are kept.");
                ReviewCore(ct); return result;
            }
            catch { result.Dispose(); throw; }
        }
    }

    internal override FileStream OpenDiscardFile(string name, string? expected, CancellationToken ct)
    {
        lock (Gate)
        {
            RequireOpen(); Leaf(name); ReviewCore(ct);
            if (!isPrivate || !discard) throw new UnauthorizedAccessException("Discard requires a reviewed private native edit namespace.");
            var file = UnixArchiveNative.Child(directory, name);
            try
            {
                if (UnixFiles.Stat(file) is not { IsRegular: true } facts || facts.Owner != user)
                    throw new IOException("The reviewed edit file is not an ordinary file of this user. Its files are kept.");
                if (expected is not null && facts.Identity.ToString() != expected) throw new IOException("The reviewed edit file was replaced. Its files are kept.");
                if (ArchiveUnixOuterMetadata.Capture(file).Native.LinkCount != 1) throw new IOException("The reviewed edit file has another link. Its files are kept.");
                ReviewCore(ct); return new FileStream(file, FileAccess.Read);
            }
            catch { file.Dispose(); throw; }
        }
    }

    internal override void ReviewRemoval(FileStream file, string name, ArchiveFileObservation expected, CancellationToken ct)
    {
        lock (Gate)
        {
            if (!ObserveSnapshot(file, name, ct, expected.Version.Length).Matches(expected))
                throw new IOException("The reviewed edit file changed. Its files are kept.");
            // Reviewed files that another program holds are refused before anything is removed, as Windows' sharing refuses
            // them during review; the removal takes the lease again right before each name goes.
            if (OperatingSystem.IsLinux()) { Lease(UnixArchiveNative.Fd(file.SafeFileHandle)); _ = Fcntl(UnixArchiveNative.Fd(file.SafeFileHandle), 1024, 2 /* F_UNLCK */); }
        }
    }

    // A Linux write lease is granted only while no other descriptor or mapping holds the file (no other reader or writer).
    private static void Lease(int fd)
    {
        if (Fcntl(fd, 1024 /* F_SETLEASE */, 1 /* F_WRLCK */) == 0) return;
        int errno = Marshal.GetLastPInvokeError();
        throw new IOException(errno == 11 /* EAGAIN */
            ? "The working copy is open in another program. Close it, then discard again. Its files are kept."
            : "FileCat could not check whether another program has the working copy open (errno " +
              errno.ToString(CultureInfo.InvariantCulture) + "). Its files are kept.");
    }

    // The discard removal of one reviewed file (DeleteSnapshot in a lease opened for discard).
    private void RemoveReviewedFile(FileStream file, string name, ArchiveFileObservation expected, CancellationToken ct)
    {
        RequireOpen(); Leaf(name); ReviewCore(ct);
        var held = file.SafeFileHandle; int fd = UnixArchiveNative.Fd(held); bool leased = false;
        try
        {
            if (OperatingSystem.IsLinux()) { Lease(fd); leased = true; }
            if (UnixFiles.Stat(held) is not { IsRegular: true } facts || facts.Identity.ToString() != expected.Version.Identity ||
                UnixFiles.StatChild(directory, name)?.Identity != facts.Identity || ArchiveUnixOuterMetadata.Capture(held).Native.LinkCount != 1)
                throw new IOException("The reviewed edit file was replaced. Its files are kept.");
            ct.ThrowIfCancellationRequested();
            if (UnlinkAt(UnixArchiveNative.Fd(directory), name, 0) != 0)
                throw new IOException("The reviewed edit file could not be removed (errno " +
                    Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture) + "). Its files are kept.");
            ulong links = ArchiveUnixOuterMetadata.Capture(held).Native.LinkCount;
            UnixArchiveNative.SyncDirectory(directory);
            if (links != 0)
                throw new IOException("Another file took the reviewed edit file's name during discard and was removed; the reviewed copy is kept under another name.");
        }
        finally { if (leased) _ = Fcntl(fd, 1024, 2 /* F_UNLCK */); }
    }

    // The discard removal of this reviewed, now empty folder (RemoveEmpty in a lease opened for discard).
    private void RemoveReviewedFolder(CancellationToken ct)
    {
        RequireNoChildren(); ReviewCore(ct);
        if (parent is null || leaf is null) throw new IOException("The edit store's own folder is never removed.");
        if (Names(ct, 8).Count != 0) throw new IOException("The private edit folder is not empty. Its files are kept.");
        parent.ReviewRetained(ct);
        if (UnixFiles.StatChild(parent.Handle, leaf)?.Identity.ToString() != Identity) throw new IOException("The reviewed edit folder was replaced. Its files are kept.");
        ct.ThrowIfCancellationRequested();
        if (UnlinkAt(UnixArchiveNative.Fd(parent.Handle), leaf, OperatingSystem.IsMacOS() ? 0x80 : 0x200 /* AT_REMOVEDIR */) != 0)
            throw new IOException("The reviewed edit folder could not be removed (errno " +
                Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture) + "). Its files are kept.");
        if (UnixFiles.StatChild(parent.Handle, leaf)?.Identity.ToString() == Identity) throw new IOException("The reviewed edit folder is still present. Its files are kept.");
        UnixArchiveNative.SyncDirectory(parent.Handle);
    }

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command, int argument);
    [LibraryImport("libc", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int UnlinkAt(int directory, string leaf, int flags);
}
