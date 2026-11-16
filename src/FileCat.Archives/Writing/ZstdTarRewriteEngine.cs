using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record ZstdTarCatalog(TarWriteCatalog Tar, ZstdFrameHeader Header);

internal static class ZstdTarRewriteEngine
{
    internal static ZstdTarCatalog Read(Stream source, CancellationToken ct)
    {
        source.Position = 0; using var decoded = new StrictZstdReadStream(source, ct);
        var catalog = StrictTarCatalog.Read(decoded, ct); return new(catalog, decoded.Header);
    }
    internal static TarRewriteManifest Rebuild(Stream? original, string? expectedPhysicalRevision, ArchiveWriteProfile profile, Stream stage,
        IReadOnlyList<TarWriteEdit> edits, IReadOnlyList<TarWriteAddition> additions,
        IReadOnlyDictionary<int, TarInputDescriptor> inputs, Func<int, Stream> openInput, CancellationToken ct)
    {
        if (profile.Format != ArchiveWriteFormat.TarZstd || !stage.CanWrite || stage.CanSeek && stage.Length != 0 || ReferenceEquals(stage, original) ||
            edits.Count > ArchiveWritePolicy.MaxMembers || additions.Count > ArchiveWritePolicy.MaxMembers || inputs.Count > ArchiveWritePolicy.MaxMembers)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A TAR.ZST transform requires its own empty stage and explicit profile.");
        ZstdTarCatalog? source = null; var mapped = edits;
        if (original is not null)
        {
            if (!original.CanRead || !original.CanSeek || original.Length > ArchiveWritePolicy.MaxPhysicalBytes || expectedPhysicalRevision is null)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A TAR.ZST update requires its bounded retained source and revision.");
            original.Position = 0;
            if (ArchiveAssessmentWorker.PhysicalDigest(original, ct) != expectedPhysicalRevision)
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The retained TAR.ZST differs from its reviewed physical revision.");
            source = Read(original, ct);
            mapped = edits.Select(e => e.Key.CatalogRevision == expectedPhysicalRevision ? e with
            { Key = new(source.Tar.Revision, e.Key.RawPath, e.Key.Type, e.Key.Occurrence) } : throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "A TAR.ZST edit refers to another physical revision.")).ToArray();
        }
        else if (expectedPhysicalRevision is not null || edits.Count != 0)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A new TAR.ZST cannot target existing members.");
        using var replay = source is null ? null : new SeekableZstdTarSource(original!, source.Tar.Length, source.Header, ct);
        using var encoded = new StrictZstdWriteStream(stage, profile.CompressionLevel, ct);
        try
        {
            var manifest = TarRewriteEngine.Rebuild(replay, source?.Tar.Revision, encoded, mapped, additions, inputs, openInput, ct, source?.Tar);
            replay?.Complete(); return manifest;
        }
        catch { encoded.Abort(); throw; }
    }
}
