namespace FileCat.Core.Analysis;

public sealed partial class OwnedAnalysisStore
{
    private async Task<AnalysisPrivateFile> CopyRecoveryPayloadAsync(Contents data, AnalysisPrivateFile source,
        AnalysisInventoryFormat.ContainerInspection expected, string name, CancellationToken cancellation)
    {
        AnalysisPrivateInventoryStore.DatasetWrite? output = null; AnalysisPrivateFile? published = null; bool named = false;
        try
        {
            output = await AnalysisIo.Run(_io, _device, token =>
            {
                source.Verify(); source.Stream.Position = 0;
                return AnalysisPrivateNames.IsReplacementPayload(name)
                    ? AnalysisPrivateInventoryStore.BeginReplacementWrite(data.Store!.BorrowDirectory(), name, token)
                    : AnalysisPrivateInventoryStore.BeginDatasetWrite(data.Store!.BorrowDirectory(), name, token);
            }, cancellation).ConfigureAwait(false);
            byte[] buffer = new byte[64 * 1024]; long copied = 0;
            while (copied < expected.Bytes)
            {
                int count = await AnalysisIo.Run(_io, _device, token =>
                {
                    token.ThrowIfCancellationRequested(); source.Verify();
                    int read = source.Stream.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, expected.Bytes - copied)));
                    if (read == 0) throw new InvalidDataException("The previous inventory ended during recovery copying.");
                    output.Stream.Write(buffer.AsSpan(0, read)); output.FlushBatch(); return read;
                }, cancellation).ConfigureAwait(false);
                copied += count;
            }
            await AnalysisIo.Run(_io, _device, token => { token.ThrowIfCancellationRequested(); output.Prepare(); return true; }, cancellation).ConfigureAwait(false);
            var verified = await AnalysisInventoryFormat.InspectContainerAsync(output.PreparedStream, output.Verify, _io, _device, cancellation).ConfigureAwait(false);
            if (verified != expected) throw new InvalidDataException("The complete copied inventory did not match its reviewed bytes.");
            string identity = await AnalysisIo.Run(_io, _device, token =>
            {
                output.PublishPrepared(expected.Id, token); named = true; return output.PublishedIdentity;
            }, cancellation).ConfigureAwait(false);
            var closing = output; output = null; await AnalysisIo.ReleaseScratchAsync(_io, _device, closing).ConfigureAwait(false);
            await AnalysisIo.Run(_io, _device, _ =>
            {
                using var directory = data.Store!.BorrowDirectory(); published = directory.OpenReplacementFile(name);
                if (published.Identity != identity) throw new IOException("The published recovery name was substituted. Its current occupant was preserved.");
                return true;
            }, cancellation).ConfigureAwait(false);
            var confirmed = await AnalysisInventoryFormat.InspectContainerAsync(published!.Stream, published.Verify, _io, _device, cancellation).ConfigureAwait(false);
            if (confirmed != expected) throw new InvalidDataException("The published recovery bytes changed.");
            var result = published; published = null; return result;
        }
        catch (Exception error) when (named && error is not AnalysisPublicationUncertainException)
        { throw new AnalysisPublicationUncertainException(name, error); }
        finally
        {
            // Every actual native owner is independently closed even when publication or another close fails.
            Exception? failure = null;
            if (output is not null) { try { await AnalysisIo.ReleaseScratchAsync(_io, _device, output).ConfigureAwait(false); } catch (Exception error) { failure = error; } }
            if (published is not null)
            {
                try { await AnalysisIo.ReleaseScratchAsync(_io, _device, new RecoveryFileOwner(published)).ConfigureAwait(false); }
                catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
    private sealed class RecoveryFileOwner(AnalysisPrivateFile file) : IAnalysisScratchOwned
    { public void Dispose() => AnalysisIo.CloseOnWorker(file); }
}
