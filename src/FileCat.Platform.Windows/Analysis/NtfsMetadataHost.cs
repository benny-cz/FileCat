using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Analysis;

/// <summary>The installed helper owns this one read session. Its broker result contains only counts and controlled status.</summary>
internal static class NtfsMetadataHost
{
    internal static async Task<ElevatedStepResult> RunAsync(NtfsMetadataVolume volume, string nonce, NtfsMetadataPeer requester,
        Func<bool> stopRequested)
    {
        using var server = NtfsMetadataPipe.CreateServer(nonce, requester.LogonSid);
        using var channel = await NtfsMetadataPipe.AcceptAsync(server, nonce, requester, CancellationToken.None).ConfigureAwait(false);
        using var stop = new CancellationTokenSource();
        CancellationToken token = stop.Token;
        _ = WatchStopAsync(stopRequested, stop, token);
        var hello = await channel.ReceiveAsync(token).ConfigureAwait(false);
        if (hello.Kind != NtfsMetadataMessage.Hello) throw NtfsMetadataProtocol.InvalidFrame();
        NtfsMetadataReader? reader = null;
        Task<NtfsMetadataReader>? opening = null;
        Task<NtfsMetadataPage?>? pending = null;
        Task<NtfsMetadataFrame>? control = null;
        int delivered = 0;
        try
        {
            // Native volume I/O owns its handle on a background worker. The broker's main thread can
            // exit after a disconnect or deadline even if the driver has stopped responding.
            opening = Task.Run(() => NtfsMetadataReader.Open(volume, token), CancellationToken.None);
            while (!opening.IsCompleted)
            {
                Check();
                await Task.WhenAny(opening, Task.Delay(200, token)).ConfigureAwait(false);
            }
            reader = await opening.ConfigureAwait(false);
            await Send(NtfsMetadataMessage.Accepted, new { RootId = volume.RootFileId }).ConfigureAwait(false);
            control = channel.ReceiveAsync(token).AsTask();
            var state = ScanState.Active;
            ulong heartbeat = NtfsMetadataChannel.MonotonicMilliseconds();
            while (true)
            {
                Check();
                if (control.IsCompleted || channel.CancelPending)
                {
                    var request = await control.ConfigureAwait(false);
                    if (request.Kind == NtfsMetadataMessage.Cancel)
                        return await Complete(NtfsMetadataCompletion.Canceled).ConfigureAwait(false) ?? throw NtfsMetadataProtocol.InvalidFrame();
                    state = request.Kind == NtfsMetadataMessage.Pause ? ScanState.Pausing : ScanState.Active;
                    control = channel.ReceiveAsync(token).AsTask();
                    heartbeat = NtfsMetadataChannel.MonotonicMilliseconds();
                }
                if (state == ScanState.Pausing && (pending is null || pending.IsCompleted))
                {
                    if (!await Send(NtfsMetadataMessage.Paused, new { }).ConfigureAwait(false)) continue;
                    state = ScanState.Paused;
                }
                if (state == ScanState.Active)
                {
                    pending ??= Task.Run(() => reader.ReadNext(token), CancellationToken.None);
                    if (pending.IsCompleted)
                    {
                        var page = await pending.ConfigureAwait(false);
                        pending = null;
                        if (page is null)
                        {
                            if (await Complete(NtfsMetadataCompletion.Finished).ConfigureAwait(false) is { } finished) return finished;
                            continue;
                        }
                        int accepted = Math.Min(page.Records.Length, AnalysisLimits.Observations - delivered);
                        if (accepted > 0)
                        {
                            if (!await Send(NtfsMetadataMessage.ObservationBatch, new { Records = page.Records.Take(accepted) }).ConfigureAwait(false)) continue;
                            delivered += accepted;
                        }
                        if (delivered == AnalysisLimits.Observations)
                        {
                            if (!await Issue(NtfsMetadataMessage.Issue, NtfsMetadataIssue.LimitReached).ConfigureAwait(false)) continue;
                            if (await Complete(NtfsMetadataCompletion.Partial).ConfigureAwait(false) is { } partial) return partial;
                            continue;
                        }
                    }
                }
                ulong now = NtfsMetadataChannel.MonotonicMilliseconds();
                if (state != ScanState.Paused && now - heartbeat >= 600)
                {
                    if (!await Send(NtfsMetadataMessage.Progress, new { Seen = delivered }).ConfigureAwait(false)) continue;
                    heartbeat = now;
                }
                if (state == ScanState.Active && pending is null) continue;
                var tick = Task.Delay(150, token);
                if (pending is not null && !pending.IsCompleted && state != ScanState.Paused)
                    await Task.WhenAny(pending, control, tick).ConfigureAwait(false);
                else
                    await Task.WhenAny(control, tick).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or TimeoutException)
        {
            var reason = error switch
            {
                AnalysisSourceChangedException => NtfsMetadataIssue.ChangedVolume,
                InvalidDataException => NtfsMetadataIssue.InvalidRecords,
                NotSupportedException => NtfsMetadataIssue.UnsupportedVolume,
                TimeoutException => NtfsMetadataIssue.TimedOut,
                _ => NtfsMetadataIssue.Unavailable,
            };
            if (!channel.IsClosed)
                try { await Issue(NtfsMetadataMessage.Refused, reason).ConfigureAwait(false); }
                catch (Exception failure) when (failure is IOException or InvalidOperationException or TimeoutException) { }
            return new(0, delivered == 0 ? ElevatedOutcome.Failed : ElevatedOutcome.PartiallyApplied,
                "The NTFS metadata session ended before enumeration finished.", delivered);
        }
        catch (OperationCanceledException)
        {
            return new(0, delivered == 0 ? ElevatedOutcome.NotRun : ElevatedOutcome.PartiallyApplied,
                "The NTFS metadata session was stopped.", delivered);
        }
        finally
        {
            stop.Cancel();
            channel.Dispose();
            if (reader is null && opening is not null)
                _ = opening.ContinueWith(static task =>
                {
                    if (task.IsCompletedSuccessfully) task.Result.Dispose();
                    else _ = task.Exception;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            else if (pending is { IsCompleted: false } && reader is not null)
            {
                var owner = reader;
                _ = pending.ContinueWith(task => { _ = task.Exception; owner.Dispose(); }, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else { if (pending is not null) _ = pending.Exception; reader?.Dispose(); }
            if (control is not null)
                _ = control.ContinueWith(static task => _ = task.Exception,
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (channel.IsClosed) throw new TimeoutException();
        }
        ValueTask<bool> Send<T>(NtfsMetadataMessage kind, T body) =>
            channel.TrySendAsync(new(kind, JsonSerializer.SerializeToElement(body)), token);
        ValueTask<bool> Issue(NtfsMetadataMessage kind, NtfsMetadataIssue reason) => Send(kind, new { Code = reason.ToString() });
        async Task<ElevatedStepResult?> Complete(NtfsMetadataCompletion outcome)
        {
            if (!await Send(NtfsMetadataMessage.Complete, new { Seen = delivered, Outcome = outcome.ToString() }).ConfigureAwait(false)) return null;
            var settled = outcome switch
            {
                NtfsMetadataCompletion.Finished => ElevatedOutcome.Committed,
                NtfsMetadataCompletion.Canceled when delivered == 0 => ElevatedOutcome.NotRun,
                _ => ElevatedOutcome.PartiallyApplied
            };
            return new(0, settled,
                outcome == NtfsMetadataCompletion.Finished ? "NTFS metadata enumeration finished." : "The NTFS metadata session ended with partial observations.",
                delivered);
        }
    }

    private static async Task WatchStopAsync(Func<bool> stopRequested, CancellationTokenSource stop, CancellationToken token)
    {
        try
        {
            while (!await Task.Run(stopRequested, token).ConfigureAwait(false))
                await Task.Delay(200, token).ConfigureAwait(false);
            stop.Cancel();
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { stop.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private enum ScanState { Active, Pausing, Paused }
}
