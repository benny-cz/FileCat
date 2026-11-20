using System.ComponentModel;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveWorkerSessionDiagnosticsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Controlled_child_CPU_observation_remains_valid_at_the_process_exit_boundary()
    {
        await using var child = ArchiveAssessmentClientTests.Child.Start(Guid.NewGuid(), "exit0");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!child.HasExited) await Task.Delay(10, timeout.Token);
        Assert.Equal(0u, child.ExitCode);
        Assert.True(child.TotalProcessorTime >= TimeSpan.Zero);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_native_monitor_observation_retains_its_cause_and_releases_only_after_reaping(bool exitObservation)
    {
        var cause = new Win32Exception(5, "Owned diagnostic native observation."); var queue = new ArchiveWorkerQueue();
        var process = new Controlled { ObservationFailure = cause, FailExitObservation = exitObservation };
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => Run(process, queue, () => 7));
        Assert.Equal(ArchiveWriteReason.WorkerUnavailable, error.Reason); Assert.Same(cause, error.InnerException);
        Assert.Equal("monitor", error.Data["archiveWorkerPhase"]); Assert.True(process.Killed); Assert.True(process.Disposed);
        var next = new Controlled { Exited = true }; Assert.Equal(7, await Run(next, queue, () => 7)); Assert.True(next.Disposed);
    }
    [Theory]
    [InlineData(7u, false)]
    [InlineData(0u, true)]
    public async Task Terminal_rejection_keeps_exact_exit_and_diagnostic_facts(uint exit, bool diagnostics)
    {
        var process = new Controlled { Exited = true, ExitCode = exit, ErrorLimitExceeded = diagnostics };
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => Run(process, new(), () => 7));
        Assert.Equal("terminal", error.Data["archiveWorkerPhase"]); Assert.Equal(exit, error.Data["archiveWorkerExitCode"]);
        Assert.Equal(diagnostics, error.Data["archiveWorkerDiagnosticsExceeded"]); Assert.True(process.Disposed);
    }
    [Fact]
    public async Task Conversation_IO_failure_is_distinct_from_a_native_monitor_or_terminal_failure()
    {
        var cause = new IOException("Owned conversation failure."); var process = new Controlled();
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => Run(process, new(), () => throw cause));
        Assert.Same(cause, error.InnerException); Assert.Equal("conversation", error.Data["archiveWorkerPhase"]); Assert.True(process.Disposed);
    }
    private static Task<int> Run(Controlled process, ArchiveWorkerQueue queue, Func<int> conversation) => ArchiveWorkerSession.RunAsync(
        (_, _) => process, queue, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), (_, _, _) => conversation(), Ct);

    [Fact]
    public async Task Cancellation_exit_observation_failure_still_reaps_and_keeps_the_queue_until_disposal_finishes()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var process = new CancellationFault { FailObservation = true }; var queue = new ArchiveWorkerQueue();
        Task run = ArchiveWorkerSession.RunAsync((_, _) => process, queue, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1), (_, _, _) => { cancellation.Cancel(); return 7; }, cancellation.Token);
        await process.Disposing.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var pending = queue.EnterAsync(Ct).AsTask(); Assert.False(pending.IsCompleted);
        process.Reap.SetResult();
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => run);
        Assert.Equal("cancellation", error.Data["archiveWorkerPhase"]); Assert.Same(process.Cause, error.InnerException);
        Assert.True(process.Disposed); using var next = await pending;
    }

    [Fact]
    public async Task Native_signal_and_stop_failures_do_not_throw_from_the_UI_cancellation_callback()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var process = new CancellationFault { FailSignalAndStop = true }; process.Reap.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveWorkerSession.RunAsync((_, _) => process, new(),
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), (_, _, _) =>
            { Assert.Null(Record.Exception(() => cancellation.Cancel())); return 7; }, cancellation.Token));
        Assert.True(process.Disposed);
    }

    private sealed class CancellationFault : IArchiveWorkerProcess
    {
        internal readonly Win32Exception Cause = new(5, "Owned cancellation observation failure.");
        internal readonly TaskCompletionSource Disposing = new(TaskCreationOptions.RunContinuationsAsynchronously), Reap = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool FailObservation, FailSignalAndStop, Disposed;
        private bool signaled, exited;
        public Stream Input => Stream.Null; public Stream Output => Stream.Null;
        public bool HasExited => Disposed || (signaled && FailObservation ? throw Cause : exited);
        public uint ExitCode => 0; public TimeSpan TotalProcessorTime => TimeSpan.Zero; public bool ErrorLimitExceeded => false;
        public void RequestCancellation() { signaled = true; if (FailSignalAndStop) throw Cause; }
        public void Kill() { exited = true; if (FailSignalAndStop) throw new Win32Exception(6, "Owned cancellation stop failure."); }
        public async ValueTask DisposeAsync() { Disposing.SetResult(); await Reap.Task; Disposed = true; }
    }
    private sealed class Controlled : IArchiveWorkerProcess
    {
        internal bool Exited, Killed, Disposed;
        internal Exception? ObservationFailure;
        internal bool FailExitObservation;
        public Stream Input => Stream.Null; public Stream Output => Stream.Null;
        public bool HasExited => !Killed && ObservationFailure is not null && FailExitObservation ? throw ObservationFailure : Exited;
        public uint ExitCode { get; init; }
        public TimeSpan TotalProcessorTime => ObservationFailure is not null && !FailExitObservation ? throw ObservationFailure : TimeSpan.Zero;
        public bool ErrorLimitExceeded { get; init; }
        public void Kill() { Killed = true; Exited = true; }
        public ValueTask DisposeAsync() { Kill(); Disposed = true; return ValueTask.CompletedTask; }
    }
}
