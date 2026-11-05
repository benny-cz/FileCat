using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
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
    [Fact]
    public void Held_NTFS_capacity_agrees_with_independent_path_API_and_requires_the_exact_parent()
    {
        RequireWindows(); using var root = new Fixture(); using var directory = WindowsArchiveDirectory.Open(root.Root);
        var estimate = new ArchiveSpaceEstimate(0, 1024, 52, 0);
        var review = ((IArchiveDestinationSpace)directory).ReviewSpace(directory.Identity.ToString(), estimate, TestContext.Current.CancellationToken);
        var independent = FileCat.Core.FileSystem.FileSystemCapacity.Query(root.Root);
        Assert.NotNull(independent); Assert.True(review.CapacityKnown); review.RequireAvailable();
        Assert.Equal("windows-volume:" + directory.Identity.VolumeSerial.ToString("X16"), review.Capacity!.VolumeIdentity);
        Assert.InRange(review.Capacity.AvailableBytes, Math.Max(0, independent.AvailableBytes - (64L << 20)), independent.AvailableBytes + (64L << 20));
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() => ((IArchiveDestinationSpace)directory).ReviewSpace("wrong-parent", estimate, TestContext.Current.CancellationToken)).Reason);
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
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveRetainedTarPipeline.RebuildAndVerifyAsync(plan, null, new Dictionary<int, ArchiveRetainedBinding>(), stage,
            new((_, _) => { launches++; throw new InvalidOperationException(); }), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, error.Reason); Assert.Equal(0, launches); Assert.Equal(0, stage.Length); Assert.False(stage.CanWrite); Assert.True(File.Exists(stage.DisplayPath));
    }
}
