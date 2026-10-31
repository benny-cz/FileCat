using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

public interface IJobExecutor
{
    void Execute();
}

/// <summary>
/// An executor that applies <see cref="TransferOptions.Filter"/> ("only files matching"). A copy or move with a filter
/// that its executor would not apply is refused before anything changes: it would copy or move more than was asked.
/// </summary>
public interface IHonorsTransferFilter;

/// <summary>
/// An executor that performs <see cref="VerifyMode.ReadBack"/> ("read back and compare content") on the copies it makes,
/// or makes none (a move within one server renames there). A copy or move with that choice whose executor does neither
/// runs as usual and says that its copies were checked by size only: the choice never passes silently (plan PI-06).
/// </summary>
public interface IHonorsReadBackVerification;

public static class JobExecutors
{
    /// <summary>
    /// Whether this admitted route can copy bytes through Job.Throttle. A local Move may instead finish by
    /// renaming on one volume; the UI describes the limit as applying to copied bytes only.
    /// </summary>
    public static bool MayThrottle(JobRequest request)
    {
        if (request.Sources.Count == 0 || request.Destination is not { } destination) return false;
        bool fromFileSystem = request.Sources.All(source => source.Parent.IsFileSystem);
        if (destination.IsFileSystem)
            return request.Kind is JobKind.Copy or JobKind.Extract ||
                request.Kind == JobKind.Move && (fromFileSystem ||
                    request.Sources.All(source => source.Parent.Scheme == Schemes.Sftp));
        if (destination.Scheme == Schemes.Sftp)
            return request.Kind == JobKind.Copy || request.Kind == JobKind.Move && fromFileSystem;
        return false;
    }

    /// <summary>Typed dispatch per operation (plan §7.1): no Cartesian product of provider methods.</summary>
    public static IJobExecutor Create(Job job, IFileSystemOperations fs, ProviderRegistry providers, JobJournal journal)
    {
        var executor = CreateFor(job, fs, providers, journal);
        bool transfer = job.Request.Kind is JobKind.Copy or JobKind.Move or JobKind.Extract;
        if (transfer && job.Request.Options.Filter is not null && executor is not IHonorsTransferFilter)
            return new RefusedExecutor(job, fs, journal,
                "\"Only files matching\" is not available for this copy or move (it works between folders on disk, from archives, servers, and phones to disk, and from disk to servers and phones). Nothing was copied or moved.");
        if (transfer && job.Request.Options.Verify == VerifyMode.ReadBack && executor is not IHonorsReadBackVerification)
            return new UnverifiedExecutor(executor, job, fs, journal);
        return executor;
    }

    private static IJobExecutor CreateFor(Job job, IFileSystemOperations fs, ProviderRegistry providers, JobJournal journal)
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
            case JobKind.ArchiveUpdate when r.Archive is not null:
                return new Archives.ZipUpdateExecutor(job, fs, journal);
            case JobKind.ArchiveTest when fsSources:
                return new Archives.ZipTestExecutor(job, fs, journal);
            case JobKind.Attributes when fsSources:
                return new AttributesExecutor(job, fs, journal);
            case JobKind.ApplyCommand when r.Invocations is not null:
                return new Tools.ApplyCommandExecutor(job, fs, journal);
            case JobKind.VerifyChecksums when fsSources:
                return new Operations.VerifyChecksumsExecutor(job, fs, journal);
            case JobKind.VerifyBeside when fsSources:
                return new Verification.VerifyBesideExecutor(job, fs, journal);
            case JobKind.CreateLink when r.Link is not null:
                return new Operations.LinkExecutor(job, fs, journal);
            case JobKind.Rename when fsSources && r.NewNames is not null:
                return new Operations.BulkRenameExecutor(job, fs, journal);
            case JobKind.Rename when fsSources:
                return new RenameExecutor(job, fs, journal);
            case JobKind.Undo when r.UndoPlan is not null:
                return new UndoExecutor(job, fs, journal);
            default:
                foreach (var module in _modules)
                {
                    if (module(job, fs, providers, journal) is { } handled) return handled;
                }
                if (ExtraExecutors.TryGetValue(r.Kind, out var factory) && factory(job, fs, providers, journal) is { } custom) return custom;
                throw new NotSupportedException(Unsupported(r, providers));
        }
    }

    private static System.Collections.Immutable.ImmutableList<Func<Job, IFileSystemOperations, ProviderRegistry, JobJournal, IJobExecutor?>> _modules = [];

    /// <summary>
    /// A module that handles some requests of any kind (a remote provider's copy, delete, rename…): it returns null
    /// for requests that are not its own. Modules are asked after the built-in cases, in registration order.
    /// </summary>
    public static void RegisterModule(Func<Job, IFileSystemOperations, ProviderRegistry, JobJournal, IJobExecutor?> factory) =>
        ImmutableInterlocked.Update(ref _modules, list => list.Contains(factory) ? list : list.Add(factory));

    /// <summary>Executors contributed by first-party modules (archives, registry, remote) as they arrive.</summary>
    /// <summary>Platform executors; concurrent because platforms register while other jobs may be starting.</summary>
    public static System.Collections.Concurrent.ConcurrentDictionary<JobKind, Func<Job, IFileSystemOperations, ProviderRegistry, JobJournal, IJobExecutor?>> ExtraExecutors { get; } = new();

    private static string Unsupported(JobRequest r, ProviderRegistry providers)
    {
        var from = r.Sources.FirstOrDefault()?.Parent.Scheme ?? "?";
        var to = r.Destination?.Scheme ?? "?";
        return $"{r.Kind} from a {from} location to a {to} location is not supported. Nothing was changed.";
    }
}

/// <summary>A request refused before anything changed: every item fails with the reason.</summary>
internal sealed class RefusedExecutor(Job job, IFileSystemOperations fs, JobJournal journal, string reason) : ExecutorBase(job, fs, journal)
{
    public override void Execute()
    {
        var sources = Job.Request.Sources;
        Job.AddTotals(Math.Max(1, sources.Count), 0);
        Issue(IssueSeverity.Error, sources.Count > 0 ? sources[0].Name : string.Empty, reason, StepOutcome.CanceledBeforeChange);
        for (int i = 0; i < sources.Count; i++)
        {
            Job.ItemFailed();
            Job.RootFailed(i);
        }
        if (sources.Count == 0) Job.ItemFailed();
    }
}

/// <summary>
/// A copy or move with "read back and compare content" whose executor cannot read its copies back (release V08 found the
/// choice ignored silently): it runs as usual, then says what the copies were checked by.
/// </summary>
internal sealed class UnverifiedExecutor(IJobExecutor inner, Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public override void Execute()
    {
        inner.Execute();
        if (Job.ItemsDone > 0)
            Issue(IssueSeverity.Warning, Job.Request.Destination?.Path ?? string.Empty,
                "Not read back: this kind of copy cannot read its copies back to compare them, so they were checked by their size only.", StepOutcome.Committed);
    }
}

/// <summary>Shared helpers: retry/skip/cancel decisions for I/O failures and plain-language errors.</summary>
internal abstract class ExecutorBase(Job job, IFileSystemOperations fs, JobJournal journal) : IJobExecutor
{
    protected const int MaxRecursiveTreeDepth = 512;
    protected readonly Job Job = job;
    protected readonly IFileSystemOperations Fs = fs;
    protected readonly JobJournal Journal = journal;

    public abstract void Execute();

    /// <summary>
    /// Whether an item planned by a comparison changed since (<see cref="JobRequest.OnlyAsCompared"/>): a file's or link's
    /// size or modified time; for a folder, all it holds, read again (its own time tells nothing of changes deeper in, and
    /// on NTFS it is listed late and stays the same within a clock tick). Such an item stays, and the job says why.
    /// </summary>
    protected bool ChangedSinceCompared(ItemRef source, FileSystemItemInfo now, string path)
    {
        if (!Job.Request.OnlyAsCompared) return false;
        string? why;
        if (now.IsDirectory && !now.IsLink)
        {
            var then = Job.Request.ExpectedContents?.GetValueOrDefault(source);
            var contents = then is null ? null : FolderContents.Read(path, Job.Token);
            why = then is null ? "the comparison that planned this did not read all it holds"
                : contents is null ? "all it holds could not be read again to see that it is as compared"
                : contents != then ? $"what it holds changed after the comparison that planned this ({then.Describe()} then, {contents.Describe()} now)"
                : null;
        }
        else
        {
            why = source.Modified > 0 && (now.ModifiedUtc.Ticks != source.Modified || !now.IsDirectory && source.Size >= 0 && now.Size != source.Size)
                ? "it changed after the comparison that planned this"
                : null;
        }
        if (why is not null)
        {
            Issue(IssueSeverity.Warning, path, $"Not removed: {why}, so it stays. Compare again to decide about it.", StepOutcome.CanceledBeforeChange);
            return true;
        }
        if (Job.Request.ExpectedSourceTrees is { } reviewed && reviewed.TryGetValue(source, out var approved) &&
            approved.Tree.Matches(Fs, approved.Provider, Job.Checkpoint)) return false;
        Issue(IssueSeverity.Warning, path,
            "Not removed: its complete bytes, identity, or descendants could not be confirmed as compared. Compare again to decide about it.",
            StepOutcome.CanceledBeforeChange);
        return true;
    }

    /// <summary>
    /// The review a replacement binds to, read only where something is to be replaced (review finding F-M1 of f134ba37):
    /// Skip and Keep both read no content, and the replacement's own check through its held handle is the only read after
    /// it. A question shown to the user binds the destination it showed, reviewed as it was asked (<paramref name="atQuestion"/>),
    /// so that a file put in its place while the question waits is never replaced. An answer given without a question
    /// (the job's policy, or one given for all) is reviewed once given; the destination must still have the type, size
    /// and modified time it had when the conflict was found (<paramref name="found"/>). Null when it does not, or cannot
    /// be read; the item is then not replaced.
    /// </summary>
    protected FileTreeReview? ReviewForReplacement(string dst, FileSystemItemInfo found, QuestionReview? atQuestion)
    {
        if (atQuestion is not null) return atQuestion.Review;
        var review = CaptureForReplacement(dst);
        var now = review?.FindItem(dst)?.Version.Info;
        return now is not null && now.IsDirectory == found.IsDirectory && now.IsLink == found.IsLink && now.Size == found.Size &&
            now.ModifiedUtc == found.ModifiedUtc ? review : null;
    }

    protected FileTreeReview? CaptureForReplacement(string dst) => FileTreeReview.Capture(dst, Fs, new LocalFileSystemProvider(),
        Job.Checkpoint, itemLimit: 1, allowLinks: true, bindByIdentity: true);

    /// <summary>A destination reviewed as a question offering to replace it was shown; its review is null where it could not be read.</summary>
    protected sealed record QuestionReview(FileTreeReview? Review);

    /// <summary>
    /// The review a conflict question offering Replace binds to: taken before the question is shown, and not where the
    /// job already holds an answer given for all, which it returns without asking (F-M1 of f134ba37).
    /// </summary>
    protected QuestionReview? ReviewAtQuestion(ConflictRequest request, string dst) =>
        request.CanReplace && !Job.Remembers(request.ClassKey) ? new(CaptureForReplacement(dst)) : null;

    protected const string ChangedSinceQuestion =
        "Not replaced: the destination changed after the conflict question, or could not be read completely. Review the conflict again.";

