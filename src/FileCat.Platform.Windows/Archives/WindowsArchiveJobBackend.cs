using System.ComponentModel;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Unregistered qualification candidate using the actual confined application worker and NTFS recipes.</summary>
internal static partial class WindowsArchiveJobBackend
{
    internal const string Elevated = "Archive writing needs FileCat running without administrator rights.";
    internal const string LinkedFolder = "Archive writing is not available here: this folder is reached through a link, a junction " +
        "or a mount point, and FileCat writes archives only in folders reached without one. Open the folder where the link leads " +
        "(for example the drive a profile folder was moved to) to change archives there.";
    internal const string CloudFolder = "Archive writing is not available in a folder a cloud service syncs (such as OneDrive, " +
        "iCloud or Dropbox).";
    internal const string LinkedArchive = "This archive is a link or a cloud file (such as OneDrive), which FileCat does not change. " +
        "Copy it into a local folder to change it.";
    internal const string NoTransactions = "This drive does not support the NTFS transactions FileCat needs to replace an archive " +
        "safely, so its existing archives are read-only here.";
    internal const string NotWritableDrive = "Archive writing is not available on this drive: FileCat writes archives only on " +
        "writable, local NTFS drives.";
    internal const string NotOpenable = "Archive writing is not available in this folder: FileCat cannot hold it the way it holds a " +
        "folder it writes in (it is not on a local drive, or access is denied).";
    internal const string ArchiveNotWritable = "This archive cannot be changed here: it is read-only, or its permissions do not let you " +
        "write it.";
    private const uint SupportsTransactions = 0x0020_0000; // FILE_SUPPORTS_TRANSACTIONS

    /// <summary>
    /// Why this writer cannot write <paramref name="archivePath"/> at all, from its own checks, or null when it can: an
    /// unelevated process, a folder held without following links on a local, writable NTFS volume, and an archive that is
    /// a regular, non-reparse file with one link, within the size FileCat changes. Cheap (a few handle opens), shares
    /// everything and holds nothing; the job checks all of it again.
    /// </summary>
    internal static string? TargetProblem(string archivePath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (WindowsTokenAuthority.IsHighIntegrity) return Elevated;
        if (!Path.IsPathFullyQualified(archivePath) || Path.GetDirectoryName(archivePath) is not { Length: > 0 } folder ||
            Path.GetFileName(archivePath) is not { Length: > 0 } leaf) return LinkedFolder;
        if (!Directory.Exists(folder)) return null; // Nothing to judge yet: the job says what is missing.
        if (FolderProblem(folder) is { } problem) return problem;
        try
        {
            using var directory = WindowsArchiveDirectory.Open(folder);
            uint? links = directory.ProbeExisting(leaf);
            if (links is null) return null; // A new archive.
            if (links > 1) return ArchiveWriteMessages.HardLinkedText(archivePath);
            // Every update is published in an NTFS transaction; some volumes do not support one.
            if ((directory.Volume.Flags & SupportsTransactions) == 0) return NoTransactions;
            // Attributes only: the job itself tries to open the original for writing before anything is rebuilt.
            return (File.GetAttributes(archivePath) & FileAttributes.ReadOnly) != 0 ? ArchiveNotWritable : null;
        }
        catch (ArchiveWriteException error) when (error.Reason == ArchiveWriteReason.BudgetExceeded) { return ArchiveListingRevision.TooLarge; }
        catch (ArchiveWriteException error) when (error.Reason == ArchiveWriteReason.PublicationUnavailable) { return error.Message; }
        catch (IOException error) when (error.Message == WindowsArchiveNative.NotRegular) { return LinkedArchive; }
        catch (NotSupportedException) { return NotWritableDrive; }
        // The way to the folder holds no link or cloud folder (checked above): what is left is a folder it cannot hold.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            System.ComponentModel.Win32Exception) { return NotOpenable; }
    }

