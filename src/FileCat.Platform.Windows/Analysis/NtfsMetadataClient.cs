using System.Collections.Immutable;
using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Analysis;

/// <summary>An ordinary installed requester owns one approved broker, exchange and authenticated stream.</summary>
internal sealed class NtfsMetadataClient : IAsyncDisposable
{
    private readonly ElevatedProcess _helper;
    private readonly ElevationExchange _exchange;
    private readonly NtfsMetadataChannel _channel;
    private readonly NtfsMetadataPeer _peer;
    private int _disposed;

    private NtfsMetadataClient(NtfsMetadataVolume volume, ElevatedProcess helper, ElevationExchange exchange,
        NtfsMetadataChannel channel, NtfsMetadataPeer peer)
    {
        Volume = volume;
        _helper = helper;
        _exchange = exchange;
        _channel = channel;
        _peer = peer;
    }

    internal NtfsMetadataVolume Volume { get; }

    /// <summary>Returns null when Windows or the installed broker declines authorization before a metadata connection.</summary>
    internal static async Task<NtfsMetadataClient?> StartAsync(NtfsMetadataVolume volume, bool portable, string exchangeRoot,
        nint owner, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (AnalysisAdmission.Refusal() is { } refusal) throw new UnauthorizedAccessException(refusal);
        string helperImage = ElevationBroker.LocateNtfsMetadata(portable, out var unavailable) ?? throw new NotSupportedException(unavailable);
        using var requester = NtfsMetadataPeer.HoldSelf(elevated: false);
        string requesterImage = Path.Combine(Path.GetDirectoryName(helperImage)!, ElevationBroker.RequesterExecutableName);
        requester.RequireIdentity(requesterImage, requester.Session, elevated: false);
        using var ownImage = ElevationBroker.OpenNtfsMetadataImage(requesterImage);
        using var installedHelper = ElevationBroker.OpenNtfsMetadataImage(helperImage);
        var scope = new ElevatedNtfsMetadataScope(volume.GuidPath, volume.Serial, volume.RootFileId, requester.Creation, requester.Session,
            requester.LogonSid.Value, ElevationBroker.ImageId(ownImage), NtfsMetadataProtocol.Version, AnalysisLimits.Observations,
            ElevationPlanCodec.MetadataFields);
        var plan = new ElevationPlan
        {
            Nonce = NtfsMetadataProtocol.NewNonce(), CreatedUtc = DateTime.UtcNow, UserSid = requester.UserSid.Value,
            Title = ElevationPlanCodec.MetadataTitle, RequesterProcessId = Environment.ProcessId,
            Steps = [new ElevatedStep(ElevatedVerb.EnumerateNtfsMetadata) { NtfsMetadata = scope }],
        };
        cancellation.ThrowIfCancellationRequested();
        var exchange = ElevationExchange.Create(exchangeRoot, plan);
        ElevatedProcess? helper = null;
        NtfsMetadataPeer? peer = null;
        NtfsMetadataChannel? channel = null;
        try
        {
            helper = ElevationBroker.Launch(helperImage, exchange.VolumePlanPath, exchange.Hash, owner);
            peer = helper.HoldMetadataServer(helperImage, requester.Session);
            channel = await NtfsMetadataPipe.ConnectAsync(plan.Nonce, peer, cancellation).ConfigureAwait(false);
            await channel.SendAsync(Empty(NtfsMetadataMessage.Hello), cancellation).ConfigureAwait(false);
            var response = await channel.ReceiveAsync(cancellation).ConfigureAwait(false);
            if (response.Kind != NtfsMetadataMessage.Accepted || response.Payload.GetProperty("RootId").GetString() != volume.RootFileId)
                throw new IOException("The helper could not confirm the approved NTFS volume.");
            return new(volume, helper, exchange, channel, peer);
        }
        catch (Exception error)
        {
            // Only the held Windows launch handle can report the broker's consent exit status. Observe it
            // before requesting cleanup, which could itself make a waiting broker exit with the same code.
            bool declined = channel is null && !cancellation.IsCancellationRequested &&
                (helper is null && error is OperationCanceledException ||
                 error is IOException && helper is not null && helper.WaitForExit(0) &&
                 helper.ExitCode == ElevationBroker.ConsentDeclinedExitCode);
            channel?.Dispose();
            peer?.Dispose();
            if (helper is not null) await StopAsync(helper, exchange).ConfigureAwait(false);
            else exchange.Dispose();
            if (declined) return null;
            throw;
        }
    }

    internal Task<NtfsMetadataCompletion> CaptureAsync(AnalysisNtfsDiscovery discovery, AnalysisNtfsControl control,
        CancellationToken cancellation) => CaptureControlledAsync(_channel, discovery, control, cancellation);