    protected BoundReplacement ReplaceReviewed(string source, string target, FileTreeReview? review, int step)
    {
        var root = review?.FindItem(target);
        string? recoveryPath = Fs.PrepareReviewedReplacement(source, target);
        if (recoveryPath is not null) Journal.ReplacementRecovery(step, recoveryPath);
        // Content-reviewed, or (beyond the budget, or kept by a cloud provider) bound by identity, length and last write.
        return Fs.ReplaceReviewed(source, target, root?.Version.Identity, root?.Content?.Sha256 ?? string.Empty,
            root?.Content?.Length ?? (root is null || root.Version.Info.IsDirectory ? -1 : root.Version.Info.Size), Job.Token, recoveryPath,
            root is { Content: null } ? root.Version.Info.ModifiedUtc : null)
            ?? new BoundReplacement(false, true,
                Reason: "This file system cannot bind replacement to the exact reviewed destination; it was kept.");
    }

    /// <summary>
    /// How a bound replacement that published nothing is journaled: refused before any change, failed with the original
    /// back at its name, or partly applied with the original kept at a recovery name, which is never recorded as canceled
    /// before any change (review finding W9 of f134ba37).
    /// </summary>
    protected static StepOutcome Unpublished(BoundReplacement bound) => bound.RetainedPath is not null ? StepOutcome.PartiallyApplied
        : bound.Changed ? StepOutcome.CanceledBeforeChange : StepOutcome.Failed;

    protected void Issue(IssueSeverity severity, string path, string message, StepOutcome outcome, string? cause = null) =>
        Job.AddIssue(new JobIssue(severity, path, message, outcome) { Cause = cause });

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
                // Once the user chose Skip for all sharing errors, retrying every following locked item adds
                // 250 + 500 + 750 ms per item before Ask can apply that remembered answer.
                if (cls == "sharing" && autoRetries < 3 && !Job.HasRememberedSkip("error-" + cls))
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
                        Issue(IssueSeverity.Error, path, $"Could not {what}: {ErrorText.Describe(ex)}", StepOutcome.Skipped, cls);
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
        // Linux and macOS: .NET reports the C library's error number itself, whose numbers mean other things on Windows
        // (17 is "exists" there, not "another drive"). A Windows-style code (0x8007xxxx) is read as one everywhere.
        if (!OperatingSystem.IsWindows() && (ex.HResult & 0xFFFF0000) == 0) return ClassifyErrno(ex.HResult);
        return Win32Code(ex) switch
        {
            32 or 33 or 1224 => "sharing",
            39 or 112 => "diskfull",
            1295 => "quota",
            5 => "access",
            2 or 3 => "notfound",
            206 or 111 => "toolong",
            1314 => "privilege",
            19 => "writeprotect",
            123 or 161 => "badname",
            225 or 226 => "blocked",
            1260 or 4551 => "policy",
            21 or 55 or 1167 => "device",
            53 or 59 or 64 or 67 or 121 or 1222 or 1231 or 1232 => "offline",
            483 or 1117 => "hardware",
            362 or 389 or 395 or 396 or 397 => "cloud",
            50 => "unsupported",
            80 or 183 => "exists",
            6000 => "encryption",
            17 => "crossdevice",
            _ => "io",
        };
    }

    /// <summary>A Linux or macOS error number (errno), in the same classes as Windows' codes.</summary>
    public static string ClassifyErrno(int errno)
    {
        bool mac = OperatingSystem.IsMacOS();
        switch (errno)
        {
            case 1 or 13: return "access"; // EPERM, EACCES
            case 2: return "notfound"; // ENOENT
            case 5: return "hardware"; // EIO
            case 6 or 19: return "device"; // ENXIO, ENODEV
            case 16 or 26: return "sharing"; // EBUSY, ETXTBSY
            case 17: return "exists"; // EEXIST
            case 18: return "crossdevice"; // EXDEV
            case 28: return "diskfull"; // ENOSPC
            case 30: return "writeprotect"; // EROFS
        }
        if (mac)
            return errno switch
            {
                63 => "toolong", // ENAMETOOLONG
                69 => "quota", // EDQUOT
                45 or 102 => "unsupported", // ENOTSUP, EOPNOTSUPP
                50 or 51 or 54 or 57 or 60 or 64 or 65 or 70 => "offline", // ENETDOWN … ESTALE
                _ => "io",
            };
        return errno switch
        {
            36 => "toolong", // ENAMETOOLONG
            122 => "quota", // EDQUOT
            95 => "unsupported", // EOPNOTSUPP
            100 or 101 or 104 or 107 or 110 or 112 or 113 or 116 => "offline", // ENETDOWN … ESTALE
            _ => "io",
        };
    }

    public static string Describe(Exception ex) => Classify(ex) switch
    {
        "sharing" => "The item is in use by another program or window (for example an open viewer or editor, or an antivirus scan).",
        "diskfull" => "There is not enough free space on the destination.",
        "quota" => "Your disk quota on the destination is used up.",
        "access" => "Access is denied. If the destination is a protected folder, Windows Controlled Folder Access may be blocking FileCat.",
        "notfound" => "The item no longer exists or its folder was removed.",
        "toolong" => "The name or path is too long for the destination.",
        "privilege" => "A required privilege is not held (creating symbolic links needs Developer Mode or administrator rights).",
        "writeprotect" => "The destination is write-protected.",
        "badname" => "The name is not valid on the destination (reserved names or characters).",
        "blocked" => "Windows Security blocked this file because it contains a threat or potentially unwanted software.",
        "policy" => "A system policy (for example Smart App Control or an administrator rule) blocks this file.",
        "device" => "The device is not ready or was disconnected (removable media ejected?).",
        "offline" => "The network location is no longer reachable; the connection was lost or the server is offline.",
        "hardware" => "The device reported a hardware or I/O error; the medium may be damaged.",
        "cloud" => "The cloud storage provider (for example OneDrive) is unavailable, so this online-only file cannot be read.",
        "unsupported" => "The destination does not support this operation.",
        "exists" => "An item with this name appeared at the destination meanwhile.",
        "encryption" => "The file is encrypted (EFS) and the destination cannot keep it encrypted.",
        "crossdevice" => "The destination is on another volume, so the item cannot simply be renamed there.",
        _ => ex.Message,
    };
}

/// <summary>
/// File-system copy and move (plan §9.2): every file is written to a unique staged name and then published,
/// so an existing destination is never truncated before its replacement is ready. Cross-volume moves delete a
/// source only after the copy is published and the source is revalidated; directories are removed only when
/// empty, never recursively.
/// </summary>
internal sealed class TransferExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal), IHonorsTransferFilter, IHonorsReadBackVerification
{
    private static readonly EnumerationOptions ChildOptions = new() { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false };
    private readonly HashSet<string> _stagingDirs = new(PathUtil.SafetyComparer);
    private readonly Dictionary<string, VolumeInfo> _volumes = new(PathUtil.SafetyComparer);
    private ReviewedSourceTree? _sourceTree;
    private int _stagedCounter;
    private readonly object _totalsLock = new();
    private long _discoveredItems, _discoveredBytes, _discoveredVerify;
    private long _admittedItems, _admittedBytes, _admittedVerify;
    private bool _discoveryComplete, _discoveryReliable = true;
    private bool Move => Job.Kind == JobKind.Move;

    private void TransferIssue(IssueSeverity severity, string source, string target, string message,
        StepOutcome outcome, string effect, string sourceDisposition, string targetDisposition, string certainty)
    {
        Job.AddIssue(new JobIssue(severity, source, message, outcome)
        {
            SourcePath = source,
            TargetPath = target,
            SourceIsLocal = true,
            TargetIsLocal = true,
            RequestedAction = Move ? "Move" : "Copy",
            CommittedEffect = effect,
            SourceDisposition = sourceDisposition,
            TargetDisposition = targetDisposition,
            Certainty = certainty,
        });
    }

    private TransferOptions Options => Job.Request.Options;

    private enum Result { Committed, Skipped, Failed, Filtered }

