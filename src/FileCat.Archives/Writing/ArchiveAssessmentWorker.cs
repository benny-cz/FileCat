using System.Buffers.Binary;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>
/// Disposable assessment endpoint. The application must establish and verify native confinement before calling it.
/// This endpoint has only inherited streams; sources are numeric proxy grants and there is no stage or host path.
/// </summary>
public static class ArchiveAssessmentWorker
{
    public static int RunAfterIsolation(Stream input, Stream output, Guid nonce, CancellationToken ct = default)
        => Run(input, output, nonce, verification: false, ct);

    public static int RunVerificationAfterIsolation(Stream input, Stream output, Guid nonce, CancellationToken ct = default)
        => Run(input, output, nonce, verification: true, ct);

    private static int Run(Stream input, Stream output, Guid nonce, bool verification, CancellationToken ct)
    {
        var receive = new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes);
        var send = new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes);
        var incoming = new ArchiveWorkerRecords(receive, input, writing: false);
        var outgoing = new ArchiveWorkerRecords(send, output, writing: true);
        try
        {
            send.Write(output, ArchiveWorkerMessage.Hello, verification ? ArchiveAssessmentWire.VerificationHello : ArchiveAssessmentWire.Hello);
            var request = ArchiveAssessmentWire.ReadRequest(incoming.Read(ArchiveControlRecord.AssessmentRequest));
            if (request.Profile.Format != ArchiveWriteFormat.Tar)
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This worker profile has not been implemented.");
            var connection = new ArchiveProxyConnection(frame =>
            {
                ct.ThrowIfCancellationRequested();
                send.Write(output, frame.Kind, frame.Payload);
                return receive.Read(input);
            });
            using var source = new ArchiveProxyStream(connection, 0, request.SourceLength, request.SourceLength);
            string physicalDigest = Convert.ToHexString(SHA256.HashData(source));
            if (physicalDigest != request.SourceSha256)
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The pinned source differs from its reviewed revision.");
            source.Position = 0;
            var catalog = StrictTarCatalog.Read(source, ct);
            if (catalog.Revision != request.SourceSha256 || catalog.Length != request.SourceLength)
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The pinned TAR changed during assessment.");
            source.Position = 0;
            StrictTarCatalog.CompareDecoder(source, catalog, ct);
            var members = new List<ArchiveManifestMember>(catalog.Members.Count);
            var budget = new ArchiveWriteBudget();
            foreach (var member in catalog.Members)
            {
                ct.ThrowIfCancellationRequested();
                int size = MetadataLength(member);
                int nameBytes = Encoding.UTF8.GetByteCount(member.Name);
                // Reserve the second representation before producing/copying its canonical metadata buffer.
                budget.AddMember(); budget.AddLogicalBytes(member.Length);
                budget.AddMetadata(nameBytes + size + 64L, 2L * member.Name.Length + size + 512L);
                members.Add(new(member.Name, member.Type, member.Occurrence, member.Length, member.Sha256, Metadata(member, size)));
            }
            var manifest = new ArchiveWriteManifest(request.Profile, members,
                [1, catalog.Format == TarEntryFormat.Ustar ? (byte)1 : (byte)2]);
            var result = new ArchiveAssessment(manifest, catalog.Revision, request.SourceLength, physicalDigest);
            outgoing.Write(ArchiveControlRecord.Catalog, ArchiveAssessmentWire.Catalog(result));
            foreach (var member in manifest.Members)
            {
                ct.ThrowIfCancellationRequested();
                outgoing.Write(ArchiveControlRecord.Member, ArchiveAssessmentWire.Member(member));
            }
            send.Write(output, ArchiveWorkerMessage.Completed, Convert.FromHexString(manifest.Digest));
            return 0;
        }
        catch (ArchiveWriteException ex)
        {
            byte[] reason = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reason, (int)ex.Reason);
            try { send.Write(output, ArchiveWorkerMessage.Refused, reason); }
            catch (IOException) { return 1; }
            return 0; // controlled refusal; no raw names, archive bytes or uncontrolled exception text on the wire
        }
    }

    private static int MetadataLength(TarWriteMember member) => checked(2 + 12 + 8 + 8 +
        Encoding.UTF8.GetByteCount(member.UserName) + Encoding.UTF8.GetByteCount(member.GroupName) +
        2 + (member.Accessed.HasValue ? 8 : 0) + (member.Changed.HasValue ? 8 : 0));

    private static byte[] Metadata(TarWriteMember member, int length)
    {
        using var stream = new MemoryStream(length);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)1);
        writer.Write(member.Mode); writer.Write(member.Uid); writer.Write(member.Gid);
        Text(member.UserName); Text(member.GroupName);
        writer.Write(StrictTarCatalog.ToTime(member.Modified).UtcTicks);
        Time(member.Accessed); Time(member.Changed);
        return stream.ToArray();
        void Text(string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes); }
        void Time(decimal? value) { writer.Write(value.HasValue); if (value is { } time) writer.Write(StrictTarCatalog.ToTime(time).UtcTicks); }
    }
}
