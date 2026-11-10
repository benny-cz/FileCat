namespace FileCat.Core.Archives;

/// <summary>A retained read-only target. This role has no stage, reservation, publication or deletion methods.</summary>
internal interface IArchiveTargetReadLease : IDisposable
{
    IArchiveRetainedFile File { get; }
}

/// <summary>Command-time evidence, held only while preparing a command. No password or native handle is retained.</summary>
internal sealed class ArchiveTargetReview
{
    internal string Path { get; }
    internal ArchiveWriteProfile Profile { get; }
    internal ArchiveFileObservation Native { get; }
    internal ArchiveBaseline Baseline { get; }
    internal ArchiveAssessment Assessment { get; }

    internal ArchiveTargetReview(string path, ArchiveWriteProfile profile, ArchiveFileObservation native, ArchiveAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(native); ArgumentNullException.ThrowIfNull(assessment);
        if (!System.IO.Path.IsPathFullyQualified(path) || profile != assessment.Manifest.Profile ||
            native.Version.Length != assessment.SourceLength || native.Version.Sha256 != assessment.SourceSha256 ||
            native.LeafName != System.IO.Path.GetFileName(path) || native.MemberModified is not { } modified)
            throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The archive review does not describe the requested native target.");
        ArchiveWritePolicy.ValidateDigest(assessment.CatalogRevision);
        Path = System.IO.Path.GetFullPath(path); Profile = profile; Native = native; Assessment = assessment;
        // The legacy baseline has tick precision; the accompanying native observation retains exact nanoseconds.
        Baseline = new(native.Version.Length, DateTimeOffset.FromUnixTimeSeconds(modified.Seconds).AddTicks(modified.Nanoseconds / 100).UtcTicks,
            native.Version.Sha256);
    }

    internal ArchivePlan CreatePlan(IReadOnlyList<ArchiveChange> changes, bool replaceExisting = false, bool moveSources = false)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count > ArchiveWritePolicy.MaxMembers)
            throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "Too many changes to the reviewed archive.");
        var plan = new ArchivePlan(Path, Baseline, Array.AsReadOnly(changes.ToArray()), ReplaceExisting: replaceExisting, Profile: Profile)
        { NativeBaseline = Native, ReviewedManifestDigest = Assessment.Manifest.Digest, MoveSources = moveSources };
        _ = new ArchiveJobPlanning(plan, CancellationToken.None); // Validate and bound the frozen public intent before queueing.
        return plan;
    }

    internal ArchivePlan ReviewMemberChanges(IReadOnlyList<ArchiveChange> changes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var plan = CreatePlan(changes);
        if (plan.Changes.Any(change => change.Kind is not (ArchiveChangeKind.Delete or ArchiveChangeKind.Rename or ArchiveChangeKind.CreateFolderEntry)))
            throw new ArgumentException("Member-only review cannot capture local inputs.", nameof(changes));
        if (ArchiveStandalone.IsStandalone(Profile.Format))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, ArchiveStandalone.MemberChangesUnavailable);
        using var empty = new ArchiveCapturedInputs(); empty.Seal(ct);
        _ = new ArchiveJobPlanning(plan, ct).Review(Assessment, null, empty, ct).CreatePlan(false, false, ct);
        return plan;
    }
}

/// <summary>Complete confined assessment over one pinned native source, followed by a full native recheck.</summary>
internal sealed class ArchiveTargetReviewer(Func<string, CancellationToken, IArchiveTargetReadLease> open, ArchiveAssessmentClient assessor)
{
    /// <summary>Consumes retained reads inside the callback; returned values must not borrow this native lease.</summary>
    internal T ReadRetained<T>(ArchiveTargetReview review, Func<IArchiveRetainedFile, T> read, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(review); ArgumentNullException.ThrowIfNull(read);
        ct.ThrowIfCancellationRequested();
        using var lease = open(review.Path, ct);
        lease.File.Review(review.Native, ct);
        ct.ThrowIfCancellationRequested();
        return read(lease.File);
    }

