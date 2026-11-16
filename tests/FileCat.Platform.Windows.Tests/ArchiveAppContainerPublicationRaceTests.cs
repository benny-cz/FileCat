using System.Formats.Tar;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// FZ10 on the Windows route (plan §13.3): the actual confined application worker and NTFS recipes (review, journaled
/// preparation, new-file publication or the transactional same-object update, cleanup) run to their end while another
/// program of the same user acts immediately before and after each durable archive record. The Unix route's
/// coverage-guided <c>publication-races</c> target uses the same oracle; here every single action at every early point
/// runs as a deterministic case for creation and update, and an opt-in lane replays seeded random action lists
/// (FILECAT_ARCHIVE_FZ10_ROUNDS, FILECAT_ARCHIVE_FZ10_SEED). Oracle: only typed refusals; no version another program
/// wrote is lost, and the original only when the update was published; a concurrent version FileCat moved from the
/// archive name is reported, never silent; a job that stopped before publication never leaves its output at the name;
/// the name never holds bytes nobody wrote completely.
/// </summary>
public sealed partial class ArchiveAppContainerTests
{
    public static TheoryData<bool, int, int> PublicationRaceCases()
    {
        var cases = new TheoryData<bool, int, int>();
        foreach (bool update in new[] { false, true })
        {
            cases.Add(update, 0, (int)WindowsPublicationRace.Act.None);
            for (int act = 1; act < WindowsPublicationRace.ActCount; act++)
                for (int at = 0; at < 24; at += 3) cases.Add(update, at, act);
        }
        return cases;
    }

    [Theory, MemberData(nameof(PublicationRaceCases))]
    public void Windows_publication_races_keep_every_version_and_report_moved_ones(bool update, int at, int act)
    {
        bool published = WindowsPublicationRace.Run(PublicationApplication(), update, [(at, (WindowsPublicationRace.Act)act, (byte)(act * 7 + at))]);
        // The control: without another program's action the route itself must publish, or the lane proves nothing.
        if (act == (int)WindowsPublicationRace.Act.None) Assert.True(published, "A job no other program disturbed did not publish.");
    }

    [Fact]
    public void Seeded_random_windows_publication_races_keep_every_version()
    {
        string executable = PublicationApplication();
        if (!int.TryParse(Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_FZ10_ROUNDS"), out int rounds) || rounds < 1)
            Assert.Skip("Opt-in seeded publication-race lane (FILECAT_ARCHIVE_FZ10_ROUNDS).");
        int seed = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_FZ10_SEED"), out int chosen) ? chosen : 42829401;
        var random = new Random(seed); int publishedRounds = 0;
        for (int round = 0; round < rounds; round++)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            bool update = random.Next(2) == 1;
            var actions = Enumerable.Range(0, random.Next(1, WindowsPublicationRace.MaximumActions + 1))
                .Select(_ => (random.Next(64), (WindowsPublicationRace.Act)random.Next(1, WindowsPublicationRace.ActCount), (byte)random.Next(256))).ToList();
            try { if (WindowsPublicationRace.Run(executable, update, actions)) publishedRounds++; }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Seed {seed}, round {round}, {(update ? "update" : "creation")} with actions " +
                    string.Join(" ", actions.Select(action => $"{action.Item1}:{action.Item2}:{action.Item3}")), error);
            }
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"Seed {seed}: {publishedRounds} of {rounds} rounds published, {rounds - publishedRounds} refused.");
        Assert.True(publishedRounds > 0, "No seeded round published; the lane would only have exercised refusals.");
    }

    private static string PublicationApplication()
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional job lane.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
        Assert.True(assets is not null && Path.IsPathFullyQualified(assets), "Supply a dedicated self-contained FileCat application bundle.");
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
        return Path.Combine(assets!, "FileCat.exe");
    }
}

internal static class WindowsPublicationRace
{
    internal const int MaximumActions = 6;
    internal enum Act : byte
    {
        None, ReplaceTarget, MoveTargetAway, AppendToTarget, TouchTarget, CreateAtTarget, PlantArtifactName, RenameArtifact,
        ShareFolder, ReplaceArtifact, HardLinkTarget, SymlinkAtTarget,
    }
    internal const int ActCount = 12;
    private const string ArtifactPrefix = ".filecat-archive-";
    private static readonly byte[] Added = Encoding.UTF8.GetBytes("added payload\r\n");
    private static readonly (string Name, byte[]? Data)[] OriginalMembers =
        [("one.txt", Encoding.UTF8.GetBytes("first\r\n")), ("dir/", null), ("dir/two.txt", Encoding.UTF8.GetBytes("second\r\n"))];

