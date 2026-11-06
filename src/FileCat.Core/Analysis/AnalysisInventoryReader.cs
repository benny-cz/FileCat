using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace FileCat.Core.Analysis;

public static partial class AnalysisInventoryFormat
{
    public static AnalysisInventory Read(Stream source, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("A readable inventory stream is required.", nameof(source));
        if (source.CanSeek && (source.Length < source.Position || source.Length - source.Position > AnalysisLimits.InventoryBytes)) throw Invalid("Inventory exceeds 8 GiB.");
        long reservation = 0;
        try
        {
            Reserve(4L * AnalysisLimits.RecordBytes, ref reservation);
            var lines = new Lines(source); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Manifest? manifest = null; AnalysisCoverageRecord? coverage = null;
            var objects = new List<AnalysisObject>(); var nodes = new List<AnalysisNode>(); var facts = new List<StorageFact>(); var issues = new List<AnalysisIssue>();
            var objectIds = new HashSet<string>(StringComparer.Ordinal); var nodeIds = new HashSet<ulong>(); var factIds = new HashSet<string>(StringComparer.Ordinal);
            var issueTotals = ImmutableDictionary<MeasurementState, ulong>.Empty; var counts = new ulong[Kinds.Length];
            int stage = 0; ulong records = 0;
            while (lines.Next(cancellation) is { } line)
            {
                cancellation.ThrowIfCancellationRequested();
                if (++records > AnalysisLimits.InventoryRecords) throw Invalid("Inventory record limit reached.");
                ValidateJson(line.Span);
                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = AnalysisLimits.JsonDepth });
                var r = document.RootElement; string kind = String(r.GetProperty("kind"), 32);
                if (kind == "trailer")
                {
                    Fields(r, "kind", "counts", "sha256");
                    if (manifest is null || coverage is null) throw Invalid("Incomplete inventory.");
                    var declared = r.GetProperty("counts"); Fields(declared, Kinds);
                    for (int i = 0; i < Kinds.Length; i++) if (Id(declared.GetProperty(Kinds[i])) != counts[i]) throw Invalid("Inventory record counts do not match.");
                    if (String(r.GetProperty("sha256"), 64) != Convert.ToHexStringLower(hash.GetHashAndReset())) throw Invalid("Inventory checksum does not match.");
                    if (lines.Next(cancellation) is not null) throw Invalid("Data follows the inventory trailer.");
                    var aliases = nodes.Where(n => n.ObjectId is not null).ToLookup(n => n.ObjectId!, StringComparer.Ordinal);
                    var codes = issues.Where(i => i.NodeId.HasValue).ToLookup(i => i.NodeId!.Value);
                    var snapshot = new AnalysisSnapshot(manifest.Identity, manifest.Roots,
                        nodes.Select(n => n with { IssueCodes = codes[n.Id].Select(i => i.Code).Distinct(StringComparer.Ordinal).ToImmutableArray() }).ToImmutableArray(),
                        objects.Select(o => o with { ObservedAliases = aliases[o.Id].Select(n => n.Id).ToImmutableArray() }).ToImmutableArray(),
                        facts.ToImmutableArray(), issues.ToImmutableArray(), manifest.Policy, manifest.Phase, manifest.Coverage, manifest.Freshness,
                        AnalysisOrigin.ImportedInventory, AnalysisPrivacy.Imported(manifest.Taint))
                        { CoverageDetails = coverage, IssueTotals = issueTotals, BoundaryTotals = coverage.BoundaryTotals, Persistence = AnalysisPersistence.Temporary };
                    AnalysisValidation.Validate(snapshot, cancellation);
                    // Recompute every metric before accepting the inventory, including checked total arithmetic.
                    // No supplied summary can hide an overflowing or contradictory fact set.
                    foreach (var metric in Enum.GetValues<AnalysisMetric>()) _ = AnalysisAggregate.Project(snapshot, metric, cancellation: cancellation);
                    var result = new AnalysisInventory(manifest.Id, snapshot, reservation); reservation = 0; return result;
                }
                int index = System.Array.IndexOf(Kinds, kind);
                if (index < 0 || index < stage || manifest is null && index != 0 || index == 0 && manifest is not null || index == 5 && coverage is not null) throw Invalid("Unknown or out-of-order inventory record.");
                stage = index; counts[index]++;
                if (counts[index] > (ulong)(index switch { 1 or 2 => AnalysisLimits.Observations, 4 => AnalysisLimits.Issues, _ => AnalysisLimits.InventoryRecords })) throw Invalid("Inventory kind limit reached.");
                Reserve(checked(2048L + line.Length * 8L), ref reservation);
                hash.AppendData(line.Span); hash.AppendData(Lf);
                switch (index)
                {
                    case 0: manifest = ReadManifest(r); break;
                    case 1:
                        var obj = ReadObject(r); if (!objectIds.Add(obj.Id)) throw Invalid(); objects.Add(obj); break;
                    case 2:
                        var node = ReadNode(r);
                        if (node.ParentId != 0 && !nodeIds.Contains(node.ParentId) || !nodeIds.Add(node.Id) || node.ObjectId is { } o && !objectIds.Contains(o)) throw Invalid("Invalid path-node reference or order.");
                        nodes.Add(node); break;
                    case 3:
                        var fact = ReadFact(r); if (!factIds.Add(fact.Id)) throw Invalid(); facts.Add(fact); break;
                    case 4: issues.Add(ReadIssue(r)); break;
                    case 5: (coverage, issueTotals) = ReadCoverage(r); break;
                }
            }
            throw Invalid("Inventory ended without its mandatory trailer.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException or ArgumentException)
        { throw new InvalidDataException("Invalid analysis inventory encoding or schema.", ex); }
        finally { Release(reservation); }
    }

