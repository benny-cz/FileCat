using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FileCat.Core.Analysis;

/// <summary>A private, declarative filter bound to recorded root IDs in one captured session.</summary>
internal sealed record AnalysisSavedRecipe(Guid Id, string Name, Guid Session, long Epoch, ImmutableArray<string> RootIds,
    string ScopeFingerprint, string Query, bool CaseSensitive, AnalysisMetric PresentationMetric)
{
    internal const int MaximumBytes = 128 * 1024;
    private static readonly ImmutableHashSet<string> Fields = ImmutableHashSet.Create(StringComparer.Ordinal,
        "schema", "kind", "id", "name", "session", "epoch", "roots", "scopeFingerprint", "query", "caseSensitive", "metric");

    internal static AnalysisSavedRecipe Create(Guid id, string name, SessionIdentity identity, ImmutableArray<RootBinding> roots,
        ImmutableArray<string> selectedRoots, string query, bool caseSensitive, AnalysisMetric metric)
    {
        var recipe = new AnalysisSavedRecipe(id, name, identity.SessionId, identity.Epoch, selectedRoots,
            Fingerprint(roots, selectedRoots), query, caseSensitive, metric);
        recipe.Validate();
        return recipe;
    }

    internal ScopedFilter CompileFor(SessionIdentity identity, ImmutableArray<RootBinding> roots)
    {
        Validate();
        if (!BelongsTo(identity, roots))
            throw new InvalidOperationException("This recipe belongs to a different captured location set. Open its captured analysis or save a new recipe for this one.");
        TimeZoneInfo? zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(identity.TimeZoneId); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException) { zone = null; }
        var parsed = AnalysisFilter.Parse(Query, zone ?? TimeZoneInfo.Utc, CaseSensitive, roots: roots, allowDateOnly: zone is not null);
        return new(parsed, RootIds.ToImmutableHashSet(StringComparer.Ordinal));
    }

    internal bool BelongsTo(SessionIdentity identity, ImmutableArray<RootBinding> roots) =>
        identity.SessionId == Session && identity.Epoch == Epoch && Fingerprint(roots, RootIds) == ScopeFingerprint;

    internal sealed class ScopedFilter(AnalysisFilter filter, ImmutableHashSet<string> roots)
    {
        internal AnalysisFilterNeeds Needs => filter.Needs;
        internal bool Includes(string rootId) => roots.Contains(rootId);
        internal AnalysisTruth Evaluate(AnalysisFilterItem item) => Includes(item.Node.RootId) ? filter.Evaluate(item) : AnalysisTruth.False;
    }

    internal byte[] Encode()
    {
        Validate();
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "1");
            writer.WriteString("kind", "analysis-saved-recipe");
            writer.WriteString("id", Id.ToString("D"));
            writer.WriteString("name", Name);
            writer.WriteString("session", Session.ToString("D"));
            writer.WriteString("epoch", Epoch.ToString(CultureInfo.InvariantCulture));
            writer.WriteStartArray("roots");
            foreach (string root in RootIds) writer.WriteStringValue(root);
            writer.WriteEndArray();
            writer.WriteString("scopeFingerprint", ScopeFingerprint);
            writer.WriteString("query", Query);
            writer.WriteBoolean("caseSensitive", CaseSensitive);
            writer.WriteString("metric", MetricText(PresentationMetric));
            writer.WriteEndObject();
        }
        if (buffer.Length > MaximumBytes) throw Invalid();
        return buffer.ToArray();
    }

    internal static AnalysisSavedRecipe Decode(ReadOnlyMemory<byte> data)
    {
        if (data.Length is < 1 or > MaximumBytes) throw Invalid();
        try
        {
            using var doc = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 3 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid();
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!Fields.Contains(property.Name) || !fields.Add(property.Name)) throw Invalid();
            if (fields.Count != Fields.Count || root.GetProperty("schema").GetString() != "1" ||
                root.GetProperty("kind").GetString() != "analysis-saved-recipe") throw Invalid();

            var scope = root.GetProperty("roots");
            if (scope.ValueKind != JsonValueKind.Array || scope.GetArrayLength() is < 1 or > AnalysisLimits.Roots) throw Invalid();
            var ids = ImmutableArray.CreateBuilder<string>(scope.GetArrayLength());
            foreach (var entry in scope.EnumerateArray()) ids.Add(entry.GetString() ?? throw Invalid());
            string? epochText = root.GetProperty("epoch").GetString();
            if (!long.TryParse(epochText, NumberStyles.None, CultureInfo.InvariantCulture, out long epoch) ||
                epoch.ToString(CultureInfo.InvariantCulture) != epochText) throw Invalid();
            var recipe = new AnalysisSavedRecipe(IdText(root.GetProperty("id")), root.GetProperty("name").GetString() ?? throw Invalid(),
                IdText(root.GetProperty("session")), epoch, ids.MoveToImmutable(), root.GetProperty("scopeFingerprint").GetString() ?? throw Invalid(),
                root.GetProperty("query").GetString() ?? throw Invalid(),
                root.GetProperty("caseSensitive").GetBoolean(), root.GetProperty("metric").GetString() switch
                {
                    "file-size" => AnalysisMetric.FileSize, "unique-file-size" => AnalysisMetric.UniqueFileSize,
                    "on-disk" => AnalysisMetric.OnDisk, "files" => AnalysisMetric.Files, _ => throw Invalid()
                });
            recipe.Validate();
            return recipe;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw Invalid();
        }
    }

    private void Validate()
    {
        if (Id == Guid.Empty || Session == Guid.Empty || Epoch < 1 || !Enum.IsDefined(PresentationMetric) ||
            ScopeFingerprint is not { Length: 64 } || !ScopeFingerprint.All(char.IsAsciiHexDigitLower) ||
            !BoundedText(Name, 240) || Name.Length > 120 || Name != Name.Trim() || Name.Any(char.IsControl) ||
            !BoundedText(Query, AnalysisLimits.QueryBytes) || Query.Contains('\0') ||
            RootIds.IsDefaultOrEmpty || RootIds.Length > AnalysisLimits.Roots ||
            RootIds.Any(id => !BoundedText(id, 256) || id.Any(char.IsControl)) || RootIds.Distinct(StringComparer.Ordinal).Count() != RootIds.Length)
            throw Invalid();
        _ = AnalysisFilter.Parse(Query, TimeZoneInfo.Utc, CaseSensitive);
    }

    // Root ordinals repeat across captures. Use recorded binding facts to distinguish the selected
    // namespaces even if an imported manifest repeats the session and ordinal values.
    private static string Fingerprint(ImmutableArray<RootBinding> roots, ImmutableArray<string> selected)
    {
        if (selected.IsDefaultOrEmpty || selected.Length > AnalysisLimits.Roots) throw Invalid();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string id in selected.Order(StringComparer.Ordinal))
        {
            var root = roots.FirstOrDefault(root => root.Id == id && root.Disposition == AnalysisRootDisposition.Active);
            if (root is null) return "";
            Add(root.Id);
            Add(root.Scheme);
            Add(root.CanonicalLocator);
            Add(root.SourceIdentity);
            Add(root.ObservedObjectIdentity);
            Add(root.ConnectionIdentityReference);
            Add(((int)root.Kind).ToString(CultureInfo.InvariantCulture));
            Add(((int)root.NameRules).ToString(CultureInfo.InvariantCulture));
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());

        void Add(string? value)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, value?.Length ?? -1);
            digest.AppendData(length);
            if (value is null) return;
            Span<byte> bytes = stackalloc byte[256];
            for (int offset = 0; offset < value.Length; offset += bytes.Length / 2)
            {
                int count = Math.Min(bytes.Length / 2, value.Length - offset);
                for (int i = 0; i < count; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes[(i * 2)..], value[offset + i]);
                digest.AppendData(bytes[..(count * 2)]);
            }
        }
    }

    private static bool BoundedText(string? text, int max) => text is not null && text.Length <= max && !string.IsNullOrWhiteSpace(text) &&
        AnalysisPrivateInventoryStore.WellFormedText(text) && Encoding.UTF8.GetByteCount(text) <= max;

    private static Guid IdText(JsonElement value)
    {
        string? text = value.GetString();
        return Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty && text == id.ToString("D") ? id : throw Invalid();
    }

    private static string MetricText(AnalysisMetric metric) => metric switch
    {
        AnalysisMetric.FileSize => "file-size", AnalysisMetric.UniqueFileSize => "unique-file-size", AnalysisMetric.OnDisk => "on-disk",
        AnalysisMetric.Files => "files", _ => throw Invalid()
    };

    private static InvalidDataException Invalid() => new("The saved recipe has an unsupported or invalid format.");
}
