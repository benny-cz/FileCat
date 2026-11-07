using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveCapturedInputsTests
{
    [Fact]
    public void RAR_capture_admission_retains_owner_cost_and_refuses_equal_content_from_another_owner()
    {
        var profile = ArchiveWriteProfile.ForRar5();
        using var first = new ArchiveCapturedInputs(); using var second = new ArchiveCapturedInputs();
        first.Capture(1, new Source(1).Open, Ct); second.Capture(1, new Source(2).Open, Ct); first.Seal(Ct); second.Seal(Ct);
        var plan = first.CreatePlan(profile, null, [], [new("input", ArchiveMemberType.File, 1)], Ct);
        Assert.IsType<ArchiveRar5TransformPlan>(plan); Assert.Equal(first.Cost, plan.CaptureCost); ArchiveCapturedInputs.RequirePlanCost(plan, first.Bindings);
        Assert.Throws<ArchiveWriteException>(() => ArchiveCapturedInputs.RequirePlanCost(plan, second.Bindings));
        var dropped = ArchiveRar5TransformPlan.Create(profile, null, first.Inputs, [], [new("input", ArchiveMemberType.File, 1)], Ct);
        Assert.Throws<ArchiveWriteException>(() => ArchiveCapturedInputs.RequirePlanCost(dropped, first.Bindings));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void RAR_empty_capture_carries_its_scope_charge_and_plan_admission_counts_every_capture_budget(int damage)
    {
        var profile = ArchiveWriteProfile.ForRar5(); using var captured = new ArchiveCapturedInputs(); captured.Seal(Ct);
        var plan = captured.CreatePlan(profile, null, [], [], Ct); Assert.True(plan.CaptureCost.HeapBytes >= 2048);
        ArchiveCapturedInputs.RequirePlanCost(plan, captured.Bindings);
        ArchiveCapturedInputCost cost = damage switch
        {
            0 => new(0, ArchiveWritePolicy.MaxRetainedHeapBytes + 1, 0),
            1 => new(ArchiveWritePolicy.MaxMetadataBytes + 1, 0, 0),
            2 => new(0, 0, ArchiveWritePolicy.MaxRetainedNameChars + 1),
            _ => new(0, ArchiveWritePolicy.MaxRetainedHeapBytes - 1, 0),
        };
        ArchiveTransformInput[] inputs = damage == 3 ? [new(1, 0, "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", DateTimeOffset.UnixEpoch)] : [];
        ArchiveTransformAddition[] additions = damage == 3 ? [new("input", ArchiveMemberType.File, 1)] : [];
        var error = Assert.Throws<ArchiveWriteException>(() => ArchiveRar5TransformPlan.CreateCaptured(profile, null, inputs, [], additions, cost, Ct));
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, error.Reason);
    }
}
