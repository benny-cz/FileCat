using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
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