    public override void Execute()
    {
        var destDir = Job.Request.Destination!.Path;
        // A queued recovery may outlive the UI review. Refuse the whole request before destination creation or
        // discovery when any reviewed root changed or became unavailable.
        if (Job.Request.ExpectedSources is not null || Job.Request.ExpectedSourceTrees is not null || Job.Request.RequireResultSourceMetadata)
            foreach (var source in Job.Request.Sources)
                if (!SourceStillReviewed(source)) return;
        using var discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(Job.Token);
        var discovery = Task.Run(() => Discover(discoveryCts.Token), discoveryCts.Token);
        try
        {
            if (!Directory.Exists(destDir))
            {
                if (!TryIo(destDir, "create the destination folder", () => Directory.CreateDirectory(destDir))) return;
                Journal.Note("created destination " + destDir);
            }
            var sources = Job.Request.Sources;
            for (int i = 0; i < sources.Count; i++)
            {
                Job.Checkpoint();
                var result = ProcessRoot(sources[i], destDir);
                if (result is Result.Committed) Job.RootCompleted(i);
                else if (result is Result.Failed or Result.Skipped) Job.RootFailed(i);
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
            if (info is null) { _discoveryReliable = false; continue; }
            if (!info.IsDirectory || info.IsLink)
            {
                CountTransferItem(info.IsLink ? 0 : Math.Max(0, info.Size), VerifyWork(info.IsLink ? 0 : info.Size), discovered: true);
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
                        {
                            long size = fi is FileInfo f && !link ? f.Length : 0;
                            CountTransferItem(size, VerifyWork(size), discovered: true);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _discoveryReliable = false; }
            }
        }
        lock (_totalsLock)
        {
            _discoveryComplete = !ct.IsCancellationRequested;
            SetTotalsFinal();
        }
    }

    private void CountTransferItem(long bytes, long verify, bool discovered, bool countItem = true)
    {
        // Discovery is an estimate on a separate worker. It may miss a moved file or stop before the transfer
        // ends. Actual admission independently supplies a lower bound; late discovery must not count it twice.
        // Keep aggregate counters rather than retaining another path entry for every file in a large tree.
        lock (_totalsLock)
        {
            long itemsBefore = Math.Max(_discoveredItems, _admittedItems);
            long bytesBefore = Math.Max(_discoveredBytes, _admittedBytes);
            long verifyBefore = Math.Max(_discoveredVerify, _admittedVerify);
            if (discovered) { _discoveredItems++; _discoveredBytes += bytes; _discoveredVerify += verify; }
            else { if (countItem) _admittedItems++; _admittedBytes += bytes; _admittedVerify += verify; }
            Job.AddTotals(Math.Max(_discoveredItems, _admittedItems) - itemsBefore,
                Math.Max(_discoveredBytes, _admittedBytes) - bytesBefore);
            Job.AddVerifyTotal(Math.Max(_discoveredVerify, _admittedVerify) - verifyBefore);
            SetTotalsFinal();
        }
    }

    private void SetTotalsFinal() => Job.TotalsFinal = _discoveryComplete && _discoveryReliable &&
        _discoveredItems >= _admittedItems && _discoveredBytes >= _admittedBytes && _discoveredVerify >= _admittedVerify;

    private string TargetDirectoryFor(ItemRef root, string destDir)
    {
        if (Options.Flatten || root.RelativeFolder is not { Length: > 0 } rel) return destDir;
        var dir = RelativeFolders.Resolve(destDir, rel);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private Result ProcessRoot(ItemRef root, string destDir)
    {
        var src = root.FileSystemPath!;
        if (!SourceStillReviewed(root)) return Result.Failed;
        _sourceTree = Job.Request.ExpectedSourceTrees is { } trees && trees.TryGetValue(root, out var reviewed) ? reviewed : null;
        var info = Fs.TryGetInfo(src);
        if (info is null)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, "The item no longer exists; nothing was copied.", StepOutcome.Failed);
            return Result.Failed;
        }
        var name = Job.Request.Sources.Count == 1 && !string.IsNullOrEmpty(Job.Request.NewName) ? Job.Request.NewName! : root.Name;
        string targetDir;
        try { targetDir = TargetDirectoryFor(root, destDir); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, ex.Message, StepOutcome.Failed);
            return Result.Failed;
        }
        var dst = Path.Combine(targetDir, name);
        if (info.IsDirectory && !info.IsLink && InsideItself(dst, src))
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, "A folder cannot be copied or moved into itself (the destination leads into it, perhaps through a link).", StepOutcome.Failed);
            return Result.Failed;
        }
        string srcText = PathUtil.NormalizeForCompare(src), dstText = PathUtil.NormalizeForCompare(dst);
        // The same text, or (other than in letter case, which is a rename) the same item reached through a link.
        if (Move && (string.Equals(srcText, dstText, StringComparison.Ordinal) ||
                     !string.Equals(srcText, dstText, StringComparison.OrdinalIgnoreCase) && SameItem(src, dst)))
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Info, src, "Source and destination are the same item; nothing to move.", StepOutcome.Skipped);
            return Result.Skipped;
        }
        // Renaming an entire folder would also move files excluded by the requested filter. The
        // existing copy-then-delete traversal leaves excluded children (and their source folders) intact.
        if (Move && SameVolume(src, dst) && (Options.Filter is null || !info.IsDirectory || info.IsLink))
            return MoveByRename(src, dst, info);
        // A replacement planned by a comparison happens only while the target is as compared: one edited while the plan
        // was reviewed is not overwritten (release plan DPI P10).
        (long Size, long ModifiedTicks)? compared = Job.Request.ExpectedTargets is { } expected && expected.TryGetValue(root, out var seen) ? seen : null;
        if (compared is { } baseline && !TargetStillCompared(dst, baseline))
        {
            Job.ItemSkipped();
            return Result.Skipped;
        }
        return info.IsDirectory && !info.IsLink ? CopyDirectory(src, dst, info) : CopyFileItem(src, dst, info, expectedTarget: compared);
    }

    private bool TargetStillCompared(string path, (long Size, long ModifiedTicks) expected)
    {
        try
        {
            if (Fs.TryGetInfo(path) is { IsDirectory: false, IsLink: false } current &&
                current.Size == expected.Size && current.ModifiedUtc.Ticks == expected.ModifiedTicks) return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Issue(IssueSeverity.Warning, path, "Not replaced: the file here changed after the comparison that planned this, so it stays. Compare again to decide about it.", StepOutcome.CanceledBeforeChange);
        return false;
    }

    private bool SourceStillReviewed(ItemRef source)
    {
        if (Job.Request.ExpectedSources is null && Job.Request.ExpectedSourceTrees is null && !Job.Request.RequireResultSourceMetadata) return true;
        var path = source.FileSystemPath!;
        try
        {
            bool pathMatches = (!Job.Request.RequireResultSourceMetadata || SourcePathReview.MatchesSelectionMetadata(source, Fs)) &&
                (Job.Request.ExpectedSources is not { } expected ||
                expected.TryGetValue(source, out var review) && review.Matches(path, Fs, Job.Checkpoint));
            bool treeMatches = pathMatches && (Job.Request.ExpectedSourceTrees is not { } trees ||
                trees.TryGetValue(source, out var reviewed) && reviewed.Tree.Matches(Fs, reviewed.Provider, Job.Checkpoint));
            if (pathMatches && treeMatches) return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
        Job.ItemFailed();
        Issue(IssueSeverity.Error, path, "Not continued: a source changed or could not be checked after review. Select the sources again to decide what to copy or move.", StepOutcome.CanceledBeforeChange);
        return false;
    }

    private bool SourceItemStillReviewed(string path)
    {
        if (_sourceTree is null) return true;
        try
        {
            if (_sourceTree.Tree.MatchesItem(path, Fs, _sourceTree.Provider, Job.Checkpoint)) return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
        RefuseChangedSource(path);
        return false;
    }

    private void RefuseChangedSource(string path)
    {
        Job.ItemFailed();
        Issue(IssueSeverity.Error, path, "Not continued: a source changed or could not be checked after review. Select the sources again to decide what to copy or move.", StepOutcome.CanceledBeforeChange);
    }

    private bool SourceRenameStillReviewed(string path, FileSystemItemInfo info)
    {
        if (_sourceTree is null) return true;
        if (!info.IsDirectory || info.IsLink) return SourceItemStillReviewed(path);
        try
        {
            if (_sourceTree.Tree.MatchesSubtree(path, Fs, _sourceTree.Provider, Job.Checkpoint)) return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
        RefuseChangedSource(path);
        return false;
    }

    private bool CopiedItemStillReviewed(string source, string written)
    {
        if (_sourceTree?.Tree.FindItem(source)?.Content is not { } approved) return true;
        var copy = JournalRecovery.ReviewStagedFile(written, new LocalFileSystemProvider(), Job.Checkpoint, approved.Length);
        if (copy is not null && copy.Length == approved.Length && copy.Sha256 == approved.Sha256) return true;
        TryDeleteStaged(written);
        Job.ItemFailed();
        Issue(IssueSeverity.Error, source, "Not continued: the copied bytes do not match the reviewed source.", StepOutcome.Failed);
        return false;
    }

    private VolumeInfo Volume(string path)
    {
        var root = VolumeRootOf(path);
        if (!_volumes.TryGetValue(root, out var v))
        {
            v = Fs.GetVolumeInfo(root);
            _volumes[root] = v;
        }
        return v;
    }

    private readonly Dictionary<string, string> _volumeRoots = new(PathUtil.SafetyComparer);

    /// <summary>Volume root of an item's folder, cached per folder (a mounted folder is its own volume).</summary>
    private string VolumeRootOf(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? path;
        if (!_volumeRoots.TryGetValue(dir, out var root))
        {
            root = Fs.GetVolumeRoot(dir);
            _volumeRoots[dir] = root;
        }
        return root;
    }

    private bool SameVolume(string a, string b) => string.Equals(VolumeRootOf(a), VolumeRootOf(b), PathUtil.SafetyComparison);

    /// <summary>
    /// Whether two paths name the same item: the same text, or the same file or folder reached another way (a junction
    /// or symbolic link on the way, a mapped drive, a hard link). Replacing an item with itself, or deleting a moved
    /// source that is its own destination, would lose it.
    /// </summary>
    private bool SameItem(string a, string b) =>
        string.Equals(PathUtil.NormalizeForCompare(a), PathUtil.NormalizeForCompare(b), PathUtil.SafetyComparison) ||
        Fs.GetFileIdentity(a) is { } x && x == Fs.GetFileIdentity(b);

    /// <summary>
    /// Whether <paramref name="dst"/> lies in the folder <paramref name="src"/>, also when a link on the way leads there:
    /// copying a folder into itself never ends, and moving it would delete what was copied. Where the destination really
    /// is shows in its deepest folder that exists, with every link resolved; the rest of it is plain names.
    /// </summary>
    private bool InsideItself(string dst, string src)
    {
        if (PathUtil.IsSameOrUnder(dst, src)) return true;
        if (Fs.GetFinalPath(src) is not { } source) return false;
        for (var at = dst; !string.IsNullOrEmpty(at); at = Path.GetDirectoryName(at))
            if (Fs.GetFinalPath(at) is { } real) return PathUtil.IsSameOrUnder(real, source);
        return false;
    }

    // ---- Same-volume move -----------------------------------------------------------------------------

    private Result MoveByRename(string src, string dst, FileSystemItemInfo info)
    {
        Job.Checkpoint();
        if (!SourceRenameStillReviewed(src, info)) return Result.Failed;
        Job.SetCurrent(src);
        var existing = Fs.TryGetInfo(dst);
        FileTreeReview? targetReview = null;
        QuestionReview? atQuestion = null;
        bool replace = false;
        var target = dst;
        if (existing is not null)
        {
            bool caseOnly = string.Equals(src, dst, StringComparison.OrdinalIgnoreCase) && !string.Equals(src, dst, StringComparison.Ordinal);
            if (!caseOnly)
            {
                if (info.IsDirectory && !info.IsLink && existing.IsDirectory && !existing.IsLink)
                    return MergeMoveDirectory(src, dst);
                var d = ResolveConflict(src, dst, info, existing, out atQuestion, replaceable: !existing.IsDirectory && !existing.IsLink && Fs.CanReplaceReviewed(dst));
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
        // A conflict answer can arrive long after the root was admitted.
        if (!SourceRenameStillReviewed(src, info)) return Result.Failed;
        if (replace && (targetReview = ReviewForReplacement(dst, existing!, atQuestion)) is null)
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Warning, target, ChangedSinceQuestion, StepOutcome.CanceledBeforeChange);
            return Result.Skipped;
        }
        // A rename to a new name loses nothing if interrupted (the item is at one of the two names): group-committed.
        int step = Journal.Intent(replace ? "move-replace" : "move", src, target, null, durable: replace);
        string? retainedTargetPath = null;
        if (replace)
        {
            BoundReplacement? bound = null;
            // Held open by another program: asked as in use, and Retry tries the same replacement again.
            if (!TryIo(target, "replace the existing item", () =>
                {
                    bound = ReplaceReviewed(src, target, targetReview, step);
                    if (bound.InUse is { } busy) throw busy;
                }))
            {
                Journal.Done(step, StepOutcome.CanceledBeforeChange);
                Job.ItemFailed();
                return Result.Failed;
            }
            Journal.Done(step, bound!.Published ? StepOutcome.Committed : Unpublished(bound));
            if (!bound.Published)
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Warning, target, "Not replaced: " + (bound.Reason ?? "the reviewed destination changed."), Unpublished(bound));
                if (bound.RetainedPath is not null)
                    Issue(IssueSeverity.Warning, bound.RetainedPath, "The original destination was kept at this recovery name.", StepOutcome.PartiallyApplied);
                return Result.Failed;
            }
            retainedTargetPath = bound.RetainedPath;
            if (retainedTargetPath is not null)
                Issue(IssueSeverity.Warning, retainedTargetPath, bound.Reason ?? "The replaced file remains at this recovery name.", StepOutcome.PartiallyApplied);
            goto Moved;
        }
        bool ok;
        try
        {
            Fs.Move(src, target, replaceExisting: false);
            ok = true;
        }
        catch (IOException ex) when (ErrorText.Classify(ex) == "crossdevice")
        {
            // Another volume behind the same drive letter (a mounted folder): use the guarded copy-then-delete path.
            Journal.Done(step, StepOutcome.CanceledBeforeChange);
            return info.IsDirectory && !info.IsLink ? CopyDirectory(src, target, info) : CopyFileItem(src, target, info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bool refused = false;
            ok = TryIo(src, "move the item", () =>
            {
                if (!SourceRenameStillReviewed(src, info)) { refused = true; return; }
                Fs.Move(src, target, replaceExisting: false);
            }) && !refused;
            if (refused)
            {
                Journal.Done(step, StepOutcome.CanceledBeforeChange);
                return Result.Failed;
            }
        }
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Skipped);
        if (!ok)
        {
            Job.ItemFailed();
            return Result.Failed;
        }
    Moved:
        var moved = Fs.TryGetInfo(target);
        Job.AddUndo(new UndoStep(UndoKind.MoveBack, target, src, moved?.Size ?? info.Size, (moved?.ModifiedUtc ?? info.ModifiedUtc).Ticks));
        if (replace) Job.RecordUndoRemainder(target, retainedTargetPath is null
            ? "The overwritten destination contents were not retained. Moving the incoming item back will not restore them."
            : $"The replaced destination remains at {retainedTargetPath}; moving the incoming item back will not restore it automatically.");
        Job.AddBytes(Math.Max(0, info.Size));
        Job.ItemDone();
        return Result.Committed;
    }

    private Result MergeMoveDirectory(string src, string dst)
    {
        bool all = true;
        foreach (var ci in SafeChildren(src))
        {
            Job.Checkpoint();
            var r = MoveByRename(ci.Path, Path.Combine(dst, Path.GetFileName(ci.Path)), ci);
            all &= r == Result.Committed;
        }
        if (all) RemoveIfEmpty(src);
        return all ? Result.Committed : Result.Failed;
    }

    // ---- Copy -----------------------------------------------------------------------------------------

    /// <param name="ensureParent">
    /// With a filter, folders are created only when something inside matches: a folder then asks its parent to exist first,
    /// so every folder is made (and later given its times and permissions) by its own step, whatever order the file
    /// system lists entries in.
    /// </param>
    private Result CopyDirectory(string src, string dst, FileSystemItemInfo info, Func<bool>? ensureParent = null, int depth = 0)
    {
        Job.Checkpoint();
        if (depth >= MaxRecursiveTreeDepth)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, $"The folder tree exceeds the {MaxRecursiveTreeDepth:N0}-level copy limit; the remaining source is kept.", StepOutcome.Failed);
            return Result.Failed;
        }
        if (!SourceItemStillReviewed(src)) return Result.Failed;
        Job.SetCurrent(src);
        var existing = Fs.TryGetInfo(dst);
        var target = dst;
        if (existing is not null && (!existing.IsDirectory || existing.IsLink))
        {
            // Merging into a folder link would write wherever it points: only skipping or keeping both are offered.
            var d = ResolveConflict(src, dst, info, existing, out _, replaceable: false);
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
            if (ensureParent is not null && !ensureParent()) return false;
            if (!TryIo(target, "create a folder", () => Fs.CreateDirectory(target))) return false;
            created = true;
            UnixPermissions.TryCopyFolderMode(src, target, final: false); // no wider than the source while it fills
            return true;
        }
        // Without a filter, empty folders are recreated; with a filter only folders holding matches are.
        if (Options.Filter is null && !EnsureCreated()) return Result.Failed;
        bool allOk = true;
        bool anyTransferred = false;
        var children = SafeChildren(src);
        if (_sourceTree is not null && !_sourceTree.Tree.MatchesChildren(src, children))
        {
            RefuseChangedSource(src);
            return Result.Failed;
        }
        foreach (var ci in children)
        {
            Job.Checkpoint();
            var child = ci.Path;
            var childDst = Path.Combine(target, Path.GetFileName(child));
            Result r;
            if (ci.IsDirectory && !ci.IsLink)
            {
                r = CopyDirectory(child, childDst, ci, EnsureCreated, depth + 1);
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
            if (created) UnixPermissions.TryCopyFolderMode(src, target, final: true);
            if (Options.PreserveTimestamps) TrySetTimes(target, info);
            if (created && Options.PreserveAttributes) TrySetAttributes(target, info.Attributes);
        }
        if (Move && allOk) RemoveIfEmpty(src);
        return allOk ? Result.Committed : anyTransferred ? Result.Failed : Result.Failed;
    }

    private readonly HashSet<string> _enumerationFailed = new(PathUtil.SafetyComparer);

    /// <summary>Children with the attributes, sizes, and times the enumeration already returned (no stat per item).</summary>
    private List<FileSystemItemInfo> SafeChildren(string dir)
    {
        List<FileSystemItemInfo> children;
        try
        {
            children = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", ChildOptions).Select(fi => PortableFileOperations.FromInfo(fi)).ToList();
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
            // Removing an emptied folder loses nothing if interrupted: group-committed.
            int step = Journal.Intent("delete-empty-source-dir", dir, null, null, durable: false);
            Fs.DeleteDirectory(dir);
            Journal.Done(step, StepOutcome.Committed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Issue(IssueSeverity.Warning, dir, "Everything was moved, but the empty source folder could not be removed: " + ErrorText.Describe(ex), StepOutcome.PartiallyApplied);
        }
    }

    /// <summary>
    /// New files below this size are copied straight to their final name (plan §9.2 stages for replacement; a staged
    /// rename per small file doubled copy time). Larger files and every replacement are staged and then published.
    /// </summary>
    internal const long DirectCopyLimit = 1024 * 1024;

    /// <summary>
    /// Source and destination folder pairs already journaled as direct-copy targets. A destination fed from several
    /// source folders (a result set, a working set) gets a record per source folder, so recovery compares each file
    /// with the folder it came from and never with an unrelated namesake (release issue I19).
    /// </summary>
    private readonly HashSet<string> _fillPairs = new(PathUtil.SafetyComparer);

    private void RecordFill(string src, string destinationDirectory)
    {
        var sourceDirectory = Path.GetDirectoryName(src)!;
        if (_fillPairs.Add(sourceDirectory + "\0" + destinationDirectory)) Journal.Fill(sourceDirectory, destinationDirectory);
    }

    private Result CopyFileItem(string src, string dst, FileSystemItemInfo info, bool firstAttempt = true, (long Size, long ModifiedTicks)? expectedTarget = null, bool followLink = false)
    {
        Job.Checkpoint();
        if (!SourceItemStillReviewed(src)) return Result.Failed;
        Job.SetCurrent(src);
        long planned = info.IsLink ? 0 : Math.Max(0, info.Size);
        // Following an admitted link adds its contents, not a second item.
        if (firstAttempt) CountTransferItem(planned, VerifyWork(planned), discovered: false, countItem: !followLink);
        Job.BeginItem(planned, VerifyWork(planned));
        if (followLink && expectedTarget is { } followedBaseline && !TargetStillCompared(dst, followedBaseline))
        {
            Job.ItemSkipped();
            return Result.Skipped;
        }
        if (firstAttempt && expectedTarget is null && TryFastDirectCopy(src, dst, info, followLink) is { } fast) return fast;
        var target = dst;
        bool replace = false;
        var existing = Fs.TryGetInfo(dst);
        FileTreeReview? targetReview = null;
        QuestionReview? atQuestion = null;
        if (existing is not null)
        {
            // A real folder is never replaced by a file or a link: that question offers skipping or keeping both.
            // Nor is anything this file system cannot replace bound to its review (a link, or on Linux, macOS or a
            // network share, by the owner's policy): offered keeping both instead, before anything is copied.
            var d = ResolveConflict(src, dst, info, existing, out atQuestion, replaceable: !existing.IsDirectory && !existing.IsLink && Fs.CanReplaceReviewed(dst));
            switch (d)
            {
                case DecisionAction.Skip:
                    Job.ItemSkipped();
                    Job.AddBytes(Math.Max(0, info.Size));
                    TransferIssue(IssueSeverity.Info, src, dst, "Skipped: the destination already has this name.",
                        StepOutcome.Skipped, "No transfer attempted for this item", "Original kept",
                        "Existing destination kept", "Confirmed before transfer");
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

        if (replace && (targetReview = ReviewForReplacement(dst, existing!, atQuestion)) is null)
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Warning, dst, ChangedSinceQuestion, StepOutcome.CanceledBeforeChange);
            return Result.Skipped;
        }

        if (info.IsLink) return CopyLink(src, target, info, replace, target == dst ? expectedTarget : null, targetReview);

        // PI-05: a move that would drop metadata the destination cannot store asks before anything irreversible.
        bool keepSource = false;
        if (Move && PredictMetadataLoss(src, target) is { } loss)
        {
            var d = Job.Ask(new ConfirmRequest("Moving would lose file metadata",
                $"\"{Path.GetFileName(src)}\": {loss}. A move deletes the original afterwards, so they would be gone.", src, "metadata-loss",
                [DecisionAction.Proceed, DecisionAction.KeepSource, DecisionAction.Skip, DecisionAction.CancelJob]) { ProceedLabel = "Move anyway" });
            switch (d.Action)
            {
                case DecisionAction.Proceed:
                    break;
                case DecisionAction.KeepSource:
                    keepSource = true;
                    break;
                case DecisionAction.Skip:
                    Job.ItemSkipped();
                    TransferIssue(IssueSeverity.Info, src, target, "Skipped: moving it would have lost metadata the destination cannot store.",
                        StepOutcome.Skipped, "No transfer attempted for this item", "Original kept",
                        "Destination unchanged by this item", "Confirmed before transfer");
                    return Result.Skipped;
                default:
                    throw new OperationCanceledException();
            }
        }

        // Where the bytes go: straight to the new name (small, new, and named like its source, so recovery can match
        // it) or to a staged name that is published afterwards.
        var dir = Path.GetDirectoryName(target)!;
        bool direct = expectedTarget is null && !replace && info.Size is >= 0 and < DirectCopyLimit &&
                      string.Equals(Path.GetFileName(target), Path.GetFileName(src), StringComparison.Ordinal);
        string writeTo;
        if (direct)
        {
            RecordFill(src, dir);
            writeTo = target;
        }
        else
        {
            if (_stagingDirs.Add(dir)) Journal.StagingDirectory(dir);
            writeTo = Path.Combine(dir, $"{JournalRecovery.StagedPrefix}{Job.ShortId}-{Interlocked.Increment(ref _stagedCounter)}.tmp");
        }
        bool copied = false;
        bool allowDecrypted = false;
        int quietRetries = 0;
        while (!copied)
        {
            long reported = 0;
            var pace = new TransferPace();
            try
            {
                var options = new FileCopyOptions
                {
                    CopyLinkAsLink = !followLink,
                    NoBuffering = info.Size > 256L * 1024 * 1024,
                    DisablePreallocation = direct,
                    AllowDecryptedDestination = allowDecrypted,
                    // A move deletes the source next: the copy must be on the device first, not in the write cache.
                    FlushDestination = Move,
                };
                Fs.CopyFile(src, writeTo, options, (done, total) =>
                {
                    long advanced = done - reported;
                    Job.AddBytes(advanced);
                    Job.AddDataProcessed(advanced);
                    reported = done;
                    if (Job.IsPaused) Job.Checkpoint();
                    Job.Throttle(done, pace);
                    return Job.IsCancellationRequested ? CopyProgressAction.Cancel : CopyProgressAction.Continue;
                }, Job.Token);
                copied = true;
            }
            catch (OperationCanceledException)
            {
                // Only the copy engine knows whether it created the destination; it owns failed-copy cleanup.
                Job.AddBytes(-reported);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Job.AddBytes(-reported);
                if (Job.IsCancellationRequested) throw new OperationCanceledException();
                var cls = ErrorText.Classify(ex);
                if (cls == "sharing" && quietRetries < 3)
                {
                    // Antivirus scans and indexers hold new files briefly: retry quietly before asking.
                    quietRetries++;
                    Job.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250 * quietRetries));
                    continue;
                }
                if (cls == "exists" && direct && firstAttempt)
                {
                    // Another program created the name meanwhile: treat it as the conflict it is (once; a name that
                    // exists but cannot be inspected gets the ordinary question below).
                    return CopyFileItem(src, dst, info, firstAttempt: false, expectedTarget: expectedTarget, followLink: followLink);
                }
                if (cls == "encryption" && !allowDecrypted)
                {
                    var d = Job.Ask(new ConfirmRequest("Encrypted file",
                        $"\"{Path.GetFileName(src)}\" is encrypted with Windows EFS, and the destination cannot keep it encrypted. Copy it decrypted?",
                        src, "decrypt", [DecisionAction.Proceed, DecisionAction.Skip, DecisionAction.CancelJob]) { ProceedLabel = "Copy decrypted" });
                    if (d.Action == DecisionAction.Proceed)
                    {
                        allowDecrypted = true;
                        continue;
                    }
                    if (d.Action != DecisionAction.Skip) throw new OperationCanceledException();
                    Job.ItemSkipped();
                    TransferIssue(IssueSeverity.Warning, src, target, "Skipped: it is encrypted and the destination cannot keep it encrypted.",
                        StepOutcome.Skipped, "No transfer attempted for this item", "Original kept",
                        "Destination unchanged by this item", "Confirmed before transfer");
                    return Result.Skipped;
                }
                var decision = Job.Ask(new ErrorRequest("Could not copy the file", $"{Path.GetFileName(src)}: {ErrorText.Describe(ex)}", src, true, cls));
                if (decision.Action == DecisionAction.Retry) continue;
                if (decision.Action == DecisionAction.Skip)
                {
                    Job.ItemFailed();
                    TransferIssue(IssueSeverity.Error, src, target, "Not copied: " + ErrorText.Describe(ex),
                        StepOutcome.Failed, "Copy failed; no published copy confirmed", "Original kept",
                        "Target state not confirmed; inspect before retrying", "Failure reported by the copy operation");
                    return Result.Failed;
                }
                throw new OperationCanceledException();
            }
        }
        if (allowDecrypted) Issue(IssueSeverity.Warning, src, "Copied decrypted: the destination cannot store EFS encryption.", StepOutcome.Committed);

        // Verification at the selected profile. A copy that fails it is removed: this job created it.
        var writtenInfo = Fs.TryGetInfo(writeTo);
        if (writtenInfo is null || writtenInfo.Size != info.Size && info.Size >= 0)
        {
            TryDeleteStaged(writeTo);
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, "The copy has a different size than the source; it was discarded.", StepOutcome.Failed);
            return Result.Failed;
        }
        if (!CopiedItemStillReviewed(src, writeTo)) return Result.Failed;
        if (Options.Verify == VerifyMode.ReadBack && !ContentEqual(src, writeTo, info.Size))
        {
            TryDeleteStaged(writeTo);
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, "Read-back verification found different content; the copy was discarded.", StepOutcome.Failed);
            return Result.Failed;
        }
        PreserveOrigin(src, writeTo, target);
        if (!Fs.CopyPreservesMetadata && Options.PreserveAttributes) TrySetAttributes(writeTo, info.Attributes & ~FileAttributes.ReadOnly);

        if (!direct)
        {
            // Publish: replacing an existing item is journaled durably first; a new name is group-committed. A move
            // writes the rename through, so the source is never deleted while its copy only exists under a staged
            // name (which recovery would offer to delete).
            int step = Journal.Intent(replace ? "replace" : "publish", src, target, writeTo, durable: replace);
            bool refused = false, retained = false;
            string? refusal = null;
            bool published = TryIo(target, replace ? "replace the existing item" : "publish the copied item", () =>
            {
                // The target may change while copying or while a failed publication waits for Retry.
                // This is a fresh metadata/type check, not an atomic filesystem compare-and-replace.
                if (expectedTarget is { } baseline && target == dst && !TargetStillCompared(target, baseline))
                {
                    refused = true;
                    return;
                }
                if (replace)
                {
                    // It binds the reviewed identity and bytes (or length and time) through its own held handle.
                    var bound = ReplaceReviewed(writeTo, target, targetReview, step);
                    // Held open by another program: asked as in use, and Retry tries the same replacement again.
                    if (bound.InUse is { } busy) throw busy;
                    if (!bound.Published)
                    {
                        refused = true;
                        refusal = bound.Reason;
                        retained = bound.RetainedPath is not null;
                        if (bound.RetainedPath is not null)
                            Issue(IssueSeverity.Warning, bound.RetainedPath,
                                "The original destination remains at this recovery name.", StepOutcome.PartiallyApplied);
                    }
                    else if (bound.RetainedPath is not null)
                        Issue(IssueSeverity.Warning, bound.RetainedPath,
                            bound.Reason ?? "The replaced destination remains at this recovery name.", StepOutcome.PartiallyApplied);
                    return;
                }
                Fs.Move(writeTo, target, replaceExisting: false, writeThrough: Move);
            });
            if (!published || refused)
            {
                TryDeleteStaged(writeTo);
                // Moved aside and unable to return to its name, the original is a partial change, never one canceled
                // before any, and the item failed (review finding W9 of f134ba37).
                var unpublished = retained ? StepOutcome.PartiallyApplied : StepOutcome.CanceledBeforeChange;
                Journal.Done(step, unpublished);
                if (refused)
                    Issue(IssueSeverity.Warning, target, "Not replaced: " + (refusal ?? "the reviewed destination changed."), unpublished);
                if (refused && !retained)
                {
                    Job.ItemSkipped();
                    return Result.Skipped;
                }
                Job.ItemFailed();
                return Result.Failed;
            }
            Journal.Done(step, StepOutcome.Committed);
        }
        if (!Fs.CopyPreservesMetadata && Options.PreserveAttributes && (info.Attributes & FileAttributes.ReadOnly) != 0) TrySetAttributes(target, info.Attributes);
        if (!Move)
        {
            Job.ItemDone();
            return Result.Committed;
        }
        Job.RecordUndoRemainder(target, "This copy-then-delete move route has no recorded inverse; Undo will not remove the published copy or recreate its source.");
        if (replace) Job.RecordUndoRemainder(target, "The overwritten destination contents were not retained.");
        if (keepSource)
        {
            Job.ItemSkipped();
            TransferIssue(IssueSeverity.Info, src, target, "Copied; the original was kept because the destination cannot store all of its metadata.",
                StepOutcome.Skipped, "Copy published; source removal not attempted", "Original kept",
                "Copy published", "Observed at publication; later external changes are possible");
            return Result.Skipped;
        }
        if (followLink)
        {
            // The approved operation copied contents through a link. Keep both the link and its referent: the
            // ordinary-file move checks cannot prove either may be removed after following it.
            Job.ItemSkipped();
            TransferIssue(IssueSeverity.Info, src, target, "Copied the link's contents; the source link and its target were kept.",
                StepOutcome.Skipped, "Link contents copied; source removal not attempted", "Link and referent kept",
                "Copy published", "Observed at publication; later external changes are possible");
            return Result.Skipped;
        }
        return DeleteMovedSource(src, target, info);
    }

    /// <summary>
    /// The common case of a small copy to a free name, without probing the destination first: the engine's
    /// fail-if-exists flag detects a conflict. Any failure returns null, and the careful path (conflict prompts,
    /// retries, explanations) runs from the start. Moves never take this path: they may need the metadata question.
    /// </summary>
    private Result? TryFastDirectCopy(string src, string dst, FileSystemItemInfo info, bool followLink)
    {
        if (Move || info.IsLink || info.Size is < 0 or >= DirectCopyLimit || Options.Verify != VerifyMode.Native) return null;
        if (!string.Equals(Path.GetFileName(dst), Path.GetFileName(src), StringComparison.Ordinal)) return null; // recovery pairs by name
        if (!Fs.CopyPreservesMetadata || !Volume(dst).SupportsNamedStreams) return null;
        RecordFill(src, Path.GetDirectoryName(dst)!);
        long reported = 0;
        var pace = new TransferPace();
        try
        {
            Fs.CopyFile(src, dst, new FileCopyOptions { CopyLinkAsLink = !followLink, DisablePreallocation = true }, (done, total) =>
            {
                long advanced = done - reported;
                Job.AddBytes(advanced);
                Job.AddDataProcessed(advanced);
                reported = done;
                if (Job.IsPaused) Job.Checkpoint();
                Job.Throttle(done, pace);
                return Job.IsCancellationRequested ? CopyProgressAction.Cancel : CopyProgressAction.Continue;
            }, Job.Token);
        }
        catch (OperationCanceledException)
        {
            Job.AddBytes(-reported);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Job.AddBytes(-reported);
            if (Job.IsCancellationRequested) throw new OperationCanceledException();
            return null;
        }
        var written = Fs.TryGetInfo(dst);
        if (written is null || written.Size != info.Size)
        {
            TryDeleteStaged(dst);
            Job.ItemFailed();
            Issue(IssueSeverity.Error, src, "The copy has a different size than the source; it was discarded.", StepOutcome.Failed);
            return Result.Failed;
        }
        if (!CopiedItemStillReviewed(src, dst)) return Result.Failed;
        Job.ItemDone();
        return Result.Committed;
    }

    /// <summary>Metadata a move to <paramref name="target"/> would lose, in plain words, or null (plan §8.1, PI-05).</summary>
    private string? PredictMetadataLoss(string src, string target)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var vol = Volume(target);
        if (vol.SupportsNamedStreams) return null;
        var streams = Fs.GetAlternateStreams(src);
        if (streams.Count == 0) return null;
        static bool IsZone(string s) => s.Equals("Zone.Identifier", StringComparison.OrdinalIgnoreCase);
        var parts = new List<string>();
        if (streams.Any(IsZone)) parts.Add("its download origin (Mark of the Web)");
        var others = streams.Where(s => !IsZone(s)).ToList();
        if (others.Count == 1) parts.Add($"an alternate data stream ({others[0]})");
        else if (others.Count > 1) parts.Add($"{others.Count} alternate data streams ({string.Join(", ", others.Take(3))}{(others.Count > 3 ? ", …" : "")})");
        string holder = Holder(vol);
        return $"{char.ToUpperInvariant(holder[0])}{holder[1..]} cannot store {string.Join(" or ", parts)}";
    }

    /// <summary>
    /// What holds the destination, for messages: its file system, or "the network share" — a server names whatever file
    /// system it likes (Samba says NTFS by default), and that is not what cannot store the metadata (release issue I34).
    /// </summary>
    private static string Holder(VolumeInfo vol) => vol.IsRemote ? "the network share" : vol.FileSystem ?? "the destination";

    private Result DeleteMovedSource(string src, string target, FileSystemItemInfo before)
    {
        // Revalidate: a source that changed while it was copied is kept (plan §9.2).
        var now = Fs.TryGetInfo(src);
        if (now is null)
        {
            TransferIssue(IssueSeverity.Warning, src, target, "The copy was published, but the source had already disappeared.",
                StepOutcome.Uncertain, "Copy published; source disappearance observed", "Not present when checked",
                "Copy published", "Who removed the source and later target state are unknown");
            Job.ItemDone();
            return Result.Committed;
        }
        // The only irreversible step of a move: the published copy must be another file than the source, never the
        // source itself reached through a link.
        if (Fs.GetFileIdentity(src) is { } source && source == Fs.GetFileIdentity(target))
        {
            TransferIssue(IssueSeverity.Warning, src, target, "Not deleted: the destination turned out to be the source itself (reached through a link), so it stays where it is.",
                StepOutcome.PartiallyApplied, "Source deletion refused after publication", "Original kept",
                "Target path aliases the original", "Confirmed by file identity at deletion check");
            Job.ItemDone();
            return Result.Failed;
        }
        if (now.Size != before.Size || now.ModifiedUtc != before.ModifiedUtc)
        {
            TransferIssue(IssueSeverity.Warning, src, target, "Copied, but the source changed during the copy, so it was not deleted. Both versions now exist.",
                StepOutcome.PartiallyApplied, "Copy published; source deletion refused", "Changed original kept",
                "Copy published", "Both paths were observed at the deletion check");
            Job.ItemDone();
            return Result.Failed;
        }
        // The copy must still be there, whole, when its source goes: an antivirus quarantine or a sync client can take a
        // new file away at once, and the source would then be the only copy (release plan DPI P01).
        if (Fs.TryGetInfo(target) is not { IsDirectory: false } copy || copy.Size != now.Size)
        {
            TransferIssue(IssueSeverity.Warning, src, target, "Not deleted: the copy is no longer at the destination as it was written (another program may have moved or removed it), so the source stays.",
                StepOutcome.PartiallyApplied, "Publication occurred; source deletion refused", "Original kept",
                "Published copy no longer matched", "Target state observed at the deletion check");
            Job.ItemDone();
            return Result.Failed;
        }
        // Size and time can be preserved by a writer. Before the irreversible step, read both complete files again,
        // including when read-back verification already passed before publication. This is still a path check, not an
        // atomic native-handle guarantee: later changes and aliases require separate native qualification.
        bool matching = false;
        try
        {
            if (!now.IsDirectory && !now.IsLink && (now.Attributes & FileAttributes.ReparsePoint) == 0 &&
                !copy.IsLink && (copy.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                Job.Checkpoint();
                long planned = 2 * Math.Max(0, now.Size), started = Job.VerifyBytesDone;
                Job.AddVerifyTotal(planned, currentItem: true);
                try { matching = ContentEqual(src, target, now.Size); }
                finally
                {
                    // Refused reads are no longer pending work; only bytes actually read belong to this added check.
                    Job.AddVerifyTotal(Job.VerifyBytesDone - started - planned, currentItem: true);
                }
                var after = Fs.TryGetInfo(src); var copyAfter = Fs.TryGetInfo(target);
                matching &= after is { IsDirectory: false, IsLink: false } && after.Size == now.Size && after.ModifiedUtc == now.ModifiedUtc &&
                    (after.Attributes & FileAttributes.ReparsePoint) == 0 && copyAfter is { IsDirectory: false, IsLink: false } &&
                    copyAfter.Size == copy.Size && copyAfter.ModifiedUtc == copy.ModifiedUtc && (copyAfter.Attributes & FileAttributes.ReparsePoint) == 0;
                if (after is not null) now = after;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
        if (!matching)
        {
            TransferIssue(IssueSeverity.Warning, src, target, "Not deleted: the source and published copy changed or their complete bytes could not be checked. The source stays.",
                StepOutcome.PartiallyApplied, "Copy published; source deletion refused", "Original kept",
                "Published copy may remain", "Complete equality could not be established");
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
        if (!ok) TransferIssue(IssueSeverity.Warning, src, target, "The item was copied, but the source could not be deleted; it still exists in both places.",
            StepOutcome.PartiallyApplied, "Copy published; source deletion failed", "Original kept",
            "Copy published", "Observed after the failed deletion attempt");
        Job.ItemDone();
        return ok ? Result.Committed : Result.Failed;
    }

    private Result CopyLink(string src, string target, FileSystemItemInfo info, bool replace, (long Size, long ModifiedTicks)? expectedTarget,
        FileTreeReview? targetReview)
    {
        // A replaced item goes only once its replacement exists: the new link is made under a staged name first.
        string linkAt = target;
        bool staged = replace || expectedTarget is not null;
        if (staged)
        {
            var dir = Path.GetDirectoryName(target)!;
            if (_stagingDirs.Add(dir)) Journal.StagingDirectory(dir);
            linkAt = Path.Combine(dir, $"{JournalRecovery.StagedPrefix}{Job.ShortId}-{Interlocked.Increment(ref _stagedCounter)}.tmp");
        }
        if (Fs.TryCopyLink(src, linkAt, info.IsDirectory, out var error))
        {
            if (staged && PublishLink(src, linkAt, target, info.IsDirectory, expectedTarget, targetReview) is { } result && result != Result.Committed) return result;
            if (Move)
            {
                Job.RecordUndoRemainder(target, "This copied link has no recorded inverse; Undo will not remove it or recreate its source link.");
                if (replace) Job.RecordUndoRemainder(target, "The replaced destination entry was not retained.");
                DeleteLinkSource(src, info);
            }
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
                if (info.IsDirectory)
                {
                    var directoryInfo = info with { IsLink = false, Attributes = info.Attributes & ~FileAttributes.ReparsePoint };
                    return CopyDirectory(src, target, directoryInfo);
                }
                // Link metadata describes the link itself. The selected fallback copies the referent's bytes,
                // while keeping the original source path so a move never deletes the resolved referent.
                try
                {
                    string? resolved = Fs.GetFinalPath(src) ?? new FileInfo(src).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                    if (resolved is null || Fs.TryGetInfo(resolved) is not { IsDirectory: false, IsLink: false } targetInfo)
                        throw new IOException("The link's target could not be read as an ordinary file.");
                    return CopyFileItem(src, target, targetInfo with { Path = src }, expectedTarget: expectedTarget, followLink: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    Job.ItemFailed();
                    Issue(IssueSeverity.Error, src, "The link's contents were not copied: " + ErrorText.Describe(ex), StepOutcome.Failed);
                    return Result.Failed;
                }
            case DecisionAction.CreateJunction when info.LinkTarget is not null:
                if (TryIo(linkAt, "create a junction", () => Junctions.Create(linkAt, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(src)!, info.LinkTarget)))))
                {
                    if (staged && PublishLink(src, linkAt, target, isDirectory: true, expectedTarget, targetReview) is { } result && result != Result.Committed) return result;
                    if (Move)
                    {
                        Job.RecordUndoRemainder(target, "This created junction has no recorded inverse; Undo will not remove it or recreate its source link.");
                        if (replace) Job.RecordUndoRemainder(target, "The replaced destination entry was not retained.");
                        DeleteLinkSource(src, info);
                    }
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

    /// <summary>Puts a staged link in the place of the item it replaces; if that fails, the staged link goes and the item stays.</summary>
    private Result PublishLink(string src, string staged, string target, bool isDirectory, (long Size, long ModifiedTicks)? expectedTarget,
        FileTreeReview? targetReview)
    {
        int step = Journal.Intent("replace", src, target, staged);
        bool refused = false, retained = false;
        bool ok = TryIo(target, "replace the existing item", () =>
        {
            // Link creation and a publication retry may outlive the comparison's initial target check.
            if (expectedTarget is { } baseline && !TargetStillCompared(target, baseline))
            {
                refused = true;
                return;
            }
            // It binds the reviewed identity and bytes (or length and time) through its own held handle.
            if (targetReview is null)
            {
                refused = true;
                return;
            }
            var bound = ReplaceReviewed(staged, target, targetReview, step);
            if (bound.InUse is { } busy) throw busy;
            if (!bound.Published)
            {
                refused = true;
                retained = bound.RetainedPath is not null;
                Issue(IssueSeverity.Warning, target, "Not replaced: " + (bound.Reason ?? "the reviewed destination changed."),
                    retained ? StepOutcome.PartiallyApplied : StepOutcome.CanceledBeforeChange);
                if (bound.RetainedPath is not null)
                    Issue(IssueSeverity.Warning, bound.RetainedPath,
                        "The original destination remains at this recovery name.", StepOutcome.PartiallyApplied);
            }
            else if (bound.RetainedPath is not null)
                Issue(IssueSeverity.Warning, bound.RetainedPath,
                    bound.Reason ?? "The replaced destination remains at this recovery name.", StepOutcome.PartiallyApplied);
        });
        ok &= !refused;
        // Moved aside and unable to return to its name, the original is a partial change (review finding W9 of f134ba37).
        Journal.Done(step, ok ? StepOutcome.Committed : retained ? StepOutcome.PartiallyApplied : StepOutcome.CanceledBeforeChange);
        if (ok) return Result.Committed;
        try
        {
            if (isDirectory) Fs.DeleteDirectory(staged);
            else Fs.DeleteFile(staged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Issue(IssueSeverity.Warning, staged, "A temporary link could not be removed; it can be deleted safely.", StepOutcome.PartiallyApplied);
        }
        if (refused && !retained) { Job.ItemSkipped(); return Result.Skipped; }
        Job.ItemFailed();
        return Result.Failed;
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

    /// <param name="replaceable">False where the existing item must not be replaced (a folder, or a folder link that
    /// a merge would write through): the question then offers only skipping and keeping both.</param>
    /// <param name="atQuestion">The destination's review where a question offering Replace was shown; null otherwise.</param>
    private DecisionAction ResolveConflict(string src, string dst, FileSystemItemInfo incoming, FileSystemItemInfo existing,
        out QuestionReview? atQuestion, bool replaceable = true)
    {
        atQuestion = null;
        bool typeMismatch = incoming.IsDirectory != existing.IsDirectory;
        bool same = SameItem(src, dst);
        bool newer = IsIncomingNewer(src, dst, incoming, existing);
        var policy = Options.Conflicts;
        if (!typeMismatch && !same && (replaceable || policy is not (ConflictPolicy.Replace or ConflictPolicy.ReplaceIfNewer)))
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
            CanReplace: !typeMismatch && !same && replaceable, SameItem: same, TypeMismatch: typeMismatch, IncomingIsNewer: newer,
            SuggestedIncomingName: Path.GetFileName(UniqueSibling(dst, incoming.IsDirectory)),
            SuggestedExistingName: same ? null : Path.GetFileName(UniqueSibling(dst, existing.IsDirectory)));
        atQuestion = ReviewAtQuestion(request, dst);
        var d = Job.Ask(request);
        var action = d.Action == DecisionAction.ReplaceIfNewer ? newer ? DecisionAction.Replace : DecisionAction.Skip : d.Action;
        // An answer given for all conflicts can say Replace where nothing may be replaced here.
        if (same && action is DecisionAction.Replace or DecisionAction.KeepBothRenameExisting) return DecisionAction.KeepBothRenameIncoming;
        if (!replaceable && action == DecisionAction.Replace) return DecisionAction.KeepBothRenameIncoming;
        return action;
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
        if (ok)
        {
            Job.RecordUndoRemainder(newPath, "The existing destination was renamed to keep both items; this rename has no recorded inverse.");
            Issue(IssueSeverity.Info, path, $"The existing item was renamed to \"{Path.GetFileName(newPath)}\".", StepOutcome.Committed);
        }
        return ok;
    }

    private void PreserveOrigin(string src, string staged, string finalPath)
    {
        var vol = Volume(finalPath);
        // The native engine copies streams (the download mark included) to volumes that store them: nothing to check.
        if (Fs.CopyPreservesMetadata && vol.SupportsNamedStreams) return;
        if (!vol.SupportsNamedStreams && OperatingSystem.IsWindows())
        {
            // Other alternate data streams are dropped as well: name them instead of losing them silently (FS-002).
            var lost = Fs.GetAlternateStreams(src).Where(s => !s.Equals("Zone.Identifier", StringComparison.OrdinalIgnoreCase)).ToList();
            if (lost.Count > 0)
            {
                var names = string.Join(", ", lost.Take(3)) + (lost.Count > 3 ? $" and {lost.Count - 3} more" : string.Empty);
                Issue(IssueSeverity.Warning, src, $"Not kept: {(lost.Count == 1 ? "an alternate data stream" : $"{lost.Count} alternate data streams")} ({names}); {Holder(vol)} cannot store them.", StepOutcome.Committed);
            }
        }
        var mark = Fs.ReadOriginMark(src);
        if (mark is null) return;
        if (!vol.SupportsNamedStreams && OperatingSystem.IsWindows())
        {
            Issue(IssueSeverity.Warning, src, $"Security metadata lost: the file's download origin (Mark of the Web) cannot be stored on {Holder(vol)}. Windows may no longer warn when it is opened.", StepOutcome.Committed);
            return;
        }
        if (Fs.ReadOriginMark(staged) is null && !Fs.WriteOriginMark(staged, mark))
            Issue(IssueSeverity.Warning, src, "Security metadata lost: the download origin (Mark of the Web) could not be written to the copy.", StepOutcome.Committed);
    }

    private bool ContentEqual(string a, string b, long length)
    {
        if (length < 0) return false;
        Job.SetCurrent(a + " (verifying)");
        long counted = 0;
        void Read(long done)
        {
            Job.Checkpoint();
            long advanced = done - counted;
            Job.AddVerified(advanced);
            Job.AddDataProcessed(advanced);
            counted = done;
        }
        var ha = PortableFileOperations.HashFile(a, HashAlgorithmName.SHA256, Job.Token, Read, length);
        counted = 0;
        var hb = PortableFileOperations.HashFile(b, HashAlgorithmName.SHA256, Job.Token, Read, length);
        return ha.AsSpan().SequenceEqual(hb);
    }

    /// <summary>What verifying a file of <paramref name="size"/> bytes reads: the source and the copy, or nothing.</summary>
    private long VerifyWork(long size) => Options.Verify == VerifyMode.ReadBack ? 2 * Math.Max(0, size) : 0;

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

    public override void Execute()
    {
        var sources = Job.Request.Sources;
        Run(Enumerable.Range(0, sources.Count).Select(i => (i, sources[i].FileSystemPath!)), sources.Count);
    }


    /// <param name="roots">Positions in the request's sources with their paths, streamed.</param>
    internal void Run(IEnumerable<(int Index, string Path)> roots, int count)
    {
        Job.AddTotals(count, 0);
        foreach (var (index, path) in roots)
        {
            Job.Checkpoint();
            var info = Fs.TryGetInfo(path);
            if (info is null)
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, path, "Already gone; nothing to delete.", StepOutcome.Skipped);
                Job.RootCompleted(index);
                continue;
            }
            if (index < Job.Request.Sources.Count && ChangedSinceCompared(Job.Request.Sources[index], info, path))
            {
                Job.ItemSkipped();
                Job.RootFailed(index);
                continue;
            }
            int step = Journal.Intent(info.IsDirectory && !info.IsLink ? "delete-tree" : "delete", path);
            bool ok = Job.Request.OnlyAsCompared && info.IsDirectory && !info.IsLink
                ? DeleteComparedTree(path, Job.Request.Sources[index])
                : info.IsDirectory && !info.IsLink ? DeleteTree(path)
                : Job.Request.OnlyAsCompared && index < Job.Request.Sources.Count
                    ? DeleteComparedOne(path, info, Job.Request.Sources[index]) : DeleteOne(path, info);
            Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.PartiallyApplied);
            if (ok) Job.RootCompleted(index);
            else Job.RootFailed(index);
        }
    }

    private bool DeleteComparedOne(string path, FileSystemItemInfo info, ItemRef source)
    {
        var review = Job.Request.ExpectedSourceTrees?.GetValueOrDefault(source)?.Tree;
        var root = review?.FindItem(path);
        if (Fs.DeleteReviewedFile(path, root?.Version.Identity, root?.Content?.Sha256 ?? string.Empty,
                root?.Content?.Length ?? -1, Job.Token) is not { } bound)
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Warning, path, "Not removed: this file system cannot delete the exact compared object through a held handle.",
                StepOutcome.CanceledBeforeChange);
            return false;
        }
        if (!bound.Deleted)
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Warning, path, "Not removed: " + (bound.Reason ?? "the compared file changed."),
                bound.Changed ? StepOutcome.CanceledBeforeChange : StepOutcome.Failed);
            return false;
        }
        Job.ItemDone();
        Job.RecordUndoRemainder(path, "This file was permanently deleted; its data was not retained for Undo.");
        return true;
    }

    private bool DeleteComparedTree(string path, ItemRef source)
    {
        var review = Job.Request.ExpectedSourceTrees?.GetValueOrDefault(source)?.Tree;
        if (review is null || !review.Matches(Fs, new LocalFileSystemProvider(), Job.Checkpoint))
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Warning, path, "Not removed: this folder no longer matches its complete review.",
                StepOutcome.CanceledBeforeChange);
            return false;
        }
        var entries = review.Items.Select(i => new BoundDeletionEntry(i.Path, i.Version.Identity,
            i.Version.Info.IsDirectory, i.Version.Info.IsLink,
            i.Content?.Sha256, i.Content?.Length ?? -1)).ToArray();
        if (Fs.DeleteReviewedTree(path, entries, Job.Checkpoint, Job.Token) is not { } bound)
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Warning, path, "Not removed: this file system cannot delete a bound, fully reviewed tree.",
                StepOutcome.CanceledBeforeChange);
            return false;
        }
        for (int i = 0; i < bound.Done; i++) Job.ItemDone();
        for (int i = 0; i < bound.Failed; i++) Job.ItemFailed();
        foreach (var problem in bound.Problems)
            Issue(IssueSeverity.Warning, path, "The compared folder was not fully removed: " + problem,
                bound.Done == 0 ? StepOutcome.CanceledBeforeChange : StepOutcome.PartiallyApplied);
        if (bound.Failed == 0 && bound.Done != entries.Length)
            Issue(IssueSeverity.Warning, path,
                "The compared folder's item count changed while it was being removed; inspect what remains.",
                bound.Done == 0 ? StepOutcome.CanceledBeforeChange : StepOutcome.PartiallyApplied);
        if (bound.Done > 0)
            Job.RecordUndoRemainder(path, "This reviewed folder was permanently deleted; its data was not retained for Undo.");
        return bound.Failed == 0 && bound.Done == entries.Length;
    }

    private bool DeleteTree(string dir, int depth = 0)
    {
        // The Windows adapter walks and removes through no-follow handles, so a child swapped for a junction or
        // symbolic link after classification is removed as a link and never enumerated as the outside directory. As by
        // name (finding F-M4 of f134ba37): a folder it cannot read asks Retry or Skip, a child another program holds
        // open (antivirus, the indexer) is retried as a locked file is, and a read-only file is deleted only when the
        // user agrees. Each attempt deletes what it can; a read-only file kept once is not asked about again.
        BoundTreeDeletion? bound = null;
        bool unbound = false;
        int done = 0;
        HashSet<string>? kept = null;
        bool reached = TryIo(dir, "delete the folder", () =>
        {
            bound = null;
            if (Fs.DeleteTreeBound(dir, () => Job.Checkpoint(), DeleteReadOnly) is not { } attempt)
            {
                unbound = true;
                return;
            }
            done += attempt.Done;
            bound = attempt;
            if (attempt.InUse is { } busy) throw busy;
        });
        if (!unbound)
        {
            for (int i = 0; i < done; i++) Job.ItemDone();
            if (bound is null) Job.ItemFailed(); // the folder itself could not be read, and the answer was Skip
            else
            {
                for (int i = 0; i < bound.Failed; i++) Job.ItemFailed();
                foreach (string problem in bound.Problems)
                    Issue(IssueSeverity.Error, dir, "The folder could not be fully deleted: " + problem, StepOutcome.PartiallyApplied);
            }
            if (done > 0) Job.RecordUndoRemainder(dir, "This folder was permanently deleted; its data was not retained for Undo.");
            return reached && bound is { Failed: 0, ReadOnlyKept: 0 };
        }
        if (depth >= MaxRecursiveTreeDepth)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, dir, $"The folder tree exceeds the {MaxRecursiveTreeDepth:N0}-level delete limit; the remaining folder is kept.", StepOutcome.Failed);
            return false;
        }
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
            if (info.IsDirectory && !info.IsLink && Fs.IsMountPoint(child))
            {
                // On Linux and macOS a folder a drive, a share or a bind mount is mounted at is an ordinary directory:
                // deleting the folder above it must not empty another volume (as rm -r would without --one-file-system).
                Job.ItemSkipped();
                Issue(IssueSeverity.Warning, child, "Another file system is mounted here (a drive, a share, or a bind mount): FileCat does not delete into it. Unmount it first.", StepOutcome.Skipped);
                all = false;
                continue;
            }
            all &= info.IsDirectory && !info.IsLink ? DeleteTree(child, depth + 1) : DeleteOne(child, info);
        }
        if (!all) return false;
        bool removed = TryIo(dir, "delete the folder", () => Fs.DeleteDirectory(dir));
        if (removed) { Job.ItemDone(); Job.RecordUndoRemainder(dir, "This folder was permanently deleted; its data was not retained for Undo."); }
        else Job.ItemFailed();
        return removed;

        bool DeleteReadOnly(string path)
        {
            if (kept?.Contains(path) == true) return false;
            if (ConfirmReadOnly(path)) return true;
            (kept ??= new(StringComparer.OrdinalIgnoreCase)).Add(path);
            return false;
        }
    }

    /// <summary>Asks before a read-only file is deleted: true deletes it, false keeps it (counted as skipped).</summary>
    private bool ConfirmReadOnly(string path)
    {
        var d = Job.Ask(new ConfirmRequest("Read-only item", $"\"{Path.GetFileName(path)}\" is read-only. Delete it anyway?", path, "readonly",
            [DecisionAction.Proceed, DecisionAction.Skip, DecisionAction.CancelJob]));
        if (d.Action == DecisionAction.Skip)
        {
            Job.ItemSkipped();
            return false;
        }
        if (d.Action != DecisionAction.Proceed) throw new OperationCanceledException();
        return true;
    }

    private bool DeleteOne(string path, FileSystemItemInfo info)
    {
        if (info.IsReadOnly && !info.IsDirectory && !ConfirmReadOnly(path)) return false;
        bool ok = TryIo(path, "delete the item", () =>
        {
            if (info.IsReadOnly) Fs.SetAttributes(path, info.Attributes & ~FileAttributes.ReadOnly);
            if (info.IsDirectory) Fs.DeleteDirectory(path); // a directory link: removes only the link
            else Fs.DeleteFile(path);
        });
        if (ok) { Job.ItemDone(); Job.RecordUndoRemainder(path, "This item was permanently deleted; its data was not retained for Undo."); }
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
    /// <summary>Items per Shell operation: bounds memory for huge selections and gives cancellation points.</summary>
    internal const int ChunkSize = 2048;

    public override void Execute()
    {
        var sources = Job.Request.Sources;
        var unrecyclable = new List<(int Index, RecycleClassification Why)>();
        int unrecyclableTotal = 0;
        for (int start = 0; start < sources.Count; start += ChunkSize)
        {
            Job.Checkpoint();
            var recyclable = new List<(int Index, string Path)>();
            for (int i = start; i < Math.Min(sources.Count, start + ChunkSize); i++)
            {
                var p = sources[i].FileSystemPath!;
                var info = Fs.TryGetInfo(p);
                if (info is null)
                {
                    Job.AddTotals(1, 0);
                    Job.ItemSkipped();
                    Issue(IssueSeverity.Info, p, "Already gone; nothing to delete.", StepOutcome.Skipped);
                    Job.RootCompleted(i);
                    continue;
                }
                if (ChangedSinceCompared(sources[i], info, p))
                {
                    Job.AddTotals(1, 0);
                    Job.ItemSkipped();
                    Job.RootFailed(i);
                    continue;
                }
                if (Job.Request.OnlyAsCompared)
                {
                    Job.AddTotals(1, 0);
                    Job.ItemSkipped();
                    Job.RootFailed(i);
                    Issue(IssueSeverity.Warning, p,
                        "Not recycled: this file system cannot bind the Recycle Bin operation to the exact compared object.",
                        StepOutcome.CanceledBeforeChange);
                    continue;
                }
                var c = Fs.ClassifyRecycle(p, info.IsDirectory ? -1 : info.Size);
                if (c == RecycleClassification.Recyclable || c == RecycleClassification.Unknown) recyclable.Add((i, p));
                else
                {
                    unrecyclable.Add((i, c));
                    unrecyclableTotal++;
                }
            }
            Job.AddTotals(recyclable.Count, 0);
            if (recyclable.Count > 0) RecycleChunk(recyclable);
        }
        if (unrecyclable.Count > 0)
        {
            if (Job.Request.Options.PermanentlyDeleteUnrecyclable)
            {
                new DeleteExecutor(Job, Fs, Journal).Run(unrecyclable.Select(u => (u.Index, sources[u.Index].FileSystemPath!)), unrecyclableTotal);
            }
            else
            {
                Job.AddTotals(unrecyclableTotal, 0);
                foreach (var u in unrecyclable)
                {
                    Job.ItemSkipped();
                    Job.RootFailed(u.Index);
                    Issue(IssueSeverity.Warning, sources[u.Index].FileSystemPath!, "Not deleted: " + RecycleText.Explain(u.Why) + " Nothing was changed.", StepOutcome.CanceledBeforeChange);
                }
            }
        }
    }

    private void RecycleChunk(List<(int Index, string Path)> recyclable)
    {
        foreach (var (_, p) in recyclable) Journal.Intent("recycle", p);
        var results = Fs.Recycle(recyclable.Select(r => r.Path).ToList(), started => Job.SetCurrent(started), Job.Token);
        var byPath = new Dictionary<string, int>(recyclable.Count, PathUtil.SafetyComparer);
        foreach (var (index, path) in recyclable) byPath.TryAdd(path, index);
        foreach (var r in results)
        {
            int root = byPath.TryGetValue(r.Path, out int found) ? found : -1;
            switch (r.Outcome)
            {
                case RecycleOutcome.Recycled:
                    Job.ItemDone();
                    if (root >= 0) Job.RootCompleted(root);
                    if (r.RecycledId is not null) Job.AddUndo(new UndoStep(UndoKind.RestoreRecycled, r.RecycledId, r.Path, 0, 0, r.RecycledId));
                    else Job.RecordUndoRemainder(r.Path, "The item was recycled, but the platform did not provide an identity FileCat can restore. Use the Recycle Bin to review it.");
                    break;
                case RecycleOutcome.PermanentlyDeleted:
                    Job.RecordUndoRemainder(r.Path, "The platform permanently deleted this item instead of recycling it; its data was not retained for Undo.");
                    Job.ItemDone();
                    if (root >= 0) Job.RootCompleted(root);
                    Issue(IssueSeverity.Warning, r.Path, "Windows deleted this item permanently instead of moving it to the Recycle Bin.", StepOutcome.Committed);
                    break;
                case RecycleOutcome.Aborted:
                    Job.ItemSkipped();
                    if (root >= 0) Job.RootFailed(root);
                    Issue(IssueSeverity.Warning, r.Path, "Not deleted: it would have been deleted permanently instead of recycled. " + r.Error, StepOutcome.CanceledBeforeChange);
                    break;
                case RecycleOutcome.NotAttempted:
                    if (root >= 0) Job.RootFailed(root);
                    break;
                default:
                    Job.ItemFailed();
                    if (root >= 0) Job.RootFailed(root);
                    Issue(IssueSeverity.Error, r.Path, "Could not recycle: " + r.Error, StepOutcome.Failed);
                    break;
            }
        }
    }

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
            Job.RootFailed(0);
            return;
        }
        bool caseOnly = string.Equals(src, dst, StringComparison.OrdinalIgnoreCase);
        var existing = caseOnly ? null : Fs.TryGetInfo(dst);
        QuestionReview? atQuestion = null;
        bool replace = false;
        if (existing is not null)
        {
            // Replace as a Move replaces (review finding C7 of f134ba37): offered only where the destination can be replaced
            // bound to its review, and only the destination the question showed.
            bool replaceable = !existing.IsDirectory && !existing.IsLink && !info.IsDirectory && Fs.CanReplaceReviewed(dst);
            var request = new ConflictRequest("An item with this name already exists", newName, info, existing, src, dst,
                CanReplace: replaceable, false, existing.IsDirectory != info.IsDirectory, false, null, null);
            atQuestion = ReviewAtQuestion(request, dst);
            var d = Job.Ask(request);
            if (d.Action == DecisionAction.Replace && replaceable) replace = true;
            else if (d.Action == DecisionAction.Skip)
            {
                Job.ItemSkipped();
                Job.RootFailed(0);
                return;
            }
            else throw new OperationCanceledException();
        }
        if (replace)
        {
            Replace(src, dst, existing!, atQuestion);
            return;
        }
        int step = Journal.Intent("rename", src, dst);
        bool ok = TryIo(src, "rename the item", () => Fs.Move(src, dst, replaceExisting: false));
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
        if (!ok)
        {
            Job.ItemFailed();
            Job.RootFailed(0);
            return;
        }
        var after = Fs.TryGetInfo(dst);
        Job.AddUndo(new UndoStep(UndoKind.MoveBack, dst, src, after?.Size ?? info.Size, (after?.ModifiedUtc ?? info.ModifiedUtc).Ticks));
        Job.ItemDone();
        Job.RootCompleted(0);
    }

    /// <summary>The rename over the reviewed destination, which stays at a recovery name if it cannot be removed.</summary>
    private void Replace(string src, string dst, FileSystemItemInfo found, QuestionReview? atQuestion)
    {
        if (ReviewForReplacement(dst, found, atQuestion) is not { } review)
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Warning, dst, ChangedSinceQuestion, StepOutcome.CanceledBeforeChange);
            Job.RootFailed(0);
            return;
        }
        int step = Journal.Intent("rename-replace", src, dst, null, durable: true);
        BoundReplacement? bound = null;
        // Held open by another program: asked as in use, and Retry tries the same replacement again.
        if (!TryIo(dst, "replace the existing item", () =>
            {
                bound = ReplaceReviewed(src, dst, review, step);
                if (bound.InUse is { } busy) throw busy;
            }))
        {
            Journal.Done(step, StepOutcome.CanceledBeforeChange);
            Job.ItemFailed();
            Job.RootFailed(0);
            return;
        }
        Journal.Done(step, bound!.Published ? StepOutcome.Committed : Unpublished(bound));
        if (bound.RetainedPath is not null)
            Issue(IssueSeverity.Warning, bound.RetainedPath, bound.Reason ?? "The replaced file remains at this recovery name.", StepOutcome.PartiallyApplied);
        if (!bound.Published)
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Warning, dst, "Not replaced: " + (bound.Reason ?? "the reviewed destination changed."), Unpublished(bound));
            Job.RootFailed(0);
            return;
        }
        Job.ItemDone();
        Job.RootCompleted(0);
    }
}

