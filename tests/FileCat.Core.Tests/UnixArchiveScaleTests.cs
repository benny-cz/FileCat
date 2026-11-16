using System.Diagnostics;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    // AT18 breakdown (opt-in, FILECAT_ARCHIVE_SCALE_BREAKDOWN=1): managed bytes retained per member after each preparation
    // stage for 20,101 real members, which types hold them, and what stays retained once the job's objects are released
    // (measured alone, the Mac retained about twice Ubuntu's at every phase of the limit run).
    [Fact]
    public void Retained_bytes_per_member_after_capture_plan_and_worker_request()
    {
        Native();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE_BREAKDOWN") != "1")
            Assert.Skip("Set FILECAT_ARCHIVE_SCALE_BREAKDOWN=1 for the per-stage retained-heap breakdown.");
        using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        Tree("warm", 2, 3); Tree("tree", 100, 200);
        const int members = 100 * 200 + 100 + 1;
        // A small job first loads the runtime's and FileCat's one-time state, so the measured job shows per-member state.
        Measure(root, "warm", 2 * 3 + 2 + 1, 0, ct);
        long baseline = Retained();
        foreach (string line in Measure(root, "tree", members, baseline, ct)) { TestContext.Current.TestOutputHelper?.WriteLine(line); Console.WriteLine(line); }
        long released = Retained();
        string after = $"AT18 released: {(released - baseline) / members}B per member stay retained after the selection, plan and request are released";
        TestContext.Current.TestOutputHelper?.WriteLine(after); Console.WriteLine(after);

        void Tree(string name, int folders, int perFolder)
        {
            for (int f = 0; f < folders; f++)
            {
                string folder = Directory.CreateDirectory(Path.Combine(root, name, $"f{f:000}")).FullName;
                for (int i = 0; i < perFolder; i++)
                {
                    string file = Path.Combine(folder, $"n{i:000}.txt"); File.WriteAllText(file, $"{f}:{i}");
                    File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch.AddSeconds(1700000000));
                }
            }
        }
        static long Retained() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
        // Bytes that survive a compacting full collection: live objects only, whatever the heap's free-space accounting.
        static long Survived()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            return GC.GetGCMemoryInfo(GCKind.FullBlocking).PromotedBytes;
        }
        // Not inlined, so nothing it allocated stays reachable from the test's frame once it returns.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static List<string> Measure(string root, string tree, int members, long baseline, CancellationToken ct)
        {
            var lines = new List<string>();
            long survivedBefore = Survived();
            using var selection = UnixArchiveSelectionTree.Capture(root, [tree], ct);
            long captured = Retained(), survivedCaptured = Survived();
            var plan = selection.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], false, ct);
            long planned = Retained(), survivedPlanned = Survived();
            var invocation = ArchiveTransformWire.Prepare(plan, ct);
            long requested = Retained(), survivedRequested = Survived();
            if (baseline == 0) { GC.KeepAlive(invocation); return lines; }
            lines.Add($"AT18 breakdown members={members} capture={(captured - baseline) / members}B plan=+{(planned - captured) / members}B " +
                $"request=+{(requested - planned) / members}B total={(requested - baseline) / members}B per member; " +
                $"origin={selection.Inputs.Bindings.Values.Average(binding => binding.Reviewed.Origin.Bytes.Length):F0}B " +
                $"memberMetadata={plan.Expected.Members.Average(member => member.Metadata.Length):F0}B; " +
                $"live (surviving a compacting collection): capture={(survivedCaptured - survivedBefore) / members}B " +
                $"plan=+{(survivedPlanned - survivedCaptured) / members}B request=+{(survivedRequested - survivedPlanned) / members}B " +
                $"total={(survivedRequested - survivedBefore) / members}B per member");
            // Which types hold the capture: everything reachable from the selection, sized about as the runtime lays it
            // out (16-byte header, each field at its managed size, 8-byte alignment), largest first.
            var testSource = typeof(CancellationToken).GetField("_source", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ct);
            var types = Census(selection, testSource).ToList();
            lines.Add($"AT18 census reachable={types.Sum(t => t.Bytes) / members}B per member: " +
                string.Join(", ", types.Take(24).Select(t => $"{t.Type}={t.Bytes / members}B({t.Count / (double)members:F2})")));
            GC.KeepAlive(plan); GC.KeepAlive(invocation);
            return lines;
        }

        static IEnumerable<(string Type, long Bytes, long Count)> Census(object root, object? excluded)
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (excluded is not null) seen.Add(excluded); // The test's own token source and whatever the test host hangs on it.
            var totals = new Dictionary<string, (long Bytes, long Count)>();
            var sizes = new Dictionary<Type, long>();
            var pending = new Stack<object>(); pending.Push(root); seen.Add(root);
            const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            while (pending.TryPop(out var current))
            {
                var type = current.GetType(); long size;
                if (current is string text) size = 22 + 2L * text.Length;
                else if (current is Array array)
                {
                    var element = type.GetElementType()!;
                    size = 24 + Field(element) * array.Length;
                    if (!element.IsValueType) foreach (var item in array) Visit(item);
                    else if (!element.IsPrimitive && !element.IsEnum) foreach (var item in array) VisitStruct(item);
                }
                else
                {
                    size = 16;
                    for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
                        foreach (var field in t.GetFields(all | System.Reflection.BindingFlags.DeclaredOnly))
                        {
                            size += Field(field.FieldType);
                            if (current is System.Runtime.InteropServices.SafeHandle) continue; // Sized, never followed.
                            if (!field.FieldType.IsValueType) Visit(field.GetValue(current));
                            else if (!field.FieldType.IsPrimitive && !field.FieldType.IsEnum) VisitStruct(field.GetValue(current));
                        }
                    size = Math.Max(24, size);
                }
                size = (size + 7) & ~7L;
                totals[type.Name] = totals.TryGetValue(type.Name, out var old) ? (old.Bytes + size, old.Count + 1) : (size, 1);
            }
            return totals.Select(p => (p.Key, p.Value.Bytes, p.Value.Count)).OrderByDescending(p => p.Bytes);

            long Field(Type type)
            {
                if (!type.IsValueType) return 8;
                if (sizes.TryGetValue(type, out long known)) return known;
                return sizes[type] = (int)typeof(System.Runtime.CompilerServices.Unsafe).GetMethod(nameof(System.Runtime.CompilerServices.Unsafe.SizeOf))!
                    .MakeGenericMethod(type).Invoke(null, null)!;
            }
            // A struct's own bytes are part of its holder; only the references it holds are followed (dictionary entries).
            void VisitStruct(object? boxed)
            {
                if (boxed is null) return;
                for (var t = boxed.GetType(); t is not null && t != typeof(ValueType) && t != typeof(object); t = t.BaseType)
                    foreach (var field in t.GetFields(all | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (!field.FieldType.IsValueType) Visit(field.GetValue(boxed));
                        else if (!field.FieldType.IsPrimitive && !field.FieldType.IsEnum && field.FieldType != boxed.GetType()) VisitStruct(field.GetValue(boxed));
                    }
            }
            void Visit(object? value)
            {
                if (value is null || value is Type || value is System.Reflection.MemberInfo || value is Delegate { Target: null }) return;
                var type = value.GetType();
                if (type.IsPrimitive || type.IsEnum || type.IsPointer) return;
                if (seen.Add(value)) pending.Push(value);
            }
        }
    }

    // AT18 (opt-in, FILECAT_ARCHIVE_SCALE=1): a TAR at the 100,000-member policy limit is created and updated through the
    // native job route with real worker processes; one more member past the limit refuses before anything is written.
    // Time, peak parent working set, managed heap and open descriptors are reported for the scale record.
    [Fact]
    public async Task At_the_member_limit_creation_and_update_finish_within_their_resource_budgets()
    {
        Native();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE") != "1") Assert.Skip("Set FILECAT_ARCHIVE_SCALE=1 for the AT18 scale measurement.");
        using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        // The tree itself, 497 folders of 200 tiny files and a last folder of 100: 1 + 497 x 201 + 101 = 99,999 members.
        string tree = Path.Combine(root, "tree"); int members = 1;
        for (int f = 0; members < ArchiveWritePolicy.MaxMembers - 1; f++)
        {
            string folder = Directory.CreateDirectory(Path.Combine(tree, $"f{f:000}")).FullName; members++;
            for (int i = 0; i < 200 && members < ArchiveWritePolicy.MaxMembers - 1; i++)
            {
                string file = Path.Combine(folder, $"n{i:000}.txt"); File.WriteAllText(file, $"{f}:{i}");
                File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch.AddSeconds(1700000000)); members++;
            }
        }
        Assert.Equal(ArchiveWritePolicy.MaxMembers - 1, members);
        Assert.Equal(498, Directory.GetDirectories(tree).Length);
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        // The phase each live peak was seen in; with FILECAT_ARCHIVE_SCALE_RETAINED=1 every phase change also logs the live
        // bytes (two compacting full collections each time, for a solo measurement run).
        string phaseNow = "start", peakPhase = "start"; bool retainedAtPhases = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE_RETAINED") == "1";
        var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
            (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"), new([backend]));
        string target = Path.Combine(root, "scale.tar");
        // A small job first, so the descriptors and heap the runtime opens lazily on its first job (thread pool, event
        // queues, pipes) are part of the baseline; measured alone, they otherwise read as growth of the measured jobs.
        string warm = Path.Combine(root, "warm.txt"); File.WriteAllText(warm, "warm-up"); File.SetLastWriteTimeUtc(warm, DateTime.UnixEpoch.AddSeconds(1700000000));
        await Finish("warm-up", manager.SubmitArchive(new(Path.Combine(root, "warm.tar"), null, [new(ArchiveChangeKind.AddFile, "warm.txt", warm)], Profile: profile), null));
        File.Delete(Path.Combine(root, "warm.tar")); File.Delete(warm);
        int descriptors = Descriptors();
        // The managed heap's high-water mark while the jobs run (sampled), compared with the charged retained-heap ceiling.
        long liveBefore = Live();
        long heapBefore = GC.GetTotalMemory(false), heapPeak = heapBefore, workingSetBefore = Process.GetCurrentProcess().PeakWorkingSet64;
        using var sampler = new Timer(_ =>
        {
            long heap = GC.GetTotalMemory(false), seen;
            while (heap > (seen = Interlocked.Read(ref heapPeak)) && Interlocked.CompareExchange(ref heapPeak, heap, seen) != seen) { }
        }, null, 0, 20);
        // What is actually live: compacting full collections every 15 s (the sampled heap above also counts uncollected
        // garbage). Other test classes may run in this process at the same time, so this is reported, not asserted.
        long retainedPeak = liveBefore;
        using var retainedSampler = new Timer(_ =>
        {
            long retained = Live(), seen;
            while (retained > (seen = Interlocked.Read(ref retainedPeak)))
                if (Interlocked.CompareExchange(ref retainedPeak, retained, seen) == seen) { Volatile.Write(ref peakPhase, Volatile.Read(ref phaseNow)); break; }
        }, null, 15_000, 15_000);

        var clock = Stopwatch.StartNew();
        await Finish("create", manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFolder, "tree", tree)], Profile: profile), null));
        var created = clock.Elapsed; long size = new FileInfo(target).Length;

        string extra = Path.Combine(root, "extra.txt"); File.WriteAllText(extra, "the last member"); File.SetLastWriteTimeUtc(extra, DateTime.UnixEpoch.AddSeconds(1700000001));
        var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        clock.Restart();
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
        var assessed = clock.Elapsed; Assert.Equal(members, reviewed.Assessment.Manifest.Members.Count);
        clock.Restart();
        Log($"assessed in {assessed.TotalSeconds:F1}s");
        await Finish("update", manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "extra.txt", extra)]), null));
        var updated = clock.Elapsed;

        // One past the limit: refused before writing, the archive unchanged.
        byte[] before = File.ReadAllBytes(target);
        string over = Path.Combine(root, "over.txt"); File.WriteAllText(over, "one too many"); File.SetLastWriteTimeUtc(over, DateTime.UnixEpoch.AddSeconds(1700000002));
        reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
        var refused = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "over.txt", over)]), null);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            while (!refused.State.IsFinished()) await Task.Delay(10, deadline.Token);
        }
        Assert.Equal(JobState.Failed, refused.State);
        Assert.Equal(before, File.ReadAllBytes(target));
        Assert.Equal(new[] { "extra.txt", "journals", "over.txt", "scale.tar", "tree" },
            Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int after = Descriptors();
        var process = Process.GetCurrentProcess(); process.Refresh();
        sampler.Dispose(); retainedSampler.Dispose();
        string report = $"AT18 members={members + 1} archiveBytes={size} create={created.TotalSeconds:F1}s assess={assessed.TotalSeconds:F1}s " +
            $"update={updated.TotalSeconds:F1}s heapBefore={heapBefore >> 20}MiB heapPeak={Interlocked.Read(ref heapPeak) >> 20}MiB " +
            $"liveBefore={liveBefore >> 20}MiB livePeak={Interlocked.Read(ref retainedPeak) >> 20}MiB (in {Volatile.Read(ref peakPhase)}) " +
            $"chargedCeiling={ArchiveWritePolicy.MaxRetainedHeapBytes >> 20}MiB peakWorkingSet={workingSetBefore >> 20}->{process.PeakWorkingSet64 >> 20}MiB heapAfter={GC.GetTotalMemory(false) >> 20}MiB " +
            $"descriptors={descriptors}->{after} refusal=\"{string.Join(" | ", refused.Issues.Select(issue => issue.Message))}\"";
        TestContext.Current.TestOutputHelper?.WriteLine(report); Console.WriteLine(report);
        Assert.True(after <= descriptors + 16, report); // No descriptor growth beyond the runner's own churn.

        // Live bytes: what survives a compacting full collection. GC.GetTotalMemory(true) after an ordinary full collection
        // also counted free space between survivors (about half of it on macOS in the AT18 breakdown), so it is not used.
        static long Live()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            return GC.GetGCMemoryInfo(GCKind.FullBlocking).PromotedBytes;
        }
        // A long scale deadline; every phase change is logged with its time so a slow phase is identified, not guessed.
        async Task Finish(string name, Job job)
        {
            var watch = Stopwatch.StartNew(); ArchiveJobPhase? phase = null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(30));
            while (!job.State.IsFinished())
            {
                if (job.ArchivePhase != phase)
                {
                    phase = job.ArchivePhase; Volatile.Write(ref phaseNow, $"{name}:{phase}");
                    Log($"{name} phase={phase} at {watch.Elapsed.TotalSeconds:F1}s heap={GC.GetTotalMemory(false) >> 20}MiB" +
                        (retainedAtPhases ? $" live={Live() >> 20}MiB" : ""));
                }
                await Task.Delay(10, deadline.Token);
            }
            Log($"{name} {job.State} at {watch.Elapsed.TotalSeconds:F1}s");
            Assert.True(job.State == JobState.Completed, job.State + ": " + string.Join(" | ", job.Issues.Select(issue => issue.Message)));
        }
        static void Log(string line)
        {
            line = "AT18 " + line; TestContext.Current.TestOutputHelper?.WriteLine(line); Console.WriteLine(line);
            // A test host stopped by the hang guard loses its captured output; this file, when named, keeps every phase line.
            if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE_LOG") is { Length: > 0 } progress)
                File.AppendAllText(progress, DateTime.UtcNow.ToString("O") + " " + line + Environment.NewLine);
        }
        static int Descriptors() => OperatingSystem.IsLinux() ? Directory.GetFileSystemEntries("/proc/self/fd").Length
            : Directory.Exists("/dev/fd") ? Directory.GetFileSystemEntries("/dev/fd").Length : -1;
    }
}
