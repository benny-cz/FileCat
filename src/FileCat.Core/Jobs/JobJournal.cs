using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

/// <summary>
/// Durable per-job journal (plan §9.3, ADR-04 resolved as append-only). Each line is
/// <c>crc32 json</c>; a torn last line is detected and ignored on recovery. Durability is tiered:
/// intents before destructive or externally visible transitions are flushed to disk synchronously,
/// everything else is group-committed. The journal records intent and outcome; it is not undo.
/// </summary>
public sealed class JobJournal : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _lock = new();
    private int _pendingBatched;
    private DateTime _lastFlush = DateTime.UtcNow;
    private int _step;
    private bool _disposed;

    private JobJournal(string path, FileStream stream)
    {
        Path = path;
        _stream = stream;
    }

    public string Path { get; }

    /// <summary>The header lists this many sources; a job with more gets a manifest of all of them.</summary>
    internal const int HeaderSampleSize = 64;

    /// <summary>Sources beyond this count are not listed durably (a rerun then needs a new selection).</summary>
    public const int ManifestLimit = 1_000_000;

    /// <summary>Sidecar listing every source path of a large job, one per line (plan §9.3 durable manifest).</summary>
    public static string ManifestPathOf(string journalPath) => journalPath + ".sources";

    public static JobJournal Create(string directory, Job job)
    {
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, $"job-{job.CreatedUtc:yyyyMMddHHmmss}-{job.ShortId}.fcj");
        var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.None);
        var j = new JobJournal(path, fs);
        var begin = new JsonObject
        {
            ["t"] = "begin",
            ["id"] = job.Id.ToString("N"),
            ["kind"] = job.Kind.ToString(),
            ["title"] = job.Title,
            ["created"] = job.CreatedUtc.ToString("O"),
            ["sources"] = new JsonArray(job.Request.Sources.Take(HeaderSampleSize).Select(s => (JsonNode)(s.FileSystemPath ?? s.ToString())).ToArray()),
            ["sourceCount"] = job.Request.Sources.Count,
            ["dest"] = job.Request.Destination?.Serialize(),
            ["newName"] = job.Request.NewName,
        };
        j.Write(begin, sync: true);
        int count = job.Request.Sources.Count;
        if (count > HeaderSampleSize && count <= ManifestLimit) WriteManifest(ManifestPathOf(path), job.Request.Sources);
        return j;
    }

    /// <summary>
    /// Writes every file-system source path, durably, once. Captured selections are read in bulk. Anything that
    /// cannot be listed exactly (items without a path, names with line breaks) leaves no manifest at all.
    /// </summary>
    private static void WriteManifest(string manifest, IReadOnlyList<ItemRef> sources)
    {
        try
        {
            using (var fs = new FileStream(manifest, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16))
            {
                using (var writer = new StreamWriter(fs, new UTF8Encoding(false), 1 << 16, leaveOpen: true))
                {
                    if (sources is Listing.SelectionSnapshot snapshot)
                    {
                        if (!snapshot.TryWritePaths(writer)) throw new InvalidDataException("Sources without one folder.");
                    }
                    else
                    {
                        foreach (var s in sources)
                        {
                            if (s.FileSystemPath is not { } p || p.AsSpan().IndexOfAny('\r', '\n') >= 0) throw new InvalidDataException("Not listable.");
                            writer.WriteLine(p);
                        }
                    }
                }
                fs.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
            try { File.Delete(manifest); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Records an intent durably before the transition happens; returns the step number.</summary>
    public int Intent(string op, string path, string? target = null, string? staged = null) => Intent(op, path, target, staged, durable: true);

    /// <summary>
    /// Records an intent. Destructive or replacing transitions pass <paramref name="durable"/> (flushed to disk before
    /// returning); creating new items is group-committed (plan §9.3: a sync per small file would dominate the copy).
    /// </summary>
    public int Intent(string op, string path, string? target, string? staged, bool durable)
    {
        int n = Interlocked.Increment(ref _step);
        Write(new JsonObject { ["t"] = "intent", ["n"] = n, ["op"] = op, ["path"] = path, ["target"] = target, ["staged"] = staged }, sync: durable);
        return n;
    }

    /// <summary>
    /// Records that <paramref name="path"/> is renamed to <paramref name="target"/> through the temporary name
    /// <paramref name="via"/> (bulk renames of chains and swaps). Batched: the caller flushes once before the first move,
    /// so after a crash recovery can finish every rename that was under way (<see cref="JournalRecovery.FinishRenames"/>).
    /// </summary>
    public int RenameVia(string path, string target, string via)
    {
        int n = Interlocked.Increment(ref _step);
        Write(new JsonObject { ["t"] = "intent", ["n"] = n, ["op"] = RenameViaOp, ["path"] = path, ["target"] = target, ["via"] = via }, sync: false);
        return n;
    }

    public const string RenameViaOp = "rename-via";

    /// <summary>Makes every record written so far durable (one flush for a batch of intents).</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _stream.Flush(flushToDisk: true);
            _pendingBatched = 0;
            _lastFlush = DateTime.UtcNow;
        }
    }

    public void Done(int step, StepOutcome outcome, string? message = null) =>
        Write(new JsonObject { ["t"] = "done", ["n"] = step, ["outcome"] = outcome.ToString(), ["msg"] = message }, sync: false);

    /// <summary>Directory that may hold staged files of this job (for orphan cleanup after a crash).</summary>
    public void StagingDirectory(string directory) =>
        // Durable once per directory, so recovery can always find staged leftovers of batched copies.
        Write(new JsonObject { ["t"] = "stagedir", ["path"] = directory }, sync: true);

    /// <summary>
    /// Durable once per destination folder that receives direct (unstaged) copies: after a crash, recovery compares the
    /// files the job created there with their sources to find incomplete copies (plan §9.3 per-directory progress).
    /// </summary>
    public void Fill(string sourceDirectory, string destinationDirectory) =>
        Write(new JsonObject { ["t"] = "fill", ["src"] = sourceDirectory, ["dst"] = destinationDirectory }, sync: true);

    public void Note(string message) => Write(new JsonObject { ["t"] = "note", ["msg"] = message }, sync: false);

    public void Finish(JobState state, string summary)
    {
        Write(new JsonObject { ["t"] = "end", ["state"] = state.ToString(), ["summary"] = summary, ["time"] = DateTime.UtcNow.ToString("O") }, sync: true);
        // A job that ended needs no rerun: its manifest goes with it.
        try { File.Delete(ManifestPathOf(Path)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void Write(JsonObject record, bool sync)
    {
        var json = record.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var crc = Crc32.HashToUInt32(bytes);
        var line = Encoding.UTF8.GetBytes($"{crc:x8} {json}\n");
        lock (_lock)
        {
            if (_disposed) return;
            _stream.Write(line);
            _pendingBatched++;
            if (sync || _pendingBatched >= 256 || DateTime.UtcNow - _lastFlush > TimeSpan.FromMilliseconds(500))
            {
                _stream.Flush(flushToDisk: sync);
                _pendingBatched = 0;
                _lastFlush = DateTime.UtcNow;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _stream.Flush(flushToDisk: true); } catch (IOException) { }
            _stream.Dispose();
        }
    }

    // ---- Recovery ------------------------------------------------------------------------------------------

    public static IReadOnlyList<JournalRecord> ReadAll(string path) => ReadRecords(path).ToList();

    /// <summary>CRC-checked records are yielded one at a time so recovery does not retain completed steps.</summary>
    internal static IEnumerable<JournalRecord> ReadRecords(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            // A torn line (crash mid-write) fails its checksum and is skipped; records appended later by
            // recovery still count.
            int sp = line.IndexOf(' ');
            if (sp != 8) continue;
            var json = line[(sp + 1)..];
            if (!uint.TryParse(line.AsSpan(0, 8), System.Globalization.NumberStyles.HexNumber, null, out var crc)) continue;
            if (Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)) != crc) continue;
            JsonObject? node;
            try { node = JsonNode.Parse(json) as JsonObject; }
            catch (JsonException) { continue; }
            if (node is not null) yield return new JournalRecord(node);
        }
    }
}

public sealed class JournalRecord(JsonObject node)
{
    public JsonObject Node { get; } = node;
    public string Type => Node["t"]?.GetValue<string>() ?? string.Empty;
    public string? Get(string key) => Node[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    public int Step => Node["n"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
}

/// <summary>An interrupted job. Sources is a bounded sample; SourceCount is the full count.</summary>
public sealed record InterruptedJob(string JournalPath, string Kind, string Title, DateTime CreatedUtc,
    IReadOnlyList<string> Sources, string? Destination, IReadOnlyList<PendingIntent> OpenIntents, IReadOnlyList<string> StagingDirectories, int CompletedSteps, int SourceCount)
{
    /// <summary>Folders that received direct copies (source folder, destination folder), bounded.</summary>
    public IReadOnlyList<FillDirectory> FillDirectories { get; init; } = [];

    /// <summary>The journal named more such folders than <see cref="FillDirectories"/> holds: some were not checked.</summary>
    public bool FillDirectoriesCut { get; init; }

    /// <summary>The manifest listing every source, when the job had more than the header lists.</summary>
    public string? ManifestPath { get; init; }

    /// <summary>Every source path is known: from the header when it lists them all, otherwise from the manifest.</summary>
    public bool SourcesKnown => SourceCount <= Sources.Count || ManifestPath is not null;
}

public sealed record FillDirectory(string Source, string Destination);

/// <summary>
/// A file an interrupted direct copy provably left incomplete: shorter than <paramref name="Source"/> and holding exactly
/// its first bytes. Size and times are those seen at the review; deletion checks them again.
/// </summary>
public sealed record IncompleteCopy(string Path, string Source, long Length, DateTime ModifiedUtc, DateTime CreatedUtc);

/// <param name="Incomplete">Copies cut short by the interruption: they may be deleted.</param>
/// <param name="Differing">Files the job may have created that differ from their source in any other way (changed since,
/// or copied from a source that changed): never deleted by recovery, only named.</param>
/// <param name="LimitReached">Not every file was checked or listed: more were found than the limit, or the journal named
/// more folders than recovery reads.</param>
public sealed record CopyReview(IReadOnlyList<IncompleteCopy> Incomplete, IReadOnlyList<string> Differing, bool LimitReached);

/// <summary>An intent recorded without an outcome: reality must be inspected before anything is replayed.</summary>
public sealed record PendingIntent(int Step, string Operation, string Path, string? Target, string? Staged)
{
    /// <summary>The temporary name of a <see cref="JobJournal.RenameViaOp"/> step.</summary>
    public string? Via { get; init; }
}

public static partial class JournalRecovery
{
    /// <summary>
    /// Staged files are named ".fc-{job}-{n}.tmp". No '~': .NET expands any path with one as a possible 8.3 short name,
    /// which cost about 0.2 ms per file operation on Windows.
    /// </summary>
    public const string StagedPrefix = ".fc-";

    /// <summary>The prefix earlier versions staged with; their leftovers are still recognized.</summary>
    public const string LegacyStagedPrefix = ".~fc-";

    /// <summary>Finds interrupted jobs and prunes finished journals beyond the retention limit.</summary>
    public static IReadOnlyList<InterruptedJob> Scan(string directory, int keepFinished = 200, TimeSpan? maxAge = null)
    {
        var result = new List<InterruptedJob>();
        if (!Directory.Exists(directory)) return result;
        var files = new DirectoryInfo(directory).GetFiles("job-*.fcj").OrderByDescending(f => f.Name).ToList();
        int finished = 0;
        var cutoff = DateTime.UtcNow - (maxAge ?? TimeSpan.FromDays(30));
        foreach (var f in files)
        {
            // A job still running, here or in another FileCat on the same profile, is not interrupted: nothing of it may be
            // reviewed, deleted, renamed or closed (DPI P04), nor its journal pruned.
            if (InUse(f.FullName)) continue;
            JournalRecord? begin = null;
            bool ended = false;
            int completed = 0;
            var open = new Dictionary<int, PendingIntent>();
            var directories = new HashSet<string>(StringComparer.Ordinal);
            var fills = new List<FillDirectory>();
            bool fillsCut = false;
            try
            {
                foreach (var record in JobJournal.ReadRecords(f.FullName))
                {
                    switch (record.Type)
                    {
                        case "begin": begin ??= record; break;
                        case "end": ended = true; break;
                        case "intent":
                            if (record.Step > 0)
                                open[record.Step] = new PendingIntent(record.Step, record.Get("op") ?? "",
                                    record.Get("path") ?? "", record.Get("target"), record.Get("staged")) { Via = record.Get("via") };
                            break;
                        case "done":
                            if (record.Step > 0) { open.Remove(record.Step); completed++; }
                            break;
                        case "stagedir":
                            if (record.Get("path") is { Length: > 0 } path) directories.Add(path);
                            break;
                        case "fill":
                            if (record.Get("src") is not { Length: > 0 } fs || record.Get("dst") is not { Length: > 0 } fd) break;
                            if (fills.Count < 10_000) fills.Add(new FillDirectory(fs, fd));
                            else fillsCut = true;
                            break;
                    }
                }
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            var manifest = JobJournal.ManifestPathOf(f.FullName);
            if (ended)
            {
                finished++;
                if (finished > keepFinished || f.LastWriteTimeUtc < cutoff) TryDelete(f.FullName);
                TryDelete(manifest);
                continue;
            }
            if (begin is null)
            {
                TryDelete(f.FullName);
                continue;
            }
            var sources = begin.Node["sources"] is JsonArray arr ? arr.Select(x => x?.GetValue<string>() ?? "").ToList() : [];
            int sourceCount = begin.Node["sourceCount"] is JsonValue countValue && countValue.TryGetValue<int>(out int declared)
                ? declared : sources.Count;
            DateTime.TryParse(begin.Get("created"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var created);
            result.Add(new InterruptedJob(f.FullName, begin.Get("kind") ?? "?", begin.Get("title") ?? "Operation", created, sources,
                begin.Get("dest"), open.Values.OrderBy(i => i.Step).ToList(), directories.ToList(), completed, sourceCount)
            {
                FillDirectories = fills,
                FillDirectoriesCut = fillsCut,
                ManifestPath = File.Exists(manifest) ? manifest : null,
            });
        }
        return result;
    }

    /// <summary>
    /// Whether a running job holds the journal: its writer keeps it open from the first record to the last and shares
    /// only reading, which on Linux and macOS .NET backs with an advisory lock, so asking for it alone fails while it
    /// runs. A FileCat that crashed holds nothing. (On network home folders .NET takes no such lock; such a journal reads
    /// as interrupted, as before.)
    /// </summary>
    internal static bool InUse(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>Potential staged paths, bounded by <paramref name="limit"/>. Review their current bytes before deletion.</summary>
    public static IReadOnlyList<string> FindStagedLeftovers(InterruptedJob job, int limit = 1000, Action? check = null)
    {
        var id = Path.GetFileNameWithoutExtension(job.JournalPath);
        var shortId = id.Length >= 8 ? id[^8..] : id;
        var list = new List<string>();
        foreach (var dir in job.StagingDirectories)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var prefix in new[] { StagedPrefix, LegacyStagedPrefix })
                    foreach (var path in Directory.EnumerateFiles(dir, prefix + shortId + "-*"))
                    {
                        check?.Invoke();
                        if (list.Count >= limit) return list;
                        if (!list.Contains(path, PathUtil.SafetyComparer)) list.Add(path);
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var intent in job.OpenIntents)
        {
            check?.Invoke();
            if (list.Count >= limit) return list;
            if (intent.Staged is { } s && File.Exists(s) && !list.Contains(s)) list.Add(s);
        }
        return list;
    }

    public const long StagedReviewByteLimit = 64L * 1024 * 1024;
    public sealed record StagedFileReview(string Path, long Length, DateTime ModifiedUtc, DateTime CreatedUtc, string Sha256);

    /// <summary>
    /// Captures one finite, complete staged file through its registered provider. A name alone never authorizes
    /// deletion. Unknown, partial, linked, changing or over-budget content stays unreviewed and must be kept.
    /// This is a conservative byte/metadata check, not an atomic native file-identity guarantee.
    /// </summary>
    public static StagedFileReview? ReviewStagedFile(string path, ResourceProvider provider, Action? check = null, long byteLimit = StagedReviewByteLimit)
    {
        void Check() => check?.Invoke();
        try
        {
            Check(); var before = new FileInfo(path);
            if (!before.Exists || before.LinkTarget is not null || (before.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            long length = before.Length; var modified = before.LastWriteTimeUtc; var created = before.CreationTimeUtc;
            if (length < 0 || length > byteLimit) return null;
            Check(); using var source = ProgressiveContent.Sequential(provider.OpenContent(ItemRef.ForFileSystemPath(path, EntryKind.File)));
            Check(); if (source is null || source.Length != length || !Complete(source)) return null;
            var revision = source.GetRevision(); Check();
            if (revision is { } r && r.Length != length) return null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var bytes = new byte[64 * 1024]; long offset = 0;
            while (true)
            {
                Check(); if (!Complete(source)) return null;
                int want = (int)Math.Min(bytes.Length, length - offset + 1);
                int n = source.Read(offset, bytes.AsSpan(0, want)); Check();
                if (!Complete(source) || n < 0 || n > want || n > length - offset) return null;
                if (n == 0) { if (offset != length) return null; break; }
                hash.AppendData(bytes, 0, n); offset += n;
            }
            Check(); var after = new FileInfo(path);
            if (!after.Exists || after.LinkTarget is not null || (after.Attributes & FileAttributes.ReparsePoint) != 0 ||
                after.Length != length || after.LastWriteTimeUtc != modified || after.CreationTimeUtc != created ||
                source.Length != length || source.GetRevision() != revision || !Complete(source)) return null;
            Check(); return new(path, length, modified, created, Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }

        static bool Complete(IContentSource source) => source is not IPartialContent p || p.MissingRanges.Count == 0 && p.Caveat is null;
    }

    /// <summary>Rechecks the complete reviewed version immediately before deletion; returns false if it must be kept.</summary>
    public static bool DeleteReviewedStagedFile(StagedFileReview reviewed, ResourceProvider provider, Action? check = null)
    {
        if (ReviewStagedFile(reviewed.Path, provider, check) != reviewed) return false;
        check?.Invoke();
        try { File.Delete(reviewed.Path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Renames that were under way: the item still has its temporary name (<see cref="PendingIntent.Via"/>).</summary>
    public static IReadOnlyList<PendingIntent> FindRenameLeftovers(InterruptedJob job) =>
        job.OpenIntents.Where(i => i.Operation == JobJournal.RenameViaOp && i.Via is { } via && (File.Exists(via) || Directory.Exists(via))).ToList();

    /// <summary>
    /// Immediately reviews and finishes finite temporary items, restoring the original name when the new one is
    /// taken. Callers with a confirmation dialog must retain ReviewRename's result across that dialog and use
    /// FinishReviewedRename. Unreviewable or unresolved items keep their temporary names and are reported.
    /// </summary>
    public static IReadOnlyList<string> FinishRenames(IReadOnlyList<PendingIntent> leftovers, out int finished)
    {
        finished = 0;
        var report = new List<string>();
        var files = new PortableFileOperations(); var provider = new LocalFileSystemProvider();
        foreach (var r in leftovers)
        {
            var reviewed = ReviewRename(r, files, provider);
            if (reviewed is null) { report.Add($"{r.Via}: this temporary item could not be completely reviewed; it is kept."); continue; }
            var outcome = FinishReviewedRename(reviewed, files, provider); finished += outcome.Finished; report.AddRange(outcome.Report);
        }
        return report;
    }

    /// <summary>The paths of <see cref="ReviewCopies"/>'s incomplete copies: the files recovery may delete.</summary>
    public static IReadOnlyList<string> FindIncompleteCopies(InterruptedJob job, int limit = 1000) =>
        ReviewCopies(job, limit).Incomplete.Select(c => c.Path).ToList();

    /// <summary>
    /// Sorts the files the interrupted job's direct copies may have left (created after the job started, named like a
    /// file in a source folder the job copied from into that folder). A copy cut short by the interruption is shorter
    /// than its source and holds exactly the source's first bytes (direct copies are never pre-sized, and the copy
    /// engine sets the time last): only such a file is <see cref="CopyReview.Incomplete"/>, and deleting it loses nothing
    /// the source does not hold. A file that differs in any other way (changed since, by the user or anything else, or
    /// copied from a source that changed since) is <see cref="CopyReview.Differing"/>: it is reported and never deleted.
    /// Bounded; reads at most the first megabyte of a file and its source; deletes nothing itself (release issue I19).
    /// </summary>
    public static CopyReview ReviewCopies(InterruptedJob job, int limit = 1000, Action? check = null)
    {
        var incomplete = new List<IncompleteCopy>();
        var differing = new List<string>();
        bool limitReached = job.FillDirectoriesCut;
        var since = job.CreatedUtc.AddSeconds(-2);
        int inspected = 0;
        foreach (var group in job.FillDirectories.GroupBy(f => f.Destination, PathUtil.SafetyComparer))
        {
            check?.Invoke();
            var sourceFolders = group.Select(f => f.Source).Distinct(PathUtil.SafetyComparer).ToList();
            IEnumerable<FileInfo> files;
            try
            {
                if (!Directory.Exists(group.Key)) continue;
                files = new DirectoryInfo(group.Key).EnumerateFiles();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            try
            {
                foreach (var dst in files)
                {
                    check?.Invoke();
                    if (inspected++ >= limit) return new CopyReview(incomplete, differing, true);
                    if (dst.CreationTimeUtc < since || dst.LinkTarget is not null || (dst.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        dst.Name.StartsWith(StagedPrefix, StringComparison.Ordinal) || dst.Name.StartsWith(LegacyStagedPrefix, StringComparison.Ordinal)) continue;
                    var sources = sourceFolders.Select(folder => new FileInfo(Path.Combine(folder, dst.Name)))
                        .Where(s => s.Exists && s.LinkTarget is null).ToList();
                    if (sources.Count == 0) continue;
                    // Complete: the same size and time as a source it could have come from.
                    if (sources.Any(s => s.Length == dst.Length && Math.Abs((s.LastWriteTimeUtc - dst.LastWriteTimeUtc).TotalSeconds) <= 2)) continue;
                    var (content, source) = Classify(dst, sources);
                    if (content == CopyContent.Complete) continue;
                    if (content == CopyContent.Differing)
                    {
                        if (differing.Count >= limit) { limitReached = true; continue; }
                        differing.Add(dst.FullName);
                    }
                    else
                    {
                        if (incomplete.Count >= limit) { limitReached = true; continue; }
                        incomplete.Add(new IncompleteCopy(dst.FullName, source!, dst.Length, dst.LastWriteTimeUtc, dst.CreationTimeUtc));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new CopyReview(incomplete, differing, limitReached);

        (CopyContent Content, string? Source) Classify(FileInfo dst, IReadOnlyList<FileInfo> sources)
        {
            // Direct copies are smaller than the limit, and so is anything cut short from one.
            if (dst.Length >= TransferExecutor.DirectCopyLimit) return (CopyContent.Differing, null);
            foreach (var s in sources)
            {
                check?.Invoke();
                if (s.Length < dst.Length || !StartsWithSameBytes(dst.FullName, dst.Length, s.FullName, check)) continue;
                // The same bytes as its source with only the time not set yet is a complete copy.
                return s.Length == dst.Length ? (CopyContent.Complete, null) : (CopyContent.Incomplete, s.FullName);
            }
            return (CopyContent.Differing, null);
        }
    }

    private enum CopyContent { Complete, Incomplete, Differing }

    /// <summary>
    /// Deletes the incomplete copies the user confirmed, each only after checking again that it is unchanged since the
    /// review (size, times) and still holds exactly its source's first bytes; anything else is kept and named in
    /// <paramref name="kept"/>. Links are never deleted here. Returns how many files were deleted.
    /// </summary>
    public static int DeleteIncompleteCopies(IReadOnlyList<IncompleteCopy> copies, out IReadOnlyList<string> kept, Action? check = null)
    {
        int deleted = 0;
        var keptList = new List<string>();
        foreach (var c in copies)
        {
            check?.Invoke();
            try
            {
                var now = new FileInfo(c.Path);
                var source = new FileInfo(c.Source);
                if (!now.Exists || now.LinkTarget is not null || (now.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    now.Length != c.Length || now.LastWriteTimeUtc != c.ModifiedUtc || now.CreationTimeUtc != c.CreatedUtc ||
                    !source.Exists || source.LinkTarget is not null || source.Length <= c.Length ||
                    !StartsWithSameBytes(c.Path, c.Length, source.FullName, check))
                {
                    if (now.Exists) keptList.Add(c.Path);
                    continue;
                }
                check?.Invoke(); File.Delete(c.Path);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                keptList.Add(c.Path);
            }
        }
        kept = keptList;
        return deleted;
    }

    /// <summary>
    /// True when <paramref name="path"/> is <paramref name="length"/> bytes long and those bytes are exactly the first
    /// bytes of <paramref name="source"/>. Files are opened for reading only, never locking out other programs.
    /// </summary>
    private static bool StartsWithSameBytes(string path, long length, string source, Action? check = null)
    {
        if (length > TransferExecutor.DirectCopyLimit) return false;
        try
        {
            using var a = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            using var b = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            if (a.Length != length || b.Length < length) return false;
            var x = new byte[64 * 1024];
            var y = new byte[64 * 1024];
            long left = length;
            while (left > 0)
            {
                check?.Invoke();
                int want = (int)Math.Min(x.Length, left);
                a.ReadExactly(x, 0, want);
                b.ReadExactly(y, 0, want);
                check?.Invoke();
                if (!x.AsSpan(0, want).SequenceEqual(y.AsSpan(0, want))) return false;
                left -= want;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Every source path of the job (header or manifest), or null when they are not all known.</summary>
    public static IReadOnlyList<string>? LoadSources(InterruptedJob job)
    {
        if (job.SourceCount <= job.Sources.Count) return job.Sources;
        if (job.ManifestPath is not { } manifest) return null;
        try
        {
            var list = new List<string>(Math.Min(job.SourceCount, JobJournal.ManifestLimit));
            foreach (var line in File.ReadLines(manifest))
            {
                if (line.Length > 0) list.Add(line);
                if (list.Count > JobJournal.ManifestLimit) return null;
            }
            // A manifest cut short (a crash while it was written) is not the job's selection.
            return list.Count == job.SourceCount ? list : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Marks the journal as reconciled so it no longer appears as interrupted.</summary>
    public static void Close(InterruptedJob job, string resolution) => TryClose(job, resolution);

    /// <summary>Reconciles durably before removing the source manifest. A failed append keeps the manifest.</summary>
    public static bool TryClose(InterruptedJob job, string resolution)
    {
        try
        {
            // Start on a fresh line in case the crash left a torn record without a newline.
            bool needsNewline;
            using (var fs = new FileStream(job.JournalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                needsNewline = fs.Length > 0 && SeekLastByte(fs) != '\n';
            }
            var record = Line(new JsonObject { ["t"] = "end", ["state"] = "Interrupted", ["summary"] = resolution, ["time"] = DateTime.UtcNow.ToString("O") });
            using (var fs = new FileStream(job.JournalPath, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                fs.Seek(0, SeekOrigin.End);
                fs.Write(Encoding.UTF8.GetBytes((needsNewline ? "\n" : string.Empty) + record));
                fs.Flush(flushToDisk: true);
            }
            TryDelete(JobJournal.ManifestPathOf(job.JournalPath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }

        static int SeekLastByte(FileStream fs)
        {
            fs.Seek(-1, SeekOrigin.End);
            return fs.ReadByte();
        }
    }

    /// <summary>Rejects a journal that ended, disappeared or is held by a live writer.</summary>
    public static bool IsInterrupted(InterruptedJob job)
    {
        if (InUse(job.JournalPath)) return false;
        try
        {
            bool began = false;
            foreach (var record in JobJournal.ReadRecords(job.JournalPath))
            {
                if (record.Type == "end") return false;
                if (record.Type == "begin") began = true;
            }
            return began;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string Line(JsonObject o)
    {
        var json = o.ToJsonString();
        return $"{Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)):x8} {json}\n";
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
