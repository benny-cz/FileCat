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
/// from the outermost container to every extracted item (plan §8.1, §15). A folder counts as completed only when all of
/// it arrived: a move (from a server) deletes the source of completed folders only, so a skipped link or a file the
/// filter left out keeps its folder on the server.
/// </summary>
internal sealed class StreamTransferExecutor(Job job, IFileSystemOperations fs, JobJournal journal, ProviderRegistry providers)
    : ExecutorBase(job, fs, journal), IHonorsTransferFilter, IHonorsReadBackVerification
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>1980-01-01: a content revision that states an earlier "time" states a version counter or nothing.</summary>
    private const long MinimumFileTimeTicks = 624_511_296_000_000_000;
    private readonly HashSet<string> _stagingDirs = new(PathUtil.SafetyComparer);
    private int _staged;
    private string? _originMark;

    /// <summary>
    /// Told of every file that arrived, with the version its source stated when it was read: a move from a server deletes
    /// exactly those, and only while they are still that version (release plan DPI P09).
    /// </summary>
    public Action<ItemRef, ContentRevision?>? Copied { get; init; }

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
            if ((root.Flags & EntryFlags.Link) != 0)
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, root.Name, LinkSkipMessage(root.Parent), StepOutcome.Skipped);
                Job.RootFailed(index);
                continue;
            }
            string targetDir;
            try
            {
                // Result and sync items keep their folders below the destination.
                targetDir = Job.Request.Options.Flatten || root.RelativeFolder is not { Length: > 0 } rel ? destDir : RelativeFolders.Resolve(destDir, rel);
                if (targetDir != destDir)
                {
                    if (!DestinationAllowed(root.Parent, targetDir)) { Job.RootFailed(index); continue; }
                    Directory.CreateDirectory(targetDir);
                }
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

    private bool DestinationAllowed(Location source, string folder)
    {
        if (providers.Get(source.Scheme).CheckTransferDestination(source, folder) is not { } refusal) return true;
        Job.ItemFailed();
        Issue(IssueSeverity.Error, folder, refusal, StepOutcome.Failed);
        return false;
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

    private static string LinkSkipMessage(Location parent) =>
        parent.Scheme is Schemes.Archive or Schemes.Zip
            ? "Links inside archives are not followed or extracted."
            : "Links are not followed implicitly while copying; open the target explicitly to copy its contents.";

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
        // A descendant can be a different mount or a link into the recovery source's disk.
        // The admitted root does not establish where this particular folder writes.
        if (!DestinationAllowed(dir.Parent, dst)) return false;
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
        // A non-fatal listing problem can leave members unknown. Keep the readable members, but never claim the
        // entire root arrived (a remote move uses that claim before considering deletion of its source).
        foreach (string warning in sink.Issues)
            Issue(IssueSeverity.Warning, dir.Name, "Folder listing warning: " + warning, StepOutcome.PartiallyApplied);
        bool all = sink.Issues.Count == 0;
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
            // Link targets require an explicit scope choice; folder links can also recurse back up the tree.
            if (c.Has(EntryFlags.Link))
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, c.Name, LinkSkipMessage(location), StepOutcome.Skipped);
                all &= Job.Kind != JobKind.Move;
                continue;
            }
            // "Only files matching": the others stay where they are, and so does their folder when moving.
            if (!c.IsContainer && Job.Request.Options.Filter is { } filter && !filter.IsMatch(c.Name))
            {
                all &= Job.Kind != JobKind.Move;
                continue;
            }
            var child = provider.GetItemRef(location, c);
            Job.Checkpoint();
            if ((child.Flags & EntryFlags.Link) != 0)
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, c.Name, LinkSkipMessage(child.Parent), StepOutcome.Skipped);
                all &= Job.Kind != JobKind.Move;
                continue;
            }
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
            // Read once from start to end: a large archive member is then decompressed straight into the copy.
            content = Content.ProgressiveContent.Sequential(provider.OpenContent(item));
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
        var staged = Path.Combine(dir, $"{JournalRecovery.StagedPrefix}{Job.ShortId}-{Interlocked.Increment(ref _staged)}.tmp");
        long written = 0;
        IReadOnlyList<(long Offset, long Length)>? lost = null;
        string? caveat = null;
        // A source that can be read at any offset and describes its version can resume after a dropped connection or a
        // phone that locked part way; any other source fails the item as before.
        ContentRevision? revision = null;
        long? expectedLength = null;
        bool partial = content is IPartialContent;
        bool resumable = false;
        int failures = 0;
        void DisposeContent()
        {
            // Clear ownership before calling a provider that may throw during disposal.
            var owned = content;
            content = null;
            owned?.Dispose();
        }
        void CheckLength()
        {
            Job.Checkpoint();
            long stated = content!.Length;
            if (stated < -1 || expectedLength is { } expected && stated >= 0 && stated != expected)
                throw new IOException("The source length changed or contradicts its copy revision.");
        }
        void RetainVersion()
        {
            revision = content!.GetRevision();
            long stated = content.Length;
            if (revision is { Length: < -1 }) throw new IOException("The source stated an invalid copy revision length.");
            // Progressive archive members enforce their own declared-size slack, ratio and checksum limits.
            // Ordinary providers must deliver exactly the version/length they opened, even without read-back.
            expectedLength = content is Content.ProgressiveContent ? null
                : revision is { Length: >= 0 } r ? r.Length : stated >= 0 ? stated : null;
            partial = content is IPartialContent;
            resumable = content.CanSeek && revision is { Length: >= 0 } && !partial;
            CheckLength();
        }
        try
        {
            try
            {
                // Keep metadata admission inside the lifetime/cleanup scope too: a refused query still closes its content.
                RetainVersion();
                // Conflict decisions and opening/querying content can take time or change topology.
                // Revalidate the actual staging folder before creating a file there.
                if (!DestinationAllowed(item.Parent, dir)) return false;
                // Durable once per folder, so recovery finds staged leftovers there.
                if (_stagingDirs.Add(dir)) Journal.StagingDirectory(dir);
                using (var outStream = new FileStream(staged, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.SequentialScan))
                {
                    var buffer = new byte[BufferSize];
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (true)
                    {
                        CheckLength();
                        int want = expectedLength is { } length && length - written < buffer.Length
                            ? (int)(length - written) + 1 : buffer.Length;
                        int n;
                        try
                        {
                            n = content!.Read(written, buffer.AsSpan(0, want));
                        }
                        catch (Exception ex) when (resumable && ex is IOException or UnauthorizedAccessException)
                        {
                            DisposeContent();
                            content = Resume(provider, item, ex, ++failures, revision!.Value, outStream, ref written);
                            if (content is null)
                            {
                                // Skipped: the item fails, and its partial copy goes.
                                throw new SkippedTransferException(ex);
                            }
                            // Resume may have explicitly restarted a changed file. Retain the version supplying this
                            // new copy, rather than attaching the interrupted version to read-back or remote deletion.
                            RetainVersion();
                            continue;
                        }
                        CheckLength();
                        if (n < 0 || n > want || written > long.MaxValue - n || expectedLength is { } expected && n > expected - written)
                            throw new IOException("The source exceeded its copy length or returned an invalid read count.");
                        if (n == 0)
                        {
                            if (expectedLength is { } exact && written != exact)
                                throw new IOException("The source ended before its copy length.");
                            break;
                        }
                        outStream.Write(buffer, 0, n);
                        written += n;
                        Job.AddBytes(n);
                        Job.Throttle(written, clock);
                    }
                    CheckLength();
                    if (content is not Content.ProgressiveContent && content.Length is >= 0 and var finalLength && finalLength != written)
                        throw new IOException("The source ended at a different length than it states.");
                    // Some providers query a server here. One final query covers the whole copy, avoiding a query per buffer.
                    // This is weak provider evidence; it cannot detect a change that restores the original revision.
                    if (content!.GetRevision() != revision) throw new IOException("The source revision changed during copying.");
                    lost = (content as IPartialContent)?.MissingRanges;
                    caveat = (content as IPartialContent)?.Caveat;
                    // Through the open handle: no second open, and later writes on it cannot change the time. The time is the
                    // one the source stated when this content was opened, where it gives one: an FTP server's listing may
                    // carry only the minute, or for older files the day, where its MDTM gives the second (I43).
                    long modified = revision is { ModifiedTicks: > MinimumFileTimeTicks } stated ? stated.ModifiedTicks : item.Modified;
                    if (modified > 0) File.SetLastWriteTimeUtc(outStream.SafeFileHandle, new DateTime(modified, DateTimeKind.Utc));
                }
                if (_originMark is not null && !Fs.WriteOriginMark(staged, _originMark))
                    Issue(IssueSeverity.Warning, item.Name, "Security metadata lost: the download origin (Mark of the Web) could not be written to the extracted file.", StepOutcome.Committed);
                // "Read back and compare content", before the copy takes its name. Recovered content with lost parts reads the
                // same guesses again, so it is not read back; its caveat says what it is.
                if (Job.Request.Options.Verify == VerifyMode.ReadBack && !partial)
                {
                    DisposeContent();
                    if (VerifyCopy(provider, item, staged, written, revision) is not true and var verified)
                    {
                        Job.AddBytes(-written);
                        try { File.Delete(staged); } catch (IOException) { }
                        Job.ItemFailed();
                        Issue(IssueSeverity.Error, item.Name, verified is false
                            ? "Read-back verification found different content; the copy was discarded."
                            : "Not copied: the source could not be read again to verify the copy, so the copy was discarded.", verified is false ? StepOutcome.Failed : StepOutcome.Skipped);
                        return false;
                    }
                }
            }
            finally
            {
                DisposeContent();
            }
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
        catch
        {
            // Keep the original unexpected failure, but release the owned partial copy and its progress first.
            Job.AddBytes(-written);
            try { if (File.Exists(staged)) File.Delete(staged); } catch (IOException) { }
            throw;
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
        Copied?.Invoke(item, revision);
        // A recovered file with lost parts is still worth having, but never passed off as complete (plan §17.1).
        if (caveat is not null)
            Issue(IssueSeverity.Warning, item.Name, caveat + (lost is { Count: > 0 } ? " " + PartialContent.Describe(lost, written) : ""), StepOutcome.Committed);
        else if (lost is { Count: > 0 })
            Issue(IssueSeverity.Warning, item.Name, PartialContent.Describe(lost, written) + " Check the file before relying on it.", StepOutcome.Committed);
        return true;
    }

    /// <summary>
    /// "Read back and compare content" (plan §9.2): the source is read again from its provider — downloaded again from a
    /// server, decompressed again from an archive — and compared with the copy on disk by SHA-256. False: they differ;
    /// null: the source could not be read again and the user skipped.
    /// </summary>
    private bool? VerifyCopy(ResourceProvider provider, ItemRef item, string staged, long length, ContentRevision? copiedRevision)
    {
        Job.SetCurrent(item.Name + " (verifying)");
        Job.AddVerifyTotal(2 * length);
        while (true)
        {
            long verified = 0, streamDone = 0;
            void Progress(long done)
            {
                Job.AddVerified(done - streamDone);
                verified += done - streamDone;
                streamDone = done;
            }
            try
            {
                byte[] theirs;
                using (var again = Content.ProgressiveContent.Sequential(provider.OpenContent(item)) ?? throw new IOException("The item has no readable content any more."))
                    theirs = HashContent(again, length, copiedRevision, Progress);
                streamDone = 0;
                var ours = PortableFileOperations.HashFile(staged, System.Security.Cryptography.HashAlgorithmName.SHA256, Job.Token, Progress, length);
                return theirs.AsSpan().SequenceEqual(ours);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                // A second attempt reads both again: this one's reading does not count.
                Job.AddVerified(-verified);
                var d = Job.Ask(new ErrorRequest("Could not verify the copy", $"{item.Name}: {ErrorText.Describe(ex)}", item.Name, CanRetry: true, ErrorText.Classify(ex)));
                if (d.Action == DecisionAction.Retry) continue;
                Job.AddVerifyTotal(-2 * length);
                if (d.Action == DecisionAction.Skip) return null;
                throw new OperationCanceledException();
            }
            catch
            {
                Job.AddVerified(-verified);
                Job.AddVerifyTotal(-2 * length);
                throw;
            }
        }
    }

    private byte[] HashContent(IContentSource source, long length, ContentRevision? copiedRevision, Action<long> progress)
    {
        var revision = source.GetRevision();
        if (copiedRevision is not null && revision != copiedRevision) throw new IOException("The source revision changed before read-back verification.");
        void CheckVersion()
        {
            Job.Checkpoint();
            long stated = source.Length;
            if (stated >= 0 && stated != length ||
                revision is { } r && r.Length != length ||
                source is IPartialContent partial && (partial.MissingRanges.Count != 0 || partial.Caveat is not null))
                throw new IOException("The source changed or is incomplete during read-back verification.");
        }
        CheckVersion();
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long offset = 0;
        while (true)
        {
            CheckVersion();
            int want = length - offset < buffer.Length ? (int)(length - offset) + 1 : buffer.Length;
            int n = source.Read(offset, buffer.AsSpan(0, want));
            CheckVersion();
            if (n < 0 || n > want || n > length - offset) throw new IOException("The source exceeded its read-back length or returned an invalid read count.");
            if (n == 0)
            {
                if (offset != length) throw new IOException("The source ended before its read-back length.");
                break;
            }
            hash.AppendData(buffer, 0, n);
            offset += n;
            progress(offset);
        }
        // Some real providers query a server for revisions. Check the complete read once more, without adding a
        // network metadata request for every buffer. This remains weak provider evidence, not an atomic snapshot.
        if (source.GetRevision() != revision) throw new IOException("The source revision changed during read-back verification.");
        return hash.GetHashAndReset();
    }

    private const int ResumeCheckBytes = 64 * 1024;

    /// <summary>
    /// After the source failed part way (plan §14: "verify resumable partial content before reuse"): the first failure
    /// retries on its own after a second, later ones ask. The part already copied is kept only when the source is
    /// provably the same file: the same size and time, and its first 64 KiB and the last 64 KiB before the break read the
    /// same. Otherwise the copy starts again from the beginning, and the job says so. Returns null when the user skips.
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
                var resumed = next;
                next = null; // The caller takes ownership only after every admission check succeeds.
                return resumed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex;
                failures = Math.Max(failures + 1, 2);
            }
            finally
            {
                next?.Dispose();
            }
        }
    }

    /// <summary>
    /// The same size and time, and the start and the bytes just before <paramref name="written"/> read the same from both.
    /// The start holds a file's metadata: an iPhone, reconnected, sends some photos again with other bytes there at the
    /// same size and time, which a check of the bytes before the break alone took for the same file. The start is read
    /// first, so a device that reads only forward goes through the part already copied once, as before.
    /// </summary>
    private static bool Unchanged(IContentSource source, ContentRevision revision, FileStream staged, long written)
    {
        if (source.GetRevision() is not { } now || now != revision || written > now.Length) return false;
        int n = (int)Math.Min(ResumeCheckBytes, written);
        int head = (int)Math.Min(ResumeCheckBytes, written - n);
        return ReadsTheSame(source, staged, 0, head) && ReadsTheSame(source, staged, written - n, n);
    }

    private static bool ReadsTheSame(IContentSource source, FileStream staged, long offset, int count)
    {
        if (count == 0) return true;
        var theirs = new byte[count];
        var ours = new byte[count];
        for (int done = 0; done < count;)
        {
            int got = source.Read(offset + done, theirs.AsSpan(done));
            if (got <= 0 || got > count - done) return false;
            done += got;
        }
        staged.Position = offset;
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
        public List<string> Issues { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) => Issues.Add(message);
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
