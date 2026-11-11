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
/// The same rules remove a commit capture (the private snapshot FileCat created for one commit, CreateDirectory with
/// removable) once its commit is done. Anything that does not match is kept and reported.
/// </summary>
internal sealed partial class UnixEditDirectoryLease
{
    private bool discard, capture;

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
            // them during review; the removal takes the lease again and reviews the content under it.
            if (UnixExactRemoval.TakeLease(file.SafeFileHandle, What(name)) is { } refused) throw Kept(refused);
            UnixExactRemoval.ReleaseLease(file.SafeFileHandle);
        }
    }

    // What the user is told about: the working copy of their edit, or FileCat's own temporary commit copy.
    private string What(string name) => capture ? "FileCat's temporary commit copy" : "“" + name + "”";
    private IOException Kept(string why) => new(char.ToUpperInvariant(why[0]) + why[1..] + "." +
        (capture ? " It is kept for cleanup review at " + Path : " Its files are kept."));

    // The removal of one reviewed file (DeleteSnapshot in a lease opened for discard or created removable). The Linux lease
    // is taken first and the complete content is reviewed under it, so no save can land unseen before the name goes.
    private void RemoveReviewedFile(FileStream file, string name, ArchiveFileObservation expected, CancellationToken ct)
    {
        RequireOpen(); Leaf(name); ReviewCore(ct);
        var held = file.SafeFileHandle; bool leased = false;
        try
        {
            if (UnixExactRemoval.TakeLease(held, What(name)) is { } refused) throw Kept(refused);
            leased = true;
            if (!ObserveSnapshot(file, name, ct, expected.Version.Length).Matches(expected))
                throw new IOException("The reviewed edit file changed. Its files are kept.");
            if (UnixFiles.Stat(held) is not { IsRegular: true } facts || facts.Identity.ToString() != expected.Version.Identity ||
                ArchiveUnixOuterMetadata.Capture(held).Native.LinkCount != 1)
                throw new IOException("The reviewed edit file was replaced. Its files are kept.");
            if (UnixExactRemoval.Unlink(directory, name, held, expected.Version.Identity, What(name), ct) is { } kept) throw Kept(kept);
        }
        finally { if (leased) UnixExactRemoval.ReleaseLease(held); }
    }

    // The discard removal of this reviewed, now empty folder (RemoveEmpty in a lease opened for discard).
    private void RemoveReviewedFolder(CancellationToken ct)
    {
        RequireNoChildren(); ReviewCore(ct);
        if (parent is null || leaf is null) throw new IOException("The edit store's own folder is never removed.");
        if (Names(ct, 8).Count != 0) throw new IOException("The private edit folder is not empty. Its files are kept.");
        parent.ReviewRetained(ct);
        ct.ThrowIfCancellationRequested();
        if (UnixExactRemoval.RemoveFolder(parent.Handle, leaf, Identity, "the reviewed edit folder") is { } kept) throw Kept(kept);
        // As on Windows, the removed folder's lease ends here, so its parent no longer counts it as a live child and can be
        // removed next (a commit capture removes "files", then itself).
        Dispose();
    }
}
