using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class StorageEvidenceEnvelopeTests
{
    [Fact]
    public void Three_handle_cap_and_duplicate_returns_are_owned_by_the_same_envelope()
    {
        using var lane = new SemaphoreSlim(0, 1); using var owner = new StorageEvidenceEnvelope(lane);
        using var first = owner.Claim(); using var second = owner.Claim(); using var third = owner.Claim();
        Assert.Equal(3, owner.MaximumHandles); Assert.Throws<IOException>(() => owner.Claim());
        first.Dispose(); first.Dispose(); Assert.Equal(2, owner.Handles);
        using var replacement = owner.Claim(); Assert.Equal(3, owner.Handles);
    }

    [Fact]
    public void Retiring_does_not_return_the_lane_until_the_last_started_handle_retires()
    {
        using var lane = new SemaphoreSlim(0, 1); var owner = new StorageEvidenceEnvelope(lane); var live = owner.Claim();
        owner.Dispose(); owner.Dispose(); Assert.Equal(0, lane.CurrentCount);
        Assert.Throws<ObjectDisposedException>(() => owner.Claim()); live.Dispose(); live.Dispose(); Assert.Equal(1, lane.CurrentCount);
    }
}
