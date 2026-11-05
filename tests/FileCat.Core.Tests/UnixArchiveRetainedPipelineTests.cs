using System.Formats.Tar;
using System.Buffers.Binary;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Theory]
    [InlineData("existing-target")]
    [InlineData("changed-input")]
    [InlineData("metadata-change")]
    [InlineData("cancelled")]
    [InlineData("after-claim")]
    [InlineData("unsafe-directory")]
    public async Task New_private_archive_publication_failures_retain_exact_evidence(string failure)
    {
        Native(); using var root = new TempDir(); string path = Canonical(root.Path); File.SetUnixFileMode(path, (UnixFileMode)0x1c0);
        Payload(Path.Combine(path, "input"), [1]);
        using var directory = UnixArchiveDirectory.Open(path); using var input = directory.OpenSource("input"); var state = Observe(input);
        await ArchiveCreationPublicationCases.Failure(failure, path, input, state,
            (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
            new((nonce, _) => Child.Start(nonce, "archive-transform")), new((nonce, _) => Child.Start(nonce, "archive-verify")), TestContext.Current.CancellationToken);
    }
    [Theory]
    [InlineData(0x1a4)]
    [InlineData(0x1ed)]
    [InlineData(0x140)]
    public void New_member_mode_omits_special_bits_but_native_review_still_detects_their_change(int ordinary)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path), path = Path.Combine(root, "input"); Payload(path, [1]);
        File.SetUnixFileMode(path, (UnixFileMode)(ordinary | 0xe00));
        using var directory = UnixArchiveDirectory.Open(root); using var input = directory.OpenSource("input"); var state = Observe(input);
        Assert.Equal((uint)(ordinary | 0xe00), input.CaptureOuterMetadata(TestContext.Current.CancellationToken).Native.Mode);
        Assert.Equal(ordinary, state.Input(1).Mode);
        File.SetUnixFileMode(path, (UnixFileMode)ordinary); var changed = Observe(input);
        Assert.Equal(state.MemberMode, changed.MemberMode); Assert.NotEqual(state.MetadataDigest, changed.MetadataDigest);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => ((IArchiveRetainedFile)input).Review(state, TestContext.Current.CancellationToken)).Reason);
    }
    private static ArchiveFileObservation Observe(UnixArchiveFile file) => ((IArchiveRetainedFile)file).Observe(TestContext.Current.CancellationToken);
    private static void Payload(string path, byte[] bytes)
    { File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(1700000000)); File.SetUnixFileMode(path, (UnixFileMode)0x1a4); }

    [Fact]
    public void Retained_input_captures_and_rereviews_every_native_nanosecond()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path), path = Path.Combine(root, "input"); Payload(path, [1]);
        ArchiveNativeTimestampFixture.SetModified(path, -1, 999999999);
        using var directory = UnixArchiveDirectory.Open(root); using var input = directory.OpenSource("input"); var state = Observe(input);
        Assert.Equal(new ArchiveTimestamp(-1, 999999999), state.Input(1).ModifiedTimestamp);
        ArchiveNativeTimestampFixture.SetModified(path, -1, 999999998);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => ((IArchiveRetainedFile)input).Review(state, TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public void Native_read_views_retain_identity_have_independent_cursors_and_do_not_dispose_the_held_owner()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); Payload(Path.Combine(root, "input"), [1, 2, 3]);
        using var directory = UnixArchiveDirectory.Open(root); using var file = directory.OpenSource("input"); var state = Observe(file);
        file.Position = 2; var first = ((IArchiveRetainedFile)file).OpenReadView(state, TestContext.Current.CancellationToken);
        using var second = ((IArchiveRetainedFile)file).OpenReadView(state, TestContext.Current.CancellationToken);
        Assert.Equal(1, first.ReadByte()); Assert.Equal(0, second.Position); Assert.Equal(2, file.Position);
        first.Dispose(); Assert.True(file.CanRead); Assert.Equal(1, second.ReadByte()); Assert.Equal(2, file.Position);
        Assert.Throws<NotSupportedException>(() => second.WriteByte(9)); file.Dispose(); Assert.Throws<ArchiveWriteException>(() => second.ReadByte());
    }

    [Theory]
    [InlineData(false, ArchiveWriteFormat.Tar)]
    [InlineData(true, ArchiveWriteFormat.Tar)]
    [InlineData(false, ArchiveWriteFormat.TarGzip)]
    [InlineData(true, ArchiveWriteFormat.TarGzip)]
    [InlineData(false, ArchiveWriteFormat.TarBzip2)]
    [InlineData(true, ArchiveWriteFormat.TarBzip2)]
    [InlineData(false, ArchiveWriteFormat.TarZstd)]
    [InlineData(true, ArchiveWriteFormat.TarZstd)]
    public async Task Retained_native_inputs_rebuild_into_a_real_private_sealed_and_freshly_verified_stage(bool update, ArchiveWriteFormat format)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        var profile = new ArchiveWriteProfile(format);
        if (!update) File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        byte[] input = "native retained payload"u8.ToArray(); Payload(Path.Combine(root, "payload"), input);
        if (!update) File.SetUnixFileMode(Path.Combine(root, "payload"), (UnixFileMode)0xfed);
        ArchiveNativeTimestampFixture.SetModified(Path.Combine(root, "payload"));
        using var directory = UnixArchiveDirectory.Open(root); using var captured = directory.OpenSource("payload"); var inputState = Observe(captured);
        UnixArchiveFile? original = null; ArchiveRetainedBinding? source = null; ArchiveAssessment? assessment = null;
        try
        {
            if (update)
            {
                using (var data = File.Create(Path.Combine(root, "original.tar")))
                using (Stream? gzip = format switch { ArchiveWriteFormat.TarGzip => new System.IO.Compression.GZipStream(data, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true),
                    ArchiveWriteFormat.TarBzip2 => SharpCompress.Compressors.BZip2.BZip2Stream.Create(data, SharpCompress.Compressors.CompressionMode.Compress, false, leaveOpen: true),
                    ArchiveWriteFormat.TarZstd => new SharpCompress.Compressors.ZStandard.CompressionStream(data, leaveOpen: true), _ => null })
                using (var writer = new TarWriter(gzip ?? (Stream)data, TarEntryFormat.Pax))
                { using var content = new MemoryStream("before"u8.ToArray()); writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "old") { DataStream = content }); }
                original = directory.OpenSource("original.tar"); var observed = Observe(original); source = new(original, observed);
                assessment = await new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce)).AssessAsync(new(profile, observed.Version.Length, observed.Version.Sha256),
                    () => ((IArchiveRetainedFile)original).OpenReadView(observed, ct), ct);
            }
            var plan = ArchiveTarTransformPlan.Create(profile, assessment, [inputState.Input(7)], source is null ? [] :
                [new(ArchiveChangeKind.Replace, new(assessment!.CatalogRevision, "old", ArchiveMemberType.File, 0), inputId: 7)],
                [new("added", ArchiveMemberType.File, 7)], ct);
            using var journal = ReservationJournal(root, profile);
            Child? transform = null, verifier = null;
            using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, Path.Combine(root, update ? "original.tar" : "result.tar"), plan, source,
                new Dictionary<int, ArchiveRetainedBinding> { [7] = new(captured, inputState) },
                (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
                r => { ArchiveJournaledTarAssertions.BeforeCreate(journal, r); return directory.CreateArtifact(r); },
                new((nonce, _) => { ArchiveJournaledTarAssertions.Phase(journal, ArchiveTransactionPhase.Rebuilding); return transform = Child.Start(nonce, "archive-transform"); }),
                new((nonce, _) => { ArchiveJournaledTarAssertions.Phase(journal, ArchiveTransactionPhase.Verifying); return verifier = Child.Start(nonce, "archive-verify"); }), ct);
            var stage = prepared.Stage; var result = prepared.Verified; string retained = stage.DisplayPath;
            ArchiveJournaledTarAssertions.Complete(prepared, journal, plan, ct);
            Assert.Equal(update ? 0x1a4 : 0x1ed, inputState.Input(7).Mode);
            Assert.Equal(new ArchiveTimestamp(1700000000, 123456789), inputState.Input(7).ModifiedTimestamp);
            var addedMetadata = result.Verification.ObservedManifest.Members.Single(m => m.Name == "added").Metadata.ToArray();
            Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(addedMetadata));
            Assert.Equal(1700000000, BinaryPrimitives.ReadInt64LittleEndian(addedMetadata.AsSpan(22)));
            Assert.Equal(123456789, BinaryPrimitives.ReadInt32LittleEndian(addedMetadata.AsSpan(30)));
            Assert.Equal(inputState.Input(7).Mode, BinaryPrimitives.ReadInt32LittleEndian(result.Verification.ObservedManifest.Members.Single(m => m.Name == "added").Metadata[2..]));
            Assert.Equal(Version(stage), result.Stage.Version); Assert.Equal(result.Stage.Version, result.Verification.Stage);
            Assert.Equal(plan.Expected.Digest, result.Verification.ManifestDigest); Assert.True(transform!.Reaped); Assert.True(verifier!.Reaped);
            Assert.NotEqual(transform.ProcessId, verifier.ProcessId); Assert.False(stage.CanWrite); Assert.Throws<NotSupportedException>(() => stage.WriteByte(1));
            Assert.True(File.Exists(retained)); Assert.False(File.Exists(Path.Combine(root, "result.tar"))); Assert.Equal(input, File.ReadAllBytes(Path.Combine(root, "payload")));
            Assert.True(captured.CanRead); Assert.Equal(inputState.Version, Observe(captured).Version);
            if (!update) await ArchiveJournaledTarAssertions.PublishNew(prepared, journal,
                new Dictionary<int, ArchiveRetainedBinding> { [7] = new(captured, inputState) },
                r => { ArchiveJournaledTarAssertions.BeforeCreate(journal, r); return directory.CreateArtifact(r); }, ct);
        }
        finally { original?.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Input_changes_after_copy_are_caught_by_final_native_review_even_when_the_input_was_empty(bool empty)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path), inputPath = Path.Combine(root, "input");
        Payload(inputPath, empty ? [] : "before"u8.ToArray()); using var directory = UnixArchiveDirectory.Open(root); using var input = directory.OpenSource("input"); var state = Observe(input);
        var plan = ArchiveTarTransformPlan.Create(null, [state.Input(1)], [], [new("file", ArchiveMemberType.File, 1)], ct);
        using var stage = Stage(directory, ArchiveTransformWire.OutputBound(plan.Expected, ct)); Child? verifier = null;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveRetainedTarPipeline.RebuildAndVerifyAsync(plan, null,
            new Dictionary<int, ArchiveRetainedBinding> { [1] = new(input, state) }, stage, new((nonce, _) => Child.Start(nonce, "archive-transform")),
            new((nonce, _) => { File.WriteAllBytes(inputPath, empty ? [1] : "after!"u8.ToArray()); return verifier = Child.Start(nonce, "archive-verify"); }), ct));
        Assert.Equal(ArchiveWriteReason.SourceChanged, failure.Reason); Assert.True(verifier!.Reaped); Assert.False(stage.CanWrite); Assert.True(File.Exists(stage.DisplayPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wrong_artifact_kind_or_loose_stage_bound_is_refused_before_worker_launch(bool wrongKind)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); using var directory = UnixArchiveDirectory.Open(Canonical(temp.Path));
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [], ct); long bound = ArchiveTransformWire.OutputBound(plan.Expected, ct);
        using var stage = directory.CreateArtifact(Guid.NewGuid(), wrongKind ? UnixArchiveArtifactKind.Manifest : UnixArchiveArtifactKind.Stage, wrongKind ? bound : bound + 1); int launches = 0;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveRetainedTarPipeline.RebuildAndVerifyAsync(plan, null, new Dictionary<int, ArchiveRetainedBinding>(), stage,
            new((_, _) => { launches++; throw new InvalidOperationException(); }), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, failure.Reason); Assert.Equal(0, launches); Assert.Equal(0, stage.Length); Assert.False(stage.CanWrite); Assert.True(File.Exists(stage.DisplayPath));
    }

    [Fact]
    public async Task Failed_worker_setup_closes_native_stage_writes_and_retains_the_exact_artifact()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); using var directory = UnixArchiveDirectory.Open(Canonical(temp.Path));
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [], ct); using var stage = Stage(directory, ArchiveTransformWire.OutputBound(plan.Expected, ct));
        await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveRetainedTarPipeline.RebuildAndVerifyAsync(plan, null, new Dictionary<int, ArchiveRetainedBinding>(), stage,
            new((_, _) => throw new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, "Owned setup refusal.")), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.False(stage.CanWrite); Assert.True(File.Exists(stage.DisplayPath)); Assert.Equal(0, stage.Length);
    }

    [Fact]
    public async Task Already_cancelled_preparation_closes_the_supplied_stage_without_starting_a_worker()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); using var directory = UnixArchiveDirectory.Open(Canonical(temp.Path));
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [], ct); using var stage = Stage(directory, ArchiveTransformWire.OutputBound(plan.Expected, ct));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); int launches = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveRetainedTarPipeline.RebuildAndVerifyAsync(plan, null, new Dictionary<int, ArchiveRetainedBinding>(), stage,
            new((_, _) => { launches++; throw new InvalidOperationException(); }), new((_, _) => throw new InvalidOperationException()), cancelled.Token));
        Assert.Equal(0, launches); Assert.False(stage.CanWrite); Assert.Equal(0, stage.Length); Assert.True(File.Exists(stage.DisplayPath));
    }

    [Fact]
    public async Task Two_captured_IDs_cannot_disguise_one_native_identity_before_rebuilding()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        Payload(Path.Combine(root, "input"), [1]); using var directory = UnixArchiveDirectory.Open(root); using var file = directory.OpenSource("input"); var state = Observe(file);
        var plan = ArchiveTarTransformPlan.Create(null, [state.Input(1), state.Input(2)], [], [new("one", ArchiveMemberType.File, 1), new("two", ArchiveMemberType.File, 2)], ct);
        using var stage = Stage(directory, ArchiveTransformWire.OutputBound(plan.Expected, ct)); int launches = 0;
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveRetainedTarPipeline.RebuildAndVerifyAsync(plan, null,
            new Dictionary<int, ArchiveRetainedBinding> { [1] = new(file, state), [2] = new(file, state) }, stage,
            new((_, _) => { launches++; throw new InvalidOperationException(); }), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(ArchiveWriteReason.SourceChanged, error.Reason); Assert.Equal(0, launches); Assert.False(stage.CanWrite); Assert.Equal(0, stage.Length);
    }

    [Fact]
    public async Task A_stage_changed_after_the_fresh_verifier_exits_cannot_escape_final_native_review()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); using var directory = UnixArchiveDirectory.Open(Canonical(temp.Path));
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [], ct); using var stage = Stage(directory, ArchiveTransformWire.OutputBound(plan.Expected, ct));
        Child? child = null;
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveRetainedTarPipeline.RebuildAndVerifyAsync(plan, null, new Dictionary<int, ArchiveRetainedBinding>(), stage,
            new((nonce, _) => Child.Start(nonce, "archive-transform")), new((nonce, _) => new AfterReaping(child = Child.Start(nonce, "archive-verify"), () =>
            { using var mutation = new FileStream(stage.DisplayPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); mutation.WriteByte(0xff); })), ct));
        Assert.Equal(ArchiveWriteReason.SourceChanged, error.Reason); Assert.True(child!.Reaped); Assert.False(stage.CanWrite); Assert.True(File.Exists(stage.DisplayPath));
    }

    [Fact]
    public void Derived_member_metadata_cannot_be_forged_alongside_an_unchanged_native_digest()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path); Payload(Path.Combine(root, "input"), [1]);
        using var directory = UnixArchiveDirectory.Open(root); using var file = directory.OpenSource("input"); var state = Observe(file);
        var forged = new ArchiveFileObservation(state.Version, state.ParentIdentity, state.LeafName, state.MetadataDigest, state.Origin, state.MemberModified, 0x1ed);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => ((IArchiveRetainedFile)file).Review(forged, ct)).Reason);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => ((IArchiveRetainedFile)file).OpenReadView(forged, ct)).Reason);
    }

    private sealed class AfterReaping(IArchiveWorkerProcess inner, Action mutate) : IArchiveWorkerProcess
    {
        public Stream Input => inner.Input; public Stream Output => inner.Output;
        public bool HasExited => inner.HasExited; public uint ExitCode => inner.ExitCode;
        public TimeSpan TotalProcessorTime => inner.TotalProcessorTime; public bool ErrorLimitExceeded => inner.ErrorLimitExceeded;
        public void Kill() => inner.Kill();
        public async ValueTask DisposeAsync() { await inner.DisposeAsync(); mutate(); }
    }
}
