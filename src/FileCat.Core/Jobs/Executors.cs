using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

public interface IJobExecutor
{
    void Execute();
}

public static class JobExecutors
{
    /// <summary>Typed dispatch per operation (plan §7.1): no Cartesian product of provider methods.</summary>
    public static IJobExecutor Create(Job job, IFileSystemOperations fs, ProviderRegistry providers, JobJournal journal)
    {
        var r = job.Request;
        bool fsSources = r.Sources.All(s => s.Parent.IsFileSystem);
        switch (r.Kind)
        {
            case JobKind.Copy or JobKind.Move when fsSources && r.Destination is { IsFileSystem: true }:
                return new TransferExecutor(job, fs, journal);
            case JobKind.Copy or JobKind.Extract when r.Destination is { IsFileSystem: true } && StreamTransferExecutor.CanHandle(r, providers):
                return new StreamTransferExecutor(job, fs, journal, providers);
            case JobKind.Delete when fsSources:
                return new DeleteExecutor(job, fs, journal);
            case JobKind.Recycle when fsSources:
                return new RecycleExecutor(job, fs, journal);
            case JobKind.CreateDirectory or JobKind.CreateFile when r.Destination is { IsFileSystem: true }:
                return new CreateExecutor(job, fs, journal);
            case JobKind.Rename when fsSources:
                return new RenameExecutor(job, fs, journal);
            default:
                if (ExtraExecutors.TryGetValue(r.Kind, out var factory) && factory(job, fs, providers, journal) is { } custom) return custom;
                throw new NotSupportedException(Unsupported(r, providers));
        }
    }

    /// <summary>Executors contributed by first-party modules (archives, registry, remote) as they arrive.</summary>
    public static Dictionary<JobKind, Func<Job, IFileSystemOperations, ProviderRegistry, JobJournal, IJobExecutor?>> ExtraExecutors { get; } = new();

    private static string Unsupported(JobRequest r, ProviderRegistry providers)
    {
        var from = r.Sources.FirstOrDefault()?.Parent.Scheme ?? "?";
        var to = r.Destination?.Scheme ?? "?";
        return $"{r.Kind} from a {from} location to a {to} location is not supported. Nothing was changed.";
    }
}

/// <summary>Shared helpers: retry/skip/cancel decisions for I/O failures and plain-language errors.</summary>
internal abstract class ExecutorBase(Job job, IFileSystemOperations fs, JobJournal journal) : IJobExecutor
{
    protected readonly Job Job = job;
    protected readonly IFileSystemOperations Fs = fs;
    protected readonly JobJournal Journal = journal;

    public abstract void Execute();

    protected void Issue(IssueSeverity severity, string path, string message, StepOutcome outcome) =>
        Job.AddIssue(new JobIssue(severity, path, message, outcome));

    /// <summary>Runs an I/O action; transient sharing violations (antivirus) retry briefly, others ask the user.</summary>
    protected bool TryIo(string path, string what, Action action)
    {
        int autoRetries = 0;
        while (true)
        {
            Job.Token.ThrowIfCancellationRequested();
            try
            {
                action();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var cls = ErrorText.Classify(ex);
                if (cls == "sharing" && autoRetries < 3)
                {
                    autoRetries++;
                    Job.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250 * autoRetries));
                    continue;
                }
                var d = Job.Ask(new ErrorRequest($"Could not {what}", ErrorText.Describe(ex), path, CanRetry: true, cls));
                switch (d.Action)
                {
                    case DecisionAction.Retry:
                        autoRetries = 0;
                        continue;
                    case DecisionAction.Skip:
                        Issue(IssueSeverity.Error, path, $"Could not {what}: {ErrorText.Describe(ex)}", StepOutcome.Skipped);
                        return false;
                    default:
                        throw new OperationCanceledException();
                }
            }
        }
    }
}

public static class ErrorText
{
    public static int Win32Code(Exception ex) => ex.HResult & 0xFFFF;

    public static string Classify(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return "access";
        if (ex is FileNotFoundException or DirectoryNotFoundException) return "notfound";
        return Win32Code(ex) switch
        {
            32 or 33 => "sharing",
            39 or 112 => "diskfull",
            5 => "access",
            2 or 3 => "notfound",
            206 or 111 => "toolong",
            1314 => "privilege",
            _ => "io",
        };
    }

    public static string Describe(Exception ex) => Classify(ex) switch
    {
        "sharing" => "The item is in use by another program (for example an antivirus scan or an open editor).",
        "diskfull" => "There is not enough free space on the destination.",
        "access" => "Access is denied. If the destination is a protected folder, Windows Controlled Folder Access may be blocking FileCat.",
        "notfound" => "The item no longer exists or its folder was removed.",
        "toolong" => "The name or path is too long for the destination.",
        "privilege" => "A required privilege is not held (creating symbolic links needs Developer Mode or administrator rights).",
        _ => ex.Message,
    };
}

