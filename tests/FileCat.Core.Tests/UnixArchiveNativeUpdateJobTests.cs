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
    [Theory, MemberData(nameof(ArchiveNativeJobCases.Profiles), MemberType = typeof(ArchiveNativeJobCases))]
    public async Task Native_jobs_update_existing_archives_through_the_private_exchange_and_clean_up_exactly(ArchiveWriteFormat format, int mode)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0); var changes = ArchiveNativeJobCases.Fixture(root); var expected = ArchiveNativeJobCases.Payloads(root);
        var profile = ArchiveNativeJobCases.Profile(format, mode); var children = new List<Child>();
        Child Start(Guid nonce, string role) { var child = Child.Start(nonce, role); lock (children) children.Add(child); return child; }
        var backend = UnixArchiveJobBackend.Create(format, (nonce, _) => Start(nonce, "archive-assess-v2"),
            (nonce, _) => Start(nonce, "archive-transform-v2"), (nonce, _) => Start(nonce, "archive-verify-v2"));
        string journals = Path.Combine(root, "journals"), target = Path.Combine(root, "saved" + Extension(format));
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([backend]));
        manager.DecisionRequested += pending => Assert.Fail("No decision is expected: " + pending.Request.Message);
        using (var password = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password))
            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, changes, Profile: profile), password));
        // Creation also prepared in a private stage folder: only the new archive is left beside the sources.
        Assert.Equal(new[] { "saved" + Extension(format) }, Directory.GetFiles(root).Select(Path.GetFileName).ToArray());
        Assert.Empty(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(target));
        File.SetUnixFileMode(target, (UnixFileMode)0x1a0); // 0640 must survive the update.
        string added = Path.Combine(root, "added.bin"); File.WriteAllBytes(added, "added by the update"u8.ToArray());
        File.SetUnixFileMode(added, (UnixFileMode)0x180); File.SetLastWriteTimeUtc(added, DateTime.UnixEpoch.AddSeconds(1700000002).AddTicks(7));
        var assessor = new ArchiveAssessmentClient((nonce, _) => Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        using var reviewPassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, reviewPassword, ct);
        var plan = reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "added.bin", added)]);
        var before = File.ReadAllBytes(target);
        var creationJournals = Directory.GetFiles(journals).ToHashSet(StringComparer.Ordinal);

        using (var password = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password))
        {
            var job = manager.SubmitArchive(plan, password);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(180));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
            Assert.True(job.State == JobState.Completed, string.Join(" | ", job.Issues.Select(issue => issue.Message)));
        }

        byte[] after = File.ReadAllBytes(target);
        Assert.NotEqual(before, after);
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(target));
        Assert.Empty(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        using var readingPassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
        var fresh = await assessor.AssessAsync(new(profile, after.Length, Hash(after)), () => new MemoryStream(after, false), ct, readingPassword?.CreateGrant());
        var files = fresh.Manifest.Members.Where(member => member.Type == ArchiveMemberType.File).ToArray();
        Assert.Contains(files, member => member.Name == "added.bin" && member.Length == 19);
        foreach (var (name, bytes) in expected) Assert.Contains(files, member => member.Name == name && member.Length == bytes.Length);
        // Both journals are resolved by the exact private cleanup: the creation's sidecars and the update's objects.
        foreach (string journal in Directory.GetFiles(journals))
        {
            var facts = JournalRecovery.ReadArchiveTransactions(journal);
            Assert.False(facts.RequiresReview, journal);
            if (creationJournals.Contains(journal)) Assert.True(Assert.Single(facts.CreationCleanups).IsResolved);
            else Assert.Equal(ArchiveExchangeUpdatePhase.Completed, Assert.Single(facts.ExchangeUpdates).Phase);
        }
        Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.All(children, child => { Assert.True(child.Reaped); Assert.True(child.Disposed); });
    }

    [Fact]
    public async Task Native_update_refused_by_a_changed_original_publishes_nothing_and_removes_its_private_files()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0); var changes = ArchiveNativeJobCases.Fixture(root);
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
            (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
        string journals = Path.Combine(root, "journals"), target = Path.Combine(root, "saved.tar");
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([backend]));
        await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, changes, Profile: profile), null));
        var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
        File.SetUnixFileMode(target, (UnixFileMode)0x1a0); // A metadata change after review must refuse the queued update.
        var job = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.CreateFolderEntry, "late/")]), null);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(120));
        while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Empty(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
    }

    [Fact]
    public async Task Native_jobs_reached_through_a_linked_folder_create_and_update_the_archive_in_its_actual_folder()
    {
        // macOS /tmp and /var, /home on Fedora Atomic desktops and linked project folders are ordinary places to work.
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string real = Path.Combine(root, "real"), link = Path.Combine(root, "link");
        Directory.CreateDirectory(real); File.SetUnixFileMode(real, (UnixFileMode)0x1c0); File.CreateSymbolicLink(link, real);
        ArchiveNativeJobCases.Fixture(real);
        var changes = new ArchiveChange[] { new(ArchiveChangeKind.AddFile, "first", Path.Combine(link, "a/payload")),
            new(ArchiveChangeKind.AddFolder, "tree", Path.Combine(link, "a/tree")) };
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
            (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"), new([backend]));
        manager.DecisionRequested += pending => Assert.Fail("No decision is expected: " + pending.Request.Message);
        var created = manager.SubmitArchive(new(Path.Combine(link, "saved.tar"), null, changes, Profile: profile), null);
        Assert.Equal(Path.Combine(real, "saved.tar"), created.Request.Archive!.ArchivePath);
        await ArchiveNativeJobCases.Completed(created);
        Assert.True(File.Exists(Path.Combine(real, "saved.tar")));
        Assert.Equal(real, new FileInfo(link).LinkTarget);

        string added = Path.Combine(real, "added.bin"); File.WriteAllBytes(added, "added through the link"u8.ToArray());
        File.SetUnixFileMode(added, (UnixFileMode)0x180); File.SetLastWriteTimeUtc(added, DateTime.UnixEpoch.AddSeconds(1700000002));
        var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(Path.Combine(link, "saved.tar"), profile, null, ct);
        var job = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "added.bin", Path.Combine(link, "added.bin"))]), null);
        Assert.Equal(Path.Combine(real, "saved.tar"), job.Request.Archive!.ArchivePath);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(120));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
        }
        Assert.True(job.State == JobState.Completed, string.Join(" | ", job.Issues.Select(issue => issue.Message)));
        byte[] after = File.ReadAllBytes(Path.Combine(real, "saved.tar"));
        var fresh = await assessor.AssessAsync(new(profile, after.Length, Hash(after)), () => new MemoryStream(after, false), ct);
        Assert.Equal(new[] { "added.bin", "first", "tree/sub/file" }, fresh.Manifest.Members
            .Where(member => member.Type == ArchiveMemberType.File).Select(member => member.Name).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetDirectories(real, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        Assert.Equal(real, new FileInfo(link).LinkTarget);
    }

    [Theory, MemberData(nameof(ArchiveNativeJobCases.Profiles), MemberType = typeof(ArchiveNativeJobCases))]
    public async Task Confined_Linux_application_workers_create_and_update_archives_through_the_private_exchange(ArchiveWriteFormat format, int mode)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Confined Linux application worker lane.");
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_NATIVE") != "1") Assert.Skip("Explicit owned native lane required.");
        string? app = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_APP");
        Assert.True(!string.IsNullOrEmpty(app) && Path.IsPathFullyQualified(app) && File.Exists(app), "A published self-contained FileCat application is required.");
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0); var changes = ArchiveNativeJobCases.Fixture(root); var expected = ArchiveNativeJobCases.Payloads(root);
        var profile = ArchiveNativeJobCases.Profile(format, mode); var workers = new List<FileCat.Core.Archives.Linux.LinuxArchiveProcess>();
        IArchiveWorkerProcess Start(Guid nonce, TimeSpan cpu, string role)
        { var worker = FileCat.Core.Archives.Linux.LinuxArchiveProcess.Start(app!, nonce, cpu, [role, nonce.ToString("D")]); lock (workers) workers.Add(worker); return worker; }
        var backend = UnixArchiveJobBackend.Create(format, (nonce, cpu) => Start(nonce, cpu, "archive-assess-v2"),
            (nonce, cpu) => Start(nonce, cpu, "archive-transform-v2"), (nonce, cpu) => Start(nonce, cpu, "archive-verify-v2"));
        string journals = Path.Combine(root, "journals"), target = Path.Combine(root, "saved" + Extension(format));
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([backend]));
        manager.DecisionRequested += pending => Assert.Fail("No decision is expected: " + pending.Request.Message);
        using (var password = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password))
            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, changes, Profile: profile), password));
        File.SetUnixFileMode(target, (UnixFileMode)0x1a0);
        string added = Path.Combine(root, "added.bin"); File.WriteAllBytes(added, "added by the update"u8.ToArray());
        File.SetUnixFileMode(added, (UnixFileMode)0x180); File.SetLastWriteTimeUtc(added, DateTime.UnixEpoch.AddSeconds(1700000002).AddTicks(7));
        var assessor = new ArchiveAssessmentClient((nonce, cpu) => Start(nonce, cpu, "archive-assess-v2"), protocolVersion: 2);
        using var reviewPassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, reviewPassword, ct);
        using (var password = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password))
        {
            var job = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "added.bin", added)]), password);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(300));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
            Assert.True(job.State == JobState.Completed, string.Join(" | ", job.Issues.Select(issue => issue.Message)));
        }
        byte[] after = File.ReadAllBytes(target);
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(target));
        Assert.Empty(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        using var readingPassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
        var fresh = await assessor.AssessAsync(new(profile, after.Length, Hash(after)), () => new MemoryStream(after, false), ct, readingPassword?.CreateGrant());
        var files = fresh.Manifest.Members.Where(member => member.Type == ArchiveMemberType.File).ToArray();
        Assert.Contains(files, member => member.Name == "added.bin" && member.Length == 19);
        foreach (var (name, bytes) in expected) Assert.Contains(files, member => member.Name == name && member.Length == bytes.Length);
        Assert.True(workers.Count >= 7);
        Assert.All(workers, worker => { Assert.True(worker.HasExited); Assert.Equal(0u, worker.ExitCode); Assert.False(Directory.Exists(worker.ControlPath)); });
    }

    private static string Extension(ArchiveWriteFormat format) => format switch
    {
        ArchiveWriteFormat.Tar => ".tar", ArchiveWriteFormat.TarGzip => ".tar.gz", ArchiveWriteFormat.TarBzip2 => ".tar.bz2",
        ArchiveWriteFormat.TarZstd => ".tar.zst", ArchiveWriteFormat.Rar5 => ".rar", _ => ".7z",
    };
}
