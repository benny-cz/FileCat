using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    internal sealed record IdentitySearch(AnalysisComparisonAccountingEntity Current, AnalysisIndexRange Candidates);
    private static async Task BuildIdentityLinksAsync(State state, ulong count, DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        ulong next = 1;
        while (next <= count)
        {
            var search = await AnalysisIo.Run(io, devices, token =>
            {
                int records = 0; long bytes = 0;
                while (next <= count && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested(); var candidate = state.FindIdentitySearch(next++, token, out long cost); bytes += cost;
                    if (candidate is not null) return candidate;
                }
                return null;
            }, cancellation).ConfigureAwait(false);
            if (search is null) continue;
            ulong matched = 0; bool ambiguous = false;
            for (ulong at = 0; at < search.Candidates.Count; at++)
            {
                ulong offset = at;
                var before = await AnalysisIo.Run(io, devices, token => state.IdentityCandidate(search, offset, token), cancellation).ConfigureAwait(false);
                if (before is null || !await SameMetadataAsync(state.Previous, before.Id, state.Current, search.Current.Id, io, devices, cancellation).ConfigureAwait(false)) continue;
                if (matched != 0) { ambiguous = true; break; } matched = before.Id;
            }
            if (matched != 0 && !ambiguous)
                await AnalysisIo.Run(io, devices, token => { state.AddFallback(matched, search.Current.Id, token); return true; }, cancellation).ConfigureAwait(false);
        }
    }
    internal sealed record MatchPlan(AnalysisComparisonAccountingEntity? Previous, AnalysisComparisonAccountingEntity? Current, bool Ambiguous);
    private static async Task BuildRowsAsync(State state, ulong previousCount, ulong currentCount, AnalysisMetric metric,
        DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        ulong now = 1, old = 1;
        while (now <= currentCount || old <= previousCount)
        {
            var pair = await AnalysisIo.Run(io, devices, token =>
            {
                int records = 0; long bytes = 0;
                while ((now <= currentCount || old <= previousCount) && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested(); MatchPlan? plan; long cost;
                    if (now <= currentCount) plan = state.CurrentPlan(now++, token, out cost);
                    else plan = state.PreviousPlan(old++, token, out cost);
                    bytes += cost;
                    if (plan is null) continue;
                    if (plan.Previous is not null && plan.Current is not null) return plan;
                    state.Emit(plan, metric, false, false, token);
                }
                return null;
            }, cancellation).ConfigureAwait(false);
            if (pair is null) continue;
            bool metadata = await SameMetadataAsync(state.Previous, pair.Previous!.Id, state.Current, pair.Current!.Id, io, devices, cancellation).ConfigureAwait(false);
            bool samePaths = pair.Previous.ObservedPaths == pair.Current.ObservedPaths;
            ulong member = 0;
            while (samePaths && member < pair.Current.ObservedPaths)
                samePaths = await AnalysisIo.Run(io, devices, token =>
                {
                    int records = 0;
                    while (member < pair.Current.ObservedPaths && records++ < AnalysisLimits.BatchRecords)
                    { token.ThrowIfCancellationRequested(); if (!state.SameMember(pair.Previous.Id, pair.Current.Id, member++, token)) return false; }
                    return true;
                }, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(io, devices, token => { state.Emit(pair, metric, metadata, samePaths, token); return true; }, cancellation).ConfigureAwait(false);
        }
    }
    internal sealed partial class State
    {
        internal AnalysisComparisonEntities.State Previous => previous;
        internal AnalysisComparisonEntities.State Current => current;
        internal IdentitySearch? FindIdentitySearch(ulong entity, CancellationToken token, out long cost)
        {
            var candidate = current.ReadEvidence(entity, token, out cost);
            if (!candidate.Fresh || !Mapped(candidate, false) || PathEdges!.PrefixRange(AnalysisGraphKeys.PathId(entity), token).Count != 0) return null;
            byte[]? key = IdentityKey(candidate);
            if (key is null || NewIdentities!.FindRange(key, token).Count != 1) return null;
            var range = OldIdentities!.FindRange(key, token); return range.Count == 0 ? null : new(candidate, range);
        }
        internal AnalysisComparisonAccountingEntity? IdentityCandidate(IdentitySearch search, ulong offset, CancellationToken token)
        {
            var candidate = previous.ReadEvidence(OldIdentities!.Read(search.Candidates.First + offset, token).Value, token, out _);
            return candidate.Fresh && Mapped(candidate, true) ? candidate : null;
        }
        internal MatchPlan CurrentPlan(ulong entity, CancellationToken token, out long cost)
        {
            var b = current.ReadEvidence(entity, token, out cost); var range = Edges!.PrefixRange(AnalysisGraphKeys.PathId(entity), token);
            if (range.Count == 0) return new(null, b, false);
            if (range.Count != 1) return new(null, b, true);
            ulong oldId = Second(Edges.Read(range.First, token).Key);
            if (Incoming!.PrefixRange(AnalysisGraphKeys.PathId(oldId), token).Count != 1) return new(null, b, true);
            var a = previous.ReadEvidence(oldId, token, out long extra); cost += extra; return new(a, b, false);
        }
        internal MatchPlan? PreviousPlan(ulong entity, CancellationToken token, out long cost)
        {
            var range = Incoming!.PrefixRange(AnalysisGraphKeys.PathId(entity), token); cost = 64;
            if (range.Count == 1)
            {
                ulong now = Second(Incoming.Read(range.First, token).Key);
                if (Edges!.PrefixRange(AnalysisGraphKeys.PathId(now), token).Count == 1) return null;
            }
            var a = previous.ReadEvidence(entity, token, out cost); return new(a, null, range.Count != 0);
        }
        internal bool SameMember(ulong oldEntity, ulong newEntity, ulong offset, CancellationToken token)
        {
            ulong before = paths.PreviousRow(current.MemberPathRow(newEntity, offset, token), token);
            return before != 0 && previous.EntityForPathRow(before, token) == oldEntity;
        }
        private bool SameAttribution(AnalysisComparisonAccountingEntity a, AnalysisComparisonAccountingEntity b, CancellationToken token)
        {
            if (a.AttributionParent is not { } old || b.AttributionParent is not { } now) return a.AttributionParent == b.AttributionParent;
            if (old == 0 || now == 0) return old == now;
            return paths.PreviousRow(current.Graph.PathRow(now, token), token) == previous.Graph.PathRow(old, token);
        }
        internal void Emit(MatchPlan plan, AnalysisMetric metric, bool sameMetadata, bool samePaths, CancellationToken token)
        {
            var a = plan.Previous; var b = plan.Current;
            var evidence = structural ? AnalysisComparisonEvidence.Structural : Compatibility.VerifiedIdentityContext && a?.ObservedIdentity is { } identity &&
                identity == b?.ObservedIdentity && a.VersionEvidence is { } version && version == b.VersionEvidence && a.Fresh && b.Fresh && sameMetadata
                    ? AnalysisComparisonEvidence.CompatibleIdentityAndVersion : AnalysisComparisonEvidence.PathBased;
            AnalysisChange change; AnalysisDelta? delta = null; byte reason;
            if (plan.Ambiguous) { change = AnalysisChange.Unknown; reason = b is null ? (byte)3 : (byte)2; }
            else if (a is null || b is null)
            {
                var entity = b ?? a!; bool complete = b is null ? Compatibility.CurrentComplete : Compatibility.PreviousComplete;
                bool known = complete && Compatibility.ComparableValues && Mapped(entity, b is null) && entity.Fresh && entity.Value.IsKnown;
                change = known ? b is null ? AnalysisChange.Removed : AnalysisChange.Added : b is null ? AnalysisChange.Unknown : AnalysisChange.PreviouslyUnknown;
                reason = known ? b is null ? (byte)6 : (byte)5 : (byte)4;
                if (known) delta = AnalysisDelta.Between(a?.Value.Value ?? 0, b?.Value.Value ?? 0);
            }
            else if (!Compatibility.ComparableValues || !Mapped(a, true) || !Mapped(b, false) || !a.Fresh || !b.Fresh || !a.Value.IsKnown || !b.Value.IsKnown)
            { change = AnalysisChange.Unknown; reason = 1; }
            else
            {
                delta = AnalysisDelta.Between(a.Value.Value!.Value, b.Value.Value!.Value); reason = 0;
                bool sameIdentity = a.ObservedIdentity is not null && a.ObservedIdentity == b.ObservedIdentity;
                if (a.ObservedIdentity is not null && b.ObservedIdentity is not null && !sameIdentity) change = AnalysisChange.Replaced;
                else if (delta.Value.Magnitude != 0) change = delta.Value.Negative ? AnalysisChange.Shrinking : AnalysisChange.Growing;
                else if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk && sameIdentity && (!samePaths || !SameAttribution(a, b, token))) change = AnalysisChange.Attribution;
                else if (!samePaths && evidence == AnalysisComparisonEvidence.CompatibleIdentityAndVersion) change = AnalysisChange.Moved;
                else if (!samePaths || a.VersionEvidence != b.VersionEvidence || a.ModifiedUtc != b.ModifiedUtc || !sameMetadata) change = AnalysisChange.MetadataChanged;
                else change = AnalysisChange.NoObservedMetadataChange;
            }
            _rowWriter!.Append(EncodeRow(new(a?.Id ?? 0, b?.Id ?? 0, change, evidence, delta, reason)), token);
            HasCurrentRows |= b is not null; HasPreviousOnlyRows |= b is null;
        }
    }
}
