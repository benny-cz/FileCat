using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Selection;

namespace FileCat.Core.Tests;

public sealed class RecoverableTransferIntentTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private JobRequest Request(TransferOptions? options = null, string? name = null, int count = 1, JobKind kind = JobKind.Copy) => new()
    {
        Kind = kind,
        Sources = Enumerable.Range(0, count).Select(i => new ItemRef(Location.FileSystem(_dir.Dir("source")), $"item-{i}.txt", EntryKind.File)
            { RelativeFolder = "group/" + i }).ToArray(),
        Destination = Location.FileSystem(_dir.Dir("destination")), NewName = name, Options = options ?? new(), Mode = QueueMode.Queue,
    };

    private InterruptedJob Record(JobRequest request)
    {
        string journals = _dir.Dir(Guid.NewGuid().ToString("N"));
        var job = new Job(request, "Owned transfer intent", "owned", [], []);
        using (JobJournal.Create(journals, job)) { }
        return Assert.Single(JournalRecovery.Scan(journals));
    }

    private static InterruptedJob Rewrite(InterruptedJob original, Action<JsonObject> edit)
    {
        var begin = JobJournal.ReadAll(original.JournalPath)[0].Node;
        edit(begin);
        WriteRecord(original.JournalPath, begin);
        return Assert.Single(JournalRecovery.Scan(Path.GetDirectoryName(original.JournalPath)!));
    }

    private static void WriteRecord(string path, JsonObject value)
    {
        string json = value.ToJsonString();
        File.WriteAllText(path, $"{Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)):x8} {json}\n", new UTF8Encoding(false));
    }

    [Theory]
    [InlineData(JobKind.Copy, false, VerifyMode.Native)]
    [InlineData(JobKind.Move, true, VerifyMode.ReadBack)]
    public void Complete_options_and_name_mapping_round_trip_with_conservative_recovery_conflicts(JobKind kind, bool flatten, VerifyMode verify)
    {
        var request = Request(new TransferOptions
        {
            Conflicts = ConflictPolicy.Replace, Verify = verify, Flatten = flatten, Filter = Mask.Parse("*.jpg;*.png|draft*"),
            PreserveTimestamps = false, PreserveAttributes = false, RateLimit = 524288,
        }, "approved.txt", kind: kind);
        var interrupted = Record(request);
        Assert.True(interrupted.CanContinueTransfer);
        var intent = Assert.IsType<RecoverableTransferIntent>(interrupted.TransferIntent);
        Assert.Equal(kind, intent.Kind);
        Assert.Equal(request.Destination, intent.Destination);
        Assert.Equal("approved.txt", interrupted.NewName);
        Assert.Equal("approved.txt", intent.NewName);
        Assert.Equal("group/0", Assert.Single(intent.Roots).RelativeFolder);
        Assert.Equal(request.Sources[0].FileSystemPath, intent.Roots[0].Path);
        Assert.Equal(QueueMode.Queue, intent.Mode);
        Assert.Equal(ConflictPolicy.Replace, intent.OriginalConflicts);
        var restored = intent.RecoveryOptions();
        Assert.Equal(ConflictPolicy.Skip, restored.Conflicts);
        Assert.Equal(verify, restored.Verify);
        Assert.Equal(flatten, restored.Flatten);
        Assert.False(restored.PreserveTimestamps);
        Assert.False(restored.PreserveAttributes);
        Assert.Equal(524288, restored.RateLimit);
        Assert.True(restored.Filter!.IsMatch("photo.jpg"));
        Assert.False(restored.Filter.IsMatch("notes.txt"));
        Assert.False(restored.Filter.IsMatch("draft.png"));
    }

    [Fact]
    public void The_large_selection_manifest_keeps_each_relative_folder_in_the_same_source_order()
    {
        var request = Request(count: JobJournal.HeaderSampleSize + 1);
        var interrupted = Record(request);
        Assert.Equal(JobJournal.HeaderSampleSize, interrupted.Sources.Count);
        Assert.NotNull(interrupted.ManifestPath);
        Assert.True(interrupted.CanContinueTransfer);
        var paths = JournalRecovery.LoadSources(interrupted)!;
        Assert.True(interrupted.TransferIntent!.MatchesSources(paths));
        Assert.Equal("group/64", interrupted.TransferIntent.Roots[^1].RelativeFolder);
        File.WriteAllLines(interrupted.ManifestPath!, paths.Reverse());
        Assert.False(interrupted.TransferIntent.MatchesSources(JournalRecovery.LoadSources(interrupted)!));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("kind")]
    [InlineData("destination")]
    [InlineData("roots")]
    [InlineData("newName")]
    [InlineData("conflicts")]
    [InlineData("verify")]
    [InlineData("flatten")]
    [InlineData("preserveTimestamps")]
    [InlineData("preserveAttributes")]
    [InlineData("rateLimit")]
    [InlineData("mode")]
    [InlineData("filter")]
    public void A_missing_intent_field_never_silently_uses_current_defaults(string field)
    {
        var interrupted = Rewrite(Record(Request()), begin => begin["transferIntent"]!.AsObject().Remove(field));
        Assert.False(interrupted.CanContinueTransfer);
        Assert.Null(interrupted.TransferIntent);
        Assert.NotNull(interrupted.TransferIntentProblem);
        Assert.True(interrupted.SourcesKnown);
        Assert.True(JournalRecovery.IsInterrupted(interrupted));
        Assert.True(File.Exists(interrupted.JournalPath));
    }

    [Theory]
    [InlineData("future-version")]
    [InlineData("verify-type")]
    [InlineData("unknown-verify")]
    [InlineData("unknown-conflict")]
    [InlineData("negative-rate")]
    [InlineData("wrong-kind")]
    [InlineData("other-destination")]
    [InlineData("other-source")]
    [InlineData("relative-source")]
    [InlineData("traversing-relative-folder")]
    [InlineData("absolute-relative-folder")]
    [InlineData("traversing-name")]
    [InlineData("root-type")]
    [InlineData("filter-type")]
    [InlineData("extra-option")]
    [InlineData("oversized-filter")]
    public void Malformed_or_inconsistent_intent_is_retained_for_review_without_continuation(string change)
    {
        var interrupted = Rewrite(Record(Request()), begin =>
        {
            var intent = begin["transferIntent"]!.AsObject();
            var root = intent["roots"]![0]!.AsObject();
            switch (change)
            {
                case "future-version": intent["version"] = 999; break;
                case "verify-type": intent["verify"] = false; break;
                case "unknown-verify": intent["verify"] = "Unreviewed"; break;
                case "unknown-conflict": intent["conflicts"] = "OverwriteEverything"; break;
                case "negative-rate": intent["rateLimit"] = -1L; break;
                case "wrong-kind": intent["kind"] = "Move"; break;
                case "other-destination": intent["destination"] = Location.FileSystem(_dir.Dir("other")).Serialize(); break;
                case "other-source": root["path"] = Path.Combine(_dir.Path, "other.txt"); break;
                case "relative-source": root["path"] = "relative.txt"; break;
                case "traversing-relative-folder": root["relativeFolder"] = "../outside"; break;
                case "absolute-relative-folder": root["relativeFolder"] = "/outside"; break;
                case "traversing-name": intent["newName"] = "../other.txt"; break;
                case "root-type": intent["roots"] = true; break;
                case "filter-type": intent["filter"] = "*.txt"; break;
                case "extra-option": intent["unsafeFutureOption"] = true; break;
                case "oversized-filter": intent["filter"] = new JsonObject { ["text"] = new string('x', Mask.SnapshotTextLimit + 1), ["include"] = new JsonArray(), ["exclude"] = new JsonArray() }; break;
            }
        });
        Assert.False(interrupted.CanContinueTransfer);
        Assert.Null(interrupted.TransferIntent);
        Assert.NotNull(interrupted.TransferIntentProblem);
        Assert.True(File.Exists(interrupted.JournalPath));
    }

    [Fact]
    public void A_legacy_header_retains_its_recorded_name_but_cannot_claim_equivalent_continuation()
    {
        var interrupted = Rewrite(Record(Request(name: "approved.txt")), begin => { begin.Remove("transferIntent"); begin.Remove("transferIntentProblem"); });
        Assert.Equal("approved.txt", interrupted.NewName);
        Assert.True(interrupted.SourcesKnown);
        Assert.False(interrupted.CanContinueTransfer);
        Assert.Contains("verification", interrupted.TransferIntentProblem);
    }

    [Theory]
    [InlineData("source-array")]
    [InlineData("count")]
    [InlineData("type")]
    public void Wrong_header_scalar_types_do_not_crash_the_recovery_scan(string field)
    {
        var original = Record(Request());
        var begin = JobJournal.ReadAll(original.JournalPath)[0].Node;
        if (field == "source-array") begin["sources"] = new JsonArray(4);
        if (field == "count") begin["sourceCount"] = -1;
        if (field == "type") begin["t"] = 4;
        WriteRecord(original.JournalPath, begin);
        Exception? error = Xunit.Record.Exception(() => JournalRecovery.Scan(Path.GetDirectoryName(original.JournalPath)!));
        Assert.Null(error);
        if (field != "type") Assert.False(Assert.Single(JournalRecovery.Scan(Path.GetDirectoryName(original.JournalPath)!)).CanContinueTransfer);
    }

    [Fact]
    public void Source_and_mask_limits_disable_equivalent_continuation_without_losing_the_original_manifest()
    {
        var large = Record(Request(count: RecoverableTransferIntent.RootLimit + 1));
        Assert.False(large.CanContinueTransfer);
        Assert.NotNull(large.ManifestPath);
        Assert.Equal(RecoverableTransferIntent.RootLimit + 1, JournalRecovery.LoadSources(large)!.Count);
        var filter = Record(Request(new TransferOptions { Filter = Mask.Parse(new string('x', Mask.SnapshotTextLimit + 1)) }));
        Assert.False(filter.CanContinueTransfer);
        Assert.Contains("filter", filter.TransferIntentProblem);
    }

    [Fact]
    public void Comparison_and_edit_commit_preconditions_require_a_new_review()
    {
        var ordinary = Request();
        var special = Record(new JobRequest { Kind = JobKind.Copy, Sources = ordinary.Sources, Destination = ordinary.Destination,
            ExpectedTargets = new Dictionary<ItemRef, (long, long)> { [ordinary.Sources[0]] = (42, 123) } });
        Assert.False(special.CanContinueTransfer);
        Assert.Contains("conditions", special.TransferIntentProblem);
        var hashedEdit = Record(new JobRequest { Kind = JobKind.Copy, Sources = ordinary.Sources, Destination = ordinary.Destination,
            ExpectedTargetSha256 = new string('A', 64) });
        Assert.False(hashedEdit.CanContinueTransfer);
        Assert.Contains("conditions", hashedEdit.TransferIntentProblem);
    }

    [Fact]
    public void Partial_copy_review_uses_the_original_new_name_and_excludes_unrelated_namesakes()
    {
        string source = _dir.File("source/draft.txt", "prefix and complete content"), destination = _dir.Dir("destination");
        var interrupted = Record(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(destination), NewName = "approved.txt" });
        string approved = Path.Combine(destination, "approved.txt"), unrelated = Path.Combine(destination, "draft.txt");
        File.WriteAllText(approved, "prefix"); File.WriteAllText(unrelated, "prefix");
        interrupted = interrupted with { FillDirectories = [new FillDirectory(Path.GetDirectoryName(source)!, destination)] };
        Assert.Equal(approved, Assert.Single(JournalRecovery.ReviewCopies(interrupted).Incomplete).Path);
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void Partial_copy_review_never_admits_a_file_excluded_by_the_recorded_filter()
    {
        string folder = _dir.Dir("photos"), destination = _dir.Dir("destination");
        File.WriteAllText(Path.Combine(folder, "photo.jpg"), "prefix and full photo");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "prefix and private notes");
        var interrupted = Record(new JobRequest { Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(folder, EntryKind.Directory)],
            Destination = Location.FileSystem(destination), Options = new TransferOptions { Filter = Mask.Parse("*.jpg") } });
        string arrived = Directory.CreateDirectory(Path.Combine(destination, "photos")).FullName;
        string image = Path.Combine(arrived, "photo.jpg"), excluded = Path.Combine(arrived, "notes.txt");
        File.WriteAllText(image, "prefix"); File.WriteAllText(excluded, "prefix");
        interrupted = interrupted with { FillDirectories = [new FillDirectory(folder, arrived)] };
        Assert.Equal(image, Assert.Single(JournalRecovery.ReviewCopies(interrupted).Incomplete).Path);
        Assert.True(File.Exists(excluded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_filtered_same_volume_folder_move_leaves_excluded_files_in_the_source(bool mergeExisting)
    {
        string source = _dir.Dir("photos"), destination = _dir.Dir("destination");
        File.WriteAllText(Path.Combine(source, "photo.jpg"), "photo content");
        File.WriteAllText(Path.Combine(source, "notes.txt"), "private notes");
        if (mergeExisting) Directory.CreateDirectory(Path.Combine(destination, "photos"));
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(new PortableFileOperations(), providers, _dir.Dir("journals"));
        jobs.DecisionRequested += decision => decision.Resolve(new Decision(DecisionAction.CancelJob));
        var job = jobs.Submit(new JobRequest { Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.Directory)],
            Destination = Location.FileSystem(destination), Options = new TransferOptions { Filter = Mask.Parse("*.jpg"), Conflicts = ConflictPolicy.Skip } });
        await Wait(job, jobs);
        Assert.Equal("photo content", File.ReadAllText(Path.Combine(destination, "photos", "photo.jpg")));
        Assert.False(File.Exists(Path.Combine(source, "photo.jpg")));
        Assert.Equal("private notes", File.ReadAllText(Path.Combine(source, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "photos", "notes.txt")));
        var begin = Assert.Single(JobJournal.ReadAll(Assert.Single(Directory.GetFiles(_dir.Dir("journals"), "job-*.fcj"))), r => r.Type == "begin");
        Assert.NotNull(begin.Node["transferIntent"]);
    }

    internal static async Task Wait(Job job, JobManager jobs)
    {
        var clock = Stopwatch.StartNew();
        while (!job.State.IsFinished() || jobs.HasActiveWork)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"Owned transfer did not finish: {job.State}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
