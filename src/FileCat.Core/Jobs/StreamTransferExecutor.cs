using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

/// <summary>Junction creation is Windows-only; the Windows adapter installs the implementation.</summary>
public static class Junctions
{
    public static Action<string, string>? CreateHandler { get; set; }

    public static void Create(string junctionPath, string targetDirectory)
    {
        if (CreateHandler is null) throw new PlatformNotSupportedException("Junctions are not supported on this platform.");
        CreateHandler(junctionPath, targetDirectory);
    }
}

/// <summary>A provider that can hand out origin data (Mark of the Web) for items it contains.</summary>
public interface IOriginMarkSource
{
    /// <summary>Origin mark to propagate to extracted content, or null.</summary>
    string? GetOriginMark(Location container);
}

/// <summary>
/// Common byte-stream strategy (plan §7.1): copies items from any provider that exposes content (archives,
/// remote, recovery) into a file-system destination, through staged names, with Mark-of-the-Web propagated
/// from the outermost container to every extracted item (plan §8.1, §15).
/// </summary>
internal sealed class StreamTransferExecutor(Job job, IFileSystemOperations fs, JobJournal journal, ProviderRegistry providers) : ExecutorBase(job, fs, journal)
{
    private const int BufferSize = 1024 * 1024;
    private readonly HashSet<string> _stagingDirs = new(PathUtil.SafetyComparer);
    private int _staged;
    private string? _originMark;

    public static bool CanHandle(JobRequest r, ProviderRegistry providers) =>
        r.Sources.Count > 0 && Listing.ItemSources.Parents(r.Sources)!.All(p => providers.IsRegistered(p.Scheme) &&
            (providers.Get(p.Scheme).GetCapabilities(p) & LocationCapabilities.ReadContent) != 0);

