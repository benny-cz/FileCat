using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

[CollectionDefinition("Windows archive capacity", DisableParallelization = true)]
public sealed class WindowsArchiveCapacityCollection;

public sealed partial class WindowsArchiveStorageTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A_zone_only_creation_mark_is_bound_through_exclusive_publication_and_late_streams_refuse(bool extraStream)
    {
        RequireWindows(); using var root = new Fixture(); using var directory = WindowsArchiveDirectory.Open(root.Root);
        using var stage = directory.CreateArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, 1024);
        var publication = (IArchiveCreatePublicationStage)stage; var ct = TestContext.Current.CancellationToken;
        publication.BeginRebuild(1024, ct).Write([1, 2, 3]); var sealedStage = publication.SealForVerification(ct);
        var required = ArchiveWindowsOriginZone.ZoneOnly(4); var finalized = publication.ApplyCreationOrigin(sealedStage, required, ct);
        Assert.Equal(sealedStage.Version, finalized.Version); Assert.Equal(required.Digest, finalized.Origin.Digest);
        Assert.Equal(sealedStage.MemberModified, finalized.MemberModified);
        var review = publication.ReviewCreate(finalized, "result.tar", ct);
        if (extraStream)
        {
            using (var added = new FileStream(stage.DisplayPath + ":unexpected", FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) added.WriteByte(7);
            Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => publication.ClaimNewName("result.tar", review, ct)).Reason);
            Assert.False(File.Exists(root.PathOf("result.tar"))); return;
        }
        publication.ClaimNewName("result.tar", review, ct); var published = publication.ReviewPublished("result.tar", review, ct);
        Assert.Equal(required.Digest, published.Origin.Digest); Assert.Equal(sealedStage.Version, published.Version);
        Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(stage.DisplayPath));
        Assert.Equal(required.Bytes.ToArray(), Bytes(stage.DisplayPath + ":Zone.Identifier"));
        Assert.Throws<ArchiveWriteException>(() => publication.ApplyCreationOrigin(finalized, ArchiveOriginSnapshot.Absent(required.Format), ct));
    }

    [Fact]
    public void An_added_named_stream_refuses_private_creation_without_claiming_the_final_name()
    {
        RequireWindows(); using var root = new Fixture(); using var directory = WindowsArchiveDirectory.Open(root.Root);
        using var stage = directory.CreateArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, 1024); var retained = (IArchiveRetainedStage)stage;
        retained.BeginRebuild(1024, TestContext.Current.CancellationToken).Write([1]); var verified = retained.SealForVerification(TestContext.Current.CancellationToken);
        var publication = (IArchiveCreatePublicationStage)stage; var review = publication.ReviewCreate(verified, "result.tar", TestContext.Current.CancellationToken);
        using (var added = new FileStream(stage.DisplayPath + ":unexpected", FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            added.WriteByte(7);
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => publication.ClaimNewName("result.tar", review, TestContext.Current.CancellationToken)).Reason);
        Assert.True(File.Exists(stage.DisplayPath)); Assert.False(File.Exists(root.PathOf("result.tar")));
    }
    [Collection("Windows archive capacity")]
    public sealed class CapacityTests
    {
        [Fact]
        public void Held_NTFS_capacity_agrees_with_independent_path_API_and_requires_the_exact_parent()
        {
            RequireWindows();
            using var root = new Fixture();
            using var directory = WindowsArchiveDirectory.Open(root.Root);
            var destination = (IArchiveDestinationSpace)directory;
            var estimate = new ArchiveSpaceEstimate(0, 1024, 52, 0);
            var ct = TestContext.Current.CancellationToken;

            // Compare with path-based observations on both sides of the held query. Even outside the
            // parallel test collections, the runner can allocate or free clusters on this shared volume.
            var before = FileSystemCapacity.Query(root.Root);
            var review = destination.ReviewSpace(directory.Identity.ToString(), estimate, ct);
            var after = FileSystemCapacity.Query(root.Root);

            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.Equal(before.VolumeIdentity, after.VolumeIdentity);
            Assert.True(review.CapacityKnown);
            review.RequireAvailable();
            Assert.Equal("windows-volume:" + directory.Identity.VolumeSerial.ToString("X16"), review.Capacity!.VolumeIdentity);
            Assert.InRange(review.Capacity.AvailableBytes, Math.Min(before.AvailableBytes, after.AvailableBytes),
                Math.Max(before.AvailableBytes, after.AvailableBytes));
            Assert.Equal(ArchiveWriteReason.NeedsReview,
                Assert.Throws<ArchiveWriteException>(() => destination.ReviewSpace("wrong-parent", estimate, ct)).Reason);
        }
    }
    [Fact]
    public void Readonly_source_uses_portable_new_file_mode_while_its_native_attribute_remains_reviewed()
    {
        RequireWindows(); using var f = new Fixture(); string path = f.PathOf("input"); File.WriteAllBytes(path, [1]); File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            using var directory = WindowsArchiveDirectory.Open(f.Root); using var input = directory.OpenSource("input"); var ct = TestContext.Current.CancellationToken;
            var state = ((IArchiveRetainedFile)input).Observe(ct);
            Assert.Equal(0x1a4, state.Input(1).Mode); Assert.NotEqual(0u, input.CaptureOuterMetadata(ct).Attributes & (uint)FileAttributes.ReadOnly);
            File.SetAttributes(path, FileAttributes.Normal); var changed = ((IArchiveRetainedFile)input).Observe(ct);
            Assert.Equal(state.MemberMode, changed.MemberMode); Assert.NotEqual(state.MetadataDigest, changed.MetadataDigest);
            Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => ((IArchiveRetainedFile)input).Review(state, ct)).Reason);
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }
    [Fact]
    public void Derived_member_metadata_is_checked_against_native_observations_rather_than_only_the_digest_field()
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture(); File.WriteAllBytes(fixture.PathOf("input"), [1]);
        using var directory = WindowsArchiveDirectory.Open(fixture.Root); using var file = directory.OpenSource("input"); var retained = (IArchiveRetainedFile)file; var state = retained.Observe(ct);
        var forged = new ArchiveFileObservation(state.Version, state.ParentIdentity, state.LeafName, state.MetadataDigest, state.Origin, state.MemberModified, 0x1ed);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => retained.Review(forged, ct)).Reason);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => retained.OpenReadView(forged, ct)).Reason);
    }
    [Fact]
    public void Native_read_views_do_not_reopen_by_path_or_close_the_parent_owned_handle()
    {
        RequireWindows(); using var fixture = new Fixture(); File.WriteAllBytes(fixture.PathOf("input"), [1, 2, 3]);
        using var directory = WindowsArchiveDirectory.Open(fixture.Root); using var input = directory.OpenSource("input"); var retained = (IArchiveRetainedFile)input;
        var state = retained.Observe(TestContext.Current.CancellationToken); input.Position = 2;
        var first = retained.OpenReadView(state, TestContext.Current.CancellationToken); using var second = retained.OpenReadView(state, TestContext.Current.CancellationToken);
        Assert.Equal(1, first.ReadByte()); Assert.Equal(0, second.Position); first.Dispose(); Assert.True(input.CanRead);
        Assert.Equal(1, second.ReadByte()); Assert.Equal(2, input.Position); Assert.Throws<NotSupportedException>(() => second.WriteByte(1));
        input.Dispose(); Assert.Throws<ArchiveWriteException>(() => second.ReadByte());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wrong_artifact_kind_or_loose_stage_bound_is_refused_without_worker_launch(bool wrongKind)
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture(); using var directory = WindowsArchiveDirectory.Open(fixture.Root);
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [], ct); long bound = ArchiveTransformWire.OutputBound(plan.Expected, ct);
        using var stage = directory.CreateArtifact(Guid.NewGuid(), wrongKind ? WindowsArchiveArtifactKind.Manifest : WindowsArchiveArtifactKind.Stage, wrongKind ? bound : bound + 1); int launches = 0;
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveRetainedContainerPipeline.RebuildAndVerifyAsync(plan, null, new Dictionary<int, ArchiveRetainedBinding>(), stage,
            new((_, _) => { launches++; throw new InvalidOperationException(); }), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, error.Reason); Assert.Equal(0, launches); Assert.Equal(0, stage.Length); Assert.False(stage.CanWrite); Assert.True(File.Exists(stage.DisplayPath));
    }
}
