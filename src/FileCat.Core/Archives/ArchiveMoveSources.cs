using System.Security.Cryptography;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>
/// Source accounting for the existing local ZIP route. A move never removes a skipped input, or starts removal
/// until the published archive matches the verified stage. Uses native reviewed deletion when supplied by the adapter;
/// the portable route has the same guarded pathname semantics as ordinary cross-volume local moves, not an atomic
/// filesystem snapshot. Empty directories are removed individually, never recursively.
/// </summary>
internal sealed class ArchiveMoveSources
{
    internal sealed class Input(string path, string? identity, FileSystemItemInfo info, string? mark)
    {
        internal readonly string Path = path;
        internal readonly string? Identity = identity, Mark = mark;
        internal readonly FileSystemItemInfo Info = info;
        internal uint? UnixMode;
        internal byte[]? Hash;
        internal bool Written, Skipped, Removed, ExplicitDirectory;
    }
    private readonly Job job;
    private readonly IFileSystemOperations fs;
    private readonly JobJournal journal;
    private readonly string archive;
    private readonly string? archiveIdentity;
    private readonly List<Input> roots = [];
    private readonly HashSet<string> directoryRoots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Input> inputs = new(StringComparer.Ordinal);
    private int originBytes, removedFiles, removedDirectories;

    internal ArchiveMoveSources(Job job, IFileSystemOperations fs, JobJournal journal, ArchivePlan plan, string archive)
    {
        this.job = job; this.fs = fs; this.journal = journal; this.archive = archive;
        archiveIdentity = fs.GetFileIdentity(archive);
        if (plan.Changes.Count == 0 || plan.Changes.Count != job.Request.Sources.Count ||
            plan.Changes.Any(c => c.Kind is not (ArchiveChangeKind.AddFile or ArchiveChangeKind.AddFolder)))
            throw new ArgumentException("A move into an archive requires the selected local files and folders.");
        string realArchive = fs.GetFinalPath(archive) ?? Path.Combine(fs.GetFinalPath(Path.GetDirectoryName(archive)!) ?? Path.GetDirectoryName(archive)!, Path.GetFileName(archive));
        for (int i = 0; i < plan.Changes.Count; i++)
        {
            var change = plan.Changes[i]; string path = Path.GetFullPath(change.SourcePath ?? throw new ArgumentException("A move source is required."));
            if (job.Request.Sources[i].FileSystemPath is not { } selected || !PathUtil.SafetyComparer.Equals(Path.GetFullPath(selected), path))
                throw new ArgumentException("The move sources differ from the selected items.");
            bool directory = change.Kind == ArchiveChangeKind.AddFolder;
            var input = Capture(path, directory);
            string realSource = fs.GetFinalPath(path) ?? path;
            if (PathUtil.SafetyComparer.Equals(path, archive) || PathUtil.SafetyComparer.Equals(realSource, realArchive) ||
                directory && (PathUtil.IsSameOrUnder(archive, path) || PathUtil.IsSameOrUnder(realArchive, realSource)))
                throw new IOException("An archive cannot be moved into itself or into a folder being moved. Choose an archive outside the selected sources.");
            roots.Add(input);
            if (directory) directoryRoots.Add(path);
        }
    }

