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

    internal static async Task<NtfsMetadataClient> StartAsync(string selectedRoot, bool portable, string exchangeRoot,
        nint owner, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (AnalysisAdmission.Refusal() is { } refusal) throw new UnauthorizedAccessException(refusal);
        string helperImage = ElevationBroker.Locate(portable, out _) ??
            throw new NotSupportedException("The installed NTFS metadata helper is unavailable. Use standard analysis.");
        using var requester = NtfsMetadataPeer.HoldSelf(elevated: false);
        string requesterImage = Path.Combine(Path.GetDirectoryName(helperImage)!, ElevationBroker.RequesterExecutableName);
        requester.RequireIdentity(requesterImage, requester.Session, elevated: false);
        using var ownImage = ElevationBroker.OpenNtfsMetadataImage(requesterImage);
        using var installedHelper = ElevationBroker.OpenNtfsMetadataImage(helperImage);
        var volume = NtfsMetadataVolume.CaptureSelection(selectedRoot, cancellation);
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
        catch
        {
            channel?.Dispose();
            peer?.Dispose();
            if (helper is not null) await StopAsync(helper, exchange).ConfigureAwait(false);
            else exchange.Dispose();
            throw;
        }
    }

    internal ValueTask<NtfsMetadataFrame> ReceiveAsync(CancellationToken cancellation) => _channel.ReceiveAsync(cancellation);
    internal ValueTask PauseAsync(CancellationToken cancellation) => _channel.SendAsync(Empty(NtfsMetadataMessage.Pause), cancellation);
    internal ValueTask ResumeAsync(CancellationToken cancellation) => _channel.SendAsync(Empty(NtfsMetadataMessage.Resume), cancellation);
    internal ValueTask CancelAsync(CancellationToken cancellation) => _channel.SendAsync(Empty(NtfsMetadataMessage.Cancel), cancellation);

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
