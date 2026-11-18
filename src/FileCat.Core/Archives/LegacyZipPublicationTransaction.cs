using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>
/// Compatibility boundary for the existing ZIP publication/rollback/cleanup protocol. This deliberately retains its
/// pathname-based semantics, metadata behavior and journal ordering. It is not a qualified publication adapter for new
/// profiles; those require reviewed object-bound operations, typed provenance and durable archive transaction facts.
/// </summary>
internal sealed class LegacyZipPublicationTransaction
{
    private const int BufferSize = 1024 * 1024;
    private readonly Job Job;
    private readonly IFileSystemOperations Fs;
    private readonly JobJournal Journal;
    private readonly ArchivePlan plan;
    private readonly string archive;
    private readonly string folder;
    private readonly bool updating;
    private readonly string temp;
    private readonly int step;
    private byte[]? openedSourceDigest;
    private byte[]? intendedPublishedDigest;
    private string? createdStageIdentity;
    private string? intendedPublishedIdentity;
    private string? replacedOriginalIdentity;
    private byte[]? replacedOriginalDigest;
    private string? replacedOriginalMark;
    private bool originalMarkReadable;
    private bool published;
    private bool stageCreated;
    private string? backup;
    private bool prepared;
    private bool attempted;
    private string? mark;
    private string? requiredOrigin;
    private readonly ArchiveMoveSources? move;

    public bool Published => published;
    internal string PreparedSha256 => Convert.ToHexString(intendedPublishedDigest ?? throw new InvalidOperationException("No prepared archive digest."));
    internal string? RequiredOrigin => requiredOrigin;

