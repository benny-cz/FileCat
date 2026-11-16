using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class MetadataSnapshotRetirementTests
{
    private static readonly EntryData Entry = new("item", EntryKind.File, 1, 1);

    [Theory]
    [InlineData("changed")]
    [InlineData("forget")]
    [InlineData("folder")]
    [InlineData("many")]
    [InlineData("invalidate")]
    public void Effective_changes_and_invalidation_retire_claims_before_returning(string action)
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io, () => 0);
        string path = Path.Combine("owned", "item");
        object origin = new();
        metadata.Register(new MetadataField("claim", "Claim", MetadataCost.Cheap, _ => true,
            (_, _) => new MetadataValue(MetadataState.Denied, Detail: "No access."), v => v?.ToString() ?? ""));
        metadata.Compute("claim", path, Entry, TestContext.Current.CancellationToken);
        using var claim = metadata.TrackSnapshot("claim", origin);
        using var unrelated = metadata.TrackSnapshot("unrelated");
        bool attached = false;
        Assert.True(claim.TryPublish(() => attached = true));
        Assert.True(attached);
        switch (action)
        {
            case "changed":
                metadata.Register(metadata.Field("claim")! with { Produce = (_, _) => "new" });
                metadata.Compute("claim", path, Entry, TestContext.Current.CancellationToken);
                break;
            case "forget": metadata.Forget("claim", path); break;
            case "folder": metadata.ForgetFolder("claim", "owned"); break;
            case "many": metadata.Forget("claim", [path]); break;
            default: metadata.Invalidate(); break;
        }
        Assert.False(claim.IsCurrent);
        Assert.False(claim.TryPublish(() => Assert.Fail("A retired snapshot must not publish.")));
        Assert.True(claim.RetiredBy!.Value.Revision > claim.Revision);
        Assert.Equal(action != "invalidate", unrelated.IsCurrent);
    }

    [Fact]
    public void Own_productions_and_name_repaints_keep_a_claim_but_external_changes_retire_it()
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io, () => 0);
        object origin = new();
        metadata.Register(new MetadataField("claim", "Claim", MetadataCost.Cheap, _ => true,
            (_, _) => "value", value => (string)value!));
        using var claim = metadata.TrackSnapshot("claim", origin);
        metadata.Compute("claim", "first", Entry, TestContext.Current.CancellationToken, origin: origin);
        Assert.True(claim.IsCurrent);
        metadata.NotifyValuesChanged();
        Assert.True(claim.IsCurrent);
        // Even an external read of the same effective value must not invalidate an immutable snapshot.
        metadata.Compute("claim", "first", Entry, TestContext.Current.CancellationToken);
        Assert.True(claim.IsCurrent);
        metadata.Compute("claim", "second", Entry, TestContext.Current.CancellationToken);
        Assert.False(claim.IsCurrent);
        Assert.Null(claim.RetiredBy!.Value.Origin);
        Assert.EndsWith("|second", claim.RetiredBy.Value.Key);
    }

    [Fact]
    public void Claims_are_bounded_and_disposal_and_retirement_release_tracking_capacity()
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io, () => 0, maxSnapshots: 1);
        using var first = metadata.TrackSnapshot("claim");
        using var refused = metadata.TrackSnapshot("claim");
        Assert.True(first.IsCurrent);
        Assert.False(refused.IsCurrent);
        first.Dispose();
        using var next = metadata.TrackSnapshot("claim");
        Assert.True(next.IsCurrent);
        // No cache entry is necessary: the retained value could already have been evicted.
        metadata.Forget("claim", "evicted");
        Assert.False(next.IsCurrent);
        using var afterRetirement = metadata.TrackSnapshot("claim");
        Assert.True(afterRetirement.IsCurrent);
    }

    [Fact]
    public async Task Publication_cannot_cross_the_final_claim_check_and_memory_attachment()
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io);
        using var claim = metadata.TrackSnapshot("claim");
        using var attaching = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var invalidating = new ManualResetEventSlim();
        var publish = Task.Run(() => claim.TryPublish(() =>
        {
            attaching.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }), TestContext.Current.CancellationToken);
        Task? invalidate = null;
        try
        {
            Assert.True(attaching.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            invalidate = Task.Run(() => { invalidating.Set(); metadata.Invalidate(); }, TestContext.Current.CancellationToken);
            Assert.True(invalidating.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.False(invalidate.IsCompleted);
        }
        finally { release.Set(); }
        Assert.True(await publish);
        await invalidate!;
        Assert.False(claim.IsCurrent);
    }
}