/// <summary>
/// File-system copy and move (plan §9.2): every file is written to a unique staged name and then published,
/// so an existing destination is never truncated before its replacement is ready. Cross-volume moves delete a
/// source only after the copy is published and the source is revalidated; directories are removed only when
/// empty, never recursively.
/// </summary>
internal sealed class TransferExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    private static readonly EnumerationOptions ChildOptions = new() { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false };
    private readonly HashSet<string> _stagingDirs = new(PathUtil.SafetyComparer);
    private readonly Dictionary<string, VolumeInfo> _volumes = new(PathUtil.SafetyComparer);
    private int _stagedCounter;
    private bool Move => Job.Kind == JobKind.Move;
    private TransferOptions Options => Job.Request.Options;

    private enum Result { Committed, Skipped, Failed, Filtered }

    public override void Execute()
    {
        var destDir = Job.Request.Destination!.Path;
        using var discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(Job.Token);
        var discovery = Task.Run(() => Discover(discoveryCts.Token), discoveryCts.Token);
        try
        {
            if (!Directory.Exists(destDir))
            {
                if (!TryIo(destDir, "create the destination folder", () => Directory.CreateDirectory(destDir))) return;
                Journal.Note("created destination " + destDir);
            }
            foreach (var root in Job.Request.Sources)
            {
                Job.Checkpoint();
                var result = ProcessRoot(root, destDir);
                if (result is Result.Committed) Job.RootCompleted(root);
                else if (result is Result.Failed or Result.Skipped) Job.RootFailed(root);
            }
        }
        finally
        {
            discoveryCts.Cancel();
            try { discovery.Wait(); } catch (AggregateException) { }
            Job.SetCurrent(null);
        }
    }

    private void Discover(CancellationToken ct)
    {
        foreach (var root in Job.Request.Sources)
        {
            if (ct.IsCancellationRequested) return;
            var p = root.FileSystemPath!;
            var info = Fs.TryGetInfo(p);
            if (info is null) continue;
            if (!info.IsDirectory || info.IsLink)
            {
                Job.AddTotals(1, Math.Max(0, info.Size));
                continue;
            }
            var stack = new Stack<string>();
            stack.Push(p);
            while (stack.Count > 0 && !ct.IsCancellationRequested)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var fi in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", ChildOptions))
                    {
                        bool link = (fi.Attributes & FileAttributes.ReparsePoint) != 0;
                        if (fi is DirectoryInfo && !link) stack.Push(fi.FullName);
                        else if (Options.Filter is null || Options.Filter.IsMatch(fi.Name))
                            Job.AddTotals(1, fi is FileInfo f && !link ? f.Length : 0);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        Job.TotalsFinal = !ct.IsCancellationRequested;
    }

    private string TargetDirectoryFor(ItemRef root, string destDir)
    {
        if (Options.Flatten || Job.Request.RelativeFolders is null || !Job.Request.RelativeFolders.TryGetValue(root, out var rel) || string.IsNullOrEmpty(rel))
            return destDir;
        var dir = Path.Combine(destDir, rel);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private Result ProcessRoot(ItemRef root, string destDir)
    {
        var src = root.FileSystemPath!;
        var info = Fs.TryGetInfo(src);
        if (info is null)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, "The item no longer exists; nothing was copied.", StepOutcome.Failed);
            return Result.Failed;
        }
        var name = Job.Request.Sources.Count == 1 && !string.IsNullOrEmpty(Job.Request.NewName) ? Job.Request.NewName! : root.Name;
        var dst = Path.Combine(TargetDirectoryFor(root, destDir), name);
        if (info.IsDirectory && !info.IsLink && PathUtil.IsSameOrUnder(dst, src))
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, "A folder cannot be copied or moved into itself.", StepOutcome.Failed);
            return Result.Failed;
        }
        if (Move && string.Equals(PathUtil.NormalizeForCompare(src), PathUtil.NormalizeForCompare(dst), StringComparison.Ordinal))
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Info, src, "Source and destination are the same; nothing to move.", StepOutcome.Skipped);
            return Result.Skipped;
        }
        if (Move && SameVolume(src, dst)) return MoveByRename(src, dst, info);
        return info.IsDirectory && !info.IsLink ? CopyDirectory(src, dst, info) : CopyFileItem(src, dst, info);
    }

    private VolumeInfo Volume(string path)
    {
        var root = Path.GetPathRoot(path) ?? path;
        if (!_volumes.TryGetValue(root, out var v))
        {
            v = Fs.GetVolumeInfo(path);
            _volumes[root] = v;
        }
        return v;
    }

    private bool SameVolume(string a, string b)
    {
        var ra = Path.GetPathRoot(Path.GetFullPath(a));
        var rb = Path.GetPathRoot(Path.GetFullPath(b));
        return ra is not null && rb is not null && ra.Equals(rb, PathUtil.SafetyComparison) && !PathUtil.IsUncPath(a) && !PathUtil.IsUncPath(b)
               || PathUtil.IsUncPath(a) && PathUtil.IsUncPath(b) && string.Equals(ShareRoot(a), ShareRoot(b), PathUtil.SafetyComparison);
    }

    private static string ShareRoot(string unc)
    {
        var parts = unc.TrimStart('\\').Split('\\');
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : unc;
    }

    // ---- Same-volume move -----------------------------------------------------------------------------

    private Result MoveByRename(string src, string dst, FileSystemItemInfo info)
    {
        Job.Checkpoint();
        Job.SetCurrent(src);
        var existing = Fs.TryGetInfo(dst);
        bool replace = false;
        var target = dst;
        if (existing is not null)
        {
            bool caseOnly = string.Equals(src, dst, StringComparison.OrdinalIgnoreCase) && !string.Equals(src, dst, StringComparison.Ordinal);
            if (!caseOnly)
            {
                if (info.IsDirectory && !info.IsLink && existing.IsDirectory && !existing.IsLink)
                    return MergeMoveDirectory(src, dst);
                var d = ResolveConflict(src, dst, info, existing);
                switch (d)
                {
                    case DecisionAction.Skip:
                        Job.ItemSkipped();
                        Issue(IssueSeverity.Info, src, "Skipped: an item with this name already exists at the destination.", StepOutcome.Skipped);
                        return Result.Skipped;
                    case DecisionAction.Replace:
                        if (existing.IsDirectory) return TypeMismatchFailure(src);
                        replace = true;
                        break;
                    case DecisionAction.KeepBothRenameIncoming:
                        target = UniqueSibling(dst, info.IsDirectory);
                        break;
                    case DecisionAction.KeepBothRenameExisting:
                        if (!RenameExisting(dst, existing.IsDirectory)) return Result.Failed;
                        break;
                    default:
                        throw new OperationCanceledException();
                }
            }
        }
        int step = Journal.Intent(replace ? "move-replace" : "move", src, target);
        bool ok = TryIo(src, "move the item", () => Fs.Move(src, target, replace));
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Skipped);
        if (!ok)
        {
            Job.ItemFailed();
            return Result.Failed;
        }
        var moved = Fs.TryGetInfo(target);
        Job.AddUndo(new UndoStep(UndoKind.MoveBack, target, src, moved?.Size ?? info.Size, (moved?.ModifiedUtc ?? info.ModifiedUtc).Ticks));
        Job.AddBytes(Math.Max(0, info.Size));
        Job.ItemDone();
        return Result.Committed;
    }

    private Result MergeMoveDirectory(string src, string dst)
    {
        bool all = true;
        foreach (var child in SafeChildren(src))
        {
            Job.Checkpoint();
            var ci = Fs.TryGetInfo(child);
            if (ci is null) continue;
            var r = MoveByRename(child, Path.Combine(dst, Path.GetFileName(child)), ci);
            all &= r == Result.Committed;
        }
        if (all) RemoveIfEmpty(src);
        return all ? Result.Committed : Result.Failed;
    }

    // ---- Copy -----------------------------------------------------------------------------------------

    private Result CopyDirectory(string src, string dst, FileSystemItemInfo info)
    {
        Job.Checkpoint();
        Job.SetCurrent(src);
        var existing = Fs.TryGetInfo(dst);
        var target = dst;
        if (existing is not null && (!existing.IsDirectory || existing.IsLink))
        {
            var d = ResolveConflict(src, dst, info, existing);
            if (d == DecisionAction.KeepBothRenameIncoming) target = UniqueSibling(dst, true);
            else if (d == DecisionAction.KeepBothRenameExisting)
            {
                if (!RenameExisting(dst, existing.IsDirectory)) return Result.Failed;
            }
            else if (d == DecisionAction.Skip)
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, src, "Skipped: a file with the folder's name exists at the destination.", StepOutcome.Skipped);
                return Result.Skipped;
            }
            else
            {
                throw new OperationCanceledException();
            }
            existing = null;
        }
        bool created = false;
        bool EnsureCreated()
        {
            if (created || existing is not null) return true;
            if (!TryIo(target, "create a folder", () => Fs.CreateDirectory(target))) return false;
            created = true;
            return true;
        }
        // Without a filter, empty folders are recreated; with a filter only folders holding matches are.
        if (Options.Filter is null && !EnsureCreated()) return Result.Failed;
        bool allOk = true;
        bool anyTransferred = false;
        foreach (var child in SafeChildren(src))
        {
            Job.Checkpoint();
            var ci = Fs.TryGetInfo(child);
            if (ci is null) continue;
            var childDst = Path.Combine(target, Path.GetFileName(child));
            Result r;
            if (ci.IsDirectory && !ci.IsLink)
            {
                if (!EnsureCreatedForChild()) return Result.Failed;
                r = CopyDirectory(child, childDst, ci);
            }
            else
            {
                if (Options.Filter is not null && !Options.Filter.IsMatch(Path.GetFileName(child)))
                {
                    allOk = allOk && !Move; // filtered-out items keep the source folder in place
                    continue;
                }
                if (!EnsureCreated()) return Result.Failed;
                r = CopyFileItem(child, childDst, ci);
            }
            anyTransferred |= r == Result.Committed;
            allOk &= r == Result.Committed;
        }
        if (_enumerationFailed.Remove(src)) allOk = false;
        if (created || existing is not null)
        {
            if (Options.PreserveTimestamps) TrySetTimes(target, info);
            if (created && Options.PreserveAttributes) TrySetAttributes(target, info.Attributes);
        }
        if (Move && allOk) RemoveIfEmpty(src);
        return allOk ? Result.Committed : anyTransferred ? Result.Failed : Result.Failed;

        bool EnsureCreatedForChild() => Options.Filter is not null || EnsureCreated();
    }

    private readonly HashSet<string> _enumerationFailed = new(PathUtil.SafetyComparer);

    private IEnumerable<string> SafeChildren(string dir)
    {
        List<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(dir, "*", ChildOptions).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _enumerationFailed.Add(dir);
            Job.ItemFailed();
            Issue(IssueSeverity.Error, dir, "The folder could not be read: " + ErrorText.Describe(ex), StepOutcome.Failed);
            return [];
        }
        return children;
    }

    private void RemoveIfEmpty(string dir)
    {
        try
        {
            if (Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Issue(IssueSeverity.Info, dir, "The source folder was kept because it still contains items (for example ones created during the move).", StepOutcome.Skipped);
                return;
            }
            int step = Journal.Intent("delete-empty-source-dir", dir);
            Fs.DeleteDirectory(dir);
            Journal.Done(step, StepOutcome.Committed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Issue(IssueSeverity.Warning, dir, "Everything was moved, but the empty source folder could not be removed: " + ErrorText.Describe(ex), StepOutcome.PartiallyApplied);
        }
    }

    private Result CopyFileItem(string src, string dst, FileSystemItemInfo info)
    {
        Job.Checkpoint();
        Job.SetCurrent(src);
        var target = dst;
        bool replace = false;
        var existing = Fs.TryGetInfo(dst);
        if (existing is not null)
        {
            var d = ResolveConflict(src, dst, info, existing);
            switch (d)
            {
                case DecisionAction.Skip:
                    Job.ItemSkipped();
                    Job.AddBytes(Math.Max(0, info.Size));
                    Issue(IssueSeverity.Info, src, "Skipped: the destination already has this name.", StepOutcome.Skipped);
                    return Result.Skipped;
                case DecisionAction.Replace:
                    if (existing.IsDirectory && !existing.IsLink) return TypeMismatchFailure(src);
                    replace = true;
                    break;
                case DecisionAction.KeepBothRenameIncoming:
                    target = UniqueSibling(dst, false);
                    break;
                case DecisionAction.KeepBothRenameExisting:
                    if (!RenameExisting(dst, existing.IsDirectory)) return Result.Failed;
                    break;
                default:
                    throw new OperationCanceledException();
            }
        }

        if (info.IsLink) return CopyLink(src, target, info, replace);

        var dir = Path.GetDirectoryName(target)!;
        if (_stagingDirs.Add(dir)) Journal.StagingDirectory(dir);
        var staged = Path.Combine(dir, $"{JournalRecovery.StagedPrefix}{Job.ShortId}-{Interlocked.Increment(ref _stagedCounter)}.tmp");
        long baseBytes = Job.BytesDone;
        bool copied = false;
        while (!copied)
        {
            long reported = 0;
            var clock = Stopwatch.StartNew();
            try
            {
                Fs.CopyFile(src, staged, new FileCopyOptions { CopyLinkAsLink = true, NoBuffering = info.Size > 256L * 1024 * 1024 }, (done, total) =>
                {
                    Job.AddBytes(done - reported);
                    reported = done;
                    if (Job.IsPaused) Job.Checkpoint();
                    Job.Throttle(done, clock);
                    return Job.IsCancellationRequested ? CopyProgressAction.Cancel : CopyProgressAction.Continue;
                }, Job.Token);
                copied = true;
            }
            catch (OperationCanceledException)
            {
                Job.AddBytes(-reported);
                TryDeleteStaged(staged);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Job.AddBytes(-reported);
                TryDeleteStaged(staged);
                if (Job.IsCancellationRequested) throw new OperationCanceledException();
                var cls = ErrorText.Classify(ex);
                var decision = Job.Ask(new ErrorRequest("Could not copy the file", $"{Path.GetFileName(src)}: {ErrorText.Describe(ex)}", src, true, cls));
                if (decision.Action == DecisionAction.Retry) continue;
                if (decision.Action == DecisionAction.Skip)
                {
                    Job.ItemFailed();
                    Issue(IssueSeverity.Error, src, "Not copied: " + ErrorText.Describe(ex), StepOutcome.Skipped);
                    return Result.Failed;
                }
                throw new OperationCanceledException();
            }
        }

        // Verification at the selected profile.
        var stagedInfo = Fs.TryGetInfo(staged);
        if (stagedInfo is null || stagedInfo.Size != info.Size && info.Size >= 0)
        {
            TryDeleteStaged(staged);
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, "The copy has a different size than the source; it was discarded.", StepOutcome.Failed);
            return Result.Failed;
        }
        if (Options.Verify == VerifyMode.ReadBack && !ContentEqual(src, staged))
        {
            TryDeleteStaged(staged);
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, "Read-back verification found different content; the copy was discarded.", StepOutcome.Failed);
            return Result.Failed;
        }
        PreserveOrigin(src, staged, dst);
        if (Options.PreserveAttributes) TrySetAttributes(staged, info.Attributes & ~FileAttributes.ReadOnly);

        // Publish: the only externally visible step, journaled synchronously first.
        int step = Journal.Intent(replace ? "replace" : "publish", src, target, staged);
        bool published = TryIo(target, replace ? "replace the existing item" : "publish the copied item", () => Fs.Move(staged, target, replace));
        if (!published)
        {
            TryDeleteStaged(staged);
            Journal.Done(step, StepOutcome.CanceledBeforeChange);
            Job.ItemFailed();
            return Result.Failed;
        }
        Journal.Done(step, StepOutcome.Committed);
        if (Options.PreserveAttributes && (info.Attributes & FileAttributes.ReadOnly) != 0) TrySetAttributes(target, info.Attributes);
        if (!Move)
        {
            Job.ItemDone();
            return Result.Committed;
        }
        return DeleteMovedSource(src, info);
    }

    private Result DeleteMovedSource(string src, FileSystemItemInfo before)
    {
        // Revalidate: a source that changed while it was copied is kept (plan §9.2).
        var now = Fs.TryGetInfo(src);
        if (now is null)
        {
            Issue(IssueSeverity.Warning, src, "The copy was published, but the source had already disappeared.", StepOutcome.Uncertain);
            Job.ItemDone();
            return Result.Committed;
        }
        if (now.Size != before.Size || now.ModifiedUtc != before.ModifiedUtc)
        {
            Issue(IssueSeverity.Warning, src, "Copied, but the source changed during the copy, so it was not deleted. Both versions now exist.", StepOutcome.PartiallyApplied);
            Job.ItemDone();
            return Result.Failed;
        }
        int step = Journal.Intent("delete-source", src);
        bool ok = TryIo(src, "delete the moved source", () =>
        {
            if (now.IsReadOnly) Fs.SetAttributes(src, now.Attributes & ~FileAttributes.ReadOnly);
            Fs.DeleteFile(src);
        });
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
        if (!ok) Issue(IssueSeverity.Warning, src, "The item was copied, but the source could not be deleted; it still exists in both places.", StepOutcome.PartiallyApplied);
        Job.ItemDone();
        return ok ? Result.Committed : Result.Failed;
    }

    private Result CopyLink(string src, string target, FileSystemItemInfo info, bool replace)
    {
        if (replace && !TryIo(target, "remove the existing item", () => Fs.DeleteFile(target))) return Result.Failed;
        if (Fs.TryCopyLink(src, target, info.IsDirectory, out var error))
        {
            if (Move) DeleteLinkSource(src, info);
            Job.ItemDone();
            return Result.Committed;
        }
        // Never follow a link silently (plan §8.1): ask.
        var actions = new List<DecisionAction> { DecisionAction.Skip, DecisionAction.FollowLink };
        if (info.IsDirectory && OperatingSystem.IsWindows()) actions.Add(DecisionAction.CreateJunction);
        actions.Add(DecisionAction.CancelJob);
        var d = Job.Ask(new ConfirmRequest("Link cannot be copied as a link",
            $"\"{Path.GetFileName(src)}\" is a link{(info.LinkTarget is null ? "" : " to " + info.LinkTarget)}. It could not be recreated at the destination: {error}",
            src, "link", actions));
        switch (d.Action)
        {
            case DecisionAction.FollowLink:
                var targetInfo = new FileSystemItemInfo(src, info.IsDirectory, false, info.Size, info.ModifiedUtc, info.CreatedUtc, info.Attributes & ~FileAttributes.ReparsePoint);
                return info.IsDirectory ? CopyDirectory(src, target, targetInfo) : CopyFileItem(src, target, targetInfo);
            case DecisionAction.CreateJunction when info.LinkTarget is not null:
                if (TryIo(target, "create a junction", () => Junctions.Create(target, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(src)!, info.LinkTarget)))))
                {
                    Job.ItemDone();
                    return Result.Committed;
                }
                return Result.Failed;
            case DecisionAction.Skip:
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, src, "Link skipped: " + error, StepOutcome.Skipped);
                return Result.Skipped;
            default:
                throw new OperationCanceledException();
        }
    }

    private void DeleteLinkSource(string src, FileSystemItemInfo info)
    {
        int step = Journal.Intent("delete-source-link", src);
        bool ok = TryIo(src, "remove the moved link", () =>
        {
            if (info.IsDirectory) Fs.DeleteDirectory(src);
            else Fs.DeleteFile(src);
        });
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
    }

    private Result TypeMismatchFailure(string src)
    {
        Job.ItemFailed();
        Issue(IssueSeverity.Error, src, "A file and a folder cannot replace each other. Choose \"keep both\" or rename one of them.", StepOutcome.Failed);
        return Result.Failed;
    }

    private DecisionAction ResolveConflict(string src, string dst, FileSystemItemInfo incoming, FileSystemItemInfo existing)
    {
        bool typeMismatch = incoming.IsDirectory != existing.IsDirectory;
        bool same = string.Equals(PathUtil.NormalizeForCompare(src), PathUtil.NormalizeForCompare(dst), PathUtil.SafetyComparison);
        bool newer = IsIncomingNewer(src, dst, incoming, existing);
        var policy = Options.Conflicts;
        if (!typeMismatch && !same)
        {
            switch (policy)
            {
                case ConflictPolicy.Skip: return DecisionAction.Skip;
                case ConflictPolicy.Replace: return DecisionAction.Replace;
                case ConflictPolicy.ReplaceIfNewer: return newer ? DecisionAction.Replace : DecisionAction.Skip;
                case ConflictPolicy.KeepBothRenameIncoming: return DecisionAction.KeepBothRenameIncoming;
                case ConflictPolicy.KeepBothRenameExisting: return DecisionAction.KeepBothRenameExisting;
            }
        }
        if (same && policy is ConflictPolicy.KeepBothRenameIncoming) return DecisionAction.KeepBothRenameIncoming;
        var request = new ConflictRequest(
            same ? "Copying an item onto itself" : typeMismatch ? "A file and a folder have the same name" : "An item with this name already exists",
            Path.GetFileName(dst), incoming, existing, src, dst,
            CanReplace: !typeMismatch && !same, SameItem: same, TypeMismatch: typeMismatch, IncomingIsNewer: newer,
            SuggestedIncomingName: Path.GetFileName(UniqueSibling(dst, incoming.IsDirectory)),
            SuggestedExistingName: same ? null : Path.GetFileName(UniqueSibling(dst, existing.IsDirectory)));
        var d = Job.Ask(request);
        if (d.Action == DecisionAction.ReplaceIfNewer) return newer ? DecisionAction.Replace : DecisionAction.Skip;
        if (same && d.Action is DecisionAction.Replace or DecisionAction.KeepBothRenameExisting) return DecisionAction.KeepBothRenameIncoming;
        return d.Action;
    }

    private bool IsIncomingNewer(string src, string dst, FileSystemItemInfo incoming, FileSystemItemInfo existing)
    {
        // Executables compare their version resource first (plan §9.1).
        var ext = Path.GetExtension(src);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var a = FileVersionInfo.GetVersionInfo(src);
                var b = FileVersionInfo.GetVersionInfo(dst);
                if (Version.TryParse(a.FileVersion?.Split(' ')[0], out var va) && Version.TryParse(b.FileVersion?.Split(' ')[0], out var vb) && va != vb)
                    return va > vb;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException) { }
        }
        var precision = Max(Volume(src).TimestampPrecision, Volume(dst).TimestampPrecision);
        return incoming.ModifiedUtc - existing.ModifiedUtc > precision;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private string UniqueSibling(string path, bool isDirectory)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = PathUtil.MakeUniqueName(Path.GetFileName(path), n => Fs.TryGetInfo(Path.Combine(dir, n)) is not null, isDirectory);
        return Path.Combine(dir, name);
    }

    private bool RenameExisting(string path, bool isDirectory)
    {
        var newPath = UniqueSibling(path, isDirectory);
        int step = Journal.Intent("rename-existing", path, newPath);
        bool ok = TryIo(path, "rename the existing item", () => Fs.Move(path, newPath, false));
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
        if (ok) Issue(IssueSeverity.Info, path, $"The existing item was renamed to \"{Path.GetFileName(newPath)}\".", StepOutcome.Committed);
        return ok;
    }

    private void PreserveOrigin(string src, string staged, string finalPath)
    {
        var mark = Fs.ReadOriginMark(src);
        if (mark is null) return;
        var vol = Volume(finalPath);
        if (!vol.SupportsNamedStreams && OperatingSystem.IsWindows())
        {
            Issue(IssueSeverity.Warning, src, $"Security metadata lost: the file's download origin (Mark of the Web) cannot be stored on {vol.FileSystem ?? "the destination"}. Windows will not warn when it is opened.", StepOutcome.Committed);
            return;
        }
        if (Fs.ReadOriginMark(staged) is null && !Fs.WriteOriginMark(staged, mark))
            Issue(IssueSeverity.Warning, src, "Security metadata lost: the download origin (Mark of the Web) could not be written to the copy.", StepOutcome.Committed);
    }

    private bool ContentEqual(string a, string b)
    {
        Job.SetCurrent(a + " (verifying)");
        var ha = PortableFileOperations.HashFile(a, HashAlgorithmName.SHA256, Job.Token);
        var hb = PortableFileOperations.HashFile(b, HashAlgorithmName.SHA256, Job.Token);
        return ha.AsSpan().SequenceEqual(hb);
    }

    private void TrySetTimes(string path, FileSystemItemInfo info)
    {
        try { Fs.SetTimes(path, info.CreatedUtc, info.ModifiedUtc); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private void TrySetAttributes(string path, FileAttributes attributes)
    {
        const FileAttributes Copyable = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;
        try
        {
            var current = File.GetAttributes(path);
            var wanted = (current & ~Copyable) | (attributes & Copyable);
            if (wanted != current) Fs.SetAttributes(path, wanted);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void TryDeleteStaged(string staged)
    {
        try
        {
            if (File.Exists(staged)) Fs.DeleteFile(staged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Issue(IssueSeverity.Warning, staged, "A partial temporary file could not be removed; it can be deleted safely.", StepOutcome.PartiallyApplied);
        }
    }
}

/// <summary>Permanent deletion (Shift+F8): never follows links; folders are removed only once empty.</summary>
internal sealed class DeleteExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    private static readonly EnumerationOptions ChildOptions = new() { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 };

    public override void Execute() => Run(Job.Request.Sources.Select(s => (s, s.FileSystemPath!)).ToList());

    internal void Run(IReadOnlyList<(ItemRef Root, string Path)> roots)
    {
        foreach (var (root, path) in roots) Job.AddTotals(1, 0);
        foreach (var (root, path) in roots)
        {
            Job.Checkpoint();
            var info = Fs.TryGetInfo(path);
            if (info is null)
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, path, "Already gone; nothing to delete.", StepOutcome.Skipped);
                Job.RootCompleted(root);
                continue;
            }
            int step = Journal.Intent(info.IsDirectory && !info.IsLink ? "delete-tree" : "delete", path);
            bool ok = info.IsDirectory && !info.IsLink ? DeleteTree(path) : DeleteOne(path, info);
            Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.PartiallyApplied);
            if (ok) Job.RootCompleted(root);
            else Job.RootFailed(root);
        }
    }

    private bool DeleteTree(string dir)
    {
        bool all = true;
        List<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(dir, "*", ChildOptions).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, dir, "The folder could not be read: " + ErrorText.Describe(ex), StepOutcome.Failed);
            return false;
        }
        foreach (var child in children)
        {
            Job.Checkpoint();
            var info = Fs.TryGetInfo(child);
            if (info is null) continue;
            Job.SetCurrent(child);
            all &= info.IsDirectory && !info.IsLink ? DeleteTree(child) : DeleteOne(child, info);
        }
        if (!all) return false;
        bool removed = TryIo(dir, "delete the folder", () => Fs.DeleteDirectory(dir));
        if (removed) Job.ItemDone();
        else Job.ItemFailed();
        return removed;
    }

    private bool DeleteOne(string path, FileSystemItemInfo info)
    {
        if (info.IsReadOnly && !info.IsDirectory)
        {
            var d = Job.Ask(new ConfirmRequest("Read-only item", $"\"{Path.GetFileName(path)}\" is read-only. Delete it anyway?", path, "readonly",
                [DecisionAction.Proceed, DecisionAction.Skip, DecisionAction.CancelJob]));
            if (d.Action == DecisionAction.Skip)
            {
                Job.ItemSkipped();
                return false;
            }
            if (d.Action != DecisionAction.Proceed) throw new OperationCanceledException();
        }
        bool ok = TryIo(path, "delete the item", () =>
        {
            if (info.IsReadOnly) Fs.SetAttributes(path, info.Attributes & ~FileAttributes.ReadOnly);
            if (info.IsDirectory) Fs.DeleteDirectory(path); // a directory link: removes only the link
            else Fs.DeleteFile(path);
        });
        if (ok) Job.ItemDone();
        else Job.ItemFailed();
        return ok;
    }
}

