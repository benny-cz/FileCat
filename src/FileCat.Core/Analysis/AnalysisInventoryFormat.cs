using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FileCat.Core.Analysis;

/// <summary>An inert inventory and its bounded import-memory reservation. No live binding is created by loading it.</summary>
public sealed class AnalysisInventory : IDisposable
{
    private AnalysisSnapshot? _snapshot;
    private long _reservation;
    public Guid InventoryId { get; }
    public AnalysisSnapshot Snapshot => _snapshot ?? throw new ObjectDisposedException(nameof(AnalysisInventory));
    internal AnalysisInventory(Guid id, AnalysisSnapshot snapshot, long reservation)
    { InventoryId = id; _snapshot = snapshot; _reservation = reservation; }
    public void Dispose()
    {
        _snapshot = null;
        AnalysisInventoryFormat.Release(Interlocked.Exchange(ref _reservation, 0));
    }
}

/// <summary>Schema-1 data-only JSONL codec. The caller owns destination guarding, publication and stream lifetime.</summary>
public static partial class AnalysisInventoryFormat
{
    private const string Producer = "FileCat/disk-space-1";
    private const long ImportMemory = AnalysisLimits.MemoryBytes / 4;
    private static long s_importReserved;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] Lf = [(byte)'\n'];
    private static readonly string[] Kinds = ["manifest", "object", "path-node", "storage-fact", "issue", "coverage"];
    internal static void Release(long bytes) => Interlocked.Add(ref s_importReserved, -bytes);
    private static void Reserve(long bytes, ref long reservation)
    {
        while (true)
        {
            long current = Volatile.Read(ref s_importReserved);
            if (bytes > ImportMemory - current) throw Invalid("Inventory exceeds the shared import-memory budget.");
            if (Interlocked.CompareExchange(ref s_importReserved, current + bytes, current) != current) continue;
            reservation += bytes; return;
        }
    }

    public static void Write(Stream destination, AnalysisSnapshot snapshot, Guid inventoryId, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite || inventoryId == Guid.Empty) throw new ArgumentException("A writable private stream and inventory ID are required.");
        AnalysisValidation.Validate(snapshot, cancellation);
        CheckText(snapshot.Identity.HostProfile, snapshot.Identity.TimeZoneId);
        foreach (var r in snapshot.Roots) CheckText(r.Id, r.Scheme, r.SourceIdentity, r.TraversalBoundary, r.ConnectionIdentityReference, r.CoveredByRootId, r.ObservedObjectIdentity);
        foreach (var o in snapshot.Objects) CheckText(o.Id, o.VerifiedIdentity, o.VersionEvidence);
        foreach (var n in snapshot.Nodes) CheckText(n.RootId, n.ObjectId, n.ObservedIdentity, n.NameEncoding);
        foreach (var f in snapshot.Facts) { CheckText(f.Id, f.ObjectId, f.ComponentId, f.BasisId, f.Method); CheckText(f.Includes.ToArray()); }
        foreach (var i in snapshot.Issues) CheckText(i.Code, i.RootId);
        CheckText(snapshot.Policy.AnalysisMode, snapshot.Policy.ComponentPreset, snapshot.Policy.MountPolicy, snapshot.Policy.PackagePolicy, snapshot.Policy.CloudPolicy);
        CheckText(snapshot.Policy.BasisIds.ToArray()); CheckText(snapshot.Policy.Exclusions.ToArray()); CheckText(snapshot.Policy.Limits.Keys.ToArray());
        var roots = snapshot.Roots.Select((r, i) => (r.Id, WireId: Decimal((ulong)i + 1))).ToDictionary(p => p.Id, p => p.WireId, StringComparer.Ordinal);
        var objects = snapshot.Objects.Select((o, i) => (o.Id, WireId: Decimal((ulong)i + 1))).ToDictionary(p => p.Id, p => p.WireId, StringComparer.Ordinal);
        var counts = new ulong[Kinds.Length]; long bytes = 0; ulong records = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Record(0, w =>
        {
            w.WriteString("schema", "1"); w.WriteString("inventory", inventoryId.ToString("D")); w.WriteString("producer", Producer);
            w.WriteString("session", snapshot.Identity.SessionId.ToString("D")); Number(w, "epoch", (ulong)snapshot.Identity.Epoch);
            Number(w, "revision", (ulong)snapshot.Identity.Revision); w.WriteString("hostProfile", snapshot.Identity.HostProfile);
            Date(w, "started", snapshot.Identity.StartedUtc); Date(w, "observed", snapshot.Identity.ObservedUtc);
            w.WriteString("timeZone", snapshot.Identity.TimeZoneId);
            w.WriteStartArray("roots");
            foreach (var r in snapshot.Roots)
            {
                w.WriteStartArray(); w.WriteStringValue(roots[r.Id]); w.WriteStringValue(r.Scheme);
                TaggedText(w, r.DisplayLocator); TaggedText(w, r.CanonicalLocator); w.WriteStringValue(r.SourceIdentity);
                w.WriteStringValue(r.Assurance.ToString()); TaggedText(w, r.Label); w.WriteStringValue(r.Kind.ToString());
                w.WriteStringValue(r.NameRules.ToString()); w.WriteStringValue(r.TraversalBoundary); w.WriteStringValue(r.ConnectionIdentityReference);
                w.WriteStringValue(r.Disposition.ToString()); w.WriteStringValue(r.CoveredByRootId is { } parent ? roots[parent] : null);
                if (r.CoveredRelativePath is { } path) TaggedText(w, path); else w.WriteNullValue();
                w.WriteStringValue(r.ObservedObjectIdentity); w.WriteEndArray();
            }
            w.WriteEndArray(); w.WritePropertyName("policy"); w.WriteRawValue(snapshot.Policy.CanonicalBytes());
            w.WriteString("policyFingerprint", snapshot.Policy.Fingerprint);
            w.WriteString("historicalPhase", snapshot.Phase.ToString()); w.WriteString("coverage", snapshot.Coverage.ToString());
            w.WriteString("freshness", snapshot.Freshness.ToString()); Number(w, "historicalTaint", (ulong)snapshot.Taint);
        });
        foreach (var o in snapshot.Objects) Record(1, w =>
        {
            w.WriteString("id", objects[o.Id]); w.WriteString("identity", o.VerifiedIdentity); Measurement(w, "length", o.LogicalLength);
            Measurement(w, "links", o.LinkCount); Date(w, "modified", o.ModifiedUtc); w.WriteString("version", o.VersionEvidence);
            Number(w, "precision", o.ModifiedPrecisionTicks);
        });
        foreach (var n in snapshot.Nodes) Record(2, w =>
        {
            Number(w, "id", n.Id); w.WriteString("root", roots[n.RootId]); Number(w, "parent", n.ParentId);
            w.WriteString("encoding", n.NameEncoding);
            w.WriteBase64String("raw", n.RawName.IsEmpty ? AnalysisRawNames.Encode(n.NameEncoding, n.Name) : n.RawName.AsSpan());
            w.WriteString("entryKind", n.Kind.ToString()); w.WriteString("object", n.ObjectId is { } id ? objects[id] : null);
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
        });
        ulong factId = 0;
        foreach (var f in snapshot.Facts) Record(3, w =>
        {
            Number(w, "id", ++factId); w.WriteString("object", f.ObjectId is { } id ? objects[id] : null);
            if (f.DirectoryId is { } directory) Number(w, "directory", directory); else w.WriteNull("directory");
            w.WriteString("component", f.ComponentId); w.WriteString("basis", f.BasisId); Measurement(w, "measurement", f.Measurement);
            w.WriteString("method", f.Method); Date(w, "observed", f.ObservedUtc);
            w.WriteStartArray("includes"); foreach (string component in f.Includes) w.WriteStringValue(component); w.WriteEndArray();
            w.WriteBoolean("mayShare", f.MayShareExtents);
        });
        foreach (var issue in snapshot.Issues) Record(4, w =>
        {
            w.WriteString("code", issue.Code); if (issue.NodeId is { } id) Number(w, "node", id); else w.WriteNull("node");
            w.WriteString("root", issue.RootId is { } root ? roots[root] : null); w.WriteString("state", issue.State.ToString());
        });
        var coverage = snapshot.CoverageDetails ?? AnalysisCoverage.Create(snapshot);
        Record(5, w =>
        {
            Number(w, "requested", coverage.EnumerationRequested); Number(w, "completed", coverage.EnumerationCompleted); Number(w, "failed", coverage.EnumerationFailed);
            w.WriteBoolean("denominatorFinal", coverage.DenominatorFinal); Number(w, "boundaries", coverage.Boundaries);
            w.WriteStartArray("boundaryTotals");
            foreach (var pair in coverage.BoundaryTotals.OrderBy(p => p.Key)) { w.WriteStartArray(); w.WriteStringValue(pair.Key.ToString()); w.WriteStringValue(Decimal(pair.Value)); w.WriteEndArray(); }
            w.WriteEndArray();
            Number(w, "excludedIssues", coverage.ExcludedIssues); Number(w, "failedIssues", coverage.FailedIssues); Number(w, "pendingIssues", coverage.PendingIssues);
            StateCounts(w, "measurements", coverage.MeasurementStates);
            StateCounts(w, "issueTotals", snapshot.IssueTotals.Count > 0 ? snapshot.IssueTotals : snapshot.Issues.GroupBy(i => i.State).ToImmutableDictionary(g => g.Key, g => (ulong)g.Count()));
            Number(w, "identityVerified", coverage.IdentityVerifiedPaths); Number(w, "identityUnverified", coverage.IdentityUnverifiedPaths);
            w.WriteStartArray("staleNodes"); foreach (ulong id in coverage.StaleScopeNodes) w.WriteStringValue(Decimal(id)); w.WriteEndArray();
            w.WriteStartArray("staleRoots"); foreach (string id in coverage.StaleRootIds) w.WriteStringValue(roots[id]); w.WriteEndArray();
        });
        string digest = Convert.ToHexStringLower(hash.GetHashAndReset());
        Emit(w =>
        {
            w.WriteString("kind", "trailer"); w.WriteStartObject("counts");
            for (int i = 0; i < Kinds.Length; i++) Number(w, Kinds[i], counts[i]);
            w.WriteEndObject(); w.WriteString("sha256", digest);
        }, hashed: false);

        void Record(int kind, Action<Utf8JsonWriter> write)
        {
            counts[kind]++; Emit(w => { w.WriteString("kind", Kinds[kind]); write(w); }, hashed: true);
        }
        void Emit(Action<Utf8JsonWriter> write, bool hashed)
        {
            cancellation.ThrowIfCancellationRequested();
            using var buffer = new RecordBuffer();
            using (var w = new Utf8JsonWriter(buffer)) { w.WriteStartObject(); write(w); w.WriteEndObject(); }
            var line = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
            ValidateJson(line); // apply the same structural bound to generated records
            if (++records > AnalysisLimits.InventoryRecords || line.Length + 1L > AnalysisLimits.InventoryBytes - bytes) throw Invalid("Inventory size limit reached.");
            destination.Write(line); destination.Write(Lf); bytes += line.Length + 1L;
            if (hashed) { hash.AppendData(line); hash.AppendData(Lf); }
        }
    }

    private static string Decimal(ulong value) => value.ToString(CultureInfo.InvariantCulture);
    private static void CheckText(params string?[] values)
    {
        try { foreach (string? value in values) if (value is not null) _ = StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException ex) { throw new InvalidDataException("Non-Unicode text must use a tagged raw-name field.", ex); }
    }
    private static void Number(Utf8JsonWriter w, string key, ulong value) => w.WriteString(key, Decimal(value));
    private static void NullableNumber(Utf8JsonWriter w, ulong? value) { if (value.HasValue) w.WriteStringValue(Decimal(value.Value)); else w.WriteNullValue(); }
    private static void NullableBoolean(Utf8JsonWriter w, string key, bool? value) { if (value.HasValue) w.WriteBoolean(key, value.Value); else w.WriteNull(key); }
    private static void Date(Utf8JsonWriter w, string key, DateTimeOffset? value) { w.WritePropertyName(key); DateValue(w, value); }
    private static void DateValue(Utf8JsonWriter w, DateTimeOffset? value) => w.WriteStringValue(value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    private static void TaggedText(Utf8JsonWriter w, string value)
    {
        byte[] raw = new byte[checked(value.Length * 2)];
        for (int i = 0; i < value.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(i * 2), value[i]);
        w.WriteStartArray(); w.WriteStringValue("utf16le"); w.WriteBase64StringValue(raw); w.WriteEndArray();
    }
    private static void Measurement(Utf8JsonWriter w, string key, AnalysisMeasurement measurement)
    {
        w.WriteStartArray(key); w.WriteStringValue(measurement.State.ToString());
        w.WriteStringValue(measurement.Value?.ToString(CultureInfo.InvariantCulture)); w.WriteEndArray();
    }
    private static void StateCounts(Utf8JsonWriter w, string key, ImmutableDictionary<MeasurementState, ulong> values)
    {
        w.WriteStartArray(key);
        foreach (var pair in values.OrderBy(p => p.Key)) { w.WriteStartArray(); w.WriteStringValue(pair.Key.ToString()); w.WriteStringValue(Decimal(pair.Value)); w.WriteEndArray(); }
        w.WriteEndArray();
    }
    private sealed class RecordBuffer : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count > AnalysisLimits.RecordBytes - Length) throw Invalid("Inventory record exceeds 1 MiB.");
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > AnalysisLimits.RecordBytes - Length) throw Invalid("Inventory record exceeds 1 MiB.");
            base.Write(buffer);
        }
    }
    private static InvalidDataException Invalid(string message = "Invalid analysis inventory.") => new(message);
}
