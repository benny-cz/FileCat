using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchivePreparationSpaceTests
{
    [Fact]
    public void Same_object_publication_charges_the_old_byte_copy_and_private_data_shadow_separately()
    {
        var estimate = new ArchiveSpaceEstimate(10000, 30000, 4000, 5000, retainedOriginalCopyBytes: 10000, publicationScratchBytes: 30000);
        Assert.Equal(10000, estimate.RetainedOriginalCopyBytes); Assert.Equal(30000, estimate.PublicationScratchBytes);
        Assert.Equal(30000 + 4000 + 5000 + 10000 + 30000 + (64L << 20), estimate.AdditionalBytes);
        Assert.Equal(10000 + estimate.AdditionalBytes, estimate.TotalFootprintBytes);
        Assert.Throws<ArchiveWriteException>(() => new ArchiveSpaceEstimate(0, 1024, 0, 0, -1));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveSpaceEstimate(0, 1024, 0, 0, 0, long.MaxValue));
    }

    [Fact]
    public void Full_stage_evidence_snapshots_and_margin_are_additional_while_original_is_already_allocated()
    {
        var estimate = new ArchiveSpaceEstimate(10000, 30000, 4000, 5000);
        Assert.Equal(30000 + 4000 + 5000 + (64L << 20), estimate.AdditionalBytes);
        Assert.Equal(10000 + estimate.AdditionalBytes, estimate.TotalFootprintBytes);
    }

    [Theory]
    [InlineData(-1, 1024, 0, 0)]
    [InlineData(0, 1023, 0, 0)]
    [InlineData(0, 1024, -1, 0)]
    [InlineData(0, 1024, 0, -1)]
    [InlineData(long.MaxValue, 1024, 0, 0)]
    [InlineData(0, long.MaxValue, 0, 0)]
    [InlineData(0, 1024, long.MaxValue, 0)]
    [InlineData(0, 1024, 0, long.MaxValue)]
    public void Invalid_or_unbounded_components_cannot_enter_estimation(long original, long stage, long evidence, long snapshots) =>
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => new ArchiveSpaceEstimate(original, stage, evidence, snapshots)).Reason);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Exact_available_boundary_is_checked_without_treating_unknown_as_zero(int difference)
    {
        var estimate = new ArchiveSpaceEstimate(0, 1024, 52, 0);
        var known = new ArchiveSpaceReview("parent", estimate, new("actual-volume", estimate.AdditionalBytes + difference));
        if (difference < 0) Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(known.RequireAvailable).Reason);
        else known.RequireAvailable();
        var unknown = new ArchiveSpaceReview("parent", estimate, null); Assert.False(unknown.CapacityKnown); unknown.RequireAvailable();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Known_insufficient_space_refuses_before_durable_archive_intent_creation_or_worker_launch(bool snapshots)
    {
        using var root = new TempDir(); using var journal = Journal(root); int creates = 0, launches = 0;
        var directory = new CapacityDirectory((parent, estimate) => new(parent, estimate,
            new("actual-volume", estimate.AdditionalBytes - (snapshots ? 10 : 1))));
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [], TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveJournaledContainerPreparation.PrepareAsync<UnixArchiveFile>(journal,
            Path.Combine(root.Path, "output.tar"), plan, null, new Dictionary<int, ArchiveRetainedBinding>(),
            (nonce, kind, maximum) => new(directory, nonce, kind, root.Path, "parent", maximum),
            _ => { creates++; throw new InvalidOperationException(); }, new((_, _) => { launches++; throw new InvalidOperationException(); }),
            new((_, _) => { launches++; throw new InvalidOperationException(); }), TestContext.Current.CancellationToken, snapshots ? 10 : 0));
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, error.Reason); Assert.Equal(1, directory.Calls); Assert.Equal(0, creates); Assert.Equal(0, launches);
        Assert.DoesNotContain(JobJournal.ReadAll(journal.Path), r => r.Type == "archive-transaction"); Assert.False(journal.NeedsRecovery);
        Assert.Empty(Directory.EnumerateFiles(root.Path, ".filecat-archive-*"));
    }

    [Fact]
    public async Task Unknown_capacity_is_preserved_and_does_not_prevent_the_checked_creation_path()
    {
        using var root = new TempDir(); using var journal = Journal(root); int creates = 0;
        ArchiveSpaceEstimate? observed = null;
        var directory = new CapacityDirectory((parent, estimate) => { observed = estimate; return new(parent, estimate, null); });
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<CreationReached>(() => ArchiveJournaledContainerPreparation.PrepareAsync<UnixArchiveFile>(journal,
            Path.Combine(root.Path, "output.tar"), plan, null, new Dictionary<int, ArchiveRetainedBinding>(),
            (nonce, kind, maximum) => new(directory, nonce, kind, root.Path, "parent", maximum),
            _ => { creates++; throw new CreationReached(); }, new((_, _) => throw new InvalidOperationException()), new((_, _) => throw new InvalidOperationException()), TestContext.Current.CancellationToken));
        Assert.NotNull(observed); Assert.Equal(1, creates); Assert.True(journal.NeedsRecovery);
        Assert.Single(JobJournal.ReadAll(journal.Path), r => r.Type == "archive-transaction");
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("estimate")]
    public void A_review_for_another_destination_or_byte_plan_does_not_authorize_creation(string mutation)
    {
        using var root = new TempDir(); var estimate = new ArchiveSpaceEstimate(0, 1024, 0, 0);
        var directory = new CapacityDirectory((parent, requested) => new(mutation == "parent" ? "other-parent" : parent,
            mutation == "estimate" ? new(0, 1024, 1, 0) : requested, null));
        var reservation = new ArchiveArtifactReservation(directory, Guid.NewGuid(), ArchiveArtifactKind.Stage, root.Path, "parent", 1024);
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => reservation.ReviewSpace(estimate, TestContext.Current.CancellationToken)).Reason);
    }

    private sealed class CreationReached : Exception;
    private sealed class CapacityDirectory(Func<string, ArchiveSpaceEstimate, ArchiveSpaceReview> review) : IArchiveDestinationSpace
    {
        internal int Calls;
        public ArchiveSpaceReview ReviewSpace(string parent, ArchiveSpaceEstimate estimate, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls++; return review(parent, estimate); }
    }
    private static JobJournal Journal(TempDir root) => JobJournal.Create(root.Dir("journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
        Archive = new(Path.Combine(root.Path, "output.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned space admission", "owned", [], []));
}
