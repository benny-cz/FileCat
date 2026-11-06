using System.Collections.Immutable;
using System.Text.Json;

namespace FileCat.Core.Analysis;

public static partial class AnalysisInventoryFormat
{
    private static void WriteManifest(Utf8JsonWriter w, AnalysisSnapshot snapshot, Guid inventoryId, Func<string, string> rootId)
    {
        w.WriteString("schema", "1"); w.WriteString("inventory", inventoryId.ToString("D")); w.WriteString("producer", Producer);
        w.WriteString("session", snapshot.Identity.SessionId.ToString("D")); Number(w, "epoch", (ulong)snapshot.Identity.Epoch);
        Number(w, "revision", (ulong)snapshot.Identity.Revision); w.WriteString("hostProfile", snapshot.Identity.HostProfile);
        Date(w, "started", snapshot.Identity.StartedUtc); Date(w, "observed", snapshot.Identity.ObservedUtc);
        w.WriteString("timeZone", snapshot.Identity.TimeZoneId);
        w.WriteStartArray("roots");
        foreach (var r in snapshot.Roots)
        {
            w.WriteStartArray(); w.WriteStringValue(rootId(r.Id)); w.WriteStringValue(r.Scheme);
            TaggedText(w, r.DisplayLocator); TaggedText(w, r.CanonicalLocator); w.WriteStringValue(r.SourceIdentity);
            w.WriteStringValue(r.Assurance.ToString()); TaggedText(w, r.Label); w.WriteStringValue(r.Kind.ToString());
            w.WriteStringValue(r.NameRules.ToString()); w.WriteStringValue(r.TraversalBoundary); w.WriteStringValue(r.ConnectionIdentityReference);
            w.WriteStringValue(r.Disposition.ToString()); w.WriteStringValue(r.CoveredByRootId is { } parent ? rootId(parent) : null);
            if (r.CoveredRelativePath is { } path) TaggedText(w, path); else w.WriteNullValue();
            w.WriteStringValue(r.ObservedObjectIdentity); w.WriteEndArray();
        }
        w.WriteEndArray(); w.WritePropertyName("policy"); w.WriteRawValue(snapshot.Policy.CanonicalBytes());
        w.WriteString("policyFingerprint", snapshot.Policy.Fingerprint);
        w.WriteString("historicalPhase", snapshot.Phase.ToString()); w.WriteString("coverage", snapshot.Coverage.ToString());
        w.WriteString("freshness", snapshot.Freshness.ToString()); Number(w, "historicalTaint", (ulong)snapshot.Taint);
    }

    private static void WriteObject(Utf8JsonWriter w, AnalysisObject o, string objectId)
    {
        w.WriteString("id", objectId); w.WriteString("identity", o.VerifiedIdentity); Measurement(w, "length", o.LogicalLength);
        Measurement(w, "links", o.LinkCount); Date(w, "modified", o.ModifiedUtc); w.WriteString("version", o.VersionEvidence);
        Number(w, "precision", o.ModifiedPrecisionTicks);
    }

    private static void WritePath(Utf8JsonWriter w, AnalysisNode n, Func<string, string> rootId, Func<string, string> objectId)
    {
        Number(w, "id", n.Id); w.WriteString("root", rootId(n.RootId)); Number(w, "parent", n.ParentId);
        w.WriteString("encoding", n.NameEncoding);
        w.WriteBase64String("raw", n.RawName.IsEmpty ? AnalysisRawNames.Encode(n.NameEncoding, n.Name) : n.RawName.AsSpan());
        w.WriteString("entryKind", n.Kind.ToString()); w.WriteString("object", n.ObjectId is { } id ? objectId(id) : null);
        NullableBoolean(w, "sparse", n.Sparse); NullableBoolean(w, "compressed", n.Compressed); NullableBoolean(w, "cloud", n.Cloud);
        w.WriteString("metadata", n.MetadataState.ToString()); w.WriteBoolean("included", n.IncludedByPolicy);
        w.WriteString("identity", n.ObservedIdentity);
        w.WritePropertyName("times");
        if (n.Times is not { } t) w.WriteNullValue();
        else
        {
            w.WriteStartArray(); DateValue(w, t.ObservedUtc); DateValue(w, t.ModifiedUtc); w.WriteStringValue(Decimal(t.ModifiedPrecisionTicks));
            DateValue(w, t.CreatedUtc); NullableNumber(w, t.CreatedPrecisionTicks); DateValue(w, t.ChangedUtc); NullableNumber(w, t.ChangedPrecisionTicks); w.WriteEndArray();
        }
    }