    internal Input Capture(string path, bool directory)
    {
        path = Path.GetFullPath(path);
        if (inputs.TryGetValue(path, out var previous)) return previous;
        if (inputs.Count >= ArchiveWritePolicy.MaxMembers) throw new IOException("Too many inputs for one archive move; move smaller selections.");
        var info = fs.TryGetInfo(path) ?? throw new FileNotFoundException("The move source no longer exists.", path);
        if (info.IsDirectory != directory || info.IsLink || ReparseLinks.IsLink(path) ||
            !directory && !OperatingSystem.IsWindows() && UnixFiles.Stat(path) is not { IsRegular: true })
            throw new IOException("Only local regular files and folders can be moved into an archive; links and special files stay in place.");
        string? identity = fs.GetFileIdentity(path);
        if (identity is not null && identity == archiveIdentity)
            throw new IOException("The archive itself, or a link to it, cannot be a move source.");
        string? mark = fs.ReadOriginMark(path);
        if (mark is not null)
        {
            int bytes = Encoding.UTF8.GetByteCount(mark);
            if (bytes > ArchiveOriginSnapshot.MaxBytes || bytes > ArchiveWritePolicy.MaxMetadataBytes - originBytes)
                throw new IOException("The source download marks exceed the supported archive move budget.");
            originBytes += bytes;
        }
        var input = new Input(path, identity, info, mark) { UnixMode = UnixFiles.Stat(path)?.Mode }; inputs.Add(path, input); return input;
    }

    internal void RequireCurrent(Input input)
    {
        var now = fs.TryGetInfo(input.Path);
        if (now is null || now.IsDirectory != input.Info.IsDirectory || now.IsLink || ReparseLinks.IsLink(input.Path) ||
            input.Identity is not null && !string.Equals(fs.GetFileIdentity(input.Path), input.Identity, StringComparison.Ordinal) ||
            !now.IsDirectory && (now.Size != input.Info.Size || now.ModifiedUtc != input.Info.ModifiedUtc || now.Attributes != input.Info.Attributes) ||
            input.UnixMode is { } mode && UnixFiles.Stat(input.Path)?.Mode != mode ||
            fs.ReadOriginMark(input.Path) != input.Mark)
            throw new IOException("The move source changed after it was captured; its original was kept: " + input.Path);
    }

    internal void Recorded(Input input, byte[] hash)
    {
        RequireCurrent(input);
        if (input.Hash is not null && !input.Hash.AsSpan().SequenceEqual(hash)) throw new IOException("An aliased move source changed between reads.");
        input.Hash = hash; input.Written = true;
    }

    internal string? RequiredOrigin(string? original)
    {
        int maximum = -1; string? strongest = null;
        var format = ArchiveOriginFormat.WindowsZoneIdentifier;
        foreach (var input in inputs.Values.Where(i => !i.Skipped && (i.Written || i.Info.IsDirectory && !i.ExplicitDirectory)))
        {
            job.Checkpoint(); RequireCurrent(input);
            if (input.Mark is not { } mark) continue;
            int zone = ArchiveWindowsOriginZone.Parse(ArchiveOriginSnapshot.Present(format, Encoding.UTF8.GetBytes(mark))).DeclaredZone!.Value;
            if (zone > maximum) { maximum = zone; strongest = mark; }
        }
        if (strongest is null) return original;
        if (original is null) return OperatingSystem.IsWindows()
            ? Encoding.UTF8.GetString(ArchiveWindowsOriginZone.ZoneOnly(maximum).Bytes) : strongest;
        var parsed = ArchiveWindowsOriginZone.Parse(ArchiveOriginSnapshot.Present(format, Encoding.UTF8.GetBytes(original)));
        return Encoding.UTF8.GetString(parsed.RaiseTo(Math.Max(maximum, parsed.DeclaredZone!.Value)).Bytes);
    }

