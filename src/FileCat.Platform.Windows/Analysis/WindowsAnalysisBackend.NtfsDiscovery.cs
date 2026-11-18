using System.Buffers.Binary;
using FileCat.Core.Analysis;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Analysis;

public sealed partial class WindowsAnalysisBackend
{
    internal IAnalysisNtfsScan PrepareNtfsDiscovery(bool portable, string exchangeRoot, nint owner, CancellationToken cancellation)
    {
        Guard();
        if (ElevationBroker.Locate(portable, out _) is null)
            throw new NotSupportedException("The installed read-only NTFS helper is unavailable. Use standard analysis.");
        var volume = CaptureDiscoveryVolume(cancellation);
        return new NtfsScan(this, volume, portable, exchangeRoot, owner);
    }

    internal NtfsMetadataVolume CaptureDiscoveryVolume(CancellationToken cancellation)
    {
        Guard();
        var selected = NtfsMetadataVolume.CaptureSelection(Root.DisplayLocator, cancellation);
        if (RootEntry.Kind != AnalysisKind.Directory || RootEntry.Cloud == true ||
            NtfsMetadataVolume.Native.Capture(selected.GuidPath, _root.Value) != selected)
            throw new AnalysisSourceChangedException();
        return selected;
    }

    internal unsafe UInt128? DiscoveryPrivateBoundaryId(IAnalysisBackend protection)
    {
        Guard();
        if (protection is not WindowsAnalysisBackend native) throw new AnalysisSourceChangedException();
        native.Guard();
        if (native._namespace != _namespace) return null;
        if (native._volume != _volume || native.RootEntry.Kind != AnalysisKind.Directory) throw new AnalysisSourceChangedException();
        Span<byte> identity = stackalloc byte[24];
        fixed (byte* bytes = identity)
            if (!GetInfo(native._root.Value, 18 /* FileIdInfo */, bytes, 24)) throw new AnalysisSourceChangedException();
        var id = BinaryPrimitives.ReadUInt128LittleEndian(identity[8..]);
        if (id == 0 || BinaryPrimitives.ReadUInt64LittleEndian(identity) != _volume) throw new AnalysisSourceChangedException();
        return id;
    }

    private sealed class NtfsScan(WindowsAnalysisBackend source, NtfsMetadataVolume volume, bool portable, string exchange, nint owner) : IAnalysisNtfsScan
    {
        public IAnalysisBackend Source => source;
        public UInt128 RootId { get; } = NtfsMetadataProtocol.ParseId(volume.RootFileId);

        public void ValidateVolume(CancellationToken cancellation)
        {
            if (source.CaptureDiscoveryVolume(cancellation) != volume) throw new AnalysisSourceChangedException();
        }

        public UInt128? GetPrivateBoundaryId(IAnalysisBackend protection) => source.DiscoveryPrivateBoundaryId(protection);

        public async Task<AnalysisNtfsOutcome> CaptureAsync(AnalysisNtfsDiscovery discovery, CancellationToken cancellation)
        {
            try
            {
                await using var client = await NtfsMetadataClient.StartAsync(volume, portable, exchange, owner, cancellation).ConfigureAwait(false);
                return await client.CaptureAsync(discovery, status: null, cancellation).ConfigureAwait(false) switch
                {
                    NtfsMetadataCompletion.Finished => AnalysisNtfsOutcome.Finished,
                    NtfsMetadataCompletion.Partial => AnalysisNtfsOutcome.Partial,
                    _ => AnalysisNtfsOutcome.Canceled
                };
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return AnalysisNtfsOutcome.Canceled; }
        }
    }
}
