using System.Diagnostics;

namespace FileCat.Core.Archives;

/// <summary>One queue-owned native worker lifetime, including final diagnostics/accounting and actual reaping.</summary>
internal static class ArchiveWorkerSession
{
    internal static async Task<T> RunAsync<T>(Func<Guid, TimeSpan, IArchiveWorkerProcess> launch, ArchiveWorkerQueue queue,
        TimeSpan cpu, TimeSpan idle, TimeSpan wall, Func<Guid, IArchiveWorkerProcess, Action, T> conversation, CancellationToken ct)
    {
        using var reservation = await queue.EnterAsync(ct).ConfigureAwait(false); Guid nonce = Guid.NewGuid();
        var process = await Task.Run(() => launch(nonce, cpu), ct).ConfigureAwait(false);
        using var stop = new CancellationTokenSource(); using var cancellation = ct.Register(process.Kill);
        long began = Stopwatch.GetTimestamp(), progress = began; ArchiveWriteException? monitorFailure = null;
        Task watch = Watch(); bool disposed = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await Task.Run(() => conversation(nonce, process, () => Volatile.Write(ref progress, Stopwatch.GetTimestamp())), CancellationToken.None).ConfigureAwait(false);
            while (!process.HasExited)
            {
                ct.ThrowIfCancellationRequested(); if (Volatile.Read(ref monitorFailure) is { } failure) throw failure;
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested(); if (Volatile.Read(ref monitorFailure) is { } terminal) throw terminal;
            await Finish().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); if (Volatile.Read(ref monitorFailure) is { } final) throw final;
            if (process.TotalProcessorTime > cpu) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The archive worker exceeded its CPU allowance.");
            uint exitCode = process.ExitCode; bool diagnosticsExceeded = process.ErrorLimitExceeded;
            if (exitCode != 0 || diagnosticsExceeded)
            {
                var error = Unavailable("terminal"); error.Data["archiveWorkerExitCode"] = exitCode;
                error.Data["archiveWorkerDiagnosticsExceeded"] = diagnosticsExceeded; throw error;
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested(); if (Volatile.Read(ref monitorFailure) is { } failure) throw failure;
            if (ex is ArchiveWriteException) throw; throw Unavailable("conversation", ex);
        }
        finally { if (!disposed) await Finish().ConfigureAwait(false); }

        async Task Finish()
        {
            stop.Cancel();
            try { try { await watch.ConfigureAwait(false); } catch (OperationCanceledException) { } }
            finally { await process.DisposeAsync().ConfigureAwait(false); disposed = true; }
        }
        async Task Watch()
        {
            while (!stop.IsCancellationRequested)
            {
                ArchiveWriteException? failure = null;
                try
                {
                    if (process.HasExited) return;
                    if (Stopwatch.GetElapsedTime(began) > wall || Stopwatch.GetElapsedTime(Volatile.Read(ref progress)) > idle ||
                        process.TotalProcessorTime > cpu || process.ErrorLimitExceeded)
                        failure = new(ArchiveWriteReason.BudgetExceeded, "The archive worker exceeded its time, CPU or output allowance.");
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { failure = Unavailable("monitor", ex); }
                if (failure is not null) { Interlocked.CompareExchange(ref monitorFailure, failure, null); process.Kill(); return; }
                await Task.Delay(100, stop.Token).ConfigureAwait(false);
            }
        }
    }
    private static ArchiveWriteException Unavailable(string phase, Exception? cause = null)
    {
        const string message = "The archive worker stopped or its boundary could not be monitored. The archive remains unchanged.";
        var error = cause is null ? new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, message) : new(ArchiveWriteReason.WorkerUnavailable, message, cause);
        error.Data["archiveWorkerPhase"] = phase; return error;
    }
}