    internal static async Task<NtfsMetadataCompletion> CaptureControlledAsync(NtfsMetadataChannel channel, AnalysisNtfsDiscovery discovery,
        AnalysisNtfsControl control, CancellationToken cancellation)
    {
        using var receiver = new ControlledReceiver(channel, control);
        control.ReportProgress(0);
        control.ReportPaused(false);
        try
        {
            return await CaptureAsync(receiver.ReceiveAsync, discovery, frame =>
            {
                if (frame.Kind == NtfsMetadataMessage.Progress)
                    control.ReportProgress(NtfsMetadataProtocol.ReadCount(frame.Payload.GetProperty("Seen")));
            }, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await receiver.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class ControlledReceiver(NtfsMetadataChannel channel, AnalysisNtfsControl control) : IDisposable
    {
        private Task<NtfsMetadataFrame>? _pending;
        private Task _controlTick = Task.CompletedTask;

        internal async ValueTask<NtfsMetadataFrame> ReceiveAsync(CancellationToken cancellation)
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (_pending is { IsCompleted: true })
                {
                    var frame = await _pending.ConfigureAwait(false);
                    _pending = null;
                    if (frame.Kind == NtfsMetadataMessage.Paused) control.ReportPaused(true);
                    return frame;
                }
                bool pause = control.PauseRequested();
                await channel.TrySendAsync(Empty(pause ? NtfsMetadataMessage.Pause : NtfsMetadataMessage.Resume), cancellation).ConfigureAwait(false);
                if (!pause && channel.Active) control.ReportPaused(false);

                // A single read remains owned while UI intent changes. Canceling the read itself
                // would discard valid frames that the helper sent before seeing a Cancel command.
                _pending ??= channel.ReceiveAsync(CancellationToken.None).AsTask();
                if (_pending.IsCompleted) continue;
                if (_controlTick.IsCompleted) _controlTick = Task.Delay(75, cancellation);
                await Task.WhenAny(_pending, _controlTick).ConfigureAwait(false);
            }
        }

        internal async Task CancelAsync()
        {
            using var grace = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
            try
            {
                if (channel.IsClosed || !await channel.TrySendAsync(Empty(NtfsMetadataMessage.Cancel), grace.Token).ConfigureAwait(false)) return;
                while (true)
                {
                    _pending ??= channel.ReceiveAsync(CancellationToken.None).AsTask();
                    var frame = await _pending.WaitAsync(grace.Token).ConfigureAwait(false);
                    _pending = null;
                    if (frame.Kind is NtfsMetadataMessage.Complete or NtfsMetadataMessage.Refused) return;
                }
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or InvalidDataException or InvalidOperationException or TimeoutException)
            { /* Disconnect and the owned broker stop marker remain the fallback. */ }
        }

        public void Dispose()
        {
            if (_pending is { } read)
                _ = read.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    internal static async Task<NtfsMetadataCompletion> CaptureAsync(Func<CancellationToken, ValueTask<NtfsMetadataFrame>> receive,
        AnalysisNtfsDiscovery discovery, Action<NtfsMetadataFrame>? status, CancellationToken cancellation)
    {
        var pending = ImmutableArray.CreateBuilder<AnalysisNtfsObservation>(AnalysisNtfsDiscovery.PageRecords);
        uint captured = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var frame = await receive(cancellation).ConfigureAwait(false);
            switch (frame.Kind)
            {
                case NtfsMetadataMessage.ObservationBatch:
                    foreach (var record in frame.Payload.GetProperty("Records").EnumerateArray())
                    {
                        pending.Add(NtfsMetadataProtocol.ReadObservation(record));
                        if (pending.Count == pending.Capacity) await AppendAsync().ConfigureAwait(false);
                    }
                    if (pending.Count != 0) await AppendAsync().ConfigureAwait(false);
                    break;
                case NtfsMetadataMessage.Progress:
                case NtfsMetadataMessage.Issue:
                case NtfsMetadataMessage.Paused:
                    // These closed frame kinds contain counts, issue codes or empty acknowledgments.
                    status?.Invoke(frame);
                    break;
                case NtfsMetadataMessage.Refused:
                    status?.Invoke(frame);
                    throw new IOException("The NTFS metadata helper refused to complete the approved scan.");
                case NtfsMetadataMessage.Complete:
                    if (NtfsMetadataProtocol.ReadCount(frame.Payload.GetProperty("Seen")) != captured)
                        throw NtfsMetadataProtocol.InvalidFrame();
                    var outcome = Enum.Parse<NtfsMetadataCompletion>(frame.Payload.GetProperty("Outcome").GetString()!);
                    if (outcome != NtfsMetadataCompletion.Canceled)
                        await discovery.SealAsync(captured, cancellation).ConfigureAwait(false);
                    return outcome;
                default:
                    throw NtfsMetadataProtocol.InvalidFrame();
            }
        }

        async Task AppendAsync()
        {
            await discovery.AppendAsync(pending.ToImmutable(), cancellation).ConfigureAwait(false);
            captured = checked(captured + (uint)pending.Count);
            pending.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _channel.Dispose(); }
        finally
        {
            _peer.Dispose();
            await StopAsync(_helper, _exchange).ConfigureAwait(false);
        }
    }

    private static async Task StopAsync(ElevatedProcess helper, ElevationExchange exchange)
    {
        bool defer = false;
        try
        {
            if (!helper.WaitForExit(0))
            {
                defer = true;
                exchange.RequestStop();
                defer = !await Task.Run(() => helper.WaitForExit(3000)).ConfigureAwait(false);
            }
        }
        finally
        {
            if (defer) _ = RetireWhenExitedAsync(helper, exchange);
            else { helper.Dispose(); exchange.Dispose(); }
        }
    }

    private static async Task RetireWhenExitedAsync(ElevatedProcess helper, ElevationExchange exchange)
    {
        try
        {
            // An open consent window may outlive the ordinary caller's stop wait. Keep its stop
            // marker until the exact launched helper exits, so delayed consent cannot clear it.
            while (!helper.WaitForExit(0)) await Task.Delay(500).ConfigureAwait(false);
        }
        finally { helper.Dispose(); exchange.Dispose(); }
    }

    private static NtfsMetadataFrame Empty(NtfsMetadataMessage kind) => new(kind, JsonSerializer.SerializeToElement(new { }));
}
