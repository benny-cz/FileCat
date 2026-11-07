using System.Security.Cryptography;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisComparisonFormat
{
    internal sealed record ContainerInspection(Guid Id, long Bytes, DateTimeOffset SavedUtc, AnalysisTaint Taint, string Sha256);
    private static readonly SemaphoreSlim Inspections = new(2, 2);

    // Container/schema/framing verification for private maintenance, not graph import or source authority.
    internal static async Task<ContainerInspection> InspectContainerAsync(Stream source, Action verify,
        DeviceIoScheduler io, AnalysisIoDevices device, CancellationToken cancellation)
    {
        await Inspections.WaitAsync(cancellation).ConfigureAwait(false);
        InspectionBuffer? owned = null;
        try
        {
            owned = new(new BufferedStream(new NonClosing(source), 64 * 1024)); var buffered = owned.Stream;
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] header = await AnalysisIo.Run(io, device, token =>
            { verify(); var bytes = Line(buffered, HeaderBytes, token); verify(); return bytes; }, cancellation).ConfigureAwait(false);
            var parsed = await Task.Run(() => { cancellation.ThrowIfCancellationRequested(); return ParseHeader(header); }, cancellation).ConfigureAwait(false);
            digest.AppendData(header);
            AnalysisInventoryFormat.ContainerInspection previous, current;
            using (var input = new Slice(buffered, parsed.PreviousBytes, digest, cancellation))
            {
                previous = await AnalysisInventoryFormat.InspectContainerAsync(input, verify, io, device, cancellation).ConfigureAwait(false);
                if (input.Remaining != 0 || previous.Bytes != parsed.PreviousBytes) throw Invalid("Previous comparison input has trailing data.");
            }
            using (var input = new Slice(buffered, parsed.CurrentBytes, digest, cancellation))
            {
                current = await AnalysisInventoryFormat.InspectContainerAsync(input, verify, io, device, cancellation).ConfigureAwait(false);
                if (input.Remaining != 0 || current.Bytes != parsed.CurrentBytes) throw Invalid("Current comparison input has trailing data.");
            }
            if (previous.Id != parsed.PreviousId || current.Id != parsed.CurrentId) throw Invalid("Saved comparison input identity mismatch.");
            byte[] trailer = await AnalysisIo.Run(io, device, token =>
            {
                verify(); var bytes = Line(buffered, 256, token);
                if (buffered.ReadByte() != -1) throw Invalid("Saved comparison has trailing data.");
                verify(); return bytes;
            }, cancellation).ConfigureAwait(false);
            await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested(); using var end = Document(trailer); Fields(end.RootElement, "kind", "sha256");
                if (Text(end.RootElement, "kind") != "comparison-trailer" || Text(end.RootElement, "sha256") != Convert.ToHexStringLower(digest.GetCurrentHash()))
                    throw Invalid("Saved comparison checksum is invalid.");
            }, cancellation).ConfigureAwait(false);
            var joined = AnalysisPrivacy.Join(previous.Taint, current.Taint);
            if ((joined & parsed.Taint) != parsed.Taint) throw Invalid("Saved comparison privacy declarations disagree with their inputs.");
            digest.AppendData(trailer); cancellation.ThrowIfCancellationRequested();
            return new(parsed.Id, checked(header.Length + parsed.PreviousBytes + parsed.CurrentBytes + trailer.Length), parsed.SavedUtc,
                AnalysisPrivacy.Join(parsed.Taint, joined), Convert.ToHexStringLower(digest.GetHashAndReset()));
        }
        finally
        {
            try { if (owned is not null) await AnalysisIo.ReleaseScratchAsync(io, device, owned).ConfigureAwait(false); }
            finally { Inspections.Release(); }
        }
    }
    private sealed class InspectionBuffer(BufferedStream stream) : IAnalysisScratchOwned
    {
        internal BufferedStream Stream => stream;
        public void Dispose() => stream.Dispose();
    }
}
