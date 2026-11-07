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
    public async Task Native_jobs_create_and_publish_captured_forests_through_the_real_manager(ArchiveWriteFormat format, int mode)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0); var changes = ArchiveNativeJobCases.Fixture(root); var expected = ArchiveNativeJobCases.Payloads(root);
        var profile = ArchiveNativeJobCases.Profile(format, mode); var children = new List<Child>();
        Child Start(Guid nonce, string role) { var child = Child.Start(nonce, role); children.Add(child); return child; }
        var assessor = new ArchiveAssessmentClient((nonce, _) => Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        var backend = new ArchiveNativeJobBackend<UnixArchiveFile>(format, _ => ArchiveJobAvailability.Ready,
            (plan, _) => new UnixJobScope(Path.GetDirectoryName(plan.ArchivePath)!), assessor,
            new((nonce, _) => Start(nonce, "archive-transform-v2"), protocolVersion: 2), new((nonce, _) => Start(nonce, "archive-verify-v2"), protocolVersion: 2));
        string journals = Path.Combine(root, "journals"), target = Path.Combine(root, "typed-output.zip");
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([backend]));
        manager.DecisionRequested += pending => Assert.Fail("The fixture uses tick-aligned timestamps and has no exclusions or conflicts: " + pending.Request.Message);
        using var password = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
        var job = manager.SubmitArchive(new(target, null, changes, Profile: profile), password); await ArchiveNativeJobCases.Finished(job);
        if (password is not null) Assert.Throws<ObjectDisposedException>(() => password.CreateGrant());
        var manifest = ArchiveNativeJobCases.Expected(Assert.Single(Directory.GetFiles(journals)), profile, ct); ArchiveNativeJobCases.Check(manifest, expected);
        byte[] bytes = System.IO.File.ReadAllBytes(target);
        using var readingPassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
        var fresh = await assessor.AssessAsync(new(profile, bytes.Length, Hash(bytes)), () => new MemoryStream(bytes, false), ct, readingPassword?.CreateGrant());
        Assert.Equal(manifest.Digest, fresh.Manifest.Digest); Assert.Equal(3, children.Count);
        Assert.All(children, child => { Assert.True(child.Reaped); Assert.True(child.Disposed); });
        ArchiveNativeJobCases.Export(profile, "created", bytes, manifest);
    }

    [Fact]
    public void Native_forest_uses_source_paths_when_output_ancestors_are_renamed()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        ArchiveNativeJobCases.Fixture(root); var ct = TestContext.Current.CancellationToken;
        using var selection = ArchiveSelectionForest.Capture([new(Path.Combine(root, "a/tree"), "output/alias", ArchiveSelectionKind.Directory)], UnixArchiveSelectionTree.Open, ct);
        Assert.Contains(selection.Members, member => member.Name == "output/alias/sub/file");
        var binding = Assert.Single(selection.Inputs.Bindings).Value;
        using var view = binding.File.OpenReadView(binding.Reviewed, ct); using var bytes = new MemoryStream(); view.CopyTo(bytes);
        Assert.Equal(System.IO.File.ReadAllBytes(Path.Combine(root, "a/tree/sub/file")), bytes.ToArray());
    }

    [Fact]
    public async Task Native_job_cancellation_after_publication_reports_the_committed_archive()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        var changes = ArchiveNativeJobCases.Fixture(root); var expected = ArchiveNativeJobCases.Payloads(root); var ct = TestContext.Current.CancellationToken;
        var profile = ArchiveWriteProfile.ForRar5(encryption: new(ArchiveEncryptionMode.DataAndNames));
        var children = new List<Child>(); Child Start(Guid nonce, string role) { var child = Child.Start(nonce, role); children.Add(child); return child; }
        Job? submitted = null; using var password = new ArchiveJobPassword(ArchiveNativeJobCases.Password);
        var backend = new ArchiveNativeJobBackend<UnixArchiveFile>(profile.Format, _ => ArchiveJobAvailability.Ready,
            (_, _) => new UnixJobScope(root, () =>
            {
                using var grant = password.CreateGrant(); Assert.True(grant.UseOnce(value => value.SequenceEqual(ArchiveNativeJobCases.Password)));
                submitted!.Cancel();
            }), new((nonce, _) => Start(nonce, "archive-assess-v2"), protocolVersion: 2),
            new((nonce, _) => Start(nonce, "archive-transform-v2"), protocolVersion: 2), new((nonce, _) => Start(nonce, "archive-verify-v2"), protocolVersion: 2));
        string journals = Path.Combine(root, "journals"), target = Path.Combine(root, "saved.rar");
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([backend]));
        var job = manager.SubmitArchive(new(target, null, changes, Profile: profile), password, register: value => submitted = value);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(120));
        while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
        Assert.Equal(JobState.Canceled, job.State); Assert.Equal(1, job.ItemsDone);
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Committed && issue.Message.Contains("before cancellation", StringComparison.Ordinal));
        Assert.Throws<ObjectDisposedException>(() => password.CreateGrant());
        var manifest = ArchiveNativeJobCases.Expected(Assert.Single(Directory.GetFiles(journals)), profile, ct); ArchiveNativeJobCases.Check(manifest, expected);
        byte[] saved = System.IO.File.ReadAllBytes(target); using var readingPassword = new ArchiveJobPassword(ArchiveNativeJobCases.Password);
        var fresh = await new ArchiveAssessmentClient((nonce, _) => Start(nonce, "archive-assess-v2"), protocolVersion: 2)
            .AssessAsync(new(profile, saved.Length, Hash(saved)), () => new MemoryStream(saved, false), ct, readingPassword.CreateGrant());
        Assert.Equal(manifest.Digest, fresh.Manifest.Digest);
        Assert.Equal(3, children.Count); Assert.All(children, child => { Assert.True(child.Reaped); Assert.True(child.Disposed); });
        ArchiveNativeJobCases.Export(profile, "cancelled-after-commit", saved, manifest);
    }

    private sealed class UnixJobScope : IArchiveJobNativeScope<UnixArchiveFile>
    {
        private readonly UnixArchiveDirectory directory;
        private readonly Action? afterPublication;
        internal UnixJobScope(string root, Action? afterPublication = null)
        { directory = UnixArchiveDirectory.Open(root); this.afterPublication = afterPublication; }
        public ArchiveRetainedBinding? Original => null;
        public IArchiveSelectionTree OpenInputTree(string path) => UnixArchiveSelectionTree.Open(path);
        public ArchiveArtifactReservation Reserve(Guid nonce, ArchiveArtifactKind kind, long maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum);
        public UnixArchiveFile Create(ArchiveArtifactReservation reservation) => directory.CreateArtifact(reservation);
        public ArchiveJobPublication Publish(JobJournal journal, ArchivePreparedContainer<UnixArchiveFile> prepared,
            IReadOnlyDictionary<int, ArchiveRetainedBinding> inputs, CancellationToken ct)
        {
            var published = ArchiveNewFilePublication.PublishAsync(journal, prepared, inputs, directory.CreateArtifact, ct).GetAwaiter().GetResult();
            afterPublication?.Invoke();
            return new(published.Published, published.Facts.UnresolvedArtifacts);
        }
        public void Dispose() => directory.Dispose();
    }
}