    public override void Execute()
    {
        var destDir = Job.Request.Destination!.Path;
        // Checked before anything is created: a refused destination must stay untouched.
        foreach (var parent in Job.Request.Sources.Select(s => s.Parent).Distinct())
        {
            if (providers.Get(parent.Scheme).CheckTransferDestination(parent, destDir) is not { } refusal) continue;
            for (int i = 0; i < Job.Request.Sources.Count; i++)
            {
                Job.ItemFailed();
                Job.RootFailed(i);
            }
            Issue(IssueSeverity.Error, destDir, refusal, StepOutcome.Failed);
            return;
        }
        if (!Directory.Exists(destDir) && !TryIo(destDir, "create the destination folder", () => Directory.CreateDirectory(destDir))) return;
        _originMark = FindOriginMark(Job.Request.Sources[0].Parent);
        var sources = Job.Request.Sources;
        for (int index = 0; index < sources.Count; index++)
        {
            Job.Checkpoint();
            var root = sources[index];
            var name = sources.Count == 1 && !string.IsNullOrEmpty(Job.Request.NewName) ? Job.Request.NewName! : root.Name;
            if (SafeNames.Validate(name) is { } bad)
            {
                Job.ItemFailed();
                Issue(IssueSeverity.Error, root.Name, $"Not extracted: {bad}", StepOutcome.Failed);
                Job.RootFailed(index);
                continue;
            }
            string targetDir;
            try
            {
                // Result and sync items keep their folders below the destination.
                targetDir = Job.Request.Options.Flatten || root.RelativeFolder is not { Length: > 0 } rel ? destDir : RelativeFolders.Resolve(destDir, rel);
                if (targetDir != destDir) Directory.CreateDirectory(targetDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Job.ItemFailed();
                Issue(IssueSeverity.Error, root.Name, "Not copied: its folder could not be created: " + ex.Message, StepOutcome.Failed);
                Job.RootFailed(index);
                continue;
            }
            var dst = Path.Combine(targetDir, name);
            if (!PathUtil.IsSameOrUnder(Path.GetFullPath(dst), destDir))
            {
                Job.ItemFailed();
                Issue(IssueSeverity.Error, root.Name, "Not extracted: the name would escape the destination folder.", StepOutcome.Failed);
                Job.RootFailed(index);
                continue;
            }
            bool ok = root.IsContainer ? CopyContainer(root, dst, destDir) : CopyItem(root, dst);
            if (ok) Job.RootCompleted(index);
            else Job.RootFailed(index);
        }
        Job.TotalsFinal = true;
        Job.SetCurrent(null);
    }

    private string? FindOriginMark(Location location)
    {
        // Walk to the outermost container: a marked download marks everything extracted from it, at any depth.
        for (var l = location; l is not null; l = l.Container)
        {
            if (l.IsFileSystem)
            {
                var m = Fs.ReadOriginMark(l.Path);
                if (m is not null) return m;
            }
            if (providers.TryGet(l.Scheme, out var p) && p is IOriginMarkSource s && s.GetOriginMark(l) is { } mark) return mark;
        }
        return null;
    }

    private bool CopyContainer(ItemRef dir, string dst, string destRoot)
    {
        var provider = providers.Get(dir.Parent.Scheme);
        var entry = new EntryData(dir.Name, dir.Kind);
        var location = provider.GetChildLocation(dir.Parent, entry);
        if (location is null)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, dir.Name, "This folder cannot be opened for copying.", StepOutcome.Failed);
            return false;
        }
        if (!Directory.Exists(dst) && !TryIo(dst, "create a folder", () => Fs.CreateDirectory(dst))) return false;
        var children = new List<EntryData>();
        var sink = new ListSink(children);
        try
        {
            provider.EnumerateAsync(location, sink, Job.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, dir.Name, "The folder could not be read: " + ex.Message, StepOutcome.Failed);
            return false;
        }
        bool all = true;
        foreach (var c in children)
        {
            Job.Checkpoint();
            if (SafeNames.Validate(c.Name) is { } bad)
            {
                Job.ItemFailed();
                Issue(IssueSeverity.Error, c.Name, "Not extracted: " + bad, StepOutcome.Failed);
                all = false;
                continue;
            }
            var childDst = Path.Combine(dst, c.Name);
            if (!PathUtil.IsSameOrUnder(Path.GetFullPath(childDst), destRoot))
            {
                Job.ItemFailed();
                Issue(IssueSeverity.Error, c.Name, "Not extracted: the name would escape the destination folder.", StepOutcome.Failed);
                all = false;
                continue;
            }
            // A link to a folder inside a copied tree can point back up it: never follow one (plan §8.1).
            if (c.IsContainer && c.Has(EntryFlags.Link))
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, c.Name, "Links to folders are not followed while copying; open the link and copy its contents if you need them.", StepOutcome.Skipped);
                continue;
            }
            var child = provider.GetItemRef(location, c);
            all &= c.IsContainer ? CopyContainer(child, childDst, destRoot) : CopyItem(child, childDst);
        }
        return all;
    }

    private bool CopyItem(ItemRef item, string dst)
    {
        Job.SetCurrent(item.Name);
        var provider = providers.Get(item.Parent.Scheme);
        var target = dst;
        bool replace = false;
        var existing = Fs.TryGetInfo(dst);
        if (existing is not null)
        {
            var incoming = new FileSystemItemInfo(item.Name, false, false, item.Size, item.Modified > 0 ? new DateTime(item.Modified, DateTimeKind.Utc) : DateTime.MinValue, DateTime.MinValue, FileAttributes.Normal);
            var policy = Job.Request.Options.Conflicts;
            var action = policy switch
            {
                ConflictPolicy.Skip => DecisionAction.Skip,
                ConflictPolicy.Replace => DecisionAction.Replace,
                ConflictPolicy.KeepBothRenameIncoming => DecisionAction.KeepBothRenameIncoming,
                ConflictPolicy.ReplaceIfNewer => incoming.ModifiedUtc > existing.ModifiedUtc.AddSeconds(2) ? DecisionAction.Replace : DecisionAction.Skip,
                _ => Job.Ask(new ConflictRequest("An item with this name already exists", Path.GetFileName(dst), incoming, existing, item.Name, dst,
                    !existing.IsDirectory, false, existing.IsDirectory, incoming.ModifiedUtc > existing.ModifiedUtc, Path.GetFileName(Unique(dst)), null)).Action,
            };
            if (action == DecisionAction.ReplaceIfNewer) action = incoming.ModifiedUtc > existing.ModifiedUtc ? DecisionAction.Replace : DecisionAction.Skip;
            switch (action)
            {
                case DecisionAction.Skip:
                    Job.ItemSkipped();
                    return false;
                case DecisionAction.Replace when !existing.IsDirectory:
                    replace = true;
                    break;
                case DecisionAction.KeepBothRenameIncoming:
                    target = Unique(dst);
                    break;
                case DecisionAction.CancelJob:
                    throw new OperationCanceledException();
                default:
                    Job.ItemSkipped();
                    return false;
            }
        }
        IContentSource? content;
        try
        {
            content = provider.OpenContent(item);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, item.Name, "Could not read the item: " + ex.Message, StepOutcome.Failed);
            return false;
        }
        if (content is null)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, item.Name, "This item has no readable content (for example an encrypted archive entry).", StepOutcome.Failed);
            return false;
        }
        var dir = Path.GetDirectoryName(target)!;
        // Durable once per folder, so recovery finds staged leftovers there (a flush per item dominated extracting small files).
        if (_stagingDirs.Add(dir)) Journal.StagingDirectory(dir);
        var staged = Path.Combine(dir, $"{JournalRecovery.StagedPrefix}{Job.ShortId}-{Interlocked.Increment(ref _staged)}.tmp");
        long written = 0;
        IReadOnlyList<(long Offset, long Length)>? lost = null;
        // A source that can be read at any offset and describes its version can resume after a dropped connection or a
        // phone that locked part way; any other source fails the item as before.
        var revision = content.GetRevision();
        bool resumable = content.CanSeek && revision is not null && content is not IPartialContent;
        int failures = 0;
        try
        {
            using (var outStream = new FileStream(staged, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.SequentialScan))
            {
                var buffer = new byte[BufferSize];
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    Job.Checkpoint();
                    int n;
                    try
                    {
                        n = content!.Read(written, buffer);
                    }
                    catch (Exception ex) when (resumable && ex is IOException or UnauthorizedAccessException)
                    {
                        content?.Dispose();
                        content = Resume(provider, item, ex, ++failures, revision!.Value, outStream, ref written);
                        if (content is null)
                        {
                            // Skipped: the item fails, and its partial copy goes.
                            throw new SkippedTransferException(ex);
                        }
                        continue;
                    }
                    if (n <= 0) break;
                    outStream.Write(buffer, 0, n);
                    written += n;
                    Job.AddBytes(n);
                    Job.Throttle(written, clock);
                }
                lost = (content as IPartialContent)?.MissingRanges;
                // Through the open handle: no second open, and later writes on it cannot change the time.
                if (item.Modified > 0) File.SetLastWriteTimeUtc(outStream.SafeFileHandle, new DateTime(item.Modified, DateTimeKind.Utc));
            }
            if (_originMark is not null && !Fs.WriteOriginMark(staged, _originMark))
                Issue(IssueSeverity.Warning, item.Name, "Security metadata lost: the download origin (Mark of the Web) could not be written to the extracted file.", StepOutcome.Committed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
        {
            Job.AddBytes(-written);
            try { if (File.Exists(staged)) File.Delete(staged); } catch (IOException) { }
            if (ex is OperationCanceledException) throw;
            Job.ItemFailed();
            if (ex is SkippedTransferException skipped)
                Issue(IssueSeverity.Error, item.Name, "Not copied: the transfer stopped part way and was skipped: " + ErrorText.Describe(skipped.InnerException!), StepOutcome.Skipped);
            else Issue(IssueSeverity.Error, item.Name, "Could not extract: " + ErrorText.Describe(ex), StepOutcome.Failed);
            return false;
        }
        finally
        {
            content?.Dispose();
        }
        // Publishing a new item is group-committed like local copies (plan §9.3); replacing one is flushed first.
        int step = Journal.Intent(replace ? "replace" : "publish", item.Name, target, staged, durable: replace);
        bool ok = TryIo(target, "publish the extracted item", () => Fs.Move(staged, target, replace));
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
        if (!ok)
        {
            try { File.Delete(staged); } catch (IOException) { }
            Job.ItemFailed();
            return false;
        }
        Job.ItemDone();
        // A recovered file with lost parts is still worth having, but never passed off as complete (plan §17.1).
        if (lost is { Count: > 0 })
            Issue(IssueSeverity.Warning, item.Name, PartialContent.Describe(lost, written) + " Check the file before relying on it.", StepOutcome.Committed);
        return true;
    }

    private const int ResumeCheckBytes = 64 * 1024;

    /// <summary>
    /// After the source failed part way (plan §14: "verify resumable partial content before reuse"): the first failure
    /// retries on its own after a second, later ones ask. The part already copied is kept only when the source is
    /// provably the same file: the same size and time, and the last 64 KiB before the break read the same. Otherwise the
    /// copy starts again from the beginning, and the job says so. Returns null when the user skips.
    /// </summary>
    private IContentSource? Resume(ResourceProvider provider, ItemRef item, Exception error, int failures, ContentRevision revision, FileStream staged, ref long written)
    {
        while (true)
        {
            if (failures > 1)
            {
                var d = Job.Ask(new ErrorRequest("The transfer stopped part way",
                    $"{ErrorText.Describe(error)} {written:N0} of {revision.Length:N0} bytes were copied. Retry continues where it stopped once FileCat has checked that the file is unchanged.",
                    item.Name, CanRetry: true, "transfer"));
                if (d.Action == DecisionAction.Skip) return null;
                if (d.Action != DecisionAction.Retry) throw new OperationCanceledException();
            }
            else
            {
                Job.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            }
            Job.Checkpoint();
            IContentSource? next = null;
            try
            {
                next = provider.OpenContent(item) ?? throw new IOException("The item has no readable content any more.");
                if (Unchanged(next, revision, staged, written))
                {
                    staged.Position = written;
                    Issue(IssueSeverity.Info, item.Name, $"The transfer was interrupted and resumed at {written:N0} bytes, after the part already copied was checked against the source.", StepOutcome.Committed);
                }
                else
                {
                    Job.AddBytes(-written);
                    written = 0;
                    staged.SetLength(0);
                    staged.Position = 0;
                    Issue(IssueSeverity.Info, item.Name, "The transfer was interrupted, and the file could not be shown to be unchanged at its source, so it was copied again from the start.", StepOutcome.Committed);
                }
                return next;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                next?.Dispose();
                error = ex;
                failures = Math.Max(failures + 1, 2);
            }
        }
    }

    /// <summary>The same size and time, and the bytes just before <paramref name="written"/> read the same from both.</summary>
    private static bool Unchanged(IContentSource source, ContentRevision revision, FileStream staged, long written)
    {
        if (source.GetRevision() is not { } now || now.Length != revision.Length || now.ModifiedTicks != revision.ModifiedTicks || written > now.Length) return false;
        int n = (int)Math.Min(ResumeCheckBytes, written);
        if (n == 0) return true;
        var theirs = new byte[n];
        var ours = new byte[n];
        for (int done = 0; done < n;)
        {
            int got = source.Read(written - n + done, theirs.AsSpan(done));
            if (got <= 0) return false;
            done += got;
        }
        staged.Position = written - n;
        staged.ReadExactly(ours);
        return theirs.AsSpan().SequenceEqual(ours);
    }

    /// <summary>The user skipped an item whose transfer stopped part way.</summary>
    private sealed class SkippedTransferException(Exception inner) : IOException(inner.Message, inner);

    private string Unique(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        return Path.Combine(dir, PathUtil.MakeUniqueName(Path.GetFileName(path), n => File.Exists(Path.Combine(dir, n)) || Directory.Exists(Path.Combine(dir, n))));
    }

    private sealed class ListSink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }
}

