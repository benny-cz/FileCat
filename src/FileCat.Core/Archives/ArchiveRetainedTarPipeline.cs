using System.Collections.ObjectModel;

namespace FileCat.Core.Archives;

/// <summary>Verified unpublished native object. Metadata application, journal intent and publication are separate gates.</summary>
internal sealed class ArchiveVerifiedNativeStage
{
    internal ArchiveFileObservation Stage { get; }
    internal ArchiveVerificationResult Verification { get; }
    internal ArchiveFileObservation? Original { get; }
    internal IReadOnlyDictionary<int, ArchiveFileObservation> Inputs { get; }
    internal IReadOnlyDictionary<int, ArchiveCapturedDirectory> Directories { get; }
    internal ArchiveVerifiedNativeStage(ArchiveFileObservation stage, ArchiveVerificationResult verification,
        ArchiveFileObservation? original, Dictionary<int, ArchiveFileObservation> inputs,
        IReadOnlyDictionary<int, ArchiveCapturedDirectory>? directories = null)
    { Stage = stage; Verification = verification; Original = original; Inputs = new ReadOnlyDictionary<int, ArchiveFileObservation>(inputs);
        Directories = directories ?? ArchiveTarTransformPlan.NoDirectories; }
}

/// <summary>
/// Unregistered preparation bridge. The caller owns native leases, artifact reservations and recovery records.
/// This runs no codec in the parent, publishes nothing and never deletes evidence on success or failure.
/// </summary>
internal static class ArchiveRetainedContainerPipeline
{
    internal static async Task<ArchiveVerifiedNativeStage> RebuildAndVerifyAsync(ArchiveContainerTransformPlan plan,
        ArchiveRetainedBinding? original, IReadOnlyDictionary<int, ArchiveRetainedBinding> captured,
        IArchiveRetainedStage stage, ArchiveTransformClient transform, ArchiveVerificationClient verifier, CancellationToken ct,
        Action? beforeRebuild = null, Action<ArchiveFileObservation>? beforeVerification = null, Action? beforeFinalizing = null,
        Func<ArchiveFileObservation, ArchiveFileObservation>? finalizeMetadata = null, ArchiveJobPassword? password = null,
        Action<string>? reading = null)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(transform); ArgumentNullException.ThrowIfNull(verifier);
        try
        {
            ct.ThrowIfCancellationRequested();
            RequirePassword(plan, transform, verifier, password);
            if (captured.Count != plan.Inputs.Count || captured.Count > ArchiveWritePolicy.MaxMembers || (original is null) != (plan.Source is null)) throw Invalid();
            ArchiveCapturedInputs.RequirePlanCost(plan, captured);
            var inputs = captured.ToDictionary();
            var invocation = await Task.Run(() =>
            {
                var identities = new HashSet<string>(StringComparer.Ordinal) { stage.ObjectIdentity };
                if (original is not null)
                {
                    if (!identities.Add(original.Reviewed.Version.Identity) || original.Reviewed.Version.Length != plan.Source!.SourceLength ||
                        original.Reviewed.Version.Sha256 != plan.Source.SourceSha256) throw Changed();
                    original.File.Review(original.Reviewed, ct);
                }
                foreach (var (id, binding) in inputs)
                {
                    ct.ThrowIfCancellationRequested();
                    if (binding is null || !plan.Inputs.TryGetValue(id, out var expected) || !identities.Add(binding.Reviewed.Version.Identity)) throw Changed();
                    if (binding.Reviewed.Input(id) != expected) throw Changed();
                    binding.File.Review(binding.Reviewed, ct);
                }
                foreach (var directory in plan.Directories.Values)
                {
                    ct.ThrowIfCancellationRequested(); if (!identities.Add(directory.Reviewed.Version.Identity)) throw Changed();
                    directory.Review(ct);
                }
                return ArchiveTransformWire.Prepare(plan, ct);
            }, ct).ConfigureAwait(false);
            Stream output = await Task.Run(() => { beforeRebuild?.Invoke(); return stage.BeginRebuild(invocation.Request.OutputLengthBound, ct); }, ct).ConfigureAwait(false);
            await transform.RebuildAsync(invocation, output,
                () => original!.File.OpenReadView(original.Reviewed, ct),
                id => { reading?.Invoke(inputs[id].Reviewed.LeafName); return inputs[id].File.OpenReadView(inputs[id].Reviewed, ct); }, ct, password?.CreateGrant()).ConfigureAwait(false);
            if (beforeFinalizing is not null) await Task.Run(beforeFinalizing, ct).ConfigureAwait(false);
            // The worker has exited/reaped before the native write capability is irreversibly closed and flushed.
            var sealedState = await Task.Run(() => stage.SealForVerification(ct), ct).ConfigureAwait(false);
            if (finalizeMetadata is not null)
            {
                var finalized = await Task.Run(() => finalizeMetadata(sealedState), ct).ConfigureAwait(false);
                if (finalized.Version != sealedState.Version || finalized.ParentIdentity != sealedState.ParentIdentity || finalized.LeafName != sealedState.LeafName ||
                    finalized.MemberModified != sealedState.MemberModified || finalized.MemberMode != sealedState.MemberMode) throw Changed();
                sealedState = finalized;
            }
            if (beforeVerification is not null) await Task.Run(() => beforeVerification(sealedState), ct).ConfigureAwait(false);
            var verified = await verifier.VerifyAsync(sealedState.Version, invocation.Expected, () => stage.OpenReadView(sealedState, ct), ct, password?.CreateGrant()).ConfigureAwait(false);
            await Task.Run(() =>
            {
                stage.Review(sealedState, ct);
                original?.File.Review(original.Reviewed, ct);
                foreach (var binding in inputs.Values) { ct.ThrowIfCancellationRequested(); binding.File.Review(binding.Reviewed, ct); }
                foreach (var directory in plan.Directories.Values) directory.Review(ct);
            }, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new(sealedState, verified, original?.Reviewed, inputs.ToDictionary(p => p.Key, p => p.Value.Reviewed), plan.Directories);
        }
        finally { stage.StopWrites(); }
    }
    internal static void RequirePassword(ArchiveContainerTransformPlan plan, ArchiveTransformClient transform,
        ArchiveVerificationClient verifier, ArchiveJobPassword? password)
    {
        ArchiveJobPassword.Require(plan.Expected.Profile, password);
        if (password is not null && (!transform.UsesSecretProtocol || !verifier.UsesSecretProtocol))
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "Password-bearing archive preparation requires fixed protocol-2 worker factories.");
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The retained container bindings do not match the frozen plan.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "A retained archive input is stale, aliased or differs from its reviewed plan.");
}
