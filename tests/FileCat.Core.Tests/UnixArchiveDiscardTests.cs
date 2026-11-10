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
    // Plan §7.1/§10.3: cancelling before publication leaves the original unchanged and, through the exact discard, no
    // FileCat file or folder and no journal for review. Covers a new archive and an update of an existing one.
    [Theory, InlineData(false, false), InlineData(true, false), InlineData(false, true), InlineData(true, true)]
    public async Task Cancelling_during_the_rebuild_discards_every_private_object_and_closes_the_journal(bool update, bool move)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string folder = Path.Combine(root, "archives"); Directory.CreateDirectory(folder); File.SetUnixFileMode(folder, (UnixFileMode)0x1c0);
        string small = Path.Combine(root, "small.txt"); File.WriteAllText(small, "small"); File.SetLastWriteTimeUtc(small, DateTime.UnixEpoch.AddSeconds(1700000000));
        string big = Path.Combine(root, "big.bin"); var bytes = new byte[2 << 20]; new Random(810).NextBytes(bytes); File.WriteAllBytes(big, bytes);
        File.SetLastWriteTimeUtc(big, DateTime.UnixEpoch.AddSeconds(1700000001));
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        Job? cancelled = null; bool armed = false; int launches = 0;
        var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"), (nonce, _) =>
        {
            if (Volatile.Read(ref armed))
            {
                // The rebuild has started (Rebuilding is recorded): the user cancels now.
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref cancelled) is not null, TimeSpan.FromSeconds(30)));
                Volatile.Read(ref cancelled)!.Cancel(); Interlocked.Increment(ref launches);
            }
            return Child.Start(nonce, "archive-transform-v2");
        }, (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
        string journals = Path.Combine(root, "journals");
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([backend]));
        string target = Path.Combine(folder, update ? "kept.tar" : "new.tar");
        byte[]? original = null; ArchivePlan plan;
        if (update)
        {
            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "small.txt", small)], Profile: profile), null));
            original = File.ReadAllBytes(target);
            var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
            var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
            plan = reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "big.bin", big)]);
        }
        else plan = new(target, null, [new(ArchiveChangeKind.AddFile, "big.bin", big)], Profile: profile);
        Volatile.Write(ref armed, true);
        var job = manager.SubmitArchive(plan with { MoveSources = move }, null); Volatile.Write(ref cancelled, job);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(120));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
        }
        string issues = string.Join(" | ", job.Issues.Select(issue => issue.Message));
        Assert.True(job.State == JobState.Canceled, job.State + ": " + issues);
        Assert.Equal(1, launches);
        Assert.DoesNotContain(job.Issues, issue => issue.Message.Contains("kept them for review", StringComparison.Ordinal));
        if (update) Assert.Equal(original, File.ReadAllBytes(target));
        Assert.Equal(update ? new[] { "kept.tar" } : [], Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).ToArray());
        Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        // A cancelled move published nothing, so it removed no source (plan §11.6.1).
        Assert.True(File.Exists(big)); Assert.True(File.Exists(small));
    }

    // A creation whose transform fails after Rebuilding runs the scope's exact discard; the real process is killed before it
    // and around every discard record. Recovery reopens the private folder by identity and finishes the removal exactly.
    public static IEnumerable<object[]> DiscardCrashPoints()
    {
        yield return ["after:T:Rebuilding"];
        yield return ["after:D:Cataloged"];
        // A new TAR's failed rebuild records the stage and two sealed sidecars (expected output, origin evidence).
        for (int i = 0; i < 3; i++) { yield return [$"after:D:DeleteIntent:{i}"]; yield return [$"before:D:Deleted:{i + 1}"]; }
        yield return ["before:D:Completed"];
    }

    [Theory, MemberData(nameof(DiscardCrashPoints))]
    public async Task Real_process_crashes_around_the_discard_recover_to_exact_removal(string point)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string source = Path.Combine(root, "source.txt"); File.WriteAllText(source, "never archived");
        File.SetUnixFileMode(source, (UnixFileMode)0x180); File.SetLastWriteTimeUtc(source, DateTime.UnixEpoch.AddSeconds(1700000000));

        await KillCreationProcess(root, "discard:" + point, ct);

        var job = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var interrupted = JournalRecovery.ReadArchiveTransactions(job.JournalPath); Assert.Null(interrupted.Problem);
        var facts = Assert.Single(interrupted.Transactions);
        Assert.True(interrupted.RequiresReview);
        Assert.Equal(point.Contains(":D:", StringComparison.Ordinal) ? 1 : 0, interrupted.Discards.Count);
        Assert.False(File.Exists(Path.Combine(root, "created.tar")));

        using (var discard = new ArchiveTransactionDiscard(UnixArchiveDiscard.OpenRecovery(job.JournalPath, facts.Nonce, ct)))
        {
            Assert.True(discard.Execute(ct).IsResolved);
            discard.Finish("The interrupted discard of an unpublished creation was finished exactly.");
        }

        Assert.False(JournalRecovery.ReadArchiveTransactions(job.JournalPath).RequiresReview);
        Assert.Empty(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.Empty(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        Assert.Equal(new[] { "source.txt" }, Directory.GetFiles(root).Select(Path.GetFileName));
        Assert.Equal("never archived", File.ReadAllText(source));
    }

    // The same for an update of an existing archive (plan §7.1 preparation phases, before the exchange's Reserved record):
    // its failed rebuild records the stage and three sealed sidecars (assessed source, expected output, origin evidence).
    // After each kill the original is untouched, and recovery removes exactly the private objects.
    public static IEnumerable<object[]> UpdateDiscardCrashPoints()
    {
        yield return ["after:T:Rebuilding"];
        yield return ["after:D:Cataloged"];
        for (int i = 0; i < 4; i++) { yield return [$"after:D:DeleteIntent:{i}"]; yield return [$"before:D:Deleted:{i + 1}"]; }
        yield return ["before:D:Completed"];
    }

    [Theory, MemberData(nameof(UpdateDiscardCrashPoints))]
    public async Task Real_update_process_crashes_during_preparation_recover_to_exact_removal(string point)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string target = Path.Combine(root, "original.tar");
        using (var output = File.Create(target))
        using (var writer = new System.Formats.Tar.TarWriter(output, System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: false))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "kept.txt")
            {
                DataStream = new MemoryStream("kept member"u8.ToArray()), ModificationTime = DateTimeOffset.FromUnixTimeSeconds(1700000000),
                Mode = (UnixFileMode)0x1a4,
            };
            writer.WriteEntry(entry);
        }
        File.SetUnixFileMode(target, (UnixFileMode)0x1a0);
        string added = Path.Combine(root, "added.bin"); File.WriteAllBytes(added, "added by the failed update"u8.ToArray());
        File.SetUnixFileMode(added, (UnixFileMode)0x180); File.SetLastWriteTimeUtc(added, DateTime.UnixEpoch.AddSeconds(1700000002));
        byte[] original = File.ReadAllBytes(target);
        string sentinel = Path.Combine(root, "sentinel"); File.WriteAllText(sentinel, "owned unchanged discard sentinel");

        await KillCreationProcess(root, point, ct, process: "update-discard");

        var job = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var interrupted = JournalRecovery.ReadArchiveTransactions(job.JournalPath); Assert.Null(interrupted.Problem);
        var facts = Assert.Single(interrupted.Transactions);
        Assert.Empty(interrupted.ExchangeUpdates); // Publication never started.
        Assert.True(interrupted.RequiresReview);
        Assert.Equal(point.Contains(":D:", StringComparison.Ordinal) ? 1 : 0, interrupted.Discards.Count);
        Assert.Equal(original, File.ReadAllBytes(target));

        using (var discard = new ArchiveTransactionDiscard(UnixArchiveDiscard.OpenRecovery(job.JournalPath, facts.Nonce, ct)))
        {
            Assert.True(discard.Execute(ct).IsResolved);
            discard.Finish("The interrupted discard of an unpublished update was finished exactly.");
        }

        Assert.False(JournalRecovery.ReadArchiveTransactions(job.JournalPath).RequiresReview);
        Assert.Empty(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.Empty(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        Assert.Equal(original, File.ReadAllBytes(target));
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(target));
        Assert.Equal("owned unchanged discard sentinel", File.ReadAllText(sentinel));
        Assert.Equal(new[] { "added.bin", "original.tar", "sentinel" }, Directory.GetFiles(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    // Recovery binds the private folder by its recorded identity: a replaced folder of the same name is never touched.
    [Fact]
    public async Task A_recovery_discard_refuses_a_replaced_private_folder_and_touches_nothing()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string source = Path.Combine(root, "source.txt"); File.WriteAllText(source, "never archived");
        File.SetUnixFileMode(source, (UnixFileMode)0x180); File.SetLastWriteTimeUtc(source, DateTime.UnixEpoch.AddSeconds(1700000000));
        await KillCreationProcess(root, "discard:after:T:Rebuilding", ct);
        var job = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(job.JournalPath).Transactions);
        string privateFolder = Path.GetDirectoryName(facts.StagePath)!;
        // Replace the private folder with another of the same name: its identity no longer matches, so nothing is touched.
        string moved = privateFolder + ".moved"; Directory.Move(privateFolder, moved);
        Directory.CreateDirectory(privateFolder); File.SetUnixFileMode(privateFolder, (UnixFileMode)0x1c0);
        File.WriteAllText(Path.Combine(privateFolder, Path.GetFileName(facts.StagePath)), "someone else's file");
        var refusal = Assert.Throws<ArchiveWriteException>(() => UnixArchiveDiscard.OpenRecovery(job.JournalPath, facts.Nonce, ct).Dispose());
        Assert.Equal(ArchiveWriteReason.NeedsReview, refusal.Reason);
        Assert.Equal("someone else's file", File.ReadAllText(Path.Combine(privateFolder, Path.GetFileName(facts.StagePath))));
        Assert.True(Directory.Exists(moved));
        Assert.True(JournalRecovery.ReadArchiveTransactions(job.JournalPath).RequiresReview);
    }
}
