using System.Reflection;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveJobBackendTests
{
    private const string PublicPassword = "Public job ownership 暗号 é e\u0301 42829325";
    public static IEnumerable<object[]> Profiles()
    {
        foreach (var format in new[] { ArchiveWriteFormat.Tar, ArchiveWriteFormat.TarGzip, ArchiveWriteFormat.TarBzip2,
            ArchiveWriteFormat.TarZstd, ArchiveWriteFormat.Rar5, ArchiveWriteFormat.SevenZip })
        for (int mode = 0; mode <= (format is ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip ? 2 : 0); mode++)
            yield return [(int)format, mode];
    }
    private static ArchiveWriteProfile Profile(int format, int mode)
    {
        var encryption = mode == 0 ? null : new ArchiveEncryptionOptions((ArchiveEncryptionMode)mode);
        return (ArchiveWriteFormat)format switch
        {
            ArchiveWriteFormat.Rar5 => ArchiveWriteProfile.ForRar5(encryption: encryption),
            ArchiveWriteFormat.SevenZip => ArchiveWriteProfile.ForSevenZip(encryption: encryption),
            _ => new((ArchiveWriteFormat)format),
        };
    }
    private static ArchiveJobPassword? Password(int mode) => mode == 0 ? null : new(PublicPassword);
    private static char[]? Buffer(ArchiveJobPassword? owner) => owner is null ? null : (char[])typeof(ArchiveJobPassword)
        .GetField("password", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Erased(ArchiveJobPassword? owner, char[]? buffer)
    {
        if (owner is null) return;
        Assert.All(buffer!, value => Assert.Equal('\0', value));
        Assert.Throws<ObjectDisposedException>(() => owner.CreateGrant());
    }
    private static void Live(ArchiveJobPassword owner)
    { using var grant = owner.CreateGrant(); Assert.True(grant.UseOnce(value => value.SequenceEqual(PublicPassword))); }
    private static void NoSecret(string text)
    {
        Assert.DoesNotContain(PublicPassword, text);
        Assert.DoesNotContain(JsonEncodedText.Encode(PublicPassword).ToString(), text);
    }
    private static JobManager Manager(TempDir temp, Backend? backend = null) =>
        new(new PortableFileOperations(), new ProviderRegistry(), temp.Dir("journals"),
            backend is null ? ArchiveJobBackends.Empty : new([backend]));
    private static ArchivePlan Plan(TempDir temp, ArchiveWriteProfile profile, IReadOnlyList<ArchiveChange>? changes = null) =>
        new(Path.Combine(temp.Path, "target.zip"), null, changes ?? [], Profile: profile);
    private static async Task Finished(Job job)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (!job.State.IsFinished()) await Task.Delay(5, deadline.Token);
    }
    private sealed class Backend(ArchiveWriteFormat format, Action<ArchiveJobExecution>? run = null) : IArchiveJobBackend
    {
        public ArchiveWriteFormat Format => format;
        internal ArchiveJobAvailability Status = ArchiveJobAvailability.Ready;
        internal int Calls;
        internal Action<ArchivePlan>? Check;
        public ArchiveJobAvailability Availability(ArchivePlan plan) { Check?.Invoke(plan); return Status; }
        public void Execute(ArchiveJobExecution execution)
        {
            Interlocked.Increment(ref Calls);
            if (run is not null) run(execution);
            else { execution.Job.AddTotals(1, 0); execution.Job.ItemDone(); }
        }
    }

    [Theory, MemberData(nameof(Profiles))]
    public async Task Typed_jobs_dispatch_the_exact_profile_and_release_secrets_before_terminal_publication(int format, int mode)
    {
        using var temp = new TempDir(); var profile = Profile(format, mode); using var password = Password(mode); var buffer = Buffer(password);
        var backend = new Backend(profile.Format, execution =>
        {
            Assert.Equal(profile, execution.Plan.Profile); Assert.Same(password, execution.Password);
            if (password is not null) Live(password);
            execution.Job.AddTotals(1, 0); execution.Job.ItemDone();
        });
        var manager = Manager(temp, backend); bool observed = false;
        manager.JobChanged += job => { if (job.State.IsFinished()) { Erased(password, buffer); observed = true; } };
        var job = manager.SubmitArchive(Plan(temp, profile), password);
        await Finished(job);
        Assert.Equal(JobState.Completed, job.State); Assert.Equal(1, backend.Calls); Erased(password, buffer);
        Assert.True(SpinWait.SpinUntil(() => observed, 5000));
        NoSecret(JsonSerializer.Serialize(job.Request));
        foreach (var file in Directory.GetFiles(Path.Combine(temp.Path, "journals")))
            NoSecret(File.ReadAllText(file));
        Assert.False(File.Exists(job.Request.Archive!.ArchivePath));
    }

    [Theory, MemberData(nameof(Profiles))]
    public async Task An_ordinary_profile_request_cannot_bypass_typed_admission_or_use_legacy_zip(int format, int mode)
    {
        using var temp = new TempDir(); var profile = Profile(format, mode); var backend = new Backend(profile.Format);
        var manager = Manager(temp, backend); var plan = Plan(temp, profile);
        byte[] original = "unchanged original"u8.ToArray(); File.WriteAllBytes(plan.ArchivePath, original);
        var job = manager.Submit(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }); await Finished(job);
        Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, backend.Calls);
        Assert.Equal(original, File.ReadAllBytes(plan.ArchivePath));
    }

    // ZIP design, Interface (review finding of 12 November 2026): where a ZIP route covers a ZIP, a change prepared for
    // the old route is refused, whatever the worker's state; a ZIP edit session made before the route keeps committing
    // through the old route, and a ZIP the route does not cover keeps it too.
    [Fact]
    public async Task Where_a_ZIP_route_is_composed_a_change_for_the_old_route_is_refused_but_a_legacy_edit_commit_is_kept()
    {
        using var temp = new TempDir();
        string covered = temp.Dir("covered"), elsewhere = temp.Dir("elsewhere");
        var backends = ArchiveWriteRoutes.Compose([new(ArchiveWriteFormat.Zip, "test-rid", "qualified", "evidence")], "test-rid",
            format => new Backend(format) { Status = new(false, "The worker cannot run from here.") },
            folder => folder == covered ? "qualified" : "other")!;
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), temp.Dir("journals"), backends);
        string Zip(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            using (var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
                using (var writer = new StreamWriter(archive.CreateEntry("member.txt").Open())) writer.Write("before");
            return path;
        }
        string source = Path.Combine(temp.Path, "after.txt"); File.WriteAllText(source, "after");
        async Task<Job> Replace(string archive, bool legacyEdit)
        {
            var job = manager.Submit(new JobRequest
            {
                Kind = JobKind.ArchiveUpdate,
                Archive = new ArchivePlan(archive, ArchiveBaseline.Of(archive), [new ArchiveChange(ArchiveChangeKind.Replace, "member.txt", source)])
                { LegacyEditCommit = legacyEdit },
            });
            await Finished(job); return job;
        }
        string refusedZip = Zip(covered, "refused.zip"); byte[] original = File.ReadAllBytes(refusedZip);
        var refused = await Replace(refusedZip, legacyEdit: false);
        Assert.Equal(JobState.Failed, refused.State); Assert.Equal(original, File.ReadAllBytes(refusedZip));
        Assert.Contains(refused.Issues, issue => issue.Message.Contains("prepared for the old ZIP route", StringComparison.Ordinal));
        Assert.Equal(JobState.Completed, (await Replace(Zip(covered, "edit.zip"), legacyEdit: true)).State);
        Assert.Equal(JobState.Completed, (await Replace(Zip(elsewhere, "plain.zip"), legacyEdit: false)).State);
        // Only the edit store's commit plans carry the marker; a retried commit is planned by it again.
        var store = new FileCat.Core.Edit.EditSessionStore(temp.Dir("sessions"), new PortableFileOperations());
        Assert.True(store.CommitPlan(new FileCat.Core.Edit.EditSessionRecord
        {
            ArchivePath = refusedZip, MemberPath = "member.txt", WorkingPath = source, Baseline = ArchiveBaseline.Of(refusedZip),
        }, rebase: false).LegacyEditCommit);
    }

    [Fact]
    public async Task Profiled_archive_jobs_run_one_at_a_time_while_ordinary_jobs_keep_running()
    {
        using var temp = new TempDir(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int active = 0, peak = 0;
        var backend = new Backend(ArchiveWriteFormat.Tar, execution =>
        {
            int now = Interlocked.Increment(ref active);
            for (int seen = Volatile.Read(ref peak); seen < now; seen = Volatile.Read(ref peak))
                if (Interlocked.CompareExchange(ref peak, now, seen) == seen) break;
            try { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(15))); }
            finally { Interlocked.Decrement(ref active); }
            execution.Job.AddTotals(1, 0); execution.Job.ItemDone();
        });
        var manager = Manager(temp, backend); var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        var first = manager.SubmitArchive(new(Path.Combine(temp.Dir("one"), "first.tar"), null, [], Profile: profile), description: "Pack first");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
        // Different folders: nothing but the one-at-a-time rule holds the second job back.
        var second = manager.SubmitArchive(new(Path.Combine(temp.Dir("two"), "second.tar"), null, [], Profile: profile), description: "Pack second");
        Assert.Equal(JobState.Queued, second.State); Assert.Same(first, second.WaitingFor);
        Assert.Equal("Waits for \"Pack first\": archive changes are made one at a time", second.WaitReason);
        var folder = manager.Submit(new JobRequest { Kind = JobKind.CreateDirectory, Destination = Location.FileSystem(temp.Dir("three")), NewName = "made" });
        await Finished(folder);
        Assert.Equal(JobState.Completed, folder.State); Assert.True(Directory.Exists(Path.Combine(temp.Path, "three", "made")));
        Assert.Equal(JobState.Queued, second.State);
        release.Set();
        await Finished(first); await Finished(second);
        Assert.Equal(JobState.Completed, first.State); Assert.Equal(JobState.Completed, second.State);
        Assert.Equal(2, backend.Calls); Assert.Equal(1, Volatile.Read(ref peak));
        Assert.Null(second.WaitReason);
    }

    [Theory, MemberData(nameof(Profiles))]
    public void Queue_cancellation_erases_only_the_owned_password_without_starting_a_worker(int format, int mode)
    {
        using var temp = new TempDir(); var profile = Profile(format, mode); using var password = Password(mode); var buffer = Buffer(password);
        var backend = new Backend(profile.Format); var manager = Manager(temp, backend); manager.MaxConcurrent = 0;
        var job = manager.SubmitArchive(Plan(temp, profile), password);
        if (password is not null) Live(password);
        job.Cancel(); Assert.Equal(JobState.Canceled, job.State); Assert.Equal(0, backend.Calls); Erased(password, buffer);
        Assert.Null(job.StartedUtc); Assert.Empty(Directory.GetFiles(Path.Combine(temp.Path, "journals")));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Active_cancellation_keeps_the_password_until_backend_reconciliation_finishes(bool fail)
    {
        using var temp = new TempDir(); using var password = new ArchiveJobPassword(PublicPassword); var buffer = Buffer(password);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var backend = new Backend(ArchiveWriteFormat.SevenZip, execution =>
        {
            entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); Live(execution.Password!);
            if (fail) throw new IOException("Synthetic backend failure after reconciliation.");
        });
        var manager = Manager(temp, backend); var job = manager.SubmitArchive(Plan(temp, Profile(5, 2)), password);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)); job.Cancel();
            Assert.Equal(JobState.Stopping, job.State); Live(password);
        }
        finally { release.Set(); await Finished(job); }
        Assert.Equal(fail ? JobState.Failed : JobState.Canceled, job.State); Erased(password, buffer);
    }

    [Theory]
    [InlineData("registration")] [InlineData("added")] [InlineData("added-cancel")]
    public void Failed_admission_cannot_start_from_a_reentrant_schedule_and_erases_its_password(string route)
    {
        using var temp = new TempDir(); using var password = new ArchiveJobPassword(PublicPassword); var buffer = Buffer(password);
        var backend = new Backend(ArchiveWriteFormat.Rar5); var manager = Manager(temp, backend); Job? seen = null;
        void Fail(Job job)
        {
            seen = job; if (route == "added-cancel") job.Cancel();
            manager.Schedule(); Assert.Equal(0, backend.Calls); Assert.Null(job.StartedUtc); Assert.Equal(JobState.Queued, job.State); Live(password);
            throw new InvalidOperationException("Synthetic admission failure.");
        }
        if (route != "registration") manager.JobAdded += Fail;
        Assert.Throws<InvalidOperationException>(() => manager.SubmitArchive(Plan(temp, Profile(9, 1)), password,
            register: route == "registration" ? Fail : null));
        Assert.NotNull(seen); Assert.Equal(JobState.Canceled, seen.State); Assert.Empty(manager.Jobs);
        Erased(password, buffer); Assert.Equal(0, backend.Calls);
    }

    [Theory]
    [InlineData("relative")] [InlineData("legacy")] [InlineData("mode")] [InlineData("missing-password")]
    public void Invalid_admission_releases_a_transferred_secret_before_queuing(string route)
    {
        using var temp = new TempDir(); using var password = route == "missing-password" ? null : new ArchiveJobPassword(PublicPassword);
        var buffer = Buffer(password); var manager = Manager(temp); var plan = Plan(temp, Profile(9, 1));
        if (route == "relative") plan = plan with { ArchivePath = "relative.rar" };
        if (route == "legacy") plan = plan with { Profile = null };
        if (route == "mode") plan = plan with { Profile = Profile(9, 0) };
        Assert.ThrowsAny<Exception>(() => manager.SubmitArchive(plan, password));
        Assert.Empty(manager.Jobs); Erased(password, buffer);
    }

    [Fact]
    public void A_second_owner_claim_cannot_erase_the_first_queued_jobs_password()
    {
        using var temp = new TempDir(); using var password = new ArchiveJobPassword(PublicPassword); var buffer = Buffer(password);
        var manager = Manager(temp); manager.MaxConcurrent = 0; var plan = Plan(temp, Profile(9, 1));
        var first = manager.SubmitArchive(plan, password);
        Assert.Throws<InvalidOperationException>(() => manager.SubmitArchive(plan, password));
        Live(password); Assert.Same(first, Assert.Single(manager.Jobs)); first.Cancel(); Erased(password, buffer);
    }

    [Fact]
    public async Task Queued_intent_is_frozen_and_availability_is_checked_again_before_native_work()
    {
        using var temp = new TempDir(); using var password = new ArchiveJobPassword(PublicPassword); var buffer = Buffer(password);
        var profile = Profile(5, 1); var changes = new List<ArchiveChange> { new(ArchiveChangeKind.Delete, "original") };
        var backend = new Backend(profile.Format); var manager = Manager(temp, backend); manager.MaxConcurrent = 0;
        Assert.True(manager.ArchiveAvailability(Plan(temp, profile)).Available);
        var job = manager.SubmitArchive(Plan(temp, profile, changes), password); changes[0] = new(ArchiveChangeKind.Delete, "retargeted");
        Assert.Equal("original", Assert.Single(job.Request.Archive!.Changes).MemberPath);
        Assert.Throws<NotSupportedException>(() => ((IList<ArchiveChange>)job.Request.Archive.Changes).Add(changes[0]));
        backend.Status = new(false, "Native recipe is unavailable now."); manager.MaxConcurrent = 1; manager.Schedule();
        await Finished(job); Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, backend.Calls); Erased(password, buffer);
        Assert.Contains(job.Issues, issue => issue.Message == backend.Status.Reason);
    }

    [Fact]
    public async Task Registry_composition_is_captured_and_isolated_per_manager()
    {
        using var temp = new TempDir(); var backend = new Backend(ArchiveWriteFormat.Tar);
        var list = new List<IArchiveJobBackend> { backend }; var registry = new ArchiveJobBackends(list); list.Clear();
        var enabled = new JobManager(new PortableFileOperations(), new ProviderRegistry(), temp.Dir("enabled"), registry);
        var disabled = new JobManager(new PortableFileOperations(), new ProviderRegistry(), temp.Dir("disabled"));
        var plan = Plan(temp, new(ArchiveWriteFormat.Tar)); var first = enabled.SubmitArchive(plan); var second = disabled.SubmitArchive(plan);
        await Task.WhenAll(Finished(first), Finished(second));
        Assert.Equal(JobState.Completed, first.State); Assert.Equal(JobState.Failed, second.State); Assert.Equal(1, backend.Calls);
        Assert.Throws<ArgumentException>(() => new ArchiveJobBackends([backend, backend]));
    }

    [Fact]
    public async Task Failure_to_create_a_journal_releases_the_password_without_calling_the_backend()
    {
        using var temp = new TempDir(); using var password = new ArchiveJobPassword(PublicPassword); var buffer = Buffer(password);
        string notDirectory = Path.Combine(temp.Path, "file"); File.WriteAllText(notDirectory, "occupied");
        var backend = new Backend(ArchiveWriteFormat.Rar5);
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), notDirectory, new([backend]));
        var job = manager.SubmitArchive(Plan(temp, Profile(9, 2)), password); await Finished(job);
        Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, backend.Calls); Erased(password, buffer);
    }

    [Fact]
    public void A_failed_worker_handoff_releases_ownership_even_when_a_running_observer_throws()
    {
        using var temp = new TempDir(); using var password = new ArchiveJobPassword(PublicPassword); var buffer = Buffer(password);
        var backend = new Backend(ArchiveWriteFormat.Rar5); var manager = Manager(temp, backend); Job? seen = null;
        manager.JobChanged += job => { if (job.State == JobState.Running) throw new InvalidOperationException("Synthetic handoff observer failure."); };
        Assert.Throws<InvalidOperationException>(() => manager.SubmitArchive(Plan(temp, Profile(9, 1)), password, register: job => seen = job));
        Assert.NotNull(seen); Assert.Equal(JobState.Failed, seen.State); Erased(password, buffer); Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public async Task A_reentrant_sibling_is_unblocked_after_failed_admission()
    {
        using var temp = new TempDir(); var backend = new Backend(ArchiveWriteFormat.Tar); var manager = Manager(temp, backend);
        var plan = Plan(temp, new(ArchiveWriteFormat.Tar)); Job? sibling = null;
        Assert.Throws<InvalidOperationException>(() => manager.SubmitArchive(plan, register: _ =>
        {
            sibling = manager.SubmitArchive(plan); Assert.Equal(JobState.Queued, sibling.State);
            throw new InvalidOperationException("Synthetic owner admission failure.");
        }));
        Assert.NotNull(sibling); await Finished(sibling); Assert.Equal(JobState.Completed, sibling.State); Assert.Equal(1, backend.Calls);
        Assert.Same(sibling, Assert.Single(manager.Jobs));
    }

    [Fact]
    public async Task Cancellation_during_availability_does_not_enter_the_backend()
    {
        using var temp = new TempDir(); using var password = new ArchiveJobPassword(PublicPassword); var buffer = Buffer(password);
        var backend = new Backend(ArchiveWriteFormat.Rar5); var manager = Manager(temp, backend); manager.MaxConcurrent = 0;
        var job = manager.SubmitArchive(Plan(temp, Profile(9, 1)), password); backend.Check = _ => job.Cancel();
        manager.MaxConcurrent = 1; manager.Schedule(); await Finished(job);
        Assert.Equal(JobState.Canceled, job.State); Assert.Equal(0, backend.Calls); Erased(password, buffer);
    }
}
