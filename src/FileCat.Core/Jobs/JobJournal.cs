
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
            ["sources"] = new JsonArray(job.Request.Sources.Take(64).Select(s => (JsonNode)(s.FileSystemPath ?? s.ToString())).ToArray()),
            ["sourceCount"] = job.Request.Sources.Count,
            ["dest"] = job.Request.Destination?.Serialize(),
            ["newName"] = job.Request.NewName,
        };
        j.Write(begin, sync: true);
        return j;
    }

    /// <summary>Records an intent durably before the transition happens; returns the step number.</summary>
    public int Intent(string op, string path, string? target = null, string? staged = null)
    {
        int n = Interlocked.Increment(ref _step);
        Write(new JsonObject { ["t"] = "intent", ["n"] = n, ["op"] = op, ["path"] = path, ["target"] = target, ["staged"] = staged }, sync: true);
        return n;
    }

    public void Done(int step, StepOutcome outcome, string? message = null) =>
        Write(new JsonObject { ["t"] = "done", ["n"] = step, ["outcome"] = outcome.ToString(), ["msg"] = message }, sync: false);

    /// <summary>Directory that may hold staged files of this job (for orphan cleanup after a crash).</summary>
    public void StagingDirectory(string directory) =>
        Write(new JsonObject { ["t"] = "stagedir", ["path"] = directory }, sync: false);

    public void Note(string message) => Write(new JsonObject { ["t"] = "note", ["msg"] = message }, sync: false);

    public void Finish(JobState state, string summary)
    {
        Write(new JsonObject { ["t"] = "end", ["state"] = state.ToString(), ["summary"] = summary, ["time"] = DateTime.UtcNow.ToString("O") }, sync: true);
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

    public static IReadOnlyList<JournalRecord> ReadAll(string path)
    {
        var records = new List<JournalRecord>();
        foreach (var line in File.ReadLines(path))
        {
            // A torn line (crash mid-write) fails its checksum and is skipped; records appended later by
            // recovery still count.
            int sp = line.IndexOf(' ');
            if (sp != 8) continue;
            var json = line[(sp + 1)..];
            if (!uint.TryParse(line.AsSpan(0, 8), System.Globalization.NumberStyles.HexNumber, null, out var crc)) continue;
            if (Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)) != crc) continue;
            try
            {
                if (JsonNode.Parse(json) is JsonObject o) records.Add(new JournalRecord(o));
            }
            catch (JsonException)
            {
            }
        }
        return records;
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
    IReadOnlyList<string> Sources, string? Destination, IReadOnlyList<PendingIntent> OpenIntents, IReadOnlyList<string> StagingDirectories, int CompletedSteps, int SourceCount);

/// <summary>An intent recorded without an outcome: reality must be inspected before anything is replayed.</summary>
public sealed record PendingIntent(int Step, string Operation, string Path, string? Target, string? Staged);

public static class JournalRecovery
{
    public const string StagedPrefix = ".~fc-";

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
            IReadOnlyList<JournalRecord> records;
            try { records = JobJournal.ReadAll(f.FullName); }
            catch (IOException) { continue; }
            if (records.Any(r => r.Type == "end"))
            {
                finished++;
                if (finished > keepFinished || f.LastWriteTimeUtc < cutoff) TryDelete(f.FullName);
                continue;
            }
            var begin = records.FirstOrDefault(r => r.Type == "begin");
            if (begin is null)
            {
                TryDelete(f.FullName);
                continue;
            }
            var done = records.Where(r => r.Type == "done").Select(r => r.Step).ToHashSet();
            var open = records.Where(r => r.Type == "intent" && !done.Contains(r.Step))
                .Select(r => new PendingIntent(r.Step, r.Get("op") ?? "", r.Get("path") ?? "", r.Get("target"), r.Get("staged"))).ToList();
            var sources = begin.Node["sources"] is JsonArray arr ? arr.Select(x => x?.GetValue<string>() ?? "").ToList() : [];
            int sourceCount = begin.Node["sourceCount"] is JsonValue countValue && countValue.TryGetValue<int>(out int declared)
                ? declared : sources.Count;
            var dirs = records.Where(r => r.Type == "stagedir").Select(r => r.Get("path") ?? "").Where(p => p.Length > 0).Distinct().ToList();
            DateTime.TryParse(begin.Get("created"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var created);
            result.Add(new InterruptedJob(f.FullName, begin.Get("kind") ?? "?", begin.Get("title") ?? "Operation", created, sources,
                begin.Get("dest"), open, dirs, done.Count, sourceCount));
        }
        return result;
    }

    /// <summary>Staged partial files that belong to the interrupted job (safe to delete: never published).</summary>
    public static IReadOnlyList<string> FindStagedLeftovers(InterruptedJob job)
    {
        var id = Path.GetFileNameWithoutExtension(job.JournalPath);
        var shortId = id.Length >= 8 ? id[^8..] : id;
        var list = new List<string>();
        foreach (var dir in job.StagingDirectories)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                list.AddRange(Directory.EnumerateFiles(dir, StagedPrefix + shortId + "-*"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var intent in job.OpenIntents)
        {
            if (intent.Staged is { } s && File.Exists(s) && !list.Contains(s)) list.Add(s);
        }
        return list;
    }

    /// <summary>Marks the journal as reconciled so it no longer appears as interrupted.</summary>
    public static void Close(InterruptedJob job, string resolution)
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
            File.AppendAllText(job.JournalPath, (needsNewline ? "\n" : string.Empty) + record);
        }
        catch (IOException) { }

        static int SeekLastByte(FileStream fs)
        {
            fs.Seek(-1, SeekOrigin.End);
            return fs.ReadByte();
        }
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
