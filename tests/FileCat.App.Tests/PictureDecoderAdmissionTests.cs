using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class PictureDecoderAdmissionTests
{
    [Fact]
    public async Task A_full_queue_refuses_additional_demand_and_cancellation_releases_waiting_capacity()
    {
        var admission = new PictureDecoderAdmission();
        var active = new List<IDisposable>();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        try
        {
            for (int i = 0; i < 4; i++) active.Add(await admission.EnterAsync(cancel.Token));
            var waiting = Enumerable.Range(0, 32).Select(_ => admission.EnterAsync(cancel.Token)).ToArray();
            Assert.All(waiting, task => Assert.False(task.IsCompleted));
            Assert.Equal(36, admission.Requests);
            Assert.Contains("too many pictures", (await Assert.ThrowsAsync<IOException>(() => admission.EnterAsync(cancel.Token))).Message, StringComparison.Ordinal);
            Assert.Equal(36, admission.Requests);
            cancel.Cancel();
            foreach (var task in waiting) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Equal(4, admission.Requests);
        }
        finally { foreach (var lease in active) lease.Dispose(); }
        Assert.Equal(0, admission.Requests);
        using var fresh = await admission.EnterAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, admission.Requests);
    }

    [Fact]
    public async Task Cancellation_racing_a_release_does_not_lose_or_duplicate_a_worker_slot()
    {
        var admission = new PictureDecoderAdmission(workers: 1, waiting: 1);
        for (int i = 0; i < 64; i++)
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var active = await admission.EnterAsync(cancel.Token);
            var waiting = admission.EnterAsync(cancel.Token);
            Assert.False(waiting.IsCompleted);
            await Task.WhenAll(Task.Run(cancel.Cancel, TestContext.Current.CancellationToken),
                Task.Run(active.Dispose, TestContext.Current.CancellationToken));
            try { (await waiting).Dispose(); }
            catch (OperationCanceledException) { }
            active.Dispose(); // releasing a lease twice cannot admit an extra worker
            Assert.Equal(0, admission.Requests);
            using var fresh = await admission.EnterAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, admission.Requests);
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => admission.EnterAsync(canceled.Token));
        Assert.Equal(0, admission.Requests);
    }
}