    internal void RemoveAfterPublication(string expectedDigest, string? requiredMark)
    {
        job.AddTotals(inputs.Count, 0);
        job.RecordUndoRemainder(archive, "Archive moves have no automatic inverse; Undo will not remove archive members or recreate originals.");
        var children = inputs.Values.Where(i => Path.GetDirectoryName(i.Path) is not null).GroupBy(i => Path.GetDirectoryName(i.Path)!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        try
        {
            job.SetSummary("Archive saved; removing originals.");
            using var held = fs.HoldRegularFile(archive);
            string? publishedIdentity = fs.GetFileIdentity(archive);
            using var published = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            UnixStat? unix = UnixFiles.Stat(published.SafeFileHandle);
            string digest = Convert.ToHexString(Hash(published));
            if (digest != expectedDigest || publishedIdentity is null || fs.GetFileIdentity(archive) != publishedIdentity ||
                unix is not null && (UnixFiles.Stat(published.SafeFileHandle) != unix || unix.Value.Identity.ToString() != publishedIdentity) ||
                fs.ReadOriginMark(archive) != requiredMark)
                throw new IOException("The published archive changed before source removal; the originals stay in place.");
            var version = fs.TryGetInfo(archive) ?? throw new IOException("The published archive is no longer available.");
            void CheckArchive()
            {
                job.Checkpoint(); var now = fs.TryGetInfo(archive);
                if (now is null || now.Size != version.Size || now.ModifiedUtc != version.ModifiedUtc ||
                    publishedIdentity is not null && fs.GetFileIdentity(archive) != publishedIdentity ||
                    unix is not null && UnixFiles.Stat(published.SafeFileHandle) != unix || fs.ReadOriginMark(archive) != requiredMark)
                    throw new ArchiveChanged();
            }
            foreach (var input in inputs.Values.Where(i => !i.Info.IsDirectory))
            {
                CheckArchive();
                if (!input.Written || input.Skipped) { job.ItemSkipped(); continue; }
                Remove(input, () =>
                {
                    RequireCurrent(input); RequireParents(input);
                    RequireNoUnstoredData(input);
                    if (input.Identity is null || input.Hash is null) throw new IOException("The source identity could not be confirmed.");
                    var result = fs.DeleteReviewedFile(input.Path, input.Identity, Convert.ToHexString(input.Hash), input.Info.Size, job.Token);
                    if (result is not null)
                    { if (!result.Deleted) throw new IOException(result.Reason ?? "The reviewed source was not removed."); return; }
                    using var sourceLease = fs.HoldRegularFile(input.Path, ChosenRoot(input.Path));
                    using (var source = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                    {
                        var before = UnixFiles.Stat(source.SafeFileHandle);
                        if (source.Length != input.Info.Size || !Hash(source).AsSpan().SequenceEqual(input.Hash) ||
                            before is not null && UnixFiles.Stat(source.SafeFileHandle) != before)
                            throw new IOException("The source content changed after it was archived.");
                    }
                    CheckArchive(); RequireCurrent(input); RequireParents(input); job.Checkpoint(); fs.DeleteFile(input.Path);
                });
            }
            foreach (var input in inputs.Values.Where(i => i.Info.IsDirectory).OrderByDescending(i => i.Path.Length))
            {
                CheckArchive();
                if (input.Skipped || input.ExplicitDirectory && !input.Written) { job.ItemSkipped(); continue; }
                Remove(input, () =>
                {
                    RequireCurrent(input); RequireParents(input);
                    RequireNoUnstoredData(input);
                    if (input.Identity is null) throw new IOException("The source folder identity could not be confirmed.");
                    if (children.TryGetValue(input.Path, out var capturedChildren) && capturedChildren.Any(c => !c.Removed))
                        throw new IOException("Not every captured child was confirmed moved; the source folder stays in place.");
                    if (Directory.EnumerateFileSystemEntries(input.Path).Any()) throw new IOException("The folder still contains skipped, changed or new items.");
                    var result = fs.DeleteReviewedTree(input.Path, [new(input.Path, input.Identity, true, false, null, 0)], job.Checkpoint, job.Token);
                    if (result is not null)
                    { if (result.Failed != 0 || result.Done != 1) throw new IOException(string.Join("; ", result.Problems)); return; }
                    using var parent = fs.HoldDirectory(Path.GetDirectoryName(input.Path)!, ChosenRoot(input.Path));
                    RequireCurrent(input); job.Checkpoint(); fs.DeleteDirectory(input.Path);
                });
            }
            void Remove(Input input, Action action)
            {
                job.SetCurrent("Removing original: " + input.Path);
                int step = journal.Intent("archive-move-delete", input.Path, archive);
                try
                {
                    action();
                }
                catch (OperationCanceledException) { journal.Done(step, StepOutcome.CanceledBeforeChange); throw; }
                catch (ArchiveChanged) { journal.Done(step, StepOutcome.CanceledBeforeChange); throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                { journal.Done(step, StepOutcome.PartiallyApplied, "Archive saved; source removal not completed. " + ex.Message); Warn(input.Path, ex.Message); job.ItemSkipped(); return; }
                // Namespace mutation already succeeded. A subsequent journal failure must never be described as
                // a retained source or cause another deletion attempt; stop and report the committed partial move.
                input.Removed = true;
                if (input.Info.IsDirectory) removedDirectories++; else removedFiles++;
                job.ItemDone(); journal.Done(step, StepOutcome.Committed);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { Warn(archive, ex.Message); }
        finally
        {
            for (int i = 0; i < roots.Count; i++)
            { if (roots[i].Removed) job.RootCompleted(i); else job.RootFailed(i); }
            int kept = inputs.Count(i => !i.Value.Removed);
            string files = removedFiles == 1 ? "file" : "files", folders = removedDirectories == 1 ? "folder" : "folders";
            job.SetSummary(kept == 0 ? $"Archive saved; moved {removedFiles:N0} {files} and {removedDirectories:N0} {folders}."
                : $"Archive saved; removed {removedFiles:N0} original {files} and {removedDirectories:N0} {folders}; {kept:N0} source " +
                    (kept == 1 ? "item was" : "items were") + " not confirmed removed" + (job.IsCancellationRequested ? "; move canceled." : "."));
        }
    }

    private string? ChosenRoot(string path)
    {
        string? chosen = null;
        for (string? parent = path; parent is not null; parent = Path.GetDirectoryName(parent))
            if (directoryRoots.Contains(parent)) chosen = parent;
        return chosen;
    }
    private void RequireParents(Input input)
    {
        for (string? path = Path.GetDirectoryName(input.Path); path is not null && inputs.TryGetValue(path, out var parent); path = Path.GetDirectoryName(path))
            RequireCurrent(parent);
    }
    private void RequireNoUnstoredData(Input input)
    {
        // Ordinary ZIP entries do not encode alternate streams, resource forks or opaque extended attributes.
        // Retain such a source after adding its primary data, rather than silently destroying the extra data.
        if (OperatingSystem.IsWindows())
        {
            if (fs.GetAlternateStreams(input.Path).Any(n => !n.Equals("Zone.Identifier", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("The source has additional data streams that this ZIP does not store.");
        }
        else
        {
            string origin = OperatingSystem.IsMacOS() ? UnixFileOperations.QuarantineAttribute : UnixFileOperations.OriginAttribute;
            if (new UnixHiddenData().List(input.Path).Any(a => a.Name != origin))
                throw new IOException("The source has additional extended data or metadata that this ZIP does not store.");
        }
    }
    private byte[] Hash(Stream stream)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1 << 20];
        long expected = stream.Length, total = 0;
        job.AddVerifyTotal(expected);
        try
        {
            while (true)
            {
                job.Checkpoint(); int count = stream.Read(buffer); if (count == 0) break;
                total += count; job.AddVerified(count); job.AddDataProcessed(count);
                if (total > expected) throw new IOException("A file grew during move verification."); hash.AppendData(buffer, 0, count);
            }
            if (total != expected || stream.Length != expected) throw new IOException("A file changed length during move verification.");
            return hash.GetHashAndReset();
        }
        finally { job.AddVerifyTotal(total - expected); }
    }
    private void Warn(string path, string reason) => job.AddIssue(new(IssueSeverity.Warning, path,
        "Archive saved; source removal was not completed: " + reason, StepOutcome.PartiallyApplied));
    private sealed class ArchiveChanged() : IOException("The published archive changed during source removal; remaining originals were kept.");
}
