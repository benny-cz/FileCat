using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveDirectory
{
    /// <summary>
    /// Reopen one journal-recorded object in this creator-private stage directory for exact cleanup. The name must bind
    /// the recorded identity, a regular single-link object owned by the current user. No pathname outside this pinned
    /// directory is consulted, and the binding grants nothing beyond <see cref="UnixArchiveFile.DestroyOwned"/>.
    /// </summary>
    internal UnixArchiveFile OpenOwnedForCleanup(string leaf, string expectedIdentity, long maximumLength)
    {
        UnixArchiveNative.Leaf(leaf);
        if (privateStageParent is null)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Cleanup reopening is limited to a private stage directory.");
        if (maximumLength < 0 || maximumLength > ArchiveWritePolicy.MaxPhysicalBytes) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        lock (Gate)
        {
            RequireCapacity(); VerifyNamespace(); CheckCreationNamespace();
            var held = UnixArchiveNative.Child(directory, leaf, write: true);
            try
            {
                var facts = UnixFiles.Stat(held) ?? throw UnixArchiveNative.Unavailable();
                if (!facts.IsRegular || facts.Identity.ToString() != expectedIdentity || facts.Owner != user)
                    throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The recorded transaction name binds a different object; retain it.");
                if (facts.Size < 0 || facts.Size > maximumLength)
                    throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The recorded transaction object exceeds its recorded size; retain it.");
                VerifyNamespace(); RequireName(leaf, facts.Identity);
                var file = new UnixArchiveFile(this, held, leaf, facts.Identity, maximumLength, artifact: false, writable: false); children++;
                file.MarkCleanupOwned(); return file;
            }
            catch { held.Dispose(); throw; }
        }
    }

    /// <summary>
    /// Remove this private stage directory from its reviewed public parent once it is empty. rmdir refuses a non-empty
    /// directory, so any foreign or retained entry keeps the directory and is reported for review.
    /// </summary>
    internal void RemoveEmptyPrivateStageDirectory(CancellationToken ct)
    {
        var publicParent = privateStageParent ??
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Only a private stage directory can be removed.");
        lock (publicParent.Gate)
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
            if (children != 0) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Close every object in the private directory first.");
            RequirePrivateStageOf(publicParent);
            string leaf = Path.GetFileName(DisplayPath);
            int flags = UnixArchiveNative.Mac ? 0x80 /* AT_REMOVEDIR */ : 0x200 /* AT_REMOVEDIR */;
            if (RemoveAt(UnixArchiveNative.Fd(publicParent.Handle), leaf, flags) != 0)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                    "FileCat's private transaction folder could not be removed (errno " + Marshal.GetLastPInvokeError() + "); retain it for review: " + DisplayPath);
            removedDirectory = true;
            UnixArchiveNative.SyncDirectory(publicParent.Handle);
        }
    }

    /// <summary>Recovery: reopen this folder's recorded private stage folder by exact identity, never by prefix alone.</summary>
    internal UnixArchiveDirectory OpenPrivateStageDirectory(string leaf, string expectedIdentity)
    {
        UnixArchiveNative.Leaf(leaf);
        if (!leaf.StartsWith(ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix, StringComparison.Ordinal) || privateStageParent is not null)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Only a recorded private stage folder of a reviewed archive folder can be reopened.");
        lock (Gate)
        {
            RequireCapacity(); VerifyNamespace();
            var pinned = UnixArchiveNative.ChildDirectory(directory, leaf);
            try
            {
                if (UnixFiles.Stat(pinned)?.Identity.ToString() != expectedIdentity)
                    throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The recorded private stage folder was replaced; retain it.");
                UnixArchiveNative.RequireEditDirectory(pinned, user);
                var child = new UnixArchiveDirectory(Path.Combine(DisplayPath, leaf), pinned, user, this);
                if (child.Volume != Volume) { child.Dispose(); throw UnixArchiveNative.Unavailable(); }
                return child;
            }
            catch { pinned.Dispose(); throw; }
        }
    }

    /// <summary>The identity a name currently binds in this folder, or null when the name is absent.</summary>
    internal FileIdentity? ObserveName(string leaf)
    {
        UnixArchiveNative.Leaf(leaf);
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); VerifyNamespace();
            int fd = OpenAtNoFollow(UnixArchiveNative.Fd(directory), leaf, UnixArchiveNative.SafeOpen, 0);
            if (fd < 0)
            {
                if (Marshal.GetLastPInvokeError() == 2 /* ENOENT */) return null;
                throw UnixArchiveNative.Unavailable();
            }
            using var held = new Microsoft.Win32.SafeHandles.SafeFileHandle(fd, ownsHandle: true);
            return UnixFiles.Stat(held)?.Identity ?? throw UnixArchiveNative.Unavailable();
        }
    }

    private bool removedDirectory;
    internal bool IsRemovedDirectory { get { lock (Gate) return removedDirectory; } }

    [LibraryImport("libc", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RemoveAt(int directory, string leaf, int flags);
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAtNoFollow(int directory, string leaf, int flags, uint mode);
}
