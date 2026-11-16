using System.Formats.Tar;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

/// <summary>
/// FZ10 (plan §13.3), <c>publication-races</c>: the real Unix archive job route (review, capture, journaled preparation
/// with in-process worker roles, exchange or creation claim, reconciliation, exact cleanup or discard) runs to its end
/// while the input applies concurrent file-system actions immediately before and after each durable transaction,
/// creation-cleanup and discard record, as another program of the same user could. Oracle: only typed refusals; no
/// concurrent version is ever lost, and the original only when the update was published; a concurrent version FileCat
/// moved from the archive name is reported, never silent; a job that stopped before publication never leaves its output
/// at the name; the name never holds anything torn. One process, real file-system calls: this complements, and does not
/// replace, the crash matrices and does not prove kernel behavior.
/// </summary>
static class PublicationRaces
{
    internal const int MaximumActions = 6;
    private static readonly byte[] Added = Encoding.UTF8.GetBytes("added payload\n");
    private static readonly (string Name, byte[]? Data)[] OriginalMembers =
        [("one.txt", Encoding.UTF8.GetBytes("first\n")), ("dir/", null), ("dir/two.txt", Encoding.UTF8.GetBytes("second\n"))];

    internal enum Act : byte
    {
        None, ReplaceTarget, MoveTargetAway, AppendToTarget, TouchTarget, CreateAtTarget, ExtraInPrivate, RenamePrivate,
        ShareFolder, ReplaceStage, HardLinkTarget, SymlinkAtTarget,
    }
    private const int ActCount = 12;

    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("FZ10 drives the Unix publication route.");
        var reader = new FuzzReader(data);
        bool update = reader.End || (reader.Byte() & 1) == 1;
        var actions = new List<(int At, Act Kind, byte Argument)>();
        while (!reader.End && actions.Count < MaximumActions)
            actions.Add((reader.Byte() % 64, (Act)(reader.Byte() % ActCount), reader.Byte()));
        using var world = new World(update);
        return world.Run(actions);
    }

    /// <summary>Each action alone at each early point, for creation and update, plus a few pairs around publication.</summary>
    internal static IEnumerable<(string Name, byte[] Seed)> Seeds()
    {
        yield return ("create-plain.bin", [0]);
        yield return ("update-plain.bin", [1]);
        for (int kind = 1; kind < ActCount; kind++)
            for (int at = 0; at < 24; at += 3)
            {
                yield return ($"create-{(Act)kind}-{at}.bin", [0, (byte)at, (byte)kind, (byte)(kind * 7 + at)]);
                yield return ($"update-{(Act)kind}-{at}.bin", [1, (byte)at, (byte)kind, (byte)(kind * 11 + at)]);
            }
        yield return ("update-replace-then-extra.bin", [1, 6, (byte)Act.ReplaceTarget, 1, 9, (byte)Act.ExtraInPrivate, 2]);
        yield return ("update-link-then-share.bin", [1, 4, (byte)Act.HardLinkTarget, 3, 10, (byte)Act.ShareFolder, 4]);
        yield return ("create-occupy-then-move.bin", [0, 5, (byte)Act.CreateAtTarget, 5, 12, (byte)Act.MoveTargetAway, 6]);
    }

    private sealed class World : IDisposable
    {
        // Link-free, as the native route pins every folder without following links (macOS /var is a link to /private/var).
        private static readonly string Base = Path.Combine(ArchiveLocalPath.ResolveDirectory(Path.GetTempPath()),
            "filecat-fz10-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        private readonly bool update;
        private readonly string root, box, keep, journals, target, input;
        // Every version another program wrote (content hash -> where it was last put by that program).
        private readonly Dictionary<string, string> concurrent = new(StringComparer.Ordinal);
        private string? originalHash;
        private bool originalDestroyedByOthers;
        private int point, versions, publishingPoint = -1, lastTargetAction = -1;
        private readonly List<string> points = [];

        internal World(bool update)
        {
            this.update = update;
            root = Path.Combine(Base, Guid.NewGuid().ToString("N"));
            box = Path.Combine(root, "box"); keep = Path.Combine(root, "keep"); journals = Path.Combine(root, "journals");
            target = Path.Combine(box, "a.tar"); input = Path.Combine(root, "src", "added.txt");
            foreach (string folder in new[] { Base, root, box, keep, Path.Combine(root, "src") })
            { Directory.CreateDirectory(folder); File.SetUnixFileMode(folder, (UnixFileMode)0x1c0); }
            File.WriteAllBytes(input, Added); File.SetLastWriteTimeUtc(input, DateTime.UnixEpoch.AddSeconds(1700000100));
            if (!update) return;
            using (var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: false))
                foreach (var (name, content) in OriginalMembers)
                {
                    var entry = new PaxTarEntry(content is null ? TarEntryType.Directory : TarEntryType.RegularFile, name)
                    { ModificationTime = DateTimeOffset.FromUnixTimeSeconds(1700000000), Mode = content is null ? (UnixFileMode)0x1ed : (UnixFileMode)0x1a4 };
                    if (content is not null) entry.DataStream = new MemoryStream(content, false);
                    writer.WriteEntry(entry);
                }
            originalHash = Hash(File.ReadAllBytes(target));
        }

        internal bool Run(List<(int At, Act Kind, byte Argument)> actions)
        {
            var ct = CancellationToken.None; var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
            IArchiveWorkerProcess Assess(Guid nonce, TimeSpan _) => new InProcessWorker(nonce, verification: false, ArchiveWorkerProtocol.SecretVersion);
            IArchiveWorkerProcess Transform(Guid nonce, TimeSpan _) => InProcessWorker.Transform(nonce, ArchiveWorkerProtocol.SecretVersion);
            IArchiveWorkerProcess Verify(Guid nonce, TimeSpan _) => new InProcessWorker(nonce, verification: true, ArchiveWorkerProtocol.SecretVersion);
            var backend = UnixArchiveJobBackend.Create(ArchiveWriteFormat.Tar, Assess, Transform, Verify);
            ArchivePlan plan;
            var change = new ArchiveChange(ArchiveChangeKind.AddFile, "added.txt", input);
            if (update)
            {
                var assessor = new ArchiveAssessmentClient(Assess, protocolVersion: ArchiveWorkerProtocol.SecretVersion);
                var reviewed = new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct).GetAwaiter().GetResult();
                plan = reviewed.CreatePlan([change]);
            }
            else plan = new ArchivePlan(target, null, [change], Profile: profile);
            // As JobManager.SubmitArchive does: the archive's folder is resolved once, at submission.
            plan = plan with { ArchivePath = ArchiveLocalPath.ResolveParent(plan.ArchivePath) };
            if (plan.ArchivePath != target) throw new InvalidOperationException("FZ10 fixture: the archive path is not link-free.");
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "FZ10 publication race", "owned", [], []);
            Exception? failure = null;
            using (var journal = JobJournal.Create(journals, job))
            {
                var queue = actions.OrderBy(action => action.At).ToList();
                ActingStream.Install(journal, name =>
                {
                    points.Add(name);
                    if (publishingPoint < 0 && name == "before:T:Publishing") publishingPoint = point;
                    while (queue.Count != 0 && queue[0].At <= point) { var next = queue[0]; queue.RemoveAt(0); Apply(next.Kind, next.Argument); }
                    point++;
                });
                try { backend.Execute(new ArchiveJobExecution(job, plan, null, new PortableFileOperations(), journal)); }
                catch (Exception error) when (error is ArchiveWriteException or IOException or UnauthorizedAccessException or OperationCanceledException)
                { failure = error; }
                catch (Exception error) { throw Violation("an untyped failure escaped the job: " + error.GetType().Name + ": " + error.Message, error); }
            }
            if (Environment.GetEnvironmentVariable("FILECAT_FZ10_TRACE") == "1")
                Console.Error.WriteLine($"FZ10 {(update ? "update" : "creation")}: {(failure is null ? "published" : failure.GetType().Name + ": " +
                    (failure.Data[ArchiveWriteMessages.UserMessageKey] ?? "") + " | " + failure)}; issues: " +
                    string.Join(" | ", job.Issues.Select(issue => issue.Severity + " " + issue.Message)) + "; points: " + string.Join(" ", points));
            Check(job, failure);
            return failure is null;
        }

        private void Apply(Act kind, byte argument)
        {
            try
            {
                switch (kind)
                {
                    case Act.ReplaceTarget:
                        MoveAway(target); PlaceNew(target, argument); lastTargetAction = point; break;
                    case Act.MoveTargetAway:
                        if (MoveAway(target)) lastTargetAction = point; break;
                    case Act.AppendToTarget:
                        if (!IsRegular(target)) break;
                        string before = Hash(File.ReadAllBytes(target));
                        using (var stream = new FileStream(target, FileMode.Append, FileAccess.Write)) stream.Write([argument, (byte)'\n']);
                        // This program changed that version itself; only the result is its to keep.
                        if (before == originalHash) originalDestroyedByOthers = true;
                        if (concurrent.Remove(before)) { }
                        concurrent[Hash(File.ReadAllBytes(target))] = target; lastTargetAction = point; break;
                    case Act.TouchTarget:
                        if (IsRegular(target)) { File.SetLastWriteTimeUtc(target, DateTime.UnixEpoch.AddSeconds(1600000000 + argument)); lastTargetAction = point; }
                        break;
                    case Act.CreateAtTarget:
                        if (!Exists(target)) { PlaceNew(target, argument); lastTargetAction = point; }
                        break;
                    case Act.ExtraInPrivate:
                        foreach (string folder in PrivateFolders()) PlaceNew(Path.Combine(folder, "fz10-extra-" + versions), argument);
                        break;
                    case Act.RenamePrivate:
                        if (PrivateFolders().FirstOrDefault() is { } first) Directory.Move(first, Path.Combine(box, "fz10-moved-" + versions++));
                        break;
                    case Act.ShareFolder:
                        File.SetUnixFileMode(box, (UnixFileMode)0x1ff); break;
                    case Act.ReplaceStage:
                        if (PrivateFolders().SelectMany(folder => Directory.EnumerateFiles(folder)).FirstOrDefault(IsRegular) is { } stage)
                        {
                            // FileCat's own object leaves through another program's rename; what that program puts there is its version.
                            File.Move(stage, Path.Combine(keep, "stage-" + versions++)); PlaceNew(stage, argument);
                        }
                        break;
                    case Act.HardLinkTarget:
                        if (IsRegular(target))
                        {
                            string link = Path.Combine(keep, "link-" + versions++);
                            if (Native.Link(target, link) == 0) { concurrent[Hash(File.ReadAllBytes(link))] = link; lastTargetAction = point; }
                        }
                        break;
                    case Act.SymlinkAtTarget:
                        if (MoveAway(target) || !Exists(target))
                        {
                            string other = Path.Combine(keep, "linked-" + versions++); File.WriteAllBytes(other, Content(argument));
                            concurrent[Hash(File.ReadAllBytes(other))] = other;
                            File.CreateSymbolicLink(target, other); lastTargetAction = point;
                        }
                        break;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // Another program's step may fail too.
        }

        private bool MoveAway(string path)
        {
            if (!Exists(path)) return false;
            string moved = Path.Combine(keep, "v" + versions++);
            File.Move(path, moved);
            if (!new FileInfo(moved).Attributes.HasFlag(FileAttributes.ReparsePoint) && IsRegular(moved))
            {
                string hash = Hash(File.ReadAllBytes(moved));
                if (concurrent.ContainsKey(hash)) concurrent[hash] = moved;
            }
            return true;
        }

        private void PlaceNew(string path, byte argument)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".fz10-" + versions++ + ".tmp");
            byte[] content = Content(argument); File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: false);
            concurrent[Hash(content)] = path;
        }

        private byte[] Content(byte argument) => Encoding.UTF8.GetBytes($"another program's version {versions} ({argument})\n");

        private IEnumerable<string> PrivateFolders() => Directory.Exists(box)
            ? Directory.EnumerateDirectories(box, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*").ToList() : [];

        private void Check(Job job, Exception? failure)
        {
            var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false })
                .Where(path => !path.StartsWith(journals + Path.DirectorySeparatorChar, StringComparison.Ordinal) && IsRegular(path))
                .ToDictionary(path => path, path => Hash(File.ReadAllBytes(path)), StringComparer.Ordinal);
            var present = files.Values.ToHashSet(StringComparer.Ordinal);
            bool published = files.Keys.Any(path => IsOutput(path));
            foreach (var (hash, where) in concurrent)
                if (!present.Contains(hash)) throw Violation($"another program's version last put at {Relative(where)} was lost");
            if (update && originalHash is not null && !originalDestroyedByOthers && failure is not null && !present.Contains(originalHash))
                throw Violation("the original archive was lost although the update stopped");
            if (update && originalHash is not null && !originalDestroyedByOthers && failure is null && !published && !present.Contains(originalHash))
                throw Violation("the original archive was lost and no output exists");
            // A version FileCat moved away from the archive name is never a silent success.
            bool reported = failure is not null || job.Issues.Any(issue => issue.Severity is IssueSeverity.Warning or IssueSeverity.Error);
            foreach (var (hash, where) in concurrent)
                if (where == target && files.GetValueOrDefault(target) != hash && files.Any(file => file.Value == hash && IsPrivate(file.Key)) && !reported)
                    throw Violation("another program's version left the archive name without a report");
            if (IsRegular(target))
            {
                string hash = files[target];
                if (!concurrent.ContainsKey(hash) && hash != originalHash && !IsOutput(target))
                    throw Violation("the archive name holds bytes nobody wrote completely");
                if (failure is not null && publishingPoint < 0 && IsOutput(target))
                    throw Violation("the job stopped before publication but its output is at the archive name");
            }
            if (failure is null)
            {
                if (!published) throw Violation("the job reported success but its output exists nowhere");
                if (publishingPoint >= 0 && lastTargetAction < publishingPoint && !IsOutput(target) && !reported)
                    throw Violation("the job reported a clean success but the archive name does not hold its output");
            }
        }

        private bool IsPrivate(string path) =>
            Path.GetRelativePath(box, path).Split(Path.DirectorySeparatorChar)[0].StartsWith(ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix, StringComparison.Ordinal);

        /// <summary>A complete archive with exactly the requested members, as FileCat should publish it.</summary>
        private bool IsOutput(string path)
        {
            var expected = new Dictionary<string, byte[]?>(StringComparer.Ordinal) { ["added.txt"] = Added };
            if (update) foreach (var (name, data) in OriginalMembers) expected[name] = data;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new TarReader(stream);
                int seen = 0;
                while (reader.GetNextEntry(copyData: true) is { } entry)
                {
                    if (!expected.TryGetValue(entry.Name, out var data)) return false;
                    if (data is null ? entry.EntryType != TarEntryType.Directory : entry.EntryType != TarEntryType.RegularFile) return false;
                    if (data is not null)
                    {
                        using var content = new MemoryStream(); entry.DataStream?.CopyTo(content);
                        if (!content.ToArray().AsSpan().SequenceEqual(data)) return false;
                    }
                    seen++;
                }
                return seen == expected.Count;
            }
            catch (Exception error) when (error is InvalidDataException or IOException or FormatException or ArgumentException or UnauthorizedAccessException) { return false; }
        }

        private Exception Violation(string what, Exception? inner = null) =>
            new InvalidOperationException($"FZ10 oracle ({(update ? "update" : "creation")}): {what}. Points: {string.Join(" ", points)}", inner);
        private string Relative(string path) => Path.GetRelativePath(root, path);
        private static bool Exists(string path) => File.Exists(path) || new FileInfo(path).LinkTarget is not null;
        private static bool IsRegular(string path) => File.Exists(path) && new FileInfo(path).LinkTarget is null;
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

        public void Dispose()
        {
            try
            {
                if (!Directory.Exists(root)) return;
                foreach (string folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
                    try { File.SetUnixFileMode(folder, (UnixFileMode)0x1c0); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                Directory.Delete(root, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Forwards every journal call to the real stream and lets the world act around each durable record.</summary>
    private sealed class ActingStream : FileStream
    {
        private readonly FileStream inner; private readonly Action<string> act; private string? pending;
        private ActingStream(FileStream inner, Action<string> act) : base(inner.SafeFileHandle, FileAccess.Write, 1, false)
        { this.inner = inner; this.act = act; }
        internal static void Install(JobJournal journal, Action<string> act)
        {
            var field = typeof(JobJournal).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("JobJournal._stream");
            field.SetValue(journal, new ActingStream((FileStream)field.GetValue(journal)!, act));
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var record = Record(buffer);
            if (record is not null) act("before:" + record);
            inner.Write(buffer); pending = record ?? pending;
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Flush(bool flushToDisk)
        {
            inner.Flush(flushToDisk);
            if (flushToDisk && pending is { } record) { pending = null; act("after:" + record); }
        }
        public override void Flush() => Flush(false);
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); }
        private static string? Record(ReadOnlySpan<byte> line)
        {
            string text = Encoding.UTF8.GetString(line); int space = text.IndexOf(' ');
            if (space < 0) return null;
            try
            {
                if (JsonNode.Parse(text[(space + 1)..]) is not JsonObject node) return null;
                return node["t"]?.GetValue<string>() switch
                {
                    "archive-transaction" => "T:" + node["phase"]!.GetValue<string>(),
                    ArchiveCreationCleanupFacts.RecordType => "C:" + node["phase"]!.GetValue<string>(),
                    ArchiveDiscardFacts.RecordType => "D:" + node["phase"]!.GetValue<string>(),
                    _ => null,
                };
            }
            catch (System.Text.Json.JsonException) { return null; }
        }
    }

    private static class Native
    {
        // Runtime marshalling: Unix strings are UTF-8.
        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
        internal static extern int Link(string existing, string created);
    }
}