/// <summary>Guarded undo (plan §9.3): each step is eligible only when the current state still matches.</summary>
public static class UndoService
{
    /// <summary>Low-level compatibility helper. UI callers reserve and submit a tracked JobManager inverse.</summary>
    public static IReadOnlyList<string> Undo(Job job, IFileSystemOperations fs)
    {
        var runner = new UndoRunner(job.SnapshotUndo(), fs, () => { });
        runner.Run();
        return runner.Report("This inverse did not run.").Outcomes.Select(o => UndoReport.Describe(o)).ToArray();
    }
}

/// <summary>
/// Sets attributes, times, and (Linux, macOS) permissions. Recursive changes never follow links, and reach each folder
/// after everything inside it, so taking access away from a folder cannot stop the change halfway.
/// </summary>
internal sealed class AttributesExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    private const FileAttributes Editable = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;

    public override void Execute()
    {
        var change = Job.Request.Attributes ?? throw new InvalidOperationException("No attribute change.");
        var sources = Job.Request.Sources;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        for (int i = 0; i < sources.Count; i++)
        {
            Job.Checkpoint();
            var path = sources[i].FileSystemPath!;
            bool ok = true;
            if (change.Recursive && Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            {
                var folders = new List<string>();
                var inside = new System.IO.Enumeration.FileSystemEnumerable<(string Path, bool IsDirectory)>(path,
                    (ref System.IO.Enumeration.FileSystemEntry e) => (e.ToFullPath(), e.IsDirectory), options);
                foreach (var (child, isDirectory) in inside)
                {
                    Job.Checkpoint();
                    if (isDirectory) folders.Add(child);
                    else ok &= Apply(child, change, inside: true);
                }
                // Deepest folders first: a folder changes only once nothing inside it is left to change.
                for (int f = folders.Count - 1; f >= 0; f--)
                {
                    Job.Checkpoint();
                    ok &= Apply(folders[f], change, inside: true);
                }
            }
            ok = Apply(path, change, inside: false) && ok;
            if (ok) Job.RootCompleted(i);
            else Job.RootFailed(i);
        }
    }

    private bool Apply(string path, AttributeChangeSet change, bool inside)
    {
        Job.SetCurrent(path);
        Job.AddTotals(1, 0);
        bool ok = TryIo(path, "change attributes", () =>
        {
            var info = Fs.TryGetInfo(path) ?? throw new FileNotFoundException("The item no longer exists.", path);
            var current = info.Attributes;
            var wanted = (current & ~change.Clear & Editable | change.Set & Editable) | current & ~Editable;
            // On Linux and macOS read-only is the item's permissions, which a link does not have of its own: setting it is a
            // chmod, which changes what the link points to, wherever that is (seen on macOS; release plan DPI P11).
            if (!OperatingSystem.IsWindows() && info.IsLink && wanted != current)
            {
                Issue(IssueSeverity.Info, path, "Attributes were not changed for a link: on this system it has none of its own, and FileCat leaves what it points to unchanged.", StepOutcome.Skipped);
                wanted = current;
            }
            bool times = change.ModifiedUtc is not null || change.CreatedUtc is not null;
            // Windows: times first, as a read-only file refuses time changes on some file systems. On Linux and macOS the
            // owner sets times whatever the permissions, and read-only there is derived from them.
            bool readOnlyDance = times && OperatingSystem.IsWindows() && (current & FileAttributes.ReadOnly) != 0;
            if (times)
            {
                if (readOnlyDance) Fs.SetAttributes(path, current & ~FileAttributes.ReadOnly);
                Fs.SetTimes(path, change.CreatedUtc, change.ModifiedUtc);
            }
            if (wanted != current || readOnlyDance)
                Fs.SetAttributes(path, wanted == 0 ? FileAttributes.Normal : wanted);
            if (change.ChangesPermissions && !OperatingSystem.IsWindows()) ApplyMode(path, info, change, inside);
        });
        if (ok) Job.ItemDone();
        else Job.ItemFailed();
        return ok;
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private void ApplyMode(string path, FileSystemItemInfo info, AttributeChangeSet change, bool inside)
    {
        if (info.IsLink)
        {
            // chmod would change whatever the link points to, which may be anywhere.
            Issue(IssueSeverity.Info, path, "Permissions were not changed for a link: links have none of their own, and FileCat leaves what they point to unchanged.", StepOutcome.Skipped);
            return;
        }
        var current = File.GetUnixFileMode(path);
        var wanted = UnixPermissions.Apply(current, change.ModeSet, change.ModeClear, info.IsDirectory, inside);
        if (wanted != current) File.SetUnixFileMode(path, wanted);
    }
}
