using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

internal interface ILegacyZipBackupRestorer
{
    bool TryRestoreLegacyZipBackup(string backup, string archive, string originalIdentity);
    LegacyZipRollbackResult RollbackLegacyZip(string archive, string backup, string stage,
        LegacyZipRollbackFile published, LegacyZipRollbackFile original);
}

internal sealed record LegacyZipRollbackFile(ArchiveObjectVersion Version, string? Origin, bool Encrypted);
internal sealed record LegacyZipRollbackResult(bool Restored, bool RebuildRetained, string? Reason = null);

/// <summary>
/// Legacy ZIP publication and metadata handling, with recovery recorded before replacement.
/// Windows rollback uses the qualified held-file adapter. New archive profiles use their own publication transactions.
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
    private readonly Action<string, string, string> replaceFile;
    private byte[]? openedSourceDigest;
    private string? openedSourceIdentity;
    private byte[]? intendedPublishedDigest;
    private long intendedPublishedLength;
    private string? createdStageIdentity;
    private string? intendedPublishedIdentity;
    private string? replacedOriginalIdentity;
    private byte[]? replacedOriginalDigest;
    private long replacedOriginalLength;
    private string? replacedOriginalMark;
    private bool originalMarkReadable;
    private bool published;
    private bool stageCreated;
    private bool recoveryRequired;
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
        string archive, string folder, ArchiveMoveSources? move = null, Action<string, string, string>? replaceFile = null)
    {
        if (plan.Profile is not null)
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable,
                "A new archive profile cannot use the legacy ZIP publication protocol.");
        Job = job; Fs = fs; Journal = journal; this.plan = plan; this.archive = archive; this.folder = folder;
        this.move = move;
        this.replaceFile = replaceFile ?? ((stage, target, retained) => File.Replace(stage, target, retained, ignoreMetadataErrors: true));
        updating = plan.Baseline is not null;
        temp = Path.Combine(folder, ".filecat-zip-" + Guid.NewGuid().ToString("N") + ".tmp");
        step = Journal.Intent(JobJournal.LegacyZipUpdateOp, archive, null, temp);
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
        openedSourceIdentity = Fs.GetOpenedFileIdentity(source.Handle);
        openedSourceDigest = DigestOpened(source);
        if (!string.Equals(Convert.ToHexString(openedSourceDigest), plan.Baseline!.Sha256Hex, StringComparison.Ordinal))
            throw new IOException("The archive changed after it was opened, so this change was not applied. Open it again and repeat the change.");
    }

    public void CaptureVerifiedStage(Stream stage)
    {
        if (updating || move is not null)
        {
            intendedPublishedLength = stage.Length;
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
            backup = Path.Combine(folder, ".filecat-zip-" + Guid.NewGuid().ToString("N") + ".bak");
            Journal.ReplacementRecovery(step, backup);
            RequireCreatedStage(Fs.GetFileIdentity(temp));
            RequireClearPath(temp, ArchiveOuterEfs.UpdateStageUnavailable);
            RequireClearPath(archive, ArchiveOuterEfs.UpdateUnavailable);
            // ReplaceFile keeps the original's security, attributes, and creation time on the rebuilt file.
            // Keep the actual original even when it was unmarked at the earlier read. Another
            // process can add a download mark without changing ZIP bytes or last-write time.
            ReplaceOriginal(backup);
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
                using (var original = new ArchiveMutationReadStream(backup!, ArchiveOuterEfs.UpdateUnavailable))
                {
                    if (OperatingSystem.IsWindows() && replacedOriginalIdentity is not null &&
                        Fs.GetOpenedFileIdentity(original.Handle) != replacedOriginalIdentity)
                        throw new IOException("The retained ZIP's name changed before its bytes were verified.");
                    replacedOriginalLength = original.Length;
                    replacedOriginalDigest = DigestOpened(original, checkCancellation: false);
                }
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
                bool rebuildRetained = false;
                try
                {
                    if (integrityError is ArchiveWriteException { Message: ArchiveOuterEfs.UpdateOutputUnavailable })
                        throw new IOException("Automatic restoration would remove a ZIP observed with EFS protection.");
                    if (OperatingSystem.IsWindows())
                        rebuildRetained = RollbackWindowsZip(publishedIdentity, encryptedBackup);
                    else
                        RollbackPortableZip(publishedIdentity);
                    backup = null;
                }
                catch (Exception ex) { restoreError = ex; }
                if (restoreError is not null)
                {
                    string guidance = integrityError.Message + " " + restoreError.Message +
                        $" Inspect the archive at {archive}, the possible backup at {backup}, and the temporary ZIP at {temp}.";
                    RetainForRecovery(archive, guidance);
                    throw new IOException("The archive changed and restoration could not be verified.",
                        new AggregateException(integrityError, restoreError));
                }
                if (rebuildRetained)
                    RetainForRecovery(temp, $"FileCat restored the original archive. The rebuilt ZIP was kept for review at {temp}.");
                published = false;
                if (encryptedBackup)
                    throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable,
                        "The original archive became EFS-encrypted during the update. FileCat restored the encrypted original.", integrityError);
                throw new IOException("The archive source or download mark changed; the original was restored.", integrityError);
            }
        }
        Journal.Done(step, StepOutcome.Committed);
        onCommitted();
        if (backup is not null) CleanupOriginalBackup(backup);
    }

    private bool RollbackWindowsZip(string? publishedIdentity, bool encryptedBackup)
    {
        if (Fs is not ILegacyZipBackupRestorer native || intendedPublishedIdentity is null || publishedIdentity != intendedPublishedIdentity ||
            openedSourceIdentity is null || replacedOriginalIdentity != openedSourceIdentity ||
            replacedOriginalDigest is null || !originalMarkReadable)
            throw new IOException("The exact Windows ZIP publication and original backup could not both be verified.");
        var original = new LegacyZipRollbackFile(new(openedSourceIdentity, replacedOriginalLength,
            Convert.ToHexString(replacedOriginalDigest)), replacedOriginalMark, encryptedBackup);
        var rebuild = new LegacyZipRollbackFile(new(intendedPublishedIdentity, intendedPublishedLength, PreparedSha256),
            requiredOrigin, Encrypted: false);
        var result = native.RollbackLegacyZip(archive, backup!, temp, rebuild, original);
        if (!result.Restored)
            throw new IOException(result.Reason ?? "Windows could not return the reviewed original ZIP to its name.");
        return result.RebuildRetained;
    }

    private void RollbackPortableZip(string? publishedIdentity)
    {
        // A different inode can carry identical ZIP bytes and a different origin mark.
        if (publishedIdentity is not null && !string.Equals(Fs.GetFileIdentity(archive), publishedIdentity, StringComparison.Ordinal) ||
            intendedPublishedIdentity is not null && !string.Equals(publishedIdentity, intendedPublishedIdentity, StringComparison.Ordinal))
            throw new IOException("The published archive pathname changed before restoration.");
        // A newly added or changed mark belongs to a concurrent change. Restoring a missing mark from the backup is allowed.
        if (Fs.ReadOriginMark(archive) is { } currentMark && !string.Equals(currentMark, requiredOrigin, StringComparison.Ordinal))
            throw new IOException("The published archive's download mark changed before restoration.");
        if (!DigestClearFile(archive, ArchiveOuterEfs.UpdateOutputUnavailable).AsSpan().SequenceEqual(intendedPublishedDigest))
            throw new IOException("The published archive pathname changed before restoration.");
        if (replacedOriginalDigest is null || !originalMarkReadable)
            throw new IOException("The original archive backup could not be verified before restoration.");
        if (replacedOriginalIdentity is not null && !string.Equals(Fs.GetFileIdentity(backup!), replacedOriginalIdentity, StringComparison.Ordinal))
            throw new IOException("The original archive backup pathname changed before restoration.");
        File.Move(backup!, archive, overwrite: true);
        if (!DigestClearFile(archive, ArchiveOuterEfs.UpdateUnavailable).AsSpan().SequenceEqual(replacedOriginalDigest) ||
            !string.Equals(Fs.ReadOriginMark(archive), replacedOriginalMark, StringComparison.Ordinal))
            throw new IOException("The archive's bytes or protection could not be verified after restoration.");
    }

    private void CleanupOriginalBackup(string backup)
    {
        string retainedBackup = backup;
        try
        {
            if (replacedOriginalIdentity is not null && !string.Equals(Fs.GetFileIdentity(backup), replacedOriginalIdentity, StringComparison.Ordinal))
                throw new IOException("The original archive backup pathname changed before cleanup.");
            if (replacedOriginalDigest is null ||
                !DigestClearFile(backup, ArchiveOuterEfs.UpdateUnavailable).AsSpan().SequenceEqual(replacedOriginalDigest) ||
                !originalMarkReadable || !string.Equals(Fs.ReadOriginMark(backup), replacedOriginalMark, StringComparison.Ordinal))
                throw new IOException("The original archive backup bytes or download mark changed before cleanup.");
            RequireClearPath(backup, ArchiveOuterEfs.UpdateUnavailable);
            // Recheck after moving to a fresh name. A concurrent replacement must be kept, wherever the move put it.
            string cleanupPath = backup + ".held-" + Guid.NewGuid().ToString("N") + ".bak";
            File.Move(backup, cleanupPath);
            retainedBackup = cleanupPath;
            if (replacedOriginalIdentity is not null && !string.Equals(Fs.GetFileIdentity(cleanupPath), replacedOriginalIdentity, StringComparison.Ordinal))
                throw new IOException("The original archive backup pathname changed during cleanup.");
            if (!DigestClearFile(cleanupPath, ArchiveOuterEfs.UpdateUnavailable).AsSpan().SequenceEqual(replacedOriginalDigest) ||
                !string.Equals(Fs.ReadOriginMark(cleanupPath), replacedOriginalMark, StringComparison.Ordinal))
                throw new IOException("The original archive backup bytes or download mark changed during cleanup.");
            RequireClearPath(cleanupPath, ArchiveOuterEfs.UpdateUnavailable);
            File.Delete(cleanupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Issue(IssueSeverity.Warning, retainedBackup, "The original archive backup was kept at " + retainedBackup + ": " + ex.Message,
                StepOutcome.Committed);
        }
    }

    private void ReplaceOriginal(string recoveryPath)
    {
        try { replaceFile(temp, archive, recoveryPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (!ReplacementMayBePartial(error, recoveryPath)) throw;
            bool restored = openedSourceIdentity is { } identity && Fs is ILegacyZipBackupRestorer native &&
                native.TryRestoreLegacyZipBackup(recoveryPath, archive, identity);
            string guidance = restored
                ? $"The ZIP replacement failed. FileCat returned the original file to {archive}. The rebuilt ZIP remains for review at {temp}."
                : $"The ZIP replacement failed after it may have moved the original. Inspect {archive}, the possible original backup at {recoveryPath}, and the rebuilt ZIP at {temp}.";
            RetainForRecovery(archive, guidance);
            throw new IOException(guidance, error);
        }
    }

    private bool ReplacementMayBePartial(Exception error, string recoveryPath)
    {
        const int unableToMoveReplacement = unchecked((int)0x80070499);
        if (error.HResult == unableToMoveReplacement) return true;
        try { File.GetAttributes(recoveryPath); return true; }
        catch (FileNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
        try
        {
            const FileAttributes unexpected = FileAttributes.Encrypted | FileAttributes.Directory | FileAttributes.ReparsePoint;
            return (File.GetAttributes(archive) & unexpected) != 0 ||
                openedSourceIdentity is not null && Fs.GetFileIdentity(archive) != openedSourceIdentity;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    private void RetainForRecovery(string path, string guidance)
    {
        recoveryRequired = true;
        Journal.RequireRecovery();
        // The open intent retains the exact three names after restart. A terminal step would hide the backup from review.
        Issue(IssueSeverity.Error, path, guidance, StepOutcome.Uncertain, "security");
    }

    public void RecordFailure(Exception error)
    {
        if (recoveryRequired) return;
        if (error is ArchiveWriteException { Message: ArchiveOuterEfs.UpdateStageUnavailable })
            RetainForRecovery(temp, error.Message + " The temporary ZIP was kept for review at " + temp + ".");
        else
            Journal.Done(step, StepOutcome.Failed, error.Message);
    }

    public void CleanupUnpublishedStage()
    {
        if (published || recoveryRequired || !stageCreated || !File.Exists(temp)) return;
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
