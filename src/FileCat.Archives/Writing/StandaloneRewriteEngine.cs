using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>
/// AW7: one standalone gzip, bzip2 or Zstandard stream around exactly one payload. Assessment and fresh verification decode
/// the whole stream strictly twice (one member, stream or frame; wrapper checks; physical EOF) and the passes must agree;
/// the transform writes one canonical stream from the single captured input. The manifest has one synthetic member.
/// </summary>
internal static class StandaloneRewriteEngine
{
    internal sealed record Observation(long Length, string Sha256, byte[] Features);

    internal static ArchiveWriteManifest Assess(Stream source, ArchiveWriteProfile profile, CancellationToken ct)
    {
        var first = Read(source, profile, ct);
        var second = Read(source, profile, ct); // A decoder or wrapper that differs between passes is not admitted.
        if (first.Length != second.Length || first.Sha256 != second.Sha256 || !first.Features.AsSpan().SequenceEqual(second.Features))
            throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The compressed file decoded differently between passes.");
        return ArchiveStandalone.Manifest(profile, first.Length, first.Sha256, first.Features);
    }

    internal static Observation Read(Stream source, ArchiveWriteProfile profile, CancellationToken ct)
    {
        source.Position = 0;
        switch (profile.Format)
        {
            case ArchiveWriteFormat.Gzip:
            {
                using var gzip = new StrictGzipReadStream(source, ct);
                var (length, sha) = Hash(gzip, ct);
                return new(length, sha, ArchiveStandalone.Features(profile, gzip.OperatingSystem, gzip.ExtraFlags));
            }
            case ArchiveWriteFormat.Bzip2:
            {
                using var bzip2 = new StrictBzip2ReadStream(source, ct);
                var (length, sha) = Hash(bzip2, ct);
                return new(length, sha, ArchiveStandalone.Features(profile, bzip2BlockSize: bzip2.BlockSize));
            }
            case ArchiveWriteFormat.Zstd:
            {
                using var zstd = new StrictZstdReadStream(source, ct);
                if (!zstd.Header.Checksum)
                    throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This Zstandard file has no content checksum, which this writing profile requires.");
                var (length, sha) = Hash(zstd, ct);
                return new(length, sha, ArchiveStandalone.Features(profile, zstdChecksum: 1));
            }
            default: throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Not a standalone compressed-file profile.");
        }
    }

    /// <summary>
    /// One canonical stream from the single input: an addition of the synthetic member (new file), or exactly one replacement
    /// of the existing member (update). The retained source is checked against its reviewed revision and keeps its wrapper
    /// features (gzip operating system and flags, bzip2 block size).
    /// </summary>
    internal static ArchiveWriteManifest Rebuild(Stream? original, string? expectedPhysicalRevision, ArchiveWriteProfile profile, Stream stage,
        IReadOnlyList<ArchiveTransformEdit> edits, IReadOnlyList<ArchiveTransformAddition> additions,
        IReadOnlyList<ArchiveTransformInput> inputs, Func<int, Stream> openInput, CancellationToken ct)
    {
        if (!ArchiveStandalone.IsStandalone(profile.Format) || !stage.CanWrite || stage.CanSeek && stage.Length != 0 || ReferenceEquals(stage, original))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A compressed-file transform requires its own empty stage and explicit profile.");
        if (inputs.Count != 1) throw ArchiveStandalone.OneFileOnly();
        var input = inputs[0];
        Observation? source = null;
        if (original is not null)
        {
            if (!original.CanRead || !original.CanSeek || original.Length > ArchiveWritePolicy.MaxPhysicalBytes || expectedPhysicalRevision is null)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A compressed-file update requires its bounded retained source and revision.");
            original.Position = 0;
            if (ArchiveAssessmentWorker.PhysicalDigest(original, ct) != expectedPhysicalRevision)
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The retained compressed file differs from its reviewed physical revision.");
            source = Read(original, profile, ct);
            if (additions.Count != 0 || edits.Count != 1 || edits[0].Kind != ArchiveChangeKind.Replace ||
                edits[0].Key.RawPath != ArchiveStandalone.MemberName || edits[0].Key.Occurrence != 0 || edits[0].InputId != input.Id)
                throw ArchiveStandalone.OneFileOnly();
        }
        else if (expectedPhysicalRevision is not null || edits.Count != 0 || additions.Count != 1 || additions[0].Name != ArchiveStandalone.MemberName ||
            additions[0].Type != ArchiveMemberType.File || additions[0].InputId != input.Id)
            throw ArchiveStandalone.OneFileOnly();
        byte[] features = source?.Features ?? ArchiveStandalone.Features(profile);
        Stream encoder = profile.Format switch
        {
            ArchiveWriteFormat.Gzip => new StrictGzipWriteStream(stage, ArchiveTarFeatures.Level(features[2]), features[1]),
            ArchiveWriteFormat.Bzip2 => new StrictBzip2WriteStream(stage, features[1], ct),
            _ => new StrictZstdWriteStream(stage, profile.CompressionLevel, ct),
        };
        long length; string sha;
        try
        {
            using (var payload = openInput(input.Id))
            {
                (length, sha) = Copy(payload, encoder, ct);
                if (payload.ReadByte() != -1) throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The input grew while it was compressed.");
            }
            if (length != input.Length || !string.Equals(sha, input.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The input changed while it was compressed.");
        }
        catch
        {
            switch (encoder) { case StrictGzipWriteStream g: g.Abort(); break; case StrictBzip2WriteStream b: b.Abort(); break; case StrictZstdWriteStream z: z.Abort(); break; }
            encoder.Dispose(); throw;
        }
        encoder.Dispose(); // Completes the trailer (CRC/ISIZE, stream CRC or frame checksum).
        return ArchiveStandalone.Manifest(profile, length, sha, features);
    }

    private static (long Length, string Sha256) Hash(Stream decoded, CancellationToken ct) => Copy(decoded, Stream.Null, ct);

    private static (long Length, string Sha256) Copy(Stream from, Stream to, CancellationToken ct)
    {
        byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0; int count;
        while ((count = from.Read(buffer)) != 0)
        {
            ct.ThrowIfCancellationRequested();
            length = checked(length + count);
            if (length > ArchiveWritePolicy.MaxPayloadBytes) throw ArchiveWriteBudget.Limit();
            hash.AppendData(buffer, 0, count); to.Write(buffer, 0, count);
        }
        return (length, Convert.ToHexString(hash.GetHashAndReset()));
    }
}