    private sealed record Manifest(Guid Id, SessionIdentity Identity, ImmutableArray<RootBinding> Roots, ScanPolicy Policy,
        AnalysisPhase Phase, NamespaceCoverage Coverage, AnalysisFreshness Freshness, AnalysisTaint Taint);
    private static Manifest ReadManifest(JsonElement r)
    {
        Fields(r, "kind", "schema", "inventory", "producer", "session", "epoch", "revision", "hostProfile", "started", "observed", "timeZone",
            "roots", "policy", "policyFingerprint", "historicalPhase", "coverage", "freshness", "historicalTaint");
        if (String(r.GetProperty("schema"), 8) != "1" || String(r.GetProperty("producer"), 256).Length == 0) throw Invalid("Unsupported inventory schema.");
        var identity = new SessionIdentity(Uuid(r.GetProperty("session")), checked((long)Id(r.GetProperty("epoch"))), checked((long)Id(r.GetProperty("revision"))),
            String(r.GetProperty("hostProfile"), 4096), Date(r.GetProperty("started")), Date(r.GetProperty("observed"))) { TimeZoneId = String(r.GetProperty("timeZone"), 256) };
        var roots = ImmutableArray.CreateBuilder<RootBinding>(); var array = r.GetProperty("roots"); Array(array, 1, AnalysisLimits.Roots);
        foreach (var value in array.EnumerateArray())
        {
            Array(value, 15, 15);
            roots.Add(new(Key(value[0]), String(value[1], 16), TaggedText(value[2]), TaggedText(value[3]), Optional(value[4], 4096), EnumValue<IdentityAssurance>(value[5]), TaggedText(value[6]))
            {
                Kind = EnumValue<AnalysisKind>(value[7]), NameRules = EnumValue<AnalysisNameRules>(value[8]), TraversalBoundary = String(value[9], 32),
                ConnectionIdentityReference = Optional(value[10], 4096), Disposition = EnumValue<AnalysisRootDisposition>(value[11]),
                CoveredByRootId = OptionalKey(value[12]), CoveredRelativePath = value[13].ValueKind == JsonValueKind.Null ? null : TaggedText(value[13]),
                ObservedObjectIdentity = Optional(value[14], 4096)
            });
        }
        var policy = ReadPolicy(r.GetProperty("policy"));
        if (String(r.GetProperty("policyFingerprint"), 64) != policy.Fingerprint) throw Invalid("Inventory policy fingerprint does not match.");
        var taint = (AnalysisTaint)checked((int)Id(r.GetProperty("historicalTaint")));
        if (taint == 0 || (taint & ~AnalysisPrivacy.All) != 0) throw Invalid("Invalid historical privacy classification.");
        return new(Uuid(r.GetProperty("inventory")), identity, roots.ToImmutable(), policy, EnumValue<AnalysisPhase>(r.GetProperty("historicalPhase")),
            EnumValue<NamespaceCoverage>(r.GetProperty("coverage")), EnumValue<AnalysisFreshness>(r.GetProperty("freshness")), taint);
    }
    private static ScanPolicy ReadPolicy(JsonElement p)
    {
        Fields(p, "schemaVersion", "analysisMode", "componentPreset", "basisIds", "exclusions", "mountPolicy", "packagePolicy", "cloudPolicy", "limits", "classificationVersion", "allocationVersion", "attributionVersion");
        var limits = ImmutableSortedDictionary.CreateBuilder<string, ulong>(StringComparer.Ordinal);
        var l = p.GetProperty("limits"); if (l.ValueKind != JsonValueKind.Object) throw Invalid();
        foreach (var pair in l.EnumerateObject()) limits.Add(pair.Name, Id(pair.Value));
        return new()
        {
            SchemaVersion = checked((int)Id(p.GetProperty("schemaVersion"))), AnalysisMode = String(p.GetProperty("analysisMode"), 32),
            ComponentPreset = String(p.GetProperty("componentPreset"), 32), BasisIds = Strings(p.GetProperty("basisIds"), AnalysisLimits.Components, 256),
            Exclusions = Strings(p.GetProperty("exclusions"), 4096, AnalysisLimits.QueryBytes), MountPolicy = String(p.GetProperty("mountPolicy"), 32),
            PackagePolicy = String(p.GetProperty("packagePolicy"), 32), CloudPolicy = String(p.GetProperty("cloudPolicy"), 32), Limits = limits.ToImmutable(),
            ClassificationVersion = checked((int)Id(p.GetProperty("classificationVersion"))), AllocationVersion = checked((int)Id(p.GetProperty("allocationVersion"))),
            AttributionVersion = checked((int)Id(p.GetProperty("attributionVersion")))
        };
    }
    private static AnalysisObject ReadObject(JsonElement r)
    {
        Fields(r, "kind", "id", "identity", "length", "links", "modified", "version", "precision");
        return new(Key(r.GetProperty("id")), Optional(r.GetProperty("identity"), 4096), Measurement(r.GetProperty("length")), Measurement(r.GetProperty("links")),
            OptionalDate(r.GetProperty("modified")), Optional(r.GetProperty("version"), 4096), Id(r.GetProperty("precision")));
    }
    private static AnalysisNode ReadNode(JsonElement r)
    {
        Fields(r, "kind", "id", "root", "parent", "encoding", "raw", "entryKind", "object", "sparse", "compressed", "cloud", "metadata", "included", "identity", "times");
        string encoding = String(r.GetProperty("encoding"), 16); byte[] raw = Base64(r.GetProperty("raw"), AnalysisLimits.RawNameBytes);
        AnalysisObservationTimes? times = null; var t = r.GetProperty("times");
        if (t.ValueKind != JsonValueKind.Null)
        {
            Array(t, 7, 7); times = new(Date(t[0]), OptionalDate(t[1]), Id(t[2]), OptionalDate(t[3]), OptionalId(t[4]), OptionalDate(t[5]), OptionalId(t[6]));
        }
        return new(Id(r.GetProperty("id")), Key(r.GetProperty("root")), Id(r.GetProperty("parent")), AnalysisRawNames.Decode(encoding, raw),
            EnumValue<AnalysisKind>(r.GetProperty("entryKind")), OptionalKey(r.GetProperty("object")), OptionalBoolean(r.GetProperty("sparse")),
            OptionalBoolean(r.GetProperty("compressed")), OptionalBoolean(r.GetProperty("cloud")))
        {
            NameEncoding = encoding, RawName = raw.ToImmutableArray(), Times = times, ObservedIdentity = Optional(r.GetProperty("identity"), 4096),
            MetadataState = EnumValue<MeasurementState>(r.GetProperty("metadata")), IncludedByPolicy = Boolean(r.GetProperty("included"))
        };
    }
    private static StorageFact ReadFact(JsonElement r)
    {
        Fields(r, "kind", "id", "object", "directory", "component", "basis", "measurement", "method", "observed", "includes", "mayShare");
        return new(Key(r.GetProperty("id")), OptionalKey(r.GetProperty("object")), OptionalId(r.GetProperty("directory")), String(r.GetProperty("component"), 256),
            String(r.GetProperty("basis"), 256), Measurement(r.GetProperty("measurement")), String(r.GetProperty("method"), 256), Date(r.GetProperty("observed")),
            Strings(r.GetProperty("includes"), AnalysisLimits.Components, 256), Boolean(r.GetProperty("mayShare")));
    }
    private static AnalysisIssue ReadIssue(JsonElement r)
    {
        Fields(r, "kind", "code", "node", "root", "state");
        return new(String(r.GetProperty("code"), 512), OptionalId(r.GetProperty("node")), EnumValue<MeasurementState>(r.GetProperty("state"))) { RootId = OptionalKey(r.GetProperty("root")) };
    }
    private static (AnalysisCoverageRecord, ImmutableDictionary<MeasurementState, ulong>) ReadCoverage(JsonElement r)
    {
        string[] fields = ["kind", "requested", "completed", "failed", "denominatorFinal", "boundaries", "boundaryTotals", "excludedIssues", "failedIssues", "pendingIssues", "measurements", "issueTotals", "identityVerified", "identityUnverified", "staleNodes", "staleRoots"];
        AnalysisCoverageQualification? qualification = null;
        if (r.TryGetProperty("qualification", out var q))
        {
            Fields(r, [.. fields, "qualification"]); Array(q, 4, 4);
            qualification = new(Boolean(q[0]), Id(q[1]), Boolean(q[2]), Boolean(q[3]));
        }
        else Fields(r, fields);
        var boundaryRecords = r.GetProperty("boundaryTotals"); Array(boundaryRecords, 0, 5);
        var boundaries = ImmutableDictionary.CreateBuilder<AnalysisBoundaryKind, ulong>();
        foreach (var pair in boundaryRecords.EnumerateArray()) { Array(pair, 2, 2); boundaries.Add(EnumValue<AnalysisBoundaryKind>(pair[0]), Id(pair[1])); }
        var nodes = r.GetProperty("staleNodes"); Array(nodes, 0, AnalysisLimits.Observations);
        return (new(Id(r.GetProperty("requested")), Id(r.GetProperty("completed")), Id(r.GetProperty("failed")), Boolean(r.GetProperty("denominatorFinal")),
            Id(r.GetProperty("boundaries")), Id(r.GetProperty("excludedIssues")), Id(r.GetProperty("failedIssues")), Id(r.GetProperty("pendingIssues")),
            StateCounts(r.GetProperty("measurements")), Id(r.GetProperty("identityVerified")), Id(r.GetProperty("identityUnverified")),
            nodes.EnumerateArray().Select(Id).ToImmutableArray()) { BoundaryTotals = boundaries.ToImmutable(), StaleRootIds = Strings(r.GetProperty("staleRoots"), AnalysisLimits.Roots, 20), Qualification = qualification }, StateCounts(r.GetProperty("issueTotals")));
    }