    public LegacyZipPublicationTransaction(Job job, IFileSystemOperations fs, JobJournal journal, ArchivePlan plan,
        string archive, string folder, ArchiveMoveSources? move = null)
    {
        if (plan.Profile is not null)
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable,
                "A new archive profile cannot use the legacy ZIP publication protocol.");
        Job = job; Fs = fs; Journal = journal; this.plan = plan; this.archive = archive; this.folder = folder;
        this.move = move;
        updating = plan.Baseline is not null;
        temp = Path.Combine(folder, ".filecat-zip-" + Guid.NewGuid().ToString("N") + ".tmp");
        step = Journal.Intent("zip-update", archive, null, temp);
    }

    public FileStream CreateStage()
    {
        var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize);
        stageCreated = true;
        try
        {
            createdStageIdentity = Fs.GetOpenedFileIdentity(output.SafeFileHandle);
            RequireClearStage(output.SafeFileHandle);
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    public void RequireClearStage(SafeFileHandle stage)
    {
        if (updating) ArchiveOuterEfs.RequireClear(stage, ArchiveOuterEfs.UpdateStageUnavailable);
    }

    public Stream OpenStageRead()
    {
        if (!updating)
            return new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        var read = new ArchiveMutationReadStream(temp, ArchiveOuterEfs.UpdateStageUnavailable);
        try
        {
            RequireCreatedStage(Fs.GetOpenedFileIdentity(read.Handle));
            return read;
        }
        catch { read.Dispose(); throw; }
    }

    private void RequireCreatedStage(string? identity)
    {
        if (createdStageIdentity is not null && !string.Equals(identity, createdStageIdentity, StringComparison.Ordinal))
            throw new IOException("Another file now has the rebuilt ZIP's temporary name; the original archive was kept.");
    }

    private void RequireClearPath(string path, string refusal)
    {
        if (OperatingSystem.IsWindows()) ArchiveOuterEfs.RequireClear(File.GetAttributes(path), refusal);
    }

    public void CaptureOpenedSource(ArchiveMutationReadStream source)
    {
        if (!updating || openedSourceDigest is not null || prepared) throw new InvalidOperationException("Invalid ZIP source capture phase.");
        openedSourceDigest = DigestOpened(source);
        if (!string.Equals(Convert.ToHexString(openedSourceDigest), plan.Baseline!.Sha256Hex, StringComparison.Ordinal))
            throw new IOException("The archive changed after it was opened, so this change was not applied. Open it again and repeat the change.");
    }

    public void CaptureVerifiedStage(Stream stage)
    {
        if (updating || move is not null)
        {
            intendedPublishedDigest = DigestOpened(stage);
            intendedPublishedIdentity = createdStageIdentity ?? Fs.GetFileIdentity(temp);
        }
    }

    public void Prepare()
    {
        if (prepared || updating && openedSourceDigest is null) throw new InvalidOperationException("Invalid ZIP publication preparation phase.");
        if (updating && !plan.Baseline!.MatchesForMutation(archive, Job.Checkpoint))
            throw new IOException("The archive changed while it was being rebuilt; the original was kept.");
        mark = updating ? Fs.ReadOriginMark(archive) : null;
        if (updating)
        {
            RequireCreatedStage(Fs.GetFileIdentity(temp));
            RequireClearPath(temp, ArchiveOuterEfs.UpdateStageUnavailable);
        }
        requiredOrigin = move?.RequiredOrigin(mark) ?? mark;
        if (requiredOrigin is not null &&
            (!Fs.WriteOriginMark(temp, requiredOrigin) || !string.Equals(Fs.ReadOriginMark(temp), requiredOrigin, StringComparison.Ordinal)))
            throw new IOException("The rebuilt archive could not keep its downloaded-from-the-internet mark; the original was kept.");
        prepared = true;
    }

    public void Publish(Action onCommitted)
    {
        if (!prepared || attempted) throw new InvalidOperationException("Invalid ZIP publication phase.");
        attempted = true;
        if (updating)
        {
            if (OperatingSystem.IsWindows())
            {
                using var current = OpenStageRead();
                if (!DigestOpened(current).AsSpan().SequenceEqual(intendedPublishedDigest))
                    throw new IOException("The rebuilt ZIP changed after verification; the original archive was kept.");
            }
            RequireCreatedStage(Fs.GetFileIdentity(temp));
            RequireClearPath(temp, ArchiveOuterEfs.UpdateStageUnavailable);
            RequireClearPath(archive, ArchiveOuterEfs.UpdateUnavailable);
            // ReplaceFile keeps the original's security, attributes, and creation time on the rebuilt file.
            // Keep the actual original even when it was unmarked at the earlier read. Another
            // process can add a download mark without changing ZIP bytes or last-write time.
            backup = Path.Combine(folder, ".filecat-zip-" + Guid.NewGuid().ToString("N") + ".bak");
            File.Replace(temp, archive, backup, ignoreMetadataErrors: true);
        }
        else File.Move(temp, archive, overwrite: false);
        published = true;
        if (updating)
        {
            Exception? integrityError = null;
            string? publishedIdentity = null;
            bool encryptedBackup = false;
            try
            {
                publishedIdentity = Fs.GetFileIdentity(archive);
                // Unix rename and Windows ReplaceFile both publish the replacement's identity.
                if (intendedPublishedIdentity is not null &&
                    !string.Equals(publishedIdentity, intendedPublishedIdentity, StringComparison.Ordinal))
                    throw new IOException("The published archive pathname changed immediately after replacement.");
                RequireClearPath(archive, ArchiveOuterEfs.UpdateOutputUnavailable);
                // The backup is the pathname's actual file at replacement time. It may differ
                // from the opened source even when a late check of path metadata passed.
                replacedOriginalIdentity = Fs.GetFileIdentity(backup!);
                replacedOriginalDigest = DigestClearFile(backup!, ArchiveOuterEfs.UpdateUnavailable);
                replacedOriginalMark = Fs.ReadOriginMark(backup!);
                originalMarkReadable = true;
                encryptedBackup = OperatingSystem.IsWindows() && File.GetAttributes(backup!).HasFlag(FileAttributes.Encrypted);
                if (encryptedBackup)
                    integrityError = new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, ArchiveOuterEfs.UpdateUnavailable);
                else if (!openedSourceDigest!.AsSpan().SequenceEqual(replacedOriginalDigest))
                    integrityError = new IOException("The archive source changed during the update.");
                else if (!string.Equals(replacedOriginalMark, mark, StringComparison.Ordinal))
                    integrityError = new IOException("The original archive's download mark changed during the update.");
                else if (requiredOrigin is not null && !string.Equals(Fs.ReadOriginMark(archive), requiredOrigin, StringComparison.Ordinal))
                    integrityError = new IOException("The replacement archive lost its downloaded-from-the-internet mark.");
                RequireClearPath(archive, ArchiveOuterEfs.UpdateOutputUnavailable);
                if (OperatingSystem.IsWindows() && integrityError is null)
                {
                    using var current = new ArchiveMutationReadStream(archive, ArchiveOuterEfs.UpdateOutputUnavailable);
                    if (intendedPublishedIdentity is not null && Fs.GetOpenedFileIdentity(current.Handle) != intendedPublishedIdentity)
                        throw new IOException("The published archive pathname changed before the output was verified.");
                    if (!DigestOpened(current, checkCancellation: false).AsSpan().SequenceEqual(intendedPublishedDigest))
                        integrityError = new IOException("The published ZIP differs from the verified rebuild.");
                }
            }
            catch (Exception ex) { integrityError = ex; }
            if (integrityError is not null)
            {
                Exception? restoreError = null;
                try
                {
                    if (integrityError is ArchiveWriteException { Message: ArchiveOuterEfs.UpdateOutputUnavailable })
                        throw new IOException("Automatic restoration would remove a ZIP observed with EFS protection.");
                    // A different inode can carry identical ZIP bytes and a different origin
                    // mark. Never roll back over a pathname that is no longer our publication.
                    if (publishedIdentity is not null &&
                        !string.Equals(Fs.GetFileIdentity(archive), publishedIdentity, StringComparison.Ordinal))
                        throw new IOException("The published archive pathname changed before restoration.");
                    if (intendedPublishedIdentity is not null &&
                        !string.Equals(publishedIdentity, intendedPublishedIdentity, StringComparison.Ordinal))
                        throw new IOException("The published archive pathname changed before restoration.");
                    RequireClearPath(archive, ArchiveOuterEfs.UpdateOutputUnavailable);
                    // A newly added or changed Internet mark belongs to a concurrent change,
                    // even when bytes and inode are unchanged. If a mark disappeared, rolling
                    // back to the marked backup is still the safe result.
                    if (Fs.ReadOriginMark(archive) is { } currentMark &&
                        !string.Equals(currentMark, requiredOrigin, StringComparison.Ordinal))
                        throw new IOException("The published archive's download mark changed before restoration.");
                    if (!DigestClearFile(archive, ArchiveOuterEfs.UpdateOutputUnavailable).AsSpan().SequenceEqual(intendedPublishedDigest))
                        throw new IOException("The published archive pathname changed before restoration.");
                    RequireClearPath(archive, ArchiveOuterEfs.UpdateOutputUnavailable);
                    if (replacedOriginalDigest is null || !originalMarkReadable)
                        throw new IOException("The original archive backup could not be verified before restoration.");
                    if (replacedOriginalIdentity is not null &&
                        !string.Equals(Fs.GetFileIdentity(backup!), replacedOriginalIdentity, StringComparison.Ordinal))
                        throw new IOException("The original archive backup pathname changed before restoration.");
                    // Restore the whole file that occupied the pathname at replacement time,
                    // including its bytes and attached metadata.
                    File.Move(backup!, archive, overwrite: true);
                    backup = null;
                    if (!DigestRestoredFile(archive, encryptedBackup).AsSpan().SequenceEqual(replacedOriginalDigest) ||
                        encryptedBackup && !File.GetAttributes(archive).HasFlag(FileAttributes.Encrypted) ||
                        !string.Equals(Fs.ReadOriginMark(archive), replacedOriginalMark, StringComparison.Ordinal))
                        throw new IOException("The archive's bytes or protection could not be verified after restoration.");
                }
                catch (Exception ex) { restoreError = ex; }
                if (restoreError is not null)
                {
                    string guidance = backup is null
                        ? "Restoration could not be verified; inspect the archive before using it."
                        : $"Restoration would overwrite changed archive bytes or could not be verified. The backup remains at {backup}; inspect both files before using them.";
                    guidance = integrityError.Message + " " + guidance;
                    Journal.Done(step, StepOutcome.Uncertain, guidance);
                    Issue(IssueSeverity.Error, archive, guidance, StepOutcome.Uncertain, "security");
                    throw new IOException("The archive changed and restoration could not be verified.",
                        new AggregateException(integrityError, restoreError));
                }
                published = false;
                if (encryptedBackup)
                    throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable,
                        "The original archive became EFS-encrypted during the update. FileCat restored the encrypted original.", integrityError);
                throw new IOException("The archive source or download mark changed; the original was restored.", integrityError);
            }
        }
        Journal.Done(step, StepOutcome.Committed);
        onCommitted();
        if (backup is not null)
        {
            string retainedBackup = backup;
            try
            {
                // This path is visible to other processes. A later replacement or in-place
                // change is not our original backup and must not be deleted as cleanup.
                if (replacedOriginalIdentity is not null &&
                    !string.Equals(Fs.GetFileIdentity(backup), replacedOriginalIdentity, StringComparison.Ordinal))
                    throw new IOException("The original archive backup pathname changed before cleanup.");
                if (replacedOriginalDigest is null ||
                    !DigestClearFile(backup, ArchiveOuterEfs.UpdateUnavailable).AsSpan().SequenceEqual(replacedOriginalDigest) ||
                    !originalMarkReadable ||
                    !string.Equals(Fs.ReadOriginMark(backup), replacedOriginalMark, StringComparison.Ordinal))
                    throw new IOException("The original archive backup bytes or download mark changed before cleanup.");
                if (OperatingSystem.IsWindows())
                    ArchiveOuterEfs.RequireClear(File.GetAttributes(backup), ArchiveOuterEfs.UpdateUnavailable);
                // A concurrent rename can happen after that last mark read. Move the
                // pathname to a fresh same-folder name, then verify the file actually
                // moved before deleting it. If the move captured someone else's file,
                // keep that file at the new name and report exactly where it remains.
                string cleanupPath = backup + ".held-" + Guid.NewGuid().ToString("N") + ".bak";
                File.Move(backup, cleanupPath);
                retainedBackup = cleanupPath;
                if (replacedOriginalIdentity is not null &&
                    !string.Equals(Fs.GetFileIdentity(cleanupPath), replacedOriginalIdentity, StringComparison.Ordinal))
                    throw new IOException("The original archive backup pathname changed during cleanup.");
                if (!DigestClearFile(cleanupPath, ArchiveOuterEfs.UpdateUnavailable).AsSpan().SequenceEqual(replacedOriginalDigest) ||
                    !string.Equals(Fs.ReadOriginMark(cleanupPath), replacedOriginalMark, StringComparison.Ordinal))
                    throw new IOException("The original archive backup bytes or download mark changed during cleanup.");
                if (OperatingSystem.IsWindows())
                    ArchiveOuterEfs.RequireClear(File.GetAttributes(cleanupPath), ArchiveOuterEfs.UpdateUnavailable);
                File.Delete(cleanupPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Issue(IssueSeverity.Warning, retainedBackup, "The original archive backup was kept at " +
                    retainedBackup + ": " + ex.Message,
                    StepOutcome.Committed);
            }
        }
    }

    public void RecordFailure(Exception error) => Journal.Done(step, StepOutcome.Failed, error.Message);

    public void CleanupUnpublishedStage()
    {
        if (published || !stageCreated || !File.Exists(temp)) return;
        try
        {
            RequireCreatedStage(Fs.GetFileIdentity(temp));
            if (updating)
            {
                RequireClearPath(temp, ArchiveOuterEfs.UpdateStageUnavailable);
                if (createdStageIdentity is not null && Fs.DeleteClearOwnedFile(temp, createdStageIdentity) is { } bound)
                {
                    if (!bound.Deleted) throw new IOException(bound.Reason ?? "The temporary file could not be safely removed.");
                    return;
                }
            }
            File.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Issue(IssueSeverity.Warning, temp, "The temporary ZIP was kept at " + temp + ": " + ex.Message, StepOutcome.Failed);
        }
    }

    private void Issue(IssueSeverity severity, string path, string message, StepOutcome outcome, string? cause = null) =>
        Job.AddIssue(new JobIssue(severity, path, message, outcome) { Cause = cause });

    private byte[] DigestClearFile(string path, string efsRefusal)
    {
        using var stream = new ArchiveMutationReadStream(path, efsRefusal);
        return DigestOpened(stream, checkCancellation: false);
    }

    private byte[] DigestRestoredFile(string path, bool encryptedBackup)
    {
        if (!encryptedBackup) return DigestClearFile(path, ArchiveOuterEfs.UpdateUnavailable);
        // This known EFS object went back to its original name with its protection intact. Compare
        // the previously captured digest in memory, without creating a clear copy.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        return DigestOpened(stream, checkCancellation: false);
    }

    private byte[] DigestOpened(Stream stream, bool checkCancellation = true)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        try
        {
            stream.Position = 0;
            while (true)
            {
                if (checkCancellation) Job.Checkpoint();
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
            }
            return hash.GetHashAndReset();
        }
        finally { stream.Position = 0; }
    }
}
