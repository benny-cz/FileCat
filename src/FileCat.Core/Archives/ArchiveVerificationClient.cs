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
    private readonly ushort protocolVersion;
    internal bool UsesSecretProtocol => protocolVersion == ArchiveWorkerProtocol.SecretVersion;
    public ArchiveVerificationClient(Func<Guid, TimeSpan, IArchiveWorkerProcess> launchFreshVerifier, ArchiveWorkerQueue? queue = null,
        ushort protocolVersion = ArchiveWorkerProtocol.Version)
    {
        ArgumentNullException.ThrowIfNull(launchFreshVerifier);
        worker = new(launchFreshVerifier, queue ?? ArchiveWorkerQueue.Shared, TimeSpan.FromSeconds(30), TimeSpan.FromHours(12), verification: true, protocolVersion: protocolVersion);
        this.protocolVersion = protocolVersion;
    }

    public async Task<ArchiveVerificationResult> VerifyAsync(ArchiveObjectVersion sealedStage, ArchiveWriteManifest expected,
        Func<Stream> openPinnedStage, CancellationToken ct, ArchiveWorkerSecretGrant? password = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(sealedStage); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(openPinnedStage);
            // Each endpoint uses complete strict framing/content observations; tolerant browsing cannot verify output.
            if (expected.Profile.Format is not (ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.TarZstd or ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip or ArchiveWriteFormat.Gzip or ArchiveWriteFormat.Bzip2 or ArchiveWriteFormat.Zstd or ArchiveWriteFormat.Zip))
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This archive verification profile has not been implemented.");
            if (expected.Profile.Format == ArchiveWriteFormat.Rar5) _ = ArchiveRar5Features.Decode(expected.Features);
            else if (expected.Profile.Format == ArchiveWriteFormat.SevenZip) ArchiveSevenZipEncoding.ValidateFeatures(expected.Members, ArchiveSevenZipFeatures.Decode(expected.Features));
            else if (expected.Profile.Format == ArchiveWriteFormat.Zip) ArchiveZipFeatures.Validate(expected.Features);
            else if (ArchiveStandalone.IsStandalone(expected.Profile.Format))
            {
                ArchiveStandalone.Validate(expected.Profile, expected.Features);
                if (expected.Members is not [{ Name: ArchiveStandalone.MemberName, Type: ArchiveMemberType.File, Occurrence: 0 }])
                    throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A compressed file's expectation must describe exactly one payload.");
            }
            else ArchiveTarFeatures.Validate(expected.Profile, expected.Features);
            ct.ThrowIfCancellationRequested();
            var observed = await worker.AssessAsync(new(expected.Profile, sealedStage.Length, sealedStage.Sha256), openPinnedStage, ct, password,
                expected.Members.Count).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (observed.SourceLength != sealedStage.Length || observed.SourceSha256 != sealedStage.Sha256 ||
                observed.Manifest.Digest != expected.Digest || observed.Manifest.ContentDigest != expected.ContentDigest ||
                observed.Manifest.MetadataDigest != expected.MetadataDigest)
                throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed,
                    "The fresh archive verifier did not match the complete expected member order, content and metadata. The archive remains unpublished.");
            return new(sealedStage, observed.Manifest);
        }
        finally { password?.Dispose(); }
    }
}