    /// <summary>True when the job published (it succeeded); a typed refusal returns false once the oracle holds.</summary>
    internal static bool Run(string executable, bool update, List<(int At, Act Kind, byte Argument)> actions)
    {
        using var world = new World(executable, update);
        return world.Run(actions);
    }

    private sealed class World : IDisposable
    {
        private readonly string executable;
        private readonly bool update;
        private readonly string root, box, keep, journals, target, input;
        // Every version another program wrote (content hash -> where it was last put by that program).
        private readonly Dictionary<string, string> concurrent = new(StringComparer.Ordinal);
        private string? originalHash;
        private bool originalDestroyedByOthers;
        private int point, versions, publishingPoint = -1, lastTargetAction = -1;
        private readonly List<string> points = [];

        internal World(string executable, bool update)
        {
            this.executable = executable; this.update = update;
            // Link-free, as the native route pins every folder without following links.
            root = ArchiveLocalPath.ResolveDirectory(Directory.CreateTempSubdirectory("filecat-fz10-").FullName);
            box = Path.Combine(root, "box"); keep = Path.Combine(root, "keep"); journals = Path.Combine(root, "journals");
            target = Path.Combine(box, "a.tar"); input = Path.Combine(root, "src", "added.txt");
            foreach (string folder in new[] { box, keep, journals, Path.Combine(root, "src") }) Directory.CreateDirectory(folder);
            File.WriteAllBytes(input, Added); File.SetLastWriteTimeUtc(input, DateTime.UnixEpoch.AddSeconds(1700000100));
            if (!update) return;
            using (var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: false))
                foreach (var (name, content) in OriginalMembers)
                {
                    var entry = new PaxTarEntry(content is null ? TarEntryType.Directory : TarEntryType.RegularFile, name)
                    { ModificationTime = DateTimeOffset.FromUnixTimeSeconds(1700000000) };
                    if (content is not null) entry.DataStream = new MemoryStream(content, false);
                    writer.WriteEntry(entry);
                }
            originalHash = Hash(File.ReadAllBytes(target));
        }

