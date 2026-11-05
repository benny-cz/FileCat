using System.Collections.ObjectModel;

namespace FileCat.Core.Archives;

/// <summary>Verified unpublished native object. Metadata application, journal intent and publication are separate gates.</summary>
internal sealed class ArchiveVerifiedNativeStage
{
    internal ArchiveFileObservation Stage { get; }
    internal ArchiveVerificationResult Verification { get; }
    internal ArchiveFileObservation? Original { get; }
    internal IReadOnlyDictionary<int, ArchiveFileObservation> Inputs { get; }
    internal ArchiveVerifiedNativeStage(ArchiveFileObservation stage, ArchiveVerificationResult verification,
        ArchiveFileObservation? original, Dictionary<int, ArchiveFileObservation> inputs)
    { Stage = stage; Verification = verification; Original = original; Inputs = new ReadOnlyDictionary<int, ArchiveFileObservation>(inputs); }
}

/// <summary>
/// Unregistered preparation bridge. The caller owns native leases, artifact reservations and recovery records.
/// This runs no codec in the parent, publishes nothing and never deletes evidence on success or failure.
/// </summary>
internal static class ArchiveRetainedTarPipeline
{
    internal static async Task<ArchiveVerifiedNativeStage> RebuildAndVerifyAsync(ArchiveTarTransformPlan plan,
        ArchiveRetainedBinding? original, IReadOnlyDictionary<int, ArchiveRetainedBinding> captured,
        IArchiveRetainedStage stage, ArchiveTransformClient transform, ArchiveVerificationClient verifier, CancellationToken ct,
        Action? beforeRebuild = null, Action<ArchiveFileObservation>? beforeVerification = null, Action? beforeFinalizing = null)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(transform); ArgumentNullException.ThrowIfNull(verifier);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (captured.Count != plan.Inputs.Count || captured.Count > ArchiveWritePolicy.MaxMembers || (original is null) != (plan.Source is null)) throw Invalid();
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
                return ArchiveTransformWire.Prepare(plan, ct);
            }, ct).ConfigureAwait(false);
            Stream output = await Task.Run(() => { beforeRebuild?.Invoke(); return stage.BeginRebuild(invocation.Request.OutputLengthBound, ct); }, ct).ConfigureAwait(false);
            await transform.RebuildAsync(invocation, output,
                () => original!.File.OpenReadView(original.Reviewed, ct), id => inputs[id].File.OpenReadView(inputs[id].Reviewed, ct), ct).ConfigureAwait(false);
            if (beforeFinalizing is not null) await Task.Run(beforeFinalizing, ct).ConfigureAwait(false);
            // The worker has exited/reaped before the native write capability is irreversibly closed and flushed.
            var sealedState = await Task.Run(() => stage.SealForVerification(ct), ct).ConfigureAwait(false);
            if (beforeVerification is not null) await Task.Run(() => beforeVerification(sealedState), ct).ConfigureAwait(false);
            var verified = await verifier.VerifyAsync(sealedState.Version, invocation.Expected, () => stage.OpenReadView(sealedState, ct), ct).ConfigureAwait(false);
            await Task.Run(() =>
            {
                stage.Review(sealedState, ct);
                original?.File.Review(original.Reviewed, ct);
                foreach (var binding in inputs.Values) { ct.ThrowIfCancellationRequested(); binding.File.Review(binding.Reviewed, ct); }
            }, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new(sealedState, verified, original?.Reviewed, inputs.ToDictionary(p => p.Key, p => p.Value.Reviewed));
        }
        finally { stage.StopWrites(); }
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The retained TAR bindings do not match the frozen plan.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "A retained archive input is stale, aliased or differs from its reviewed plan.");
}