/// <summary>
/// Recycle with a verified per-item outcome (plan §9.2): items the bin cannot take were classified before the
/// job and are either deleted permanently by explicit consent or left alone; a Shell-side permanent deletion is
/// reported, never hidden.
/// </summary>
internal sealed class RecycleExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public override void Execute()
    {
        var recyclable = new List<(ItemRef Root, string Path)>();
        var unrecyclable = new List<(ItemRef Root, string Path, RecycleClassification Why)>();
        foreach (var s in Job.Request.Sources)
        {
            var p = s.FileSystemPath!;
            var info = Fs.TryGetInfo(p);
            if (info is null)
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, p, "Already gone; nothing to delete.", StepOutcome.Skipped);
                Job.RootCompleted(s);
                continue;
            }
            var c = Fs.ClassifyRecycle(p, info.IsDirectory ? -1 : info.Size);
            if (c == RecycleClassification.Recyclable || c == RecycleClassification.Unknown) recyclable.Add((s, p));
            else unrecyclable.Add((s, p, c));
        }
        Job.AddTotals(recyclable.Count + unrecyclable.Count, 0);
        if (recyclable.Count > 0)
        {
            foreach (var (_, p) in recyclable) Journal.Intent("recycle", p);
            var results = Fs.Recycle(recyclable.Select(r => r.Path).ToList(), started => Job.SetCurrent(started), Job.Token);
            var byPath = recyclable.ToDictionary(r => r.Path, r => r.Root, PathUtil.SafetyComparer);
            foreach (var r in results)
            {
                var root = byPath.GetValueOrDefault(r.Path);
                switch (r.Outcome)
                {
                    case RecycleOutcome.Recycled:
                        Job.ItemDone();
                        if (root is not null) Job.RootCompleted(root);
                        if (r.RecycledId is not null) Job.AddUndo(new UndoStep(UndoKind.RestoreRecycled, r.RecycledId, r.Path, 0, 0, r.RecycledId));
                        break;
                    case RecycleOutcome.PermanentlyDeleted:
                        Job.ItemDone();
                        if (root is not null) Job.RootCompleted(root);
                        Issue(IssueSeverity.Warning, r.Path, "Windows deleted this item permanently instead of moving it to the Recycle Bin.", StepOutcome.Committed);
                        break;
                    case RecycleOutcome.Aborted:
                        Job.ItemSkipped();
                        if (root is not null) Job.RootFailed(root);
                        Issue(IssueSeverity.Warning, r.Path, "Not deleted: it would have been deleted permanently instead of recycled. " + r.Error, StepOutcome.CanceledBeforeChange);
                        break;
                    case RecycleOutcome.NotAttempted:
                        if (root is not null) Job.RootFailed(root);
                        break;
                    default:
                        Job.ItemFailed();
                        if (root is not null) Job.RootFailed(root);
                        Issue(IssueSeverity.Error, r.Path, "Could not recycle: " + r.Error, StepOutcome.Failed);
                        break;
                }
            }
        }
        if (unrecyclable.Count > 0)
        {
            if (Job.Request.Options.PermanentlyDeleteUnrecyclable)
            {
                new DeleteExecutor(Job, Fs, Journal).Run(unrecyclable.Select(u => (u.Root, u.Path)).ToList());
            }
            else
            {
                foreach (var u in unrecyclable)
                {
                    Job.ItemSkipped();
                    Job.RootFailed(u.Root);
                    Issue(IssueSeverity.Warning, u.Path, "Not deleted: " + Explain(u.Why) + " Nothing was changed.", StepOutcome.CanceledBeforeChange);
                }
            }
        }
    }

    public static string Explain(RecycleClassification c) => c switch
    {
        RecycleClassification.NoRecycleBin => "this location has no Recycle Bin (network shares and most removable drives).",
        RecycleClassification.TooLarge => "the item is larger than the Recycle Bin accepts.",
        RecycleClassification.NameTooLong => "the path is too long for the Recycle Bin.",
        _ => "the Recycle Bin cannot take it.",
    };
}

