using System.Buffers.Binary;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Stream-only TAR transform endpoint. Native isolation must already have been verified before entry.</summary>
public static class ArchiveTransformWorker
{
    public static int RunAfterIsolation(Stream input, Stream output, Guid nonce, CancellationToken ct = default)
    {
        var receive = new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes);
        var send = new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes);
        var incoming = new ArchiveWorkerRecords(receive, input, writing: false);
        var outgoing = new ArchiveWorkerRecords(send, output, writing: true);
        try
        {
            send.Write(output, ArchiveWorkerMessage.Hello, ArchiveTransformWire.Hello);
            var request = ArchiveTransformWire.ReadRequest(incoming, ct);
            var connection = new ArchiveProxyConnection(frame =>
            { ct.ThrowIfCancellationRequested(); send.Write(output, frame.Kind, frame.Payload); return receive.Read(input); });
            using var source = request.Source is { } original ? new ArchiveProxyStream(connection, 0, original.SourceLength, original.SourceLength) : null;
            using var stage = new ArchiveProxyStream(connection, ArchiveIoAuthority.StageId, 0, request.OutputLengthBound, writable: true);
            var edits = request.Edits.Select(e => new TarWriteEdit(e.Kind, e.Key, e.NewName, e.InputId)).ToArray();
            var additions = request.Additions.Select(a => new TarWriteAddition(a.Name, a.Type, a.InputId)).ToArray();
            var inputs = request.Inputs.ToDictionary(p => p.Key, p => new TarInputDescriptor(p.Key, p.Value.Length, p.Value.Sha256, p.Value.ModifiedTimestamp, p.Value.Mode));
            Stream Open(int id) => new ArchiveProxyStream(connection, id, request.Inputs[id].Length, request.Inputs[id].Length);
            TarRewriteManifest transformed; byte os = 255, block = 9; byte? flags = null;
            if (request.Profile.Format == ArchiveWriteFormat.Tar)
                transformed = TarRewriteEngine.Rebuild(source, request.Source?.SourceSha256, stage, edits, additions, inputs, Open, ct);
            else if (request.Profile.Format == ArchiveWriteFormat.TarGzip)
            {
                var compressed = GzipTarRewriteEngine.Rebuild(source, request.Source?.SourceSha256, request.Profile, stage, edits, additions, inputs, Open, ct);
                transformed = compressed.Tar; os = compressed.OperatingSystem; flags = compressed.ExtraFlags;
            }
            else if (request.Profile.Format == ArchiveWriteFormat.TarBzip2)
            {
                var compressed = Bzip2TarRewriteEngine.Rebuild(source, request.Source?.SourceSha256, request.Profile, stage, edits, additions, inputs, Open, ct);
                transformed = compressed.Tar; block = compressed.BlockSize;
            }
            else throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This transform profile has not been implemented.");
            var manifest = ArchiveAssessmentWorker.Manifest(request.Profile, transformed.Format, transformed.Members, ct, os, flags, block);
            outgoing.Write(ArchiveControlRecord.Receipt, ArchiveTransformWire.Receipt(new(stage.Length, manifest.Digest, manifest.ContentDigest, manifest.MetadataDigest)));
            send.Write(output, ArchiveWorkerMessage.Completed, Convert.FromHexString(manifest.Digest));
            return 0;
        }
        catch (ArchiveWriteException ex)
        {
            byte[] reason = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(reason, (int)ex.Reason);
            try { send.Write(output, ArchiveWorkerMessage.Refused, reason); } catch (IOException) { return 1; }
            return 0;
        }
    }
}
