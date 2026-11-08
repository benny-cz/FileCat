using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveCapturedInputsTests
{
    [Theory, MemberData(nameof(WorkflowProfiles))]
    public void Skip_and_replace_reduce_grants_and_never_bulk_replace_duplicate_members(ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        var profile = WorkflowProfile(format, mode); var source = WorkflowSource(profile, duplicates: true);
        using var captured = new ArchiveCapturedInputs(); var inputs = Enumerable.Range(1, 3).Select(id => new Source(id)).ToArray();
        for (int i = 0; i < inputs.Length; i++) captured.Capture(i + 1, inputs[i].Open, Ct);
        captured.Seal(Ct);
        var review = ArchiveWorkflowReview.Prepare(profile, source, captured, [], [],
            [new("tree/old", ArchiveMemberType.File, 1), new("Case", ArchiveMemberType.File, 2), new("new", ArchiveMemberType.File, 3)],
            0, Ct, allowSkippedTypeConflicts: true);
        Assert.Equal(2, review.ReplacementNames); Assert.Equal(1, review.DuplicateReplacementNames);
        using (var skipped = review.CreateSelectedPlan(false, false, Ct))
        {
            Assert.Equal(2, skipped.SkippedNames); Assert.Equal(new[] { 3 }, skipped.CommittedInputs.Order().ToArray());
            Assert.Single(skipped.Inputs.Bindings); ArchiveCapturedInputs.RequirePlanCost(skipped.Plan, skipped.Inputs.Bindings);
            Assert.Throws<ArchiveWriteException>(() => ArchiveCapturedInputs.RequirePlanCost(skipped.Plan, captured.Bindings));
            Assert.Equal(2, skipped.Plan.Expected.Members.Count(m => m.Name == "tree/old"));
            Assert.Equal(source.Manifest.Members.Single(m => m.Name == "Case").Sha256, skipped.Plan.Expected.Members.Single(m => m.Name == "Case").Sha256);
        }
        using (var replaced = review.CreateSelectedPlan(true, false, Ct))
        {
            Assert.Equal(1, replaced.SkippedNames); Assert.Equal(new[] { 2, 3 }, replaced.CommittedInputs.Order().ToArray());
            ArchiveCapturedInputs.RequirePlanCost(replaced.Plan, replaced.Inputs.Bindings);
            Assert.Equal(2, replaced.Plan.Expected.Members.Count(m => m.Name == "tree/old"));
            Assert.Equal(captured.Inputs.Single(i => i.Id == 2).Sha256, replaced.Plan.Expected.Members.Single(m => m.Name == "Case").Sha256);
        }
        Assert.All(inputs, input => Assert.Equal(1, input.Opened)); // Planning opens no captured input again.
        Assert.Equal(3, captured.Bindings.Count); // Loan disposal keeps its parent usable.
    }

    [Theory, MemberData(nameof(WorkflowProfiles))]
    public void Type_conflicts_skip_blocked_subtrees_while_directory_merges_retain_owned_provenance(ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        var profile = WorkflowProfile(format, mode); var source = WorkflowSource(profile);
        using var captured = new ArchiveCapturedInputs(); var input = new Source(1); captured.Capture(1, input.Open, Ct);
        captured.CaptureDirectory(2, _ => Folder(2), Ct); captured.CaptureDirectory(3, _ => Folder(3), Ct); captured.Seal(Ct);
        var review = ArchiveWorkflowReview.Prepare(profile, source, captured, [], [],
            [new("treehouse", ArchiveMemberType.File, 1), captured.DirectoryAddition("Case", 2),
             new("Case/child", ArchiveMemberType.File, 1), captured.DirectoryAddition("tree", 3)], 0, Ct, allowSkippedTypeConflicts: true);
        Assert.Equal(3, review.MandatorySkippedNames); Assert.Equal(1, review.MergedDirectories);
        using var result = review.CreateSelectedPlan(true, false, Ct);
        Assert.Equal(3, result.SkippedNames); Assert.Empty(result.Inputs.Bindings); Assert.Equal(new[] { 3 }, result.CommittedInputs.ToArray());
        Assert.Single(result.Plan.Directories); ArchiveCapturedInputs.RequirePlanCost(result.Plan, result.Inputs.Bindings);
        Assert.Equal(source.Manifest.Members.Select(m => m.Name), result.Plan.Expected.Members.Select(m => m.Name));
        Assert.Equal(source.Manifest.Members[0].Metadata.ToArray(), result.Plan.Expected.Members[0].Metadata.ToArray());
        Assert.Equal(1, input.Opened);
    }

    [Fact]
    public void An_all_skipped_selection_has_no_changes_or_input_grants()
    {
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar); var source = WorkflowSource(profile);
        using var captured = new ArchiveCapturedInputs(); captured.Capture(1, new Source(1).Open, Ct); captured.Seal(Ct);
        var review = ArchiveWorkflowReview.Prepare(profile, source, captured, [], [], [new("Case", ArchiveMemberType.File, 1)], 0, Ct, allowSkippedTypeConflicts: true);
        using var result = review.CreateSelectedPlan(false, false, Ct);
        Assert.False(result.HasChanges); Assert.Empty(result.CommittedInputs); Assert.Empty(result.Inputs.Bindings); Assert.Empty(result.Plan.Inputs);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void Borrowed_views_retain_source_identity_and_cannot_outlive_either_owner(bool disposeParent)
    {
        using var captured = new ArchiveCapturedInputs(); var input = new Source(1); captured.Capture(1, input.Open, Ct); captured.Seal(Ct);
        using var borrowed = captured.BorrowSubset(new HashSet<int> { 1 }, new HashSet<int>(), Ct);
        var binding = borrowed.Bindings[1];
        using (var view = binding.File.OpenReadView(binding.Reviewed, Ct)) Assert.Equal(1, view.ReadByte());
        Assert.Throws<ArchiveWriteException>(() => borrowed.BorrowSubset(new HashSet<int> { 1 }, new HashSet<int>(), Ct));
        if (disposeParent) captured.Dispose(); else borrowed.Dispose();
        var error = Record.Exception(() => binding.File.OpenReadView(binding.Reviewed, Ct));
        Assert.True(error is ObjectDisposedException or OperationCanceledException);
        Assert.Equal(0, input.Active);
    }

    [Fact]
    public void Subset_capture_refuses_missing_ids_and_already_canceled_requests_without_reopening_inputs()
    {
        using var captured = new ArchiveCapturedInputs(); var input = new Source(1); captured.Capture(1, input.Open, Ct); captured.Seal(Ct);
        Assert.Throws<ArchiveWriteException>(() => captured.BorrowSubset(new HashSet<int> { 2 }, new HashSet<int>(), Ct));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => captured.BorrowSubset(new HashSet<int> { 1 }, new HashSet<int>(), stop.Token));
        Assert.Equal(1, input.Opened); Assert.Single(captured.Bindings);
    }
}
