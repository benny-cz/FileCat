namespace FileCat.Core.Archives;

/// <summary>Parent-bound verification evidence, never pathname/publication authority or a substitute for native rereview.</summary>
public sealed class ArchiveVerificationResult
{
    public ArchiveObjectVersion Stage { get; }
    public string ManifestDigest { get; }
    public string ContentDigest { get; }
    public string MetadataDigest { get; }
    internal ArchiveWriteManifest ObservedManifest { get; }
    internal ArchiveVerificationResult(ArchiveObjectVersion stage, ArchiveWriteManifest manifest)
    { Stage = stage; ManifestDigest = manifest.Digest; ContentDigest = manifest.ContentDigest; MetadataDigest = manifest.MetadataDigest; ObservedManifest = manifest; }
}

/// <summary>
/// Launches a fresh role-bound read-only worker over the sealed parent's stage grant. The worker never receives the
/// expected manifest or chooses the stage identity. Acceptance requires a complete independent catalog, exact match,
/// terminal EOF, successful exit, final resource/diagnostic checks and actual reaping. No in-process codec fallback.
/// The caller retains native identity/content/metadata leases and must rereview them before publication.
/// </summary>
public sealed class ArchiveVerificationClient
{
    private readonly ArchiveAssessmentClient worker;
    public ArchiveVerificationClient(Func<Guid, TimeSpan, IArchiveWorkerProcess> launchFreshVerifier, ArchiveWorkerQueue? queue = null)
    {
        ArgumentNullException.ThrowIfNull(launchFreshVerifier);
        worker = new(launchFreshVerifier, queue ?? ArchiveWorkerQueue.Shared, TimeSpan.FromSeconds(30), TimeSpan.FromHours(12), verification: true);
    }

    public async Task<ArchiveVerificationResult> VerifyAsync(ArchiveObjectVersion sealedStage, ArchiveWriteManifest expected,
        Func<Stream> openPinnedStage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sealedStage); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(openPinnedStage);
        // The decoder endpoint handles TAR and the admitted neutral gzip wrapper. Format expansion needs complete
        // framing/metadata evidence and cannot silently route through the browsing/tolerant reader.
        if (expected.Profile.Format is not (ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.TarZstd))
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This archive verification profile has not been implemented.");
        ArchiveTarFeatures.Validate(expected.Profile, expected.Features);
        ct.ThrowIfCancellationRequested();
        var observed = await worker.AssessAsync(new(expected.Profile, sealedStage.Length, sealedStage.Sha256), openPinnedStage, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (observed.SourceLength != sealedStage.Length || observed.SourceSha256 != sealedStage.Sha256 ||
            observed.Manifest.Digest != expected.Digest || observed.Manifest.ContentDigest != expected.ContentDigest ||
            observed.Manifest.MetadataDigest != expected.MetadataDigest)
            throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed,
                "The fresh archive verifier did not match the complete expected member order, content and metadata. The archive remains unpublished.");
        return new(sealedStage, observed.Manifest);
    }
}
