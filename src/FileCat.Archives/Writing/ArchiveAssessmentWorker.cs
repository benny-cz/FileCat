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
    public static int RunAfterIsolation(Stream input, Stream output, Guid nonce, CancellationToken ct = default,
        ushort protocolVersion = ArchiveWorkerProtocol.Version)
        => Run(input, output, nonce, verification: false, ct, protocolVersion);

    public static int RunVerificationAfterIsolation(Stream input, Stream output, Guid nonce, CancellationToken ct = default,
        ushort protocolVersion = ArchiveWorkerProtocol.Version)
        => Run(input, output, nonce, verification: true, ct, protocolVersion);

    private static int Run(Stream input, Stream output, Guid nonce, bool verification, CancellationToken ct, ushort protocolVersion)
    {
        var receive = new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes, protocolVersion);
        var send = new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes, protocolVersion);
        var incoming = new ArchiveWorkerRecords(receive, input, writing: false);
        var outgoing = new ArchiveWorkerRecords(send, output, writing: true);
        try
        {
            send.Write(output, ArchiveWorkerMessage.Hello, ArchiveAssessmentWire.HelloFor(verification, protocolVersion));
            var request = ArchiveAssessmentWire.ReadRequest(incoming.Read(ArchiveControlRecord.AssessmentRequest));
            if (request.Profile.Format is not (ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.TarZstd or ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip))
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This worker profile has not been implemented.");
            using var passwords = request.Profile.Format == ArchiveWriteFormat.Rar5
                ? Rar5WorkerPasswords.Receive(receive, input, request.Profile, verification ? ArchiveWorkerRole.Verify : ArchiveWorkerRole.Assess,
                    ArchiveAssessmentWire.RequestDigest(request), read: true, write: false, ct) : null;
            using var sevenPassword = request.Profile.Format == ArchiveWriteFormat.SevenZip
                ? SevenZipWorkerPassword.Receive(receive, input, request.Profile, verification ? ArchiveWorkerRole.Verify : ArchiveWorkerRole.Assess,
                    ArchiveAssessmentWire.RequestDigest(request), ct) : null;
            var connection = new ArchiveProxyConnection(frame =>
            {
                ct.ThrowIfCancellationRequested();
                send.Write(output, frame.Kind, frame.Payload);
                return receive.Read(input);
            });
            using var source = new ArchiveProxyStream(connection, 0, request.SourceLength, request.SourceLength);
            string physicalDigest = PhysicalDigest(source, ct);
            if (physicalDigest != request.SourceSha256)
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The pinned source differs from its reviewed revision.");
            source.Position = 0;
            ArchiveWriteManifest manifest;
            if (request.Profile.Format == ArchiveWriteFormat.Rar5)
            {
                var rar = StrictRar5Catalog.Read(source, ct, passwords!.Read);
                if (rar.Physical.Revision != physicalDigest || rar.Physical.Length != request.SourceLength)
                    throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The complete RAR source changed during assessment.");
                manifest = Rar5SemanticCatalog.Assessment(rar, request.Profile, ct).Manifest;
                // Assessment remains observation. Validate all preservation/password facts again in parent planning.
            }
            else if (request.Profile.Format == ArchiveWriteFormat.SevenZip)
            {
                manifest = sevenPassword is null ? ObserveSevenZip(default) : sevenPassword.UseOnce(ObserveSevenZip);
            }
            else
            {
                TarWriteCatalog catalog; byte os = 255, block = 9, zstdChecksum = 1; byte? flags = null;
                if (request.Profile.Format == ArchiveWriteFormat.Tar)
                {
                    catalog = StrictTarCatalog.Read(source, ct);
                    if (catalog.Revision != request.SourceSha256 || catalog.Length != request.SourceLength)
                        throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The pinned TAR changed during assessment.");
                    source.Position = 0; StrictTarCatalog.CompareDecoder(source, catalog, ct);
                }
                else if (request.Profile.Format == ArchiveWriteFormat.TarGzip)
                {
                    var compressed = GzipTarRewriteEngine.Read(source, ct); catalog = compressed.Tar; os = compressed.OperatingSystem; flags = compressed.ExtraFlags;
                    source.Position = 0; using var decoded = new StrictGzipReadStream(source, ct);
                    if (decoded.OperatingSystem != os || decoded.ExtraFlags != flags)
                        throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The gzip wrapper changed between assessment passes.");
                    StrictTarCatalog.CompareDecoder(decoded, catalog, ct); decoded.CopyTo(Stream.Null);
                }
                else if (request.Profile.Format == ArchiveWriteFormat.TarBzip2)
                {
                    var compressed = Bzip2TarRewriteEngine.Read(source, ct); catalog = compressed.Tar; block = compressed.BlockSize;
                    source.Position = 0; using var decoded = new StrictBzip2ReadStream(source, ct);
                    if (decoded.BlockSize != block) throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The BZip2 block size changed between passes.");
                    StrictTarCatalog.CompareDecoder(decoded, catalog, ct); decoded.CopyTo(Stream.Null);
                }
                else
                {
                    var compressed = ZstdTarRewriteEngine.Read(source, ct); catalog = compressed.Tar; zstdChecksum = compressed.Header.Checksum ? (byte)1 : (byte)0;
                    source.Position = 0; using var decoded = new StrictZstdReadStream(source, ct);
                    if (!decoded.Header.Bytes.SequenceEqual(compressed.Header.Bytes)) throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The Zstandard header changed between passes.");
                    StrictTarCatalog.CompareDecoder(decoded, catalog, ct); decoded.CopyTo(Stream.Null);
                }
                manifest = Manifest(request.Profile, catalog.Format, catalog.Members, ct, os, flags, block, zstdChecksum);
            }
            var result = new ArchiveAssessment(manifest, physicalDigest, request.SourceLength, physicalDigest);
            outgoing.Write(ArchiveControlRecord.Catalog, ArchiveAssessmentWire.Catalog(result));
            foreach (var member in manifest.Members)
            {
                ct.ThrowIfCancellationRequested();
                outgoing.Write(ArchiveControlRecord.Member, ArchiveAssessmentWire.Member(member));
            }
            send.Write(output, ArchiveWorkerMessage.Completed, Convert.FromHexString(manifest.Digest));
            return 0;

            ArchiveWriteManifest ObserveSevenZip(ReadOnlySpan<char> password)
            {
                using var session = password.IsEmpty ? null : new SevenZipReadPasswordSession(password, new(), ct);
                var seven = StrictSevenZipCatalog.Read(source, ct, session);
                if (seven.Physical.Revision != physicalDigest || seven.Physical.Length != request.SourceLength)
                    throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The complete 7z source changed during assessment.");
                return SevenZipSemanticCatalog.Assessment(seven, request.Profile, ct).Manifest;
            }
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

    internal static ArchiveWriteManifest Manifest(ArchiveWriteProfile profile, TarEntryFormat format, IReadOnlyList<TarWriteMember> source, CancellationToken ct,
        byte operatingSystem = 255, byte? extraFlags = null, byte bzip2BlockSize = 9, byte zstdChecksum = 1)
    {
        var members = new List<ArchiveManifestMember>(source.Count); var budget = new ArchiveWriteBudget();
        foreach (var member in source)
        {
            ct.ThrowIfCancellationRequested(); int size = MetadataLength(member); int nameBytes = Encoding.UTF8.GetByteCount(member.Name);
            budget.AddMember(); budget.AddLogicalBytes(member.Length);
            budget.AddMetadata(nameBytes + size + 64L, 2L * member.Name.Length + size + 512L);
            members.Add(new(member.Name, member.Type, member.Occurrence, member.Length, member.Sha256, Metadata(member, size)));
        }
        return new(profile, members, ArchiveTarFeatures.Create(profile, source.Count == 0 || format == TarEntryFormat.Ustar ? (byte)1 : (byte)2, operatingSystem, extraFlags, bzip2BlockSize, zstdChecksum));
    }

    internal static string PhysicalDigest(Stream source, CancellationToken ct)
    {
        // Use the maximum bounded proxy read explicitly. HashData(Stream)'s runtime-selected small buffer caused
        // many more IPC frames than the archive-size policy could support on large otherwise valid sources.
        byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int count;
        while ((count = source.Read(buffer)) != 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool FineTime(TarWriteMember member) =>
        !ArchiveTimestamp.FromUnixSeconds(member.Modified).FitsTicks ||
        member.Accessed is { } a && !ArchiveTimestamp.FromUnixSeconds(a).FitsTicks ||
        member.Changed is { } c && !ArchiveTimestamp.FromUnixSeconds(c).FitsTicks;
    private static int MetadataLength(TarWriteMember member) => checked(2 + 12 + 8 + (FineTime(member) ? 12 : 8) +
        Encoding.UTF8.GetByteCount(member.UserName) + Encoding.UTF8.GetByteCount(member.GroupName) +
        2 + (member.Accessed.HasValue ? (FineTime(member) ? 12 : 8) : 0) + (member.Changed.HasValue ? (FineTime(member) ? 12 : 8) : 0));

    private static byte[] Metadata(TarWriteMember member, int length)
    {
        using var stream = new MemoryStream(length);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        bool fine = FineTime(member); writer.Write(fine ? (ushort)2 : (ushort)1);
        writer.Write(member.Mode); writer.Write(member.Uid); writer.Write(member.Gid);
        Text(member.UserName); Text(member.GroupName);
        ExactTime(member.Modified);
        Time(member.Accessed); Time(member.Changed);
        return stream.ToArray();
        void Text(string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes); }
        void Time(decimal? value) { writer.Write(value.HasValue); if (value is { } time) ExactTime(time); }
        void ExactTime(decimal value)
        {
            var time = ArchiveTimestamp.FromUnixSeconds(value);
            if (fine) { writer.Write(time.Seconds); writer.Write(time.Nanoseconds); }
            else writer.Write(time.ToDateTimeOffset().UtcTicks);
        }
    }
}
