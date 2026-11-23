using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

/// <summary>
/// T-LIMIT: the storage resource lanes are independent and bounded - 256 review pins, one exclusive native owner with
/// 128 traversal and 60 scratch handles, one history cursor and one three-handle evidence envelope. A paused native
/// owner at full capacity never makes a review, a history reader or a live report wait on it, and nothing nests.
/// </summary>
[Collection("Storage review process resources")]
public sealed class StorageResourceLaneTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_full_native_owner_a_full_pin_review_a_history_cursor_and_a_live_report_use_separate_bounded_lanes()
    {
        using var full = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception? ownerFault = null;
        // The "paused worker": an exclusive native owner holding every traversal and scratch handle.
        var owner = Task.Factory.StartNew(() =>
        {
            try
            {
                using var native = StorageReviewResources.EnterNative(Ct);
                var allowance = StorageNativeAllowance.RequireCurrent();
                var handles = new List<IDisposable>();
                try
                {
                    for (int i = 0; i < 128; i++) handles.Add(allowance.ClaimTraversal());
                    for (int i = 0; i < 60; i++) handles.Add(allowance.ClaimScratch());
                    Assert.Throws<InvalidOperationException>(() => allowance.ClaimTraversal());
                    Assert.Throws<InvalidOperationException>(() => allowance.ClaimScratch());
                    Assert.Equal(StorageReviewResources.NativeSlots, handles.Count);
                    full.Set();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(30)), "The test did not release the native owner.");
                }
                finally { foreach (var handle in handles) handle.Dispose(); }
            }
            catch (Exception ex) { ownerFault = ex; full.Set(); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var pins = new List<IDisposable>();
        try
        {
            Assert.True(full.Wait(TimeSpan.FromSeconds(10), Ct), "The native owner did not reach full capacity.");
            Assert.Null(ownerFault);

            // Reviews pin up to 64 roots each until the 256-pin lane refuses (other storage tests may hold some pins):
            // the pin lane is not the native lane, never waits on it, and refuses instead of exceeding its bound.
            var started = DateTime.UtcNow;
            int reserved = 0;
            while (true)
            {
                try { pins.Add(StorageReviewResources.ReservePins(64)); reserved += 64; }
                catch (InvalidOperationException) { break; }
                Assert.True(reserved <= StorageReviewResources.PinLimit, "The pin lane exceeded its bound.");
            }
            Assert.InRange(reserved, 0, StorageReviewResources.PinLimit);
            Assert.Throws<InvalidOperationException>(() => StorageReviewResources.ReservePins(65));

            // History paging and a live report each take their own lane while the native owner and all pins are held.
            using (var cursor = StorageEvidenceLanes.EnterCursor(Ct))
            using (var envelope = StorageEvidenceLanes.EnterEnvelope(Ct))
            {
                var reads = new[] { envelope.Claim(), envelope.Claim(), envelope.Claim() };
                Assert.Throws<IOException>(() => envelope.Claim());
                Assert.Equal(3, envelope.MaximumHandles);
                foreach (var read in reads) read.Dispose();
            }
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "No lane waited on the paused native owner.");

            // A second native entrant waits for the exclusive owner instead of sharing its capacity; cancellation ends that wait.
            using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(Ct))
            {
                waiting.CancelAfter(TimeSpan.FromMilliseconds(300));
                var second = Task.Factory.StartNew(() => StorageReviewResources.EnterNative(waiting.Token),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            }
        }
        finally
        {
            release.Set();
            // An owner still running after its release would leave the EnterNative below waiting; fail instead.
            try { await owner.WaitAsync(TimeSpan.FromSeconds(30), Ct); }
            finally { foreach (var pin in pins) pin.Dispose(); }
        }
        Assert.Null(ownerFault);
        // Every lane is whole again afterwards.
        using (var native = StorageReviewResources.EnterNative(Ct))
        {
            var allowance = StorageNativeAllowance.RequireCurrent();
            using var traversal = allowance.ClaimTraversal();
            using var scratch = allowance.ClaimScratch();
        }
        using var again = StorageReviewResources.ReservePins(64);
        using var cursorAgain = StorageEvidenceLanes.EnterCursor(Ct);
    }
}