internal sealed class CreateExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public override void Execute()
    {
        var parent = Job.Request.Destination!.Path;
        var name = Job.Request.NewName ?? throw new InvalidOperationException("A name is required.");
        var full = Path.GetFullPath(Path.Combine(parent, name));
        if (!PathUtil.IsSameOrUnder(full, parent)) throw new InvalidOperationException("The name must stay inside the current folder.");
        Job.AddTotals(1, 0);
        if (Fs.TryGetInfo(full) is not null)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, full, "An item with this name already exists.", StepOutcome.Failed);
            return;
        }
        int step = Journal.Intent(Job.Kind == JobKind.CreateDirectory ? "mkdir" : "mkfile", full);
        bool ok;
        if (Job.Kind == JobKind.CreateDirectory)
        {
            var created = new List<string>();
            for (var p = full; p is not null && !Directory.Exists(p); p = Path.GetDirectoryName(p)) created.Add(p);
            ok = TryIo(full, "create the folder", () => Directory.CreateDirectory(full));
            // Recorded outermost first so that undo (which runs in reverse) removes the deepest folder first.
            if (ok) foreach (var c in Enumerable.Reverse(created)) Job.AddUndo(new UndoStep(UndoKind.RemoveEmptyDirectory, c, c, 0, 0));
        }
        else
        {
            ok = TryIo(full, "create the file", () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                using var _ = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            });
            if (ok) Job.AddUndo(new UndoStep(UndoKind.RemoveCreatedFile, full, full, 0, File.GetLastWriteTimeUtc(full).Ticks));
        }
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
        if (ok) Job.ItemDone();
        else Job.ItemFailed();
    }
}