    /// <summary>
    /// What is on the way to <paramref name="folder"/>: a folder a cloud service syncs (OneDrive, iCloud, Dropbox: a sync root
    /// of the Windows cloud files API, which the folder pins accept as an ordinary folder but where FileCat's writing is not
    /// qualified), or a link, a junction or a mount point (the job pins every folder without following one). Windows hides a
    /// cloud folder's reparse point from ordinary processes, so the sync root is asked for by name; links show by their
    /// attributes. Nothing is opened, recalled or synced.
    /// </summary>
    internal static string? FolderProblem(string folder)
    {
        if (InCloudSyncRoot(folder)) return CloudFolder;
        for (string? current = folder; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            // A cloud folder's reparse point is hidden from this process (above); what shows is a link, a junction, a mount
            // point or another kind of reparse point (a WSL link, a projected folder), all refused like links.
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return LinkedFolder; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // The pins below decide.
        }
        return null;
    }

    private static unsafe bool InCloudSyncRoot(string folder)
    {
        try
        {
            long rootId; uint returned;
            // CF_SYNC_ROOT_INFO_BASIC: succeeds only for a path inside a registered sync root. The \\?\ form keeps a long
            // path from failing (and passing as not synced) where long paths are not enabled.
            string path = folder.StartsWith(@"\\?\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal)
                ? folder : @"\\?\" + folder;
            return CfGetSyncRootInfoByPath(path, 0, &rootId, sizeof(long), out returned) == 0;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    [System.Runtime.InteropServices.LibraryImport("cldapi.dll", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static unsafe partial int CfGetSyncRootInfoByPath(string path, int infoClass, void* buffer, uint length, out uint returned);

    internal static IArchiveJobBackend Create(ArchiveWriteFormat format, string executable, bool retainArtifacts = false)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("An absolute application worker is required.", nameof(executable));
        return new ArchiveNativeJobBackend<WindowsArchiveFile>(format,
            _ => !OperatingSystem.IsWindows() ? ArchiveJobAvailability.Unqualified
                : ArchiveAppContainer.RuntimeAccessProblem(executable) is { } problem ? new(false, problem) : ArchiveJobAvailability.Ready,
            (plan, ct) => new Scope(plan, ct, retainArtifacts),
            new((nonce, cpu) => ArchiveAppContainer.StartAssessment(executable, nonce, cpu, 2), protocolVersion: 2),
            new((nonce, cpu) => ArchiveAppContainer.StartTransform(executable, nonce, cpu, 2), protocolVersion: 2),
            new((nonce, cpu) => ArchiveAppContainer.StartVerification(executable, nonce, cpu, 2), protocolVersion: 2), supportsSourceRemoval: true,
            openReview: (path, ct) => new ReadLease(path, ct));
    }

    private sealed class ReadLease : IArchiveTargetReadLease
    {
        private readonly WindowsArchiveDirectory directory;
        private readonly WindowsArchiveFile file;
        public IArchiveRetainedFile File => file;
        internal ReadLease(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            directory = WindowsArchiveDirectory.Open(Path.GetDirectoryName(path) ?? throw new ArgumentException("A target directory is required."));
            try
            {
                file = directory.OpenSource(Path.GetFileName(path));
                try { file.RequireClearUpdateSource(); }
                catch { file.Dispose(); throw; }
            }
            catch { directory.Dispose(); throw; }
        }
        public void Dispose() { try { file.Dispose(); } finally { directory.Dispose(); } }
    }

    private sealed class Scope : IArchiveJobNativeScope<WindowsArchiveFile>
    {
        private readonly WindowsArchiveDirectory directory;
        private readonly WindowsArchiveFile? original;
        private ArchiveContentUpdateResult<WindowsArchiveFile>? update;
        private WindowsArchiveFile? publicationTarget;
        private ArchiveFileObservation? publishedObservation;
        private string? publicationPinFailure;
        private bool ownsPublicationTarget;
        private readonly bool retainArtifacts;
        public ArchiveRetainedBinding? Original { get; }
        internal Scope(ArchivePlan plan, CancellationToken ct, bool retainArtifacts)
        {
            this.retainArtifacts = retainArtifacts;
            ct.ThrowIfCancellationRequested();
            directory = WindowsArchiveDirectory.Open(Path.GetDirectoryName(plan.ArchivePath) ?? throw new ArgumentException("A target directory is required."));
            try
            {
                if (plan.Baseline is not null)
                {
                    string leaf = Path.GetFileName(plan.ArchivePath);
                    directory.ProbeExisting(leaf);
                    // Refused before anything is rebuilt: the update would end at the transaction it cannot begin, or at the
                    // write open of an original it may not write.
                    if ((directory.Volume.Flags & SupportsTransactions) == 0)
                        throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, NoTransactions);
                    if (!directory.MayWrite(leaf))
                        throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, ArchiveNotWritable);
                    original = directory.OpenSource(leaf);
                    original.RequireClearUpdateSource();
                    Original = new(original, ((IArchiveRetainedFile)original).Observe(ct));
                }
            }
            catch { try { original?.Dispose(); } finally { directory.Dispose(); } throw; }
        }
        public IArchiveSelectionTree OpenInputTree(string path) => WindowsArchiveSelectionTree.Open(path);
        public ArchiveArtifactReservation Reserve(Guid nonce, ArchiveArtifactKind kind, long maximum)
            => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum);
        public WindowsArchiveFile Create(ArchiveArtifactReservation reservation) => directory.CreateArtifact(reservation);
        public ArchiveJobPublication Publish(JobJournal journal, ArchivePreparedContainer<WindowsArchiveFile> prepared,
            IReadOnlyDictionary<int, ArchiveRetainedBinding> inputs, CancellationToken ct)
        {
            if (original is null)
            {
                var created = ArchiveNewFilePublication.PublishAsync(journal, prepared, inputs, directory.CreateArtifact, ct, cleanup: !retainArtifacts).GetAwaiter().GetResult();
                publicationTarget = prepared.Stage; publishedObservation = created.Published;
                return new(created.Published, created.Facts.UnresolvedArtifacts) { CleanupNeedsReview = !created.Cleanup.IsResolved };
            }
            update = WindowsArchiveContentUpdate.Publish(journal, prepared, original, inputs, TimeSpan.FromMinutes(2), ct);
            publishedObservation = update.Published;
            // BeginContentTransaction consumes the original lease. The transaction keeps its committed
            // deny-write/delete pin; acquire a fresh reader under that pin for the complete move lifetime.
            try
            {
                publicationTarget = directory.OpenSource(update.Published.LeafName);
                ownsPublicationTarget = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or Win32Exception)
            { publicationPinFailure = error.Message; } // Publication committed; keep originals if an extra reader is unavailable.
            return new(update.Published, Array.AsReadOnly(update.CleanupFacts.Artifacts.Select(artifact => artifact.Path).ToArray()));
        }
        public void ReviewPublication(ArchiveJobPublication published, bool verifyBytes, CancellationToken ct)
        {
            if (publicationTarget is null || !ReferenceEquals(published.Published, publishedObservation))
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The published archive no longer has its live native move reader. " + publicationPinFailure);
            var file = (IArchiveRetainedFile)publicationTarget;
            if (verifyBytes) file.Review(published.Published, ct);
            else { using var view = file.OpenReadView(published.Published, ct); }
        }
        public ArchiveJobPublication CleanupPublication(JobJournal journal, ArchiveJobPublication published, CancellationToken ct)
        {
            if (retainArtifacts || update is null || ct.IsCancellationRequested) return published;
            try { update.Cleanup(journal, ct); }
            catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException or NotSupportedException or Win32Exception) { }
            return new(published.Published, Array.AsReadOnly(update.CleanupFacts.Artifacts.Skip(update.CleanupFacts.DeletedCount).Select(artifact => artifact.Path).ToArray()))
                { CleanupNeedsReview = !update.CleanupFacts.IsResolved };
        }
        public (bool Kept, string? Location, string? Reason) DiscardUnpublished(JobJournal journal, CancellationToken ct)
        {
            var preparation = journal.DiscardableArchiveTransaction();
            if (preparation is null) return (journal.NeedsRecovery, null, null);
            if (retainArtifacts || update is not null) return (true, preparation.StagePath, null);
            try
            {
                using var discard = new ArchiveTransactionDiscard(WindowsArchiveDiscard.Live(journal, preparation, directory));
                discard.Execute(ct);
                return (journal.NeedsRecovery, null, null);
            }
            catch (Exception error) when (error is ArchiveWriteException or IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or ObjectDisposedException or Win32Exception)
            { return (true, preparation.StagePath, error.Message); }
        }
        public void Dispose()
        {
            try { if (ownsPublicationTarget) publicationTarget?.Dispose(); }
            finally { DisposeUpdate(); }
        }
        private void DisposeUpdate()
        {
            try { update?.Dispose(); }
            finally { try { original?.Dispose(); } finally { directory.Dispose(); } }
        }
    }
}