    private static void WriteFact(Utf8JsonWriter w, StorageFact f, ulong factId, Func<string, string> objectId)
    {
        Number(w, "id", factId); w.WriteString("object", f.ObjectId is { } id ? objectId(id) : null);
        if (f.DirectoryId is { } directory) Number(w, "directory", directory); else w.WriteNull("directory");
        w.WriteString("component", f.ComponentId); w.WriteString("basis", f.BasisId); Measurement(w, "measurement", f.Measurement);
        w.WriteString("method", f.Method); Date(w, "observed", f.ObservedUtc);
        w.WriteStartArray("includes"); foreach (string component in f.Includes) w.WriteStringValue(component); w.WriteEndArray();
        w.WriteBoolean("mayShare", f.MayShareExtents);
    }

    private static void WriteIssue(Utf8JsonWriter w, AnalysisIssue issue, Func<string, string> rootId)
    {
        w.WriteString("code", issue.Code); if (issue.NodeId is { } id) Number(w, "node", id); else w.WriteNull("node");
        w.WriteString("root", issue.RootId is { } root ? rootId(root) : null); w.WriteString("state", issue.State.ToString());
    }

    private static void WriteCoverage(Utf8JsonWriter w, AnalysisCoverageRecord coverage, ImmutableDictionary<MeasurementState, ulong> issueTotals, Func<string, string> rootId)
    {
        Number(w, "requested", coverage.EnumerationRequested); Number(w, "completed", coverage.EnumerationCompleted); Number(w, "failed", coverage.EnumerationFailed);
        w.WriteBoolean("denominatorFinal", coverage.DenominatorFinal); Number(w, "boundaries", coverage.Boundaries);
        w.WriteStartArray("boundaryTotals");
        foreach (var pair in coverage.BoundaryTotals.OrderBy(p => p.Key)) { w.WriteStartArray(); w.WriteStringValue(pair.Key.ToString()); w.WriteStringValue(Decimal(pair.Value)); w.WriteEndArray(); }
        w.WriteEndArray();
        Number(w, "excludedIssues", coverage.ExcludedIssues); Number(w, "failedIssues", coverage.FailedIssues); Number(w, "pendingIssues", coverage.PendingIssues);
        StateCounts(w, "measurements", coverage.MeasurementStates);
        StateCounts(w, "issueTotals", issueTotals);
        Number(w, "identityVerified", coverage.IdentityVerifiedPaths); Number(w, "identityUnverified", coverage.IdentityUnverifiedPaths);
        w.WriteStartArray("staleNodes"); foreach (ulong id in coverage.StaleScopeNodes) w.WriteStringValue(Decimal(id)); w.WriteEndArray();
        IEnumerable<string> staleRoots = coverage.StaleRootIds.Select(rootId);
        if (coverage.Qualification is not null) staleRoots = staleRoots.Order(StringComparer.Ordinal);
        w.WriteStartArray("staleRoots"); foreach (string id in staleRoots) w.WriteStringValue(id); w.WriteEndArray();
        if (coverage.Qualification is { } q)
        {
            w.WriteStartArray("qualification"); w.WriteBooleanValue(q.EnumerationKnown); w.WriteStringValue(Decimal(q.EligibleDirectories));
            w.WriteBooleanValue(q.StaleScopesComplete); w.WriteBooleanValue(q.HasUnlocatedStaleScopes); w.WriteEndArray();
        }
    }
}