internal sealed class RenameExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public override void Execute()
    {
        var root = Job.Request.Sources[0];
        var src = root.FileSystemPath!;
        var newName = Job.Request.NewName ?? throw new InvalidOperationException("A new name is required.");
        var dst = Path.Combine(Path.GetDirectoryName(src)!, newName);
        Job.AddTotals(1, 0);
        var info = Fs.TryGetInfo(src);
        if (info is null)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, "The item no longer exists.", StepOutcome.Failed);
            Job.RootFailed(root);
            return;
        }
        bool caseOnly = string.Equals(src, dst, StringComparison.OrdinalIgnoreCase);
        var existing = caseOnly ? null : Fs.TryGetInfo(dst);
        bool replace = false;
        if (existing is not null)
        {
            var d = Job.Ask(new ConflictRequest("An item with this name already exists", newName, info, existing, src, dst,
                CanReplace: !existing.IsDirectory && !info.IsDirectory, false, existing.IsDirectory != info.IsDirectory, false, null, null));
            if (d.Action == DecisionAction.Replace && !existing.IsDirectory && !info.IsDirectory) replace = true;
            else if (d.Action == DecisionAction.Skip)
            {
                Job.ItemSkipped();
                Job.RootFailed(root);
                return;
            }
            else throw new OperationCanceledException();
        }
        int step = Journal.Intent(replace ? "rename-replace" : "rename", src, dst);
        bool ok = TryIo(src, "rename the item", () => Fs.Move(src, dst, replace));
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
        if (!ok)
        {
            Job.ItemFailed();
            Job.RootFailed(root);
            return;
        }
        var after = Fs.TryGetInfo(dst);
        if (!replace) Job.AddUndo(new UndoStep(UndoKind.MoveBack, dst, src, after?.Size ?? info.Size, (after?.ModifiedUtc ?? info.ModifiedUtc).Ticks));
        Job.ItemDone();
        Job.RootCompleted(root);
    }
}