        internal bool Run(List<(int At, Act Kind, byte Argument)> actions)
        {
            var ct = TestContext.Current.CancellationToken; var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
            var backend = WindowsArchiveJobBackend.Create(ArchiveWriteFormat.Tar, executable);
            var change = new ArchiveChange(ArchiveChangeKind.AddFile, "added.txt", input);
            ArchivePlan plan;
            if (update)
            {
                var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "review-journals"), new([backend]));
                plan = manager.ReviewArchiveUpdateAsync(target, profile, null, ct).GetAwaiter().GetResult().CreatePlan([change]);
            }
            else plan = new ArchivePlan(target, null, [change], Profile: profile);
            // As JobManager.SubmitArchive does: the archive's folder is resolved once, at submission.
            plan = plan with { ArchivePath = ArchiveLocalPath.ResolveParent(plan.ArchivePath) };
            Assert.Equal(target, plan.ArchivePath);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "FZ10 Windows publication race", "owned", [], []);
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
                        if (!File.Exists(target)) break;
                        string before = Hash(File.ReadAllBytes(target));
                        using (var stream = new FileStream(target, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                            stream.Write([argument, (byte)'\n']);
                        // This program changed that version itself; only the result is its to keep.
                        if (before == originalHash) originalDestroyedByOthers = true;
                        concurrent.Remove(before);
                        concurrent[Hash(File.ReadAllBytes(target))] = target; lastTargetAction = point; break;
                    case Act.TouchTarget:
                        if (File.Exists(target)) { File.SetLastWriteTimeUtc(target, DateTime.UnixEpoch.AddSeconds(1600000000 + argument)); lastTargetAction = point; }
                        break;
                    case Act.CreateAtTarget:
                        if (!Exists(target)) { PlaceNew(target, argument); lastTargetAction = point; }
                        break;
                    case Act.PlantArtifactName:
                        // A file that only looks like one of FileCat's artifacts: it is another program's version.
                        PlaceNew(Path.Combine(box, ArtifactPrefix + Guid.NewGuid().ToString("N") + "-" + Guid.NewGuid().ToString("N") + ".stage"), argument);
                        break;
                    case Act.RenameArtifact:
                        if (Artifacts().FirstOrDefault() is { } artifact) File.Move(artifact, Path.Combine(keep, "artifact-" + versions++));
                        break;
                    case Act.ShareFolder:
                        var security = new DirectoryInfo(box).GetAccessControl();
                        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl,
                            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                        new DirectoryInfo(box).SetAccessControl(security); break;
                    case Act.ReplaceArtifact:
                        if (Artifacts().FirstOrDefault() is { } stage)
                        {
                            // FileCat's own object leaves through another program's rename; what that program puts there is its version.
                            File.Move(stage, Path.Combine(keep, "stage-" + versions++)); PlaceNew(stage, argument);
                        }
                        break;
                    case Act.HardLinkTarget:
                        if (File.Exists(target))
                        {
                            string link = Path.Combine(keep, "link-" + versions++);
                            if (CreateHardLink(link, target, 0)) { concurrent[Hash(File.ReadAllBytes(link))] = link; lastTargetAction = point; }
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
            if (IsRegular(moved))
            {
                string hash = Hash(File.ReadAllBytes(moved));
                if (concurrent.ContainsKey(hash)) concurrent[hash] = moved;
            }
            return true;
        }

        private void PlaceNew(string path, byte argument)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(path)!, "fz10-" + versions++ + ".tmp");
            byte[] content = Content(argument); File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: false);
            concurrent[Hash(content)] = path;
        }

        private byte[] Content(byte argument) => Encoding.UTF8.GetBytes($"another program's version {versions} ({argument})\r\n");

        // FileCat's Windows artifacts are reserved names beside the archive (ArchiveArtifactReservation).
        private IEnumerable<string> Artifacts() => Directory.Exists(box)
            ? Directory.EnumerateFiles(box, ArtifactPrefix + "*").Where(path => !concurrent.ContainsValue(path)).ToList() : [];

        private void Check(Job job, Exception? failure)
        {
            var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false })
                .Where(path => !path.StartsWith(journals + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                               !path.StartsWith(Path.Combine(root, "review-journals") + Path.DirectorySeparatorChar, StringComparison.Ordinal) && IsRegular(path))
                .ToDictionary(path => path, path => Hash(File.ReadAllBytes(path)), StringComparer.Ordinal);
            var present = files.Values.ToHashSet(StringComparer.Ordinal);
            bool published = files.Keys.Any(IsOutput);
            foreach (var (hash, where) in concurrent)
                if (!present.Contains(hash)) throw Violation($"another program's version last put at {Relative(where)} was lost");
            if (update && originalHash is not null && !originalDestroyedByOthers && failure is not null && !present.Contains(originalHash))
                throw Violation("the original archive was lost although the update stopped");
            if (update && originalHash is not null && !originalDestroyedByOthers && failure is null && !published && !present.Contains(originalHash))
                throw Violation("the original archive was lost and no output exists");
            // A version FileCat moved away from the archive name is never a silent success.
            bool reported = failure is not null || job.Issues.Any(issue => issue.Severity is IssueSeverity.Warning or IssueSeverity.Error);
            foreach (var (hash, where) in concurrent)
                if (where == target && files.GetValueOrDefault(target) != hash && files.Any(file => file.Value == hash && IsArtifact(file.Key)) && !reported)
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

        private bool IsArtifact(string path) =>
            Path.GetDirectoryName(path) == box && Path.GetFileName(path).StartsWith(ArtifactPrefix, StringComparison.Ordinal);

        /// <summary>A complete archive with exactly the requested members, as FileCat should publish it.</summary>
        private bool IsOutput(string path)
        {
            var expected = new Dictionary<string, byte[]?>(StringComparer.Ordinal) { ["added.txt"] = Added };
            if (update) foreach (var (name, data) in OriginalMembers) expected[name] = data;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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
                    ArchiveContentUpdateFacts.RecordType => "U:" + node["phase"]!.GetValue<string>(),
                    ArchiveContentRestorationFacts.RecordType => "R:" + node["phase"]!.GetValue<string>(),
                    ArchiveCreationCleanupFacts.RecordType => "C:" + node["phase"]!.GetValue<string>(),
                    ArchiveDiscardFacts.RecordType => "D:" + node["phase"]!.GetValue<string>(),
                    _ => null,
                };
            }
            catch (System.Text.Json.JsonException) { return null; }
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, nint securityAttributes);
}
