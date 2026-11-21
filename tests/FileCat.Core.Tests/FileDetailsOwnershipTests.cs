using FileCat.Core.Inspect;
using FileCat.Core.Records;
using FileCat.Core.Threading;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

public sealed class FileDetailsOwnershipTests
{
    private sealed class BlockedRecords : IFileRecords, IDisposable
    {
        public bool IsSupported => true;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public int Calls;
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy reader must never run.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref Calls);
            if (call == 1) { Entered.Set(); Release.Wait(TestContext.Current.CancellationToken); }
            return new(path, DateTimeOffset.UtcNow, Guid.NewGuid());
        }
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) => FileDetailsReport.Metadata(ReadSummary(path, ct));
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }

    [Fact]
    public async Task Cancellation_retains_the_native_permit_and_refreshes_coalesce_to_one_latest_request()
    {
        var ct = TestContext.Current.CancellationToken;
        string device = Guid.NewGuid().ToString();
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var records = new BlockedRecords();
        using var first = await DetailsAdmission.AcquireAsync(device, ct);
        using var second = await DetailsAdmission.AcquireAsync(device, ct);
        using var third = await DetailsAdmission.AcquireAsync(device, ct);
        using var reader = new FileDetailsReader(records, io, device, "captured");
        var running = reader.ReadAsync(FileDetailsTier.Summary, ct);
        Assert.True(records.Entered.Wait(TimeSpan.FromSeconds(10), ct));
        Task<FileDetailsObservation>? latest = null;
        var superseded = new List<Task<FileDetailsObservation>> { running };
        for (int i = 0; i < 50; i++)
        {
            if (latest is not null) superseded.Add(latest);
            latest = reader.ReadAsync(FileDetailsTier.Full, ct);
        }
        Assert.Equal(1, Volatile.Read(ref records.Calls));
        var fifth = DetailsAdmission.AcquireAsync(device, ct);
        Assert.False(fifth.IsCompleted); // cancellation has not released the real running owner
        records.Release.Set();
        using var admitted = await fifth.WaitAsync(TimeSpan.FromSeconds(10), ct);
        admitted.Dispose();
        var result = await latest!.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal("captured", result.Summary.Path);
        Assert.NotNull(result.Full);
        Assert.Equal(2, records.Calls);
        foreach (var old in superseded) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
    }

    [Theory]
    [InlineData(FileDetailsTier.Summary)]
    [InlineData(FileDetailsTier.Full)]
    public async Task Caller_cancellation_retires_publication_while_the_started_read_keeps_its_permit(FileDetailsTier tier)
    {
        var ct = TestContext.Current.CancellationToken;
        string device = Guid.NewGuid().ToString();
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var records = new BlockedRecords();
        using var first = await DetailsAdmission.AcquireAsync(device, ct);
        using var second = await DetailsAdmission.AcquireAsync(device, ct);
        using var third = await DetailsAdmission.AcquireAsync(device, ct);
        using var reader = new FileDetailsReader(records, io, device, "captured");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<IDisposable>? waiting = null;
        try
        {
            var result = reader.ReadAsync(tier, cancel.Token);
            Assert.True(records.Entered.Wait(TimeSpan.FromSeconds(10), ct));
            cancel.Cancel();
            Assert.True(result.IsCanceled); // The native delegate has not returned yet.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
            waiting = DetailsAdmission.AcquireAsync(device, ct);
            Assert.False(waiting.IsCompleted); // Its real owner still occupies the fourth slot.
        }
        finally
        {
            records.Release.Set();
            if (waiting is not null) (await waiting.WaitAsync(TimeSpan.FromSeconds(10), ct)).Dispose();
        }
        Assert.Equal(1, records.Calls);
    }

    [Fact]
    public async Task Device_and_process_admission_are_reserved_together_and_leases_are_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var leases = new List<IDisposable>();
        string device = Guid.NewGuid().ToString();
        try
        {
            for (int i = 0; i < 4; i++) leases.Add(await DetailsAdmission.AcquireAsync(device, ct));
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var blocked = DetailsAdmission.AcquireAsync(device, cancel.Token);
            Assert.False(blocked.IsCompleted);
            for (int i = 0; i < 4; i++) leases.Add(await DetailsAdmission.AcquireAsync(device + i, ct));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
            using var globalCancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var ninth = DetailsAdmission.AcquireAsync("ninth", globalCancel.Token);
            Assert.False(ninth.IsCompleted);
            leases[0].Dispose(); leases[0].Dispose();
            using var next = await ninth.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    [Fact]
    public async Task Device_case_variants_share_the_same_four_observation_permits()
    {
        var ct = TestContext.Current.CancellationToken;
        string device = "Volume-" + Guid.NewGuid().ToString("N");
        using var first = await DetailsAdmission.AcquireAsync(device, ct);
        using var second = await DetailsAdmission.AcquireAsync(device, ct);
        using var third = await DetailsAdmission.AcquireAsync(device, ct);
        using var fourth = await DetailsAdmission.AcquireAsync(device, ct);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var variant = DetailsAdmission.AcquireAsync(device.ToLowerInvariant(), cancel.Token);
        try
        {
            Assert.False(variant.IsCompleted);
            first.Dispose();
            using var fifth = await variant.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        finally
        {
            cancel.Cancel();
            if (variant.IsCompletedSuccessfully) (await variant).Dispose();
        }
    }

    [Fact]
    public async Task Nested_observation_accepts_the_admitted_devices_case_variant()
    {
        var ct = TestContext.Current.CancellationToken;
        string device = "Volume-" + Guid.NewGuid().ToString("N");
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        bool nested = await FileObservationRequests.RunOwned(io, device, IoPriority.Interactive, token =>
        {
            Assert.True(FileObservationRequests.IsAdmittedFor(device.ToLowerInvariant()));
            return ReadNestedCaseVariant(io, device, token);
        }, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(nested);
    }

    private static bool ReadNestedCaseVariant(DeviceIoScheduler io, string device, CancellationToken ct)
    {
        // An admitted worker must run the nested read inline, without queueing behind itself.
        var nested = FileObservationRequests.RunOwned(io, device.ToLowerInvariant(), IoPriority.Interactive,
            _ => FileObservationRequests.IsAdmittedFor(device), ct);
        Assert.True(nested.IsCompleted);
        return nested.GetAwaiter().GetResult();
    }

    [Fact]
    public async Task A_worker_start_failure_releases_admission_and_the_failed_read_never_reaches_a_later_worker()
    {
        var ct = TestContext.Current.CancellationToken;
        string device = Guid.NewGuid().ToString();
        var failure = new InvalidOperationException("Fixture worker failed to start.");
        int starts = 0, rejectedReads = 0;
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1)
        {
            StartWorkerThread = thread =>
            {
                if (Interlocked.Increment(ref starts) == 1) throw failure;
                thread.Start();
            }
        };
        var rejected = FileObservationRequests.RunOwned(io, device, IoPriority.Interactive,
            _ => Interlocked.Increment(ref rejectedReads), ct);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => rejected));
        using (await DetailsAdmission.AcquireAsync(device, ct))
        using (await DetailsAdmission.AcquireAsync(device, ct))
        using (await DetailsAdmission.AcquireAsync(device, ct))
        using (await DetailsAdmission.AcquireAsync(device, ct).WaitAsync(TimeSpan.FromSeconds(10), ct)) { }

        var accepted = FileObservationRequests.RunOwned(io, device, IoPriority.Interactive,
            _ => FileObservationRequests.IsAdmittedFor(device), ct);
        Assert.True(await accepted.WaitAsync(TimeSpan.FromSeconds(10), ct));
        Assert.Equal(2, starts);
        Assert.Equal(0, Volatile.Read(ref rejectedReads));
    }

    [Fact]
    public async Task Repeated_queued_cancellation_and_scheduler_shutdown_return_every_observation_permit()
    {
        var ct = TestContext.Current.CancellationToken;
        string device = Guid.NewGuid().ToString();
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = io.RunOwned(device, IoPriority.Interactive, _ =>
        {
            entered.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(15), ct));
            return true;
        }, ct);
        int reads = 0;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            for (int i = 0; i < 12; i++)
            {
                using var canceled = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var queued = FileObservationRequests.RunOwned(io, device, IoPriority.Interactive,
                    _ => Interlocked.Increment(ref reads), canceled.Token);
                Assert.False(queued.IsCompleted);
                canceled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(10), ct));
            }
            var stopped = FileObservationRequests.RunOwned(io, device, IoPriority.Interactive,
                _ => Interlocked.Increment(ref reads), ct);
            Assert.False(stopped.IsCompleted);
            io.Dispose();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopped.WaitAsync(TimeSpan.FromSeconds(10), ct));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileObservationRequests.RunOwned(io,
                device, IoPriority.Interactive, _ => Interlocked.Increment(ref reads), ct).WaitAsync(TimeSpan.FromSeconds(10), ct));
            using (await DetailsAdmission.AcquireAsync(device, ct))
            using (await DetailsAdmission.AcquireAsync(device, ct))
            using (await DetailsAdmission.AcquireAsync(device, ct))
            using (await DetailsAdmission.AcquireAsync(device, ct).WaitAsync(TimeSpan.FromSeconds(10), ct)) { }
        }
        finally { release.Set(); }
        Assert.True(await held.WaitAsync(TimeSpan.FromSeconds(10), ct));
        Assert.Equal(0, Volatile.Read(ref reads));
    }

    [Fact]
    public async Task Scheduler_shutdown_retires_a_running_observation_only_after_the_handle_closes()
    {
        var ct = TestContext.Current.CancellationToken;
        string device = Guid.NewGuid().ToString();
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var release = new ManualResetEventSlim();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var entered = new TaskCompletionSource<SafeFileHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = await DetailsAdmission.AcquireAsync(device, ct);
        using var second = await DetailsAdmission.AcquireAsync(device, ct);
        using var third = await DetailsAdmission.AcquireAsync(device, ct);
        string directory = Directory.CreateTempSubdirectory("filecat-observation-stop-").FullName;
        string path = Path.Join(directory, "held.txt");
        File.WriteAllText(path, "held observation");
        var running = FileObservationRequests.RunOwned(io, device, IoPriority.Interactive, token =>
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            entered.SetResult(handle);
            Assert.True(release.Wait(TimeSpan.FromSeconds(15), ct));
            Assert.True(token.IsCancellationRequested);
            return "stale";
        }, ct);
        Task<IDisposable>? waiting = null;
        try
        {
            var handle = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            io.Dispose();
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            waiting = DetailsAdmission.AcquireAsync(device, timeout.Token);
            Assert.False(running.IsCompleted);
            Assert.False(handle.IsClosed);
            Assert.False(waiting.IsCompleted);

            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10), ct));
            await waiting;
            Assert.True(handle.IsClosed);
        }
        finally
        {
            timeout.Cancel();
            release.Set();
            if (waiting is not null)
            {
                try { (await waiting).Dispose(); }
                catch (OperationCanceledException) { }
            }
            await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(15), CancellationToken.None));
            Directory.Delete(directory, recursive: true);
        }
    }
}