    internal async Task<ArchiveTargetReview> ReviewAsync(string path, ArchiveWriteProfile profile, ArchiveJobPassword? password, CancellationToken ct,
        bool preserveEncryption = false, string? expectedSourceSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute archive target is required.", nameof(path));
        ArchiveJobPassword.Require(profile, password); ct.ThrowIfCancellationRequested();
        if (expectedSourceSha256 is not null)
        {
            if (expectedSourceSha256.Length != 64) ArchiveWritePolicy.ValidateDigest(expectedSourceSha256);
            expectedSourceSha256 = expectedSourceSha256.ToUpperInvariant();
            ArchiveWritePolicy.ValidateDigest(expectedSourceSha256);
        }
        path = ArchiveLocalPath.ResolveParent(path); // Unix native leases pin link-free folders; the archive name itself is not followed.
        using var lease = open(path, ct);
        var observed = lease.File.Observe(ct);
        if (expectedSourceSha256 is not null && !string.Equals(observed.Version.Sha256, expectedSourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The archive changed since these members were listed. Refresh it and select them again.");
        ArchiveAssessment result;
        try
        {
            result = await assessor.AssessAsync(new(profile, observed.Version.Length, observed.Version.Sha256),
                () => lease.File.OpenReadView(observed, ct), ct, password?.CreateGrant()).ConfigureAwait(false);
        }
        catch (ArchiveWriteException error) when (preserveEncryption && (profile.Format is ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip) &&
            ProtectionMismatch(error.Reason) is { } actual)
        {
            if (actual == profile.Encryption.Mode) throw new ArchiveWriteException(ArchiveWriteReason.ProtocolViolation, "The worker reported a protection mismatch without a different mode.");
            if (actual != ArchiveEncryptionMode.None && password is null)
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "Enter the archive's existing password to review this update.");
            // At most one retry over the same retained target. Only a complete second assessment approves
            // the observed mode; a refusal alone never supplies a catalog or publication authority.
            var encryption = actual == ArchiveEncryptionMode.None ? ArchiveEncryptionOptions.None : new(actual, profile.Encryption.CountPower == 0 ? 19 : profile.Encryption.CountPower);
            profile = new(profile.Format, profile.Version, profile.CompressionLevel, profile.DictionaryBytes, profile.SevenZip, profile.Rar5, encryption);
            result = await assessor.AssessAsync(new(profile, observed.Version.Length, observed.Version.Sha256),
                () => lease.File.OpenReadView(observed, ct), ct, actual == ArchiveEncryptionMode.None ? null : password?.CreateGrant()).ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        lease.File.Review(observed, ct);
        ct.ThrowIfCancellationRequested();
        var reviewed = new ArchiveTargetReview(path, profile, observed, result);
        // Reading a catalog alone does not prove that the admitted profile can preserve it. A compressed file has no
        // change-free plan (its one update replaces the payload), so its wrapper and single payload are admitted directly.
        if (ArchiveStandalone.IsStandalone(profile.Format)) _ = ArchiveStandaloneTransformPlan.AdmitSource(profile, result);
        else
        {
            using var empty = new ArchiveCapturedInputs(); empty.Seal(ct);
            _ = new ArchiveJobPlanning(reviewed.CreatePlan([]), ct).Review(result, null, empty, ct).CreatePlan(false, false, ct);
        }
        ct.ThrowIfCancellationRequested();
        return reviewed;
    }

    private static ArchiveEncryptionMode? ProtectionMismatch(ArchiveWriteReason reason) => reason switch
    {
        ArchiveWriteReason.ProtectionModeNone => ArchiveEncryptionMode.None,
        ArchiveWriteReason.ProtectionModeData => ArchiveEncryptionMode.Data,
        ArchiveWriteReason.ProtectionModeDataAndNames => ArchiveEncryptionMode.DataAndNames,
        _ => null,
    };
}