    private static void ValidateJson(ReadOnlySpan<byte> line)
    {
        if (line.Length == 0 || line.Length > AnalysisLimits.RecordBytes) throw Invalid("Invalid inventory line length.");
        _ = StrictUtf8.GetCharCount(line);
        var reader = new Utf8JsonReader(line, new JsonReaderOptions { MaxDepth = AnalysisLimits.JsonDepth });
        var properties = new Stack<HashSet<string>>(); int count = 0, tokens = 0;
        while (reader.Read())
        {
            if (++tokens > 16_384) throw Invalid("Inventory record token limit reached.");
            if (reader.TokenType == JsonTokenType.StartObject) properties.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) properties.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                if (++count > AnalysisLimits.RecordProperties || !properties.Peek().Add(name)) throw Invalid("Duplicate or excessive inventory properties.");
            }
            else if (reader.TokenType == JsonTokenType.String) _ = StrictUtf8.GetByteCount(reader.GetString()!);
        }
        if (reader.TokenType != JsonTokenType.EndObject) throw Invalid();
    }
    private static void Fields(JsonElement r, params string[] names)
    {
        if (r.ValueKind != JsonValueKind.Object || r.EnumerateObject().Count() != names.Length || r.EnumerateObject().Any(p => !names.Contains(p.Name, StringComparer.Ordinal))) throw Invalid("Unknown or missing inventory property.");
    }
    private static void Array(JsonElement r, int min, int max)
    { if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() < min || r.GetArrayLength() > max) throw Invalid(); }
    private static string String(JsonElement r, int max)
    { if (r.ValueKind != JsonValueKind.String || r.GetString() is not { } s || StrictUtf8.GetByteCount(s) > max || s.Contains('\0')) throw Invalid(); return s; }
    private static string? Optional(JsonElement r, int max) => r.ValueKind == JsonValueKind.Null ? null : String(r, max);
    private static ulong Id(JsonElement r) => AnalysisValidation.Id(String(r, 20));
    private static ulong? OptionalId(JsonElement r) => r.ValueKind == JsonValueKind.Null ? null : Id(r);
    private static string Key(JsonElement r) { string s = String(r, 20); if (AnalysisValidation.Id(s) == 0) throw Invalid(); return s; }
    private static string? OptionalKey(JsonElement r) => r.ValueKind == JsonValueKind.Null ? null : Key(r);
    private static T EnumValue<T>(JsonElement r) where T : struct, Enum
    { string s = String(r, 64); return Enum.TryParse<T>(s, false, out var value) && Enum.IsDefined(value) && value.ToString() == s ? value : throw Invalid(); }
    private static bool Boolean(JsonElement r) => r.ValueKind is JsonValueKind.True or JsonValueKind.False ? r.GetBoolean() : throw Invalid();
    private static bool? OptionalBoolean(JsonElement r) => r.ValueKind == JsonValueKind.Null ? null : Boolean(r);
    private static Guid Uuid(JsonElement r) => Guid.TryParseExact(String(r, 36), "D", out var id) && id != Guid.Empty ? id : throw Invalid();
    private static DateTimeOffset Date(JsonElement r)
    {
        string s = String(r, 33);
        return DateTimeOffset.TryParseExact(s, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Offset == TimeSpan.Zero ? date : throw Invalid();
    }
    private static DateTimeOffset? OptionalDate(JsonElement r) => r.ValueKind == JsonValueKind.Null ? null : Date(r);
    private static AnalysisMeasurement Measurement(JsonElement r)
    {
        Array(r, 2, 2); var state = EnumValue<MeasurementState>(r[0]);
        if (state == MeasurementState.Known) return AnalysisMeasurement.Known(AnalysisValidation.Unsigned(String(r[1], 39)));
        if (r[1].ValueKind != JsonValueKind.Null) throw Invalid(); return AnalysisMeasurement.Missing(state);
    }
    private static ImmutableArray<string> Strings(JsonElement r, int max, int bytes)
    { Array(r, 0, max); return r.EnumerateArray().Select(e => String(e, bytes)).ToImmutableArray(); }
    private static ImmutableDictionary<MeasurementState, ulong> StateCounts(JsonElement r)
    {
        Array(r, 0, 6); var result = ImmutableDictionary.CreateBuilder<MeasurementState, ulong>();
        foreach (var pair in r.EnumerateArray()) { Array(pair, 2, 2); result.Add(EnumValue<MeasurementState>(pair[0]), Id(pair[1])); }
        return result.ToImmutable();
    }
    private static byte[] Base64(JsonElement r, int max)
    {
        string s = String(r, checked((max + 2) / 3 * 4)); byte[] bytes = Convert.FromBase64String(s);
        if (bytes.Length > max || Convert.ToBase64String(bytes) != s) throw Invalid("Noncanonical base64 name."); return bytes;
    }
    private static string TaggedText(JsonElement r)
    {
        Array(r, 2, 2); if (String(r[0], 16) != "utf16le") throw Invalid();
        byte[] raw = Base64(r[1], AnalysisLimits.LocatorBytes * 2);
        if ((raw.Length & 1) != 0) throw Invalid();
        char[] chars = new char[raw.Length / 2];
        for (int i = 0; i < chars.Length; i++) chars[i] = (char)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(i * 2));
        return new string(chars);
    }
    private sealed class Lines(Stream source)
    {
        private readonly byte[] _input = new byte[64 * 1024], _line = new byte[AnalysisLimits.RecordBytes];
        private int _position, _available;
        private long _bytes;
        public ReadOnlyMemory<byte>? Next(CancellationToken cancellation)
        {
            int length = 0;
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (_position == _available)
                {
                    _available = source.Read(_input); _position = 0;
                    if (_available == 0) return length == 0 ? null : throw Invalid("Inventory record lacks its LF delimiter.");
                }
                var remaining = _input.AsSpan(_position, _available - _position); int lf = remaining.IndexOf((byte)'\n');
                int count = lf < 0 ? remaining.Length : lf;
                if (count > AnalysisLimits.RecordBytes - length) throw Invalid("Inventory record exceeds 1 MiB.");
                remaining[..count].CopyTo(_line.AsSpan(length)); length += count; _position += count + (lf < 0 ? 0 : 1);
                _bytes += count + (lf < 0 ? 0 : 1); if (_bytes > AnalysisLimits.InventoryBytes) throw Invalid("Inventory exceeds 8 GiB.");
                if (lf >= 0) return _line.AsMemory(0, length);
            }
        }
    }
}