/// <summary>Guarded undo (plan §9.3): each step is eligible only when the current state still matches.</summary>
public static class UndoService
{
    public static IReadOnlyList<string> Undo(Job job, IFileSystemOperations fs)
    {
        var report = new List<string>();
        foreach (var step in job.UndoSteps.Reverse())
        {
            try
            {
                switch (step.Kind)
                {
                    case UndoKind.MoveBack:
                        var now = fs.TryGetInfo(step.From);
                        if (now is null) { report.Add($"Not undone: \"{Path.GetFileName(step.From)}\" no longer exists."); break; }
                        if (!now.IsDirectory && (now.Size != step.Size || now.ModifiedUtc.Ticks != step.ModifiedTicks)) { report.Add($"Not undone: \"{Path.GetFileName(step.From)}\" changed since the operation."); break; }
                        if (fs.TryGetInfo(step.To) is not null && !string.Equals(step.From, step.To, StringComparison.OrdinalIgnoreCase)) { report.Add($"Not undone: \"{Path.GetFileName(step.To)}\" exists again at the original location."); break; }
                        // A move removes emptied source folders; putting an item back recreates its folder.
                        Directory.CreateDirectory(Path.GetDirectoryName(step.To)!);
                        fs.Move(step.From, step.To, false);
                        report.Add($"Restored \"{Path.GetFileName(step.To)}\".");
                        break;
                    case UndoKind.RestoreRecycled:
                        report.Add(fs.TryRestoreRecycled(step.RecycledId!, step.To, out var err)
                            ? $"Restored \"{Path.GetFileName(step.To)}\" from the Recycle Bin."
                            : $"Not restored: \"{Path.GetFileName(step.To)}\": {err}");
                        break;
                    case UndoKind.RemoveEmptyDirectory:
                        if (Directory.Exists(step.From) && !Directory.EnumerateFileSystemEntries(step.From).Any())
                        {
                            fs.DeleteDirectory(step.From);
                            report.Add($"Removed the created folder \"{Path.GetFileName(step.From)}\".");
                        }
                        else report.Add($"Kept \"{Path.GetFileName(step.From)}\": it is not empty anymore.");
                        break;
                    case UndoKind.RemoveCreatedFile:
                        var fi = fs.TryGetInfo(step.From);
                        if (fi is { Size: 0 } && fi.ModifiedUtc.Ticks == step.ModifiedTicks)
                        {
                            fs.DeleteFile(step.From);
                            report.Add($"Removed the created file \"{Path.GetFileName(step.From)}\".");
                        }
                        else report.Add($"Kept \"{Path.GetFileName(step.From)}\": it was changed after it was created.");
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report.Add($"Not undone: \"{Path.GetFileName(step.From)}\": {ErrorText.Describe(ex)}");
            }
        }
        return report;
    }
}
