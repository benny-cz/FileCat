using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record GzipTarCatalog(TarWriteCatalog Tar, byte OperatingSystem, byte ExtraFlags);
internal sealed record GzipTarRewriteManifest(TarRewriteManifest Tar, byte OperatingSystem, byte ExtraFlags);

internal static class GzipTarRewriteEngine
{
    internal static GzipTarCatalog Read(Stream source, CancellationToken ct)
    {
        source.Position = 0; using var gzip = new StrictGzipReadStream(source, ct);
        var catalog = StrictTarCatalog.Read(gzip, ct); // Complete wrapper CRC/ISIZE and physical EOF are mandatory.
        return new(catalog, gzip.OperatingSystem, gzip.ExtraFlags);
    }
    internal static GzipTarRewriteManifest Rebuild(Stream? original, string? expectedPhysicalRevision, ArchiveWriteProfile profile, Stream stage,
        IReadOnlyList<TarWriteEdit> edits, IReadOnlyList<TarWriteAddition> additions,
        IReadOnlyDictionary<int, TarInputDescriptor> inputs, Func<int, Stream> openInput, CancellationToken ct)
    {
        if (profile.Format != ArchiveWriteFormat.TarGzip || !stage.CanWrite || stage.CanSeek && stage.Length != 0 || ReferenceEquals(stage, original) ||
            edits.Count > ArchiveWritePolicy.MaxMembers || additions.Count > ArchiveWritePolicy.MaxMembers || inputs.Count > ArchiveWritePolicy.MaxMembers)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A TAR.GZ transform requires its own empty stage and explicit profile.");
        GzipTarCatalog? source = null; var mapped = edits;
        if (original is not null)
        {
            if (!original.CanRead || !original.CanSeek || original.Length > ArchiveWritePolicy.MaxPhysicalBytes || expectedPhysicalRevision is null)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A TAR.GZ update requires its bounded retained source and revision.");
            original.Position = 0;
            if (ArchiveAssessmentWorker.PhysicalDigest(original, ct) != expectedPhysicalRevision)
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The retained TAR.GZ differs from its reviewed physical revision.");
            source = Read(original, ct);
            mapped = edits.Select(e => e.Key.CatalogRevision == expectedPhysicalRevision ? e with
            { Key = new(source.Tar.Revision, e.Key.RawPath, e.Key.Type, e.Key.Occurrence) } : throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "A TAR.GZ edit refers to a different physical revision.")).ToArray();
        }
        else if (expectedPhysicalRevision is not null || edits.Count != 0)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A new TAR.GZ cannot target existing members.");
        byte os = source?.OperatingSystem ?? 255, flags = source?.ExtraFlags ?? ArchiveTarFeatures.Flags(profile.CompressionLevel);
        ArchiveTarFeatures.Create(profile, source?.Tar.Format == System.Formats.Tar.TarEntryFormat.Ustar ? (byte)1 : (byte)2, os, flags);
        using var replay = source is null ? null : new SeekableGzipTarSource(original!, source.Tar.Length, os, flags, ct);
        using var gzip = new StrictGzipWriteStream(stage, ArchiveTarFeatures.Level(flags), os);
        try
        {
            var manifest = TarRewriteEngine.Rebuild(replay, source?.Tar.Revision, gzip, mapped, additions, inputs, openInput, ct);
            replay?.Complete(); return new(manifest, os, flags);
        }
        catch { gzip.Abort(); throw; }
    }
}
