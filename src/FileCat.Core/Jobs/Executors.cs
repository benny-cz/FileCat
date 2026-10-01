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
    protected readonly Job Job = job;
    protected readonly IFileSystemOperations Fs = fs;
    protected readonly JobJournal Journal = journal;

    public abstract void Execute();

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
            if (info is null) continue;
            if (!info.IsDirectory || info.IsLink)
            {
                Job.AddTotals(1, Math.Max(0, info.Size));
                Job.AddVerifyTotal(VerifyWork(info.IsLink ? 0 : info.Size));
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
                            Job.AddTotals(1, size);
                            Job.AddVerifyTotal(VerifyWork(size));
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        Job.TotalsFinal = !ct.IsCancellationRequested;
    }

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
        if (Move && SameVolume(src, dst)) return MoveByRename(src, dst, info);
        return info.IsDirectory && !info.IsLink ? CopyDirectory(src, dst, info) : CopyFileItem(src, dst, info);
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
                var d = ResolveConflict(src, dst, info, existing, replaceable: !existing.IsDirectory);
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
        // A rename to a new name loses nothing if interrupted (the item is at one of the two names): group-committed.
        int step = Journal.Intent(replace ? "move-replace" : "move", src, target, null, durable: replace);
        bool ok;
        try
        {
            Fs.Move(src, target, replace);
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
            ok = TryIo(src, "move the item", () => Fs.Move(src, target, replace));
        }
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
    private Result CopyDirectory(string src, string dst, FileSystemItemInfo info, Func<bool>? ensureParent = null)
    {
        Job.Checkpoint();
        Job.SetCurrent(src);
        var existing = Fs.TryGetInfo(dst);
        var target = dst;
        if (existing is not null && (!existing.IsDirectory || existing.IsLink))
        {
            // Merging into a folder link would write wherever it points: only skipping or keeping both are offered.
            var d = ResolveConflict(src, dst, info, existing, replaceable: false);
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
        foreach (var ci in SafeChildren(src))
        {
            Job.Checkpoint();
            var child = ci.Path;
            var childDst = Path.Combine(target, Path.GetFileName(child));
            Result r;
            if (ci.IsDirectory && !ci.IsLink)
            {
                r = CopyDirectory(child, childDst, ci, EnsureCreated);
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

    private Result CopyFileItem(string src, string dst, FileSystemItemInfo info, bool firstAttempt = true)
    {
        Job.Checkpoint();
        Job.SetCurrent(src);
        long planned = info.IsLink ? 0 : Math.Max(0, info.Size); // as discovery counted it
        Job.BeginItem(planned, VerifyWork(planned));
        if (firstAttempt && TryFastDirectCopy(src, dst, info) is { } fast) return fast;
        var target = dst;
        bool replace = false;
        var existing = Fs.TryGetInfo(dst);
        if (existing is not null)
        {
            // A real folder is never replaced by a file or a link: that question offers skipping or keeping both.
            var d = ResolveConflict(src, dst, info, existing, replaceable: !(existing.IsDirectory && !existing.IsLink));
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
                    Issue(IssueSeverity.Info, src, "Skipped: moving it would have lost metadata the destination cannot store.", StepOutcome.Skipped);
                    return Result.Skipped;
                default:
                    throw new OperationCanceledException();
            }
        }

        // Where the bytes go: straight to the new name (small, new, and named like its source, so recovery can match
        // it) or to a staged name that is published afterwards.
        var dir = Path.GetDirectoryName(target)!;
        bool direct = !replace && info.Size is >= 0 and < DirectCopyLimit &&
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
            var clock = Stopwatch.StartNew();
            try
            {
                var options = new FileCopyOptions
                {
                    CopyLinkAsLink = true,
                    NoBuffering = info.Size > 256L * 1024 * 1024,
                    DisablePreallocation = direct,
                    AllowDecryptedDestination = allowDecrypted,
                    // A move deletes the source next: the copy must be on the device first, not in the write cache.
                    FlushDestination = Move,
                };
                Fs.CopyFile(src, writeTo, options, (done, total) =>
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
                // The copy engine removes the partial file it was writing; a staged name is cleaned up here as well.
                Job.AddBytes(-reported);
                if (!direct) TryDeleteStaged(writeTo);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Job.AddBytes(-reported);
                if (!direct) TryDeleteStaged(writeTo);
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
                    return CopyFileItem(src, dst, info, firstAttempt: false);
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
                    Issue(IssueSeverity.Warning, src, "Skipped: it is encrypted and the destination cannot keep it encrypted.", StepOutcome.Skipped);
                    return Result.Skipped;
                }
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
        if (Options.Verify == VerifyMode.ReadBack && !ContentEqual(src, writeTo))
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
            bool published = TryIo(target, replace ? "replace the existing item" : "publish the copied item", () => Fs.Move(writeTo, target, replace, writeThrough: Move));
            if (!published)
            {
                TryDeleteStaged(writeTo);
                Journal.Done(step, StepOutcome.CanceledBeforeChange);
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
        if (keepSource)
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Info, src, "Copied; the original was kept because the destination cannot store all of its metadata.", StepOutcome.Skipped);
            return Result.Skipped;
        }
        return DeleteMovedSource(src, target, info);
    }

    /// <summary>
    /// The common case of a small copy to a free name, without probing the destination first: the engine's
    /// fail-if-exists flag detects a conflict. Any failure returns null, and the careful path (conflict prompts,
    /// retries, explanations) runs from the start. Moves never take this path: they may need the metadata question.
    /// </summary>
    private Result? TryFastDirectCopy(string src, string dst, FileSystemItemInfo info)
    {
        if (Move || info.IsLink || info.Size is < 0 or >= DirectCopyLimit || Options.Verify != VerifyMode.Native) return null;
        if (!string.Equals(Path.GetFileName(dst), Path.GetFileName(src), StringComparison.Ordinal)) return null; // recovery pairs by name
        if (!Fs.CopyPreservesMetadata || !Volume(dst).SupportsNamedStreams) return null;
        RecordFill(src, Path.GetDirectoryName(dst)!);
        long reported = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            Fs.CopyFile(src, dst, new FileCopyOptions { CopyLinkAsLink = true, DisablePreallocation = true }, (done, total) =>
            {
                Job.AddBytes(done - reported);
                reported = done;
                if (Job.IsPaused) Job.Checkpoint();
                Job.Throttle(done, clock);
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
            Issue(IssueSeverity.Warning, src, "The copy was published, but the source had already disappeared.", StepOutcome.Uncertain);
            Job.ItemDone();
            return Result.Committed;
        }
        // The only irreversible step of a move: the published copy must be another file than the source, never the
        // source itself reached through a link.
        if (Fs.GetFileIdentity(src) is { } source && source == Fs.GetFileIdentity(target))
        {
            Issue(IssueSeverity.Warning, src, "Not deleted: the destination turned out to be the source itself (reached through a link), so it stays where it is.", StepOutcome.PartiallyApplied);
            Job.ItemDone();
            return Result.Failed;
        }
        if (now.Size != before.Size || now.ModifiedUtc != before.ModifiedUtc)
        {
            Issue(IssueSeverity.Warning, src, "Copied, but the source changed during the copy, so it was not deleted. Both versions now exist.", StepOutcome.PartiallyApplied);
            Job.ItemDone();
            return Result.Failed;
        }
        // The copy must still be there, whole, when its source goes: an antivirus quarantine or a sync client can take a
        // new file away at once, and the source would then be the only copy (release plan DPI P01).
        if (Fs.TryGetInfo(target) is not { IsDirectory: false } copy || copy.Size != now.Size)
        {
            Issue(IssueSeverity.Warning, src, "Not deleted: the copy is no longer at the destination as it was written (another program may have moved or removed it), so the source stays.", StepOutcome.PartiallyApplied);
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
        // A replaced item goes only once its replacement exists: the new link is made under a staged name first.
        string linkAt = target;
        if (replace)
        {
            var dir = Path.GetDirectoryName(target)!;
            if (_stagingDirs.Add(dir)) Journal.StagingDirectory(dir);
            linkAt = Path.Combine(dir, $"{JournalRecovery.StagedPrefix}{Job.ShortId}-{Interlocked.Increment(ref _stagedCounter)}.tmp");
        }
        if (Fs.TryCopyLink(src, linkAt, info.IsDirectory, out var error))
        {
            if (replace && !PublishLink(src, linkAt, target, info.IsDirectory)) return Result.Failed;
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
                if (TryIo(linkAt, "create a junction", () => Junctions.Create(linkAt, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(src)!, info.LinkTarget)))))
                {
                    if (replace && !PublishLink(src, linkAt, target, isDirectory: true)) return Result.Failed;
                    if (Move) DeleteLinkSource(src, info);
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
    private bool PublishLink(string src, string staged, string target, bool isDirectory)
    {
        int step = Journal.Intent("replace", src, target, staged);
        bool ok = TryIo(target, "replace the existing item", () =>
        {
            // A folder link cannot be renamed over another folder link: the old one (only a link) is removed just before.
            if (isDirectory && Fs.TryGetInfo(target) is { IsDirectory: true, IsLink: true }) Fs.DeleteDirectory(target);
            Fs.Move(staged, target, replaceExisting: !isDirectory);
        });
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.CanceledBeforeChange);
        if (ok) return true;
        try
        {
            if (isDirectory) Fs.DeleteDirectory(staged);
            else Fs.DeleteFile(staged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Issue(IssueSeverity.Warning, staged, "A temporary link could not be removed; it can be deleted safely.", StepOutcome.PartiallyApplied);
        }
        Job.ItemFailed();
        return false;
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
    private DecisionAction ResolveConflict(string src, string dst, FileSystemItemInfo incoming, FileSystemItemInfo existing, bool replaceable = true)
    {
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
        if (ok) Issue(IssueSeverity.Info, path, $"The existing item was renamed to \"{Path.GetFileName(newPath)}\".", StepOutcome.Committed);
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

    private bool ContentEqual(string a, string b)
    {
        Job.SetCurrent(a + " (verifying)");
        long counted = 0;
        void Read(long done)
        {
            Job.AddVerified(done - counted);
            counted = done;
        }
        var ha = PortableFileOperations.HashFile(a, HashAlgorithmName.SHA256, Job.Token, Read);
        counted = 0;
        var hb = PortableFileOperations.HashFile(b, HashAlgorithmName.SHA256, Job.Token, Read);
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
            int step = Journal.Intent(info.IsDirectory && !info.IsLink ? "delete-tree" : "delete", path);
            bool ok = info.IsDirectory && !info.IsLink ? DeleteTree(path) : DeleteOne(path, info);
            Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.PartiallyApplied);
            if (ok) Job.RootCompleted(index);
            else Job.RootFailed(index);
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
                    break;
                case RecycleOutcome.PermanentlyDeleted:
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
        bool replace = false;
        if (existing is not null)
        {
            var d = Job.Ask(new ConflictRequest("An item with this name already exists", newName, info, existing, src, dst,
                CanReplace: !existing.IsDirectory && !info.IsDirectory, false, existing.IsDirectory != info.IsDirectory, false, null, null));
            if (d.Action == DecisionAction.Replace && !existing.IsDirectory && !info.IsDirectory) replace = true;
            else if (d.Action == DecisionAction.Skip)
            {
                Job.ItemSkipped();
                Job.RootFailed(0);
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
            Job.RootFailed(0);
            return;
        }
        var after = Fs.TryGetInfo(dst);
        if (!replace) Job.AddUndo(new UndoStep(UndoKind.MoveBack, dst, src, after?.Size ?? info.Size, (after?.ModifiedUtc ?? info.ModifiedUtc).Ticks));
        Job.ItemDone();
        Job.RootCompleted(0);
    }
}

/// <summary>Guarded undo (plan §9.3): each step is eligible only when the current state still matches.</summary>
public static class UndoService
{
    public static IReadOnlyList<string> Undo(Job job, IFileSystemOperations fs)
    {
        var report = new List<string>();
        // A bulk rename is undone as one batch: renames that swapped names need the same temporary-name protocol back.
        var batch = job.UndoSteps.Where(s => s.Kind == UndoKind.RenameBatchBack).ToList();
        if (batch.Count > 0)
        {
            var identity = batch.ToDictionary(s => s.From, PathUtil.SafetyComparer);
            var outcomes = Operations.BulkRenameRunner.Run(batch.Select(s => new Operations.BulkRenameRunner.Pair(s.From, s.To)).ToList(), fs, () => { },
                (pair, info) => identity.TryGetValue(pair.Source, out var s) && !info.IsDirectory && (info.Size != s.Size || info.ModifiedUtc.Ticks != s.ModifiedTicks)
                    ? "it changed since the rename" : null);
            foreach (var o in outcomes)
                report.Add(o.Done ? $"Renamed \"{Path.GetFileName(o.Source)}\" back to \"{Path.GetFileName(o.Target)}\"."
                                  : $"Not undone: \"{Path.GetFileName(o.Source)}\": {o.Error}");
        }
        foreach (var step in job.UndoSteps.Reverse())
        {
            if (step.Kind == UndoKind.RenameBatchBack) continue;
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
                    case UndoKind.RemoveCreatedLink:
                        var li = fs.TryGetInfo(step.From);
                        if (li is null) report.Add($"Nothing to undo: the link \"{Path.GetFileName(step.From)}\" no longer exists.");
                        else if (!li.IsLink || li.LinkTarget is not { } pointsTo || !string.Equals(pointsTo.TrimEnd('\\', '/'), step.To.TrimEnd('\\', '/'), PathUtil.SafetyComparison))
                            report.Add($"Kept \"{Path.GetFileName(step.From)}\": it is no longer the link that was created.");
                        else
                        {
                            // Removing a link never touches its target.
                            if (step.Size == 1) fs.DeleteDirectory(step.From);
                            else fs.DeleteFile(step.From);
                            report.Add($"Removed the created link \"{Path.GetFileName(step.From)}\".");
                        }
                        break;
                    case UndoKind.RemoveCreatedHardLink:
                        // The data survives only while the original name still exists and is the same file.
                        string? linkId = fs.TryGetInfo(step.From) is { IsDirectory: false } ? fs.GetFileIdentity(step.From) : null;
                        string? originalId = fs.TryGetInfo(step.To) is { IsDirectory: false } ? fs.GetFileIdentity(step.To) : null;
                        if (fs.TryGetInfo(step.From) is null) report.Add($"Nothing to undo: the hard link \"{Path.GetFileName(step.From)}\" no longer exists.");
                        else if (step.RecycledId is null || linkId != step.RecycledId || originalId != step.RecycledId)
                            report.Add($"Kept \"{Path.GetFileName(step.From)}\": it is no longer another name of \"{Path.GetFileName(step.To)}\", so removing it could lose data.");
                        else
                        {
                            fs.DeleteFile(step.From);
                            report.Add($"Removed the created hard link \"{Path.GetFileName(step.From)}\"; \"{Path.GetFileName(step.To)}\" keeps the data.");
                        }
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
