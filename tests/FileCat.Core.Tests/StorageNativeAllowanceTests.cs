using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class StorageNativeAllowanceTests
{
    [Fact]
    public void Typed_blocker_remains_until_the_last_charged_native_handle_actually_retires()
    {
        var job = new Job(new JobRequest { Kind = JobKind.StorageTransform }, "Storage owner", "fixture", [], []);
        using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken, job);
        using var handle = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        Assert.Same(job, StorageReviewResources.NativeBlocker);
        native.Dispose(); Assert.Same(job, StorageReviewResources.NativeBlocker);
        handle.Dispose(); Assert.Null(StorageReviewResources.NativeBlocker);
    }

    [Fact]
    public void Traversal_and_scratch_have_separate_fixed_bounds_and_duplicate_returns_do_not_expand_them()
    {
        using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
        var allowance = StorageNativeAllowance.RequireCurrent();
        var traversal = new List<IDisposable>(); var scratch = new List<IDisposable>();
        try
        {
            for (int i = 0; i < 128; i++) traversal.Add(allowance.ClaimTraversal());
            for (int i = 0; i < 60; i++) scratch.Add(allowance.ClaimScratch());
            Assert.Throws<InvalidOperationException>(() => allowance.ClaimTraversal());
            Assert.Throws<InvalidOperationException>(() => allowance.ClaimScratch());
            traversal[0].Dispose(); traversal[0].Dispose();
            using var returned = allowance.ClaimTraversal();
            Assert.Throws<InvalidOperationException>(() => allowance.ClaimTraversal());
        }
        finally { foreach (var slot in traversal) slot.Dispose(); foreach (var slot in scratch) slot.Dispose(); }
    }

    [Fact]
    public void Retiring_owner_does_not_release_exclusive_native_capacity_before_its_last_handle_returns()
    {
        var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
        var allowance = StorageNativeAllowance.RequireCurrent();
        var handle = allowance.ClaimScratch();
        try
        {
            native.Dispose(); Assert.Null(StorageNativeAllowance.Current);
            Assert.Throws<InvalidOperationException>(() => allowance.ClaimScratch());
            using var stopped = new CancellationTokenSource(); stopped.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => StorageReviewResources.EnterNative(stopped.Token));
        }
        finally { handle.Dispose(); native.Dispose(); }
        using var next = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
        Assert.NotSame(allowance, StorageNativeAllowance.RequireCurrent());
    }

    [Fact]
    public void A_native_owner_must_borrow_its_existing_allowance_and_cannot_wait_on_itself()
    {
        using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => StorageReviewResources.EnterNative(TestContext.Current.CancellationToken));
    }
}
