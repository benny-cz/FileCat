using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>A live capacity sample from the selected source's held filesystem object.</summary>
public sealed record AnalysisVolumeCapacity(string VolumeIdentity, DateTimeOffset ObservedUtc, UInt128 TotalBytes,
    UInt128 UsedBytes, UInt128 FreeBytes, UInt128 AvailableBytes, bool WholeVolumeVerified, bool MayShareCapacity);

public sealed partial class AnalysisSession
{
    private ImmutableDictionary<string, AnalysisVolumeCapacity> _volumeCapacities = ImmutableDictionary<string, AnalysisVolumeCapacity>.Empty;

    /// <summary>Live samples by root ID for this epoch. Recorded inventories do not provide live capacity.</summary>
    public ImmutableDictionary<string, AnalysisVolumeCapacity> VolumeCapacities { get { lock (_sync) return _volumeCapacities; } }

    private async Task CaptureVolumeCapacityAsync(IAnalysisVolumeBackend backend, string rootId, string device)
    {
        try
        {
            var sample = await AnalysisIo.Run(_io, device, backend.ReadVolumeCapacity, _cancel.Token).ConfigureAwait(false);
            if (sample is null) return;
            lock (_sync) _volumeCapacities = _volumeCapacities.SetItem(rootId, sample);
        }
        catch (Exception error) when (!AnalysisCloseUnconfirmedException.IsPresent(error) &&
            error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A missing or unsupported capacity sample does not invalidate captured file measurements.
        }
    }
}
