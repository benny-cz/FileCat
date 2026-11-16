using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record Bzip2TarCatalog(TarWriteCatalog Tar, byte BlockSize);
internal sealed record Bzip2TarRewriteManifest(TarRewriteManifest Tar, byte BlockSize);

internal static class Bzip2TarRewriteEngine
{
    internal static Bzip2TarCatalog Read(Stream source, CancellationToken ct)
    {
        source.Position = 0; using var decoded = new StrictBzip2ReadStream(source, ct);
        var catalog = StrictTarCatalog.Read(decoded, ct);
        return new(catalog, decoded.BlockSize);
    }
    internal static Bzip2TarRewriteManifest Rebuild(Stream? original, string? expectedPhysicalRevision, ArchiveWriteProfile profile, Stream stage,
        IReadOnlyList<TarWriteEdit> edits, IReadOnlyList<TarWriteAddition> additions,
        IReadOnlyDictionary<int, TarInputDescriptor> inputs, Func<int, Stream> openInput, CancellationToken ct)
    {
        if (profile.Format != ArchiveWriteFormat.TarBzip2 || !stage.CanWrite || stage.CanSeek && stage.Length != 0 || ReferenceEquals(stage, original) ||
            edits.Count > ArchiveWritePolicy.MaxMembers || additions.Count > ArchiveWritePolicy.MaxMembers || inputs.Count > ArchiveWritePolicy.MaxMembers)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A TAR.BZ2 transform requires its own empty stage and explicit profile.");
        Bzip2TarCatalog? source = null; var mapped = edits;
        if (original is not null)
        {
            if (!original.CanRead || !original.CanSeek || original.Length > ArchiveWritePolicy.MaxPhysicalBytes || expectedPhysicalRevision is null)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A TAR.BZ2 update requires its bounded retained source and revision.");
            original.Position = 0;
            if (ArchiveAssessmentWorker.PhysicalDigest(original, ct) != expectedPhysicalRevision)
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The retained TAR.BZ2 differs from its reviewed physical revision.");
            source = Read(original, ct);
            mapped = edits.Select(e => e.Key.CatalogRevision == expectedPhysicalRevision ? e with
            { Key = new(source.Tar.Revision, e.Key.RawPath, e.Key.Type, e.Key.Occurrence) } : throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "A TAR.BZ2 edit refers to another physical revision.")).ToArray();
        }
        else if (expectedPhysicalRevision is not null || edits.Count != 0)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A new TAR.BZ2 cannot target existing members.");
        byte block = source?.BlockSize ?? 9;
        using var replay = source is null ? null : new SeekableBzip2TarSource(original!, source.Tar.Length, block, ct);
        using var encoded = new StrictBzip2WriteStream(stage, block, ct);
        try
        {
            var manifest = TarRewriteEngine.Rebuild(replay, source?.Tar.Revision, encoded, mapped, additions, inputs, openInput, ct, source?.Tar);
            replay?.Complete(); return new(manifest, block);
        }
        catch { encoded.Abort(); throw; }
    }
}
