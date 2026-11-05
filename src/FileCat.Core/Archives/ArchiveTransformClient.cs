using System.Buffers.Binary;

namespace FileCat.Core.Archives;

/// <summary>
/// Confined transformation only: a successful receipt is unverified and grants no publication authority. The caller
/// must retain/rereview and durably seal its native stage, then invoke the fresh verifier against invocation.Expected.
/// No in-process codec fallback. The supplied streams must be views of retained reviewed native objects/snapshots.
/// </summary>
public sealed class ArchiveTransformClient
{
    private readonly Func<Guid, TimeSpan, IArchiveWorkerProcess> launch;
    private readonly ArchiveWorkerQueue queue;
    public ArchiveTransformClient(Func<Guid, TimeSpan, IArchiveWorkerProcess> launchTransform, ArchiveWorkerQueue? queue = null)
    { ArgumentNullException.ThrowIfNull(launchTransform); launch = launchTransform; this.queue = queue ?? ArchiveWorkerQueue.Shared; }

    public async Task<ArchiveTransformReceipt> RebuildAsync(ArchiveTarTransformInvocation invocation, Stream ownedEmptyStage,
        Func<Stream> openPinnedOriginal, Func<int, Stream> openPinnedCapturedInput, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invocation); ArgumentNullException.ThrowIfNull(ownedEmptyStage);
        ArgumentNullException.ThrowIfNull(openPinnedOriginal); ArgumentNullException.ThrowIfNull(openPinnedCapturedInput);
        var request = invocation.Request; long originalLength = request.Source?.SourceLength ?? 0;
        long inputLength = request.Inputs.Values.Sum(i => i.Length);
        TimeSpan cpu = ArchiveAssessmentClient.CpuAllowance(Math.Min(ArchiveWritePolicy.MaxPhysicalBytes, originalLength + inputLength));
        var result = await ArchiveWorkerSession.RunAsync(launch, queue, cpu, TimeSpan.FromSeconds(30), TimeSpan.FromHours(12), Conversation, ct).ConfigureAwait(false);
        if (result.Refusal is { } reason) throw new ArchiveWriteException(reason, "The archive worker refused this transformation. The archive remains unchanged.");
        var receipt = result.Receipt!;
        if (receipt.ManifestDigest != invocation.Expected.Digest || receipt.ContentDigest != invocation.Expected.ContentDigest || receipt.MetadataDigest != invocation.Expected.MetadataDigest)
            throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "The transform receipt differs from the complete parent expectation. The stage remains unpublished.");
        return receipt;

        (ArchiveTransformReceipt? Receipt, ArchiveWriteReason? Refusal) Conversation(Guid nonce, IArchiveWorkerProcess process, Action progress)
        {
            long sourceIo = checked((request.Profile.Format == ArchiveWriteFormat.TarGzip ? 4 : 3) * originalLength + inputLength);
            long wireBytes = Math.Min(ArchiveWritePolicy.MaxWorkerWireBytes, checked(sourceIo + 4 * request.OutputLengthBound + ArchiveWritePolicy.MaxMetadataBytes));
            var receive = new ArchiveWorkerProtocol(nonce, wireBytes); var send = new ArchiveWorkerProtocol(nonce, wireBytes);
            var incoming = new ArchiveWorkerRecords(receive, process.Output, writing: false); var outgoing = new ArchiveWorkerRecords(send, process.Input, writing: true);
            var hello = receive.Read(process.Output);
            if (hello.Kind != ArchiveWorkerMessage.Hello || !hello.Payload.AsSpan().SequenceEqual(ArchiveTransformWire.Hello)) throw Fault();
            var stage = new ArchiveStageProxy(ownedEmptyStage, request.OutputLengthBound, 4 * request.OutputLengthBound);
            var grants = request.Inputs.Values.Select(i => new ArchiveInputGrant(i.Id, i.Length)).ToList();
            if (request.Source is not null) grants.Add(new(0, originalLength));
            using var authority = new ArchiveIoAuthority(grants, id =>
            {
                ct.ThrowIfCancellationRequested();
                return id == 0 ? openPinnedOriginal() : openPinnedCapturedInput(invocation.CapturedInputIds[id]);
            }, stage, sourceIo);
            ArchiveTransformWire.WriteRequest(outgoing, request, ct, progress); progress();
            ArchiveWorkerFrame frame;
            while (true)
            {
                ct.ThrowIfCancellationRequested(); frame = receive.Read(process.Output);
                if (frame.Kind == ArchiveWorkerMessage.Refused) return Refused(frame);
                if (frame.Kind is not (ArchiveWorkerMessage.Read or ArchiveWorkerMessage.Write or ArchiveWorkerMessage.SetLength)) break;
                ct.ThrowIfCancellationRequested();
                long length = stage.Length; var response = authority.Handle(frame); send.Write(process.Input, response.Kind, response.Payload);
                if (frame.Kind == ArchiveWorkerMessage.Read && response.Payload.Length > 12 || frame.Kind == ArchiveWorkerMessage.Write && frame.Payload.Length > 8 || stage.Length > length) progress();
            }
            var receipt = ArchiveTransformWire.ReadReceipt(incoming.Read(ArchiveControlRecord.Receipt, frame));
            if (receipt.Length != stage.Length || receipt.Length > request.OutputLengthBound ||
                receipt.Length < (request.Profile.Format == ArchiveWriteFormat.Tar ? 1024 : 20)) throw Fault(); progress();
            frame = receive.Read(process.Output);
            if (frame.Kind != ArchiveWorkerMessage.Completed || frame.Payload.Length != 32 || Convert.ToHexString(frame.Payload) != receipt.ManifestDigest) throw Fault();
            End(); return (receipt, null);

            (ArchiveTransformReceipt?, ArchiveWriteReason?) Refused(ArchiveWorkerFrame refusal)
            {
                if (refusal.Payload.Length != 4) throw Fault(); var reason = (ArchiveWriteReason)BinaryPrimitives.ReadInt32LittleEndian(refusal.Payload);
                if (!Enum.IsDefined(reason)) throw Fault(); End(); return (null, reason);
            }
            void End() { if (process.Output.ReadByte() != -1) throw Fault(); progress(); }
        }
    }
    private static ArchiveWriteException Fault() => new(ArchiveWriteReason.ProtocolViolation, "The archive worker's transform conversation is invalid.");
}