/// <summary>Validation of member names from untrusted containers before they touch the file system (plan §15).</summary>
/// <summary>Folders recreated below a destination (result sets, synchronization): every segment is a plain name.</summary>
public static class RelativeFolders
{
    /// <summary>The folder below <paramref name="destination"/>; throws <see cref="ArgumentException"/> when a segment is unsafe.</summary>
    public static string Resolve(string destination, string relative)
    {
        var dir = destination;
        foreach (var segment in relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (SafeNames.Validate(segment) is { } bad) throw new ArgumentException($"The folder \"{segment}\" is not recreated: {bad}");
            dir = Path.Combine(dir, segment);
        }
        if (!PathUtil.IsSameOrUnder(Path.GetFullPath(dir), destination)) throw new ArgumentException("The folder would escape the destination.");
        return dir;
    }
}

public static class SafeNames
{
    public static string? Validate(string name)
    {
        if (string.IsNullOrEmpty(name)) return "the name is empty.";
        if (name is "." or "..") return "\".\" and \"..\" are not allowed.";
        if (name.Contains('/') || name.Contains('\\')) return "the name contains a path separator.";
        if (name.Contains(':')) return "the name contains ':' (alternate data streams or drive references are not extracted).";
        if (Path.IsPathRooted(name)) return "absolute paths are not extracted.";
        return PathUtil.ValidateNewName(name);
    }
}
