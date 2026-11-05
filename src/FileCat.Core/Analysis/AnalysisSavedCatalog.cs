using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FileCat.Core.Analysis;

/// <summary>Private display state only. No field grants a source binding or action approval.</summary>
public sealed record AnalysisSavedView(AnalysisMetric Metric, string Chart, string Query, string? Root, string Scope,
    bool CaseSensitive, bool ShowUnknown)
{
    public static AnalysisSavedView Default { get; } = new(AnalysisMetric.OnDisk, "Treemap", "", null, AnalysisTree.RootKey, false, false);
}

public sealed record AnalysisSavedEntry(Guid InventoryId, string Name, long Bytes, DateTimeOffset SavedUtc,
    NamespaceCoverage Coverage, AnalysisTaint Taint, AnalysisSavedView View);

/// <summary>An inert bounded catalog result. Dispose after copying the selected entry; no native handles are retained.</summary>
public sealed class AnalysisCatalogSnapshot : IDisposable
{
    private IDisposable? _memory;
    internal AnalysisCatalogSnapshot(Guid? id, ImmutableArray<AnalysisSavedEntry> entries, ImmutableArray<string> unindexed, IDisposable memory)
    { CatalogId = id; Entries = entries; UnindexedNames = unindexed; _memory = memory; }
    public Guid? CatalogId { get; }
    public ImmutableArray<AnalysisSavedEntry> Entries { get; }
    public ImmutableArray<string> UnindexedNames { get; }
    public void Dispose() => Interlocked.Exchange(ref _memory, null)?.Dispose();
}

/// <summary>The inventory is durably published; its catalog entry is missing or uncertain. Do not retry as replacement.</summary>
public sealed class AnalysisCatalogUpdateException(AnalysisInventoryReceipt receipt, Exception inner)
    : IOException("The inventory was saved, but its catalog entry could not be confirmed. Keep the file and open it by filename.", inner)
{
    public AnalysisInventoryReceipt Receipt { get; } = receipt;
}

/// <summary>
/// Small owner-private index with immutable create-new entries. A fixed versioned header identifies its namespace;
/// concurrent writers never replace each other's index. Enumeration and JSON remain bounded. This capability cannot
/// delete, replace, connect to sources, or turn imported locators into authority.
/// </summary>
public sealed class AnalysisSavedCatalog(AnalysisPrivateInventoryStore store)
{
    public const int EntryLimit = 4096;
    private const int EntryBytes = 32 * 1024, TotalBytes = 2 * 1024 * 1024;
    private const long Reservation = 32L * 1024 * 1024;
    private static long s_reserved;

    private static IDisposable Reserve()
    {
        while (true)
        {
            long current = Volatile.Read(ref s_reserved);
            if (current > 64L * 1024 * 1024 - Reservation) throw new InvalidOperationException("The shared private catalog memory budget is in use. Close another catalog and try again.");
            if (Interlocked.CompareExchange(ref s_reserved, current + Reservation, current) == current) return new MemoryLease();
        }
    }
    private sealed class MemoryLease : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) Interlocked.Add(ref s_reserved, -Reservation); }
    }

    public AnalysisCatalogSnapshot Read(CancellationToken cancellation = default)
    {
        var memory = Reserve();
        try
        {
            string[] names = store.Entries(cancellation); var set = names.ToHashSet(StringComparer.Ordinal);
            if (set.Count != names.Length) throw Invalid("The private catalog changed during enumeration. Try again.");
            Guid? catalog = null; var entries = ImmutableArray.CreateBuilder<AnalysisSavedEntry>();
            int bytes = 0;
            if (set.Contains(AnalysisPrivateNames.Catalog))
            {
                byte[] header = store.ReadMetadata(AnalysisPrivateNames.Catalog, cancellation, 512);
                using var document = Document(header, 512); var root = document.RootElement;
                Fields(root, "schema", "kind", "catalog"); Schema(root, "catalog"); catalog = Id(root.GetProperty("catalog"));
            }
            var ids = new HashSet<Guid>(); var indexed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!AnalysisPrivateNames.IsMetadata(name) || name is AnalysisPrivateNames.Catalog or AnalysisPrivateNames.PrivateState) continue;
                if (catalog is null || name.AsSpan(8, 32).ToString() != catalog.Value.ToString("N")) throw Invalid("A catalog entry has no matching supported catalog header.");
                if (entries.Count >= EntryLimit) throw Invalid("The saved analysis catalog entry limit was reached.");
                byte[] data = store.ReadMetadata(name, cancellation, EntryBytes); bytes = checked(bytes + data.Length);
                if (bytes > TotalBytes) throw Invalid("The saved analysis catalog exceeds its metadata budget.");
                var entry = DecodeEntry(data, catalog.Value);
                if (name != AnalysisPrivateNames.CatalogEntry(catalog.Value, entry.InventoryId) || !ids.Add(entry.InventoryId) || !indexed.Add(entry.Name))
                    throw Invalid("The saved analysis catalog has conflicting generated identities or names.");
                if (!set.Contains(entry.Name)) throw Invalid("A cataloged inventory is missing. Open remaining files by filename; no catalog repair was performed.");
                entries.Add(entry);
            }
            var unindexed = ImmutableArray.CreateBuilder<string>();
            foreach (string name in names)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!name.EndsWith(".fcdsa", StringComparison.Ordinal) || indexed.Contains(name)) continue;
                try { AnalysisPrivateInventoryStore.ValidateName(name); }
                catch (ArgumentException) { continue; }
                unindexed.Add(name);
            }
            var result = new AnalysisCatalogSnapshot(catalog, entries.OrderByDescending(e => e.SavedUtc).ThenBy(e => e.Name, StringComparer.Ordinal).ToImmutableArray(),
                unindexed.Order(StringComparer.Ordinal).ToImmutableArray(), memory);
            memory = null!; return result;
        }
        finally { memory?.Dispose(); }
    }

    public AnalysisInventoryReceipt SaveNew(string name, AnalysisSnapshot snapshot, Guid inventoryId, AnalysisSavedView view,
        CancellationToken cancellation = default)
    {
        AnalysisPrivateInventoryStore.ValidateName(name); ValidateView(view);
        if (inventoryId == Guid.Empty) throw new ArgumentException("A generated inventory identity is required.", nameof(inventoryId));
        using var current = Read(cancellation);
        if (current.Entries.Length >= EntryLimit || current.Entries.Any(e => e.InventoryId == inventoryId || e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
            current.UnindexedNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("That inventory name or identity already exists, or the catalog is full. Keep existing files and choose another name.");
        Guid catalog = current.CatalogId ?? CreateHeader(cancellation);
        var entry = new AnalysisSavedEntry(inventoryId, name, 1, DateTimeOffset.UtcNow, snapshot.Coverage, snapshot.Taint, view);
        // Refuse a full index before publishing the inventory. Concurrent writers are bounded again on the next read;
        // they use unique immutable entries and cannot replace a prior writer's data.
        byte[] preview = EncodeEntry(catalog, entry with { Bytes = AnalysisLimits.InventoryBytes });
        if (current.Entries.Sum(e => EncodeEntry(catalog, e).Length) + preview.Length > TotalBytes) throw Invalid("The saved analysis catalog metadata budget is full.");
        var receipt = store.SaveNew(name, snapshot, inventoryId, cancellation);
        try
        {
            store.WriteMetadataNew(AnalysisPrivateNames.CatalogEntry(catalog, inventoryId), EncodeEntry(catalog, entry with { Bytes = receipt.Bytes }), cancellation);
            return receipt;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
        { throw new AnalysisCatalogUpdateException(receipt, ex); }
    }

    private Guid CreateHeader(CancellationToken cancellation)
    {
        Guid id = Guid.NewGuid(); byte[] data = Encode(w => { w.WriteString("schema", "1"); w.WriteString("kind", "catalog"); w.WriteString("catalog", id.ToString("D")); });
        try { store.WriteMetadataNew(AnalysisPrivateNames.Catalog, data, cancellation); return id; }
        catch (AnalysisPublicationUncertainException ex)
        { throw new IOException("Private catalog initialization durability is uncertain. No new inventory was saved; keep the existing catalog header.", ex); }
        catch (IOException)
        {
            // A concurrent create-new winner is usable only after the complete strict read succeeds.
            using var winner = Read(cancellation);
            return winner.CatalogId ?? throw Invalid("The private catalog header could not be published.");
        }
    }

    public AnalysisInventory Load(AnalysisSavedEntry entry, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var loaded = store.Load(entry.Name, cancellation);
        if (loaded.InventoryId == entry.InventoryId) return loaded;
        loaded.Dispose(); throw Invalid("The saved inventory identity does not match its catalog entry. No source was connected.");
    }

    public static void ValidateView(AnalysisSavedView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!Enum.IsDefined(view.Metric) || view.Chart is not ("Treemap" or "Sunburst" or "Table") || view.Query is null || view.Scope is null ||
            Encoding.UTF8.GetByteCount(view.Query) > AnalysisLimits.QueryBytes || Encoding.UTF8.GetByteCount(view.Scope) > 256 || view.Root is { } text && Encoding.UTF8.GetByteCount(text) > 256 ||
            !AnalysisPrivateInventoryStore.WellFormedText(view.Query) || !AnalysisPrivateInventoryStore.WellFormedText(view.Scope) ||
            view.Root is { } root && !AnalysisPrivateInventoryStore.WellFormedText(root)) throw Invalid("Unsupported saved analysis view state.");
    }

    private static byte[] EncodeEntry(Guid catalog, AnalysisSavedEntry e) => Encode(w =>
    {
        w.WriteString("schema", "1"); w.WriteString("kind", "named"); w.WriteString("catalog", catalog.ToString("D"));
        w.WriteString("inventory", e.InventoryId.ToString("D")); w.WriteString("name", e.Name);
        w.WriteString("bytes", e.Bytes.ToString(CultureInfo.InvariantCulture)); w.WriteString("saved", e.SavedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        w.WriteString("coverage", e.Coverage.ToString()); w.WriteString("taint", ((ulong)e.Taint).ToString(CultureInfo.InvariantCulture));
        w.WriteStartObject("view"); w.WriteString("metric", e.View.Metric.ToString()); w.WriteString("chart", e.View.Chart); w.WriteString("query", e.View.Query);
        w.WriteString("root", e.View.Root); w.WriteString("scope", e.View.Scope); w.WriteBoolean("caseSensitive", e.View.CaseSensitive); w.WriteBoolean("showUnknown", e.View.ShowUnknown); w.WriteEndObject();
    });

    private static AnalysisSavedEntry DecodeEntry(byte[] data, Guid catalog)
    {
        try
        {
            using var document = Document(data, EntryBytes); var r = document.RootElement;
            Fields(r, "schema", "kind", "catalog", "inventory", "name", "bytes", "saved", "coverage", "taint", "view"); Schema(r, "named");
            if (Id(r.GetProperty("catalog")) != catalog) throw Invalid("The catalog identity does not match.");
            string name = Text(r.GetProperty("name"), 240); AnalysisPrivateInventoryStore.ValidateName(name);
            long size = Number(r.GetProperty("bytes"), AnalysisLimits.InventoryBytes); if (size == 0) throw Invalid();
            string date = Text(r.GetProperty("saved"), 40);
            if (!DateTimeOffset.TryParseExact(date, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var saved) || saved.Offset != TimeSpan.Zero || date != saved.ToString("O", CultureInfo.InvariantCulture)) throw Invalid();
            var coverage = Enumeration<NamespaceCoverage>(r.GetProperty("coverage"));
            var taint = (AnalysisTaint)Number(r.GetProperty("taint"), (long)AnalysisPrivacy.All);
            if ((taint & AnalysisTaint.OrdinarySensitive) == 0) throw Invalid();
            var v = r.GetProperty("view"); Fields(v, "metric", "chart", "query", "root", "scope", "caseSensitive", "showUnknown");
            var view = new AnalysisSavedView(Enumeration<AnalysisMetric>(v.GetProperty("metric")), Text(v.GetProperty("chart"), 16), Text(v.GetProperty("query"), AnalysisLimits.QueryBytes),
                v.GetProperty("root").ValueKind == JsonValueKind.Null ? null : Text(v.GetProperty("root"), 256), Text(v.GetProperty("scope"), 256), v.GetProperty("caseSensitive").GetBoolean(), v.GetProperty("showUnknown").GetBoolean());
            ValidateView(view); return new(Id(r.GetProperty("inventory")), name, size, saved, coverage, taint, view);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        { throw Invalid("Invalid saved analysis catalog entry."); }
    }
    private static byte[] Encode(Action<Utf8JsonWriter> fields)
    {
        using var stream = new MemoryStream(); using (var w = new Utf8JsonWriter(stream)) { w.WriteStartObject(); fields(w); w.WriteEndObject(); }
        if (stream.Length > EntryBytes) throw Invalid("The saved analysis catalog entry exceeds its size limit.");
        return stream.ToArray();
    }
    private static JsonDocument Document(byte[] data, int maximum)
    {
        if (data.Length is < 1 || data.Length > maximum) throw Invalid("The saved analysis metadata exceeds its size limit.");
        try { return JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 4 }); }
        catch (JsonException) { throw Invalid("Invalid saved analysis metadata encoding."); }
    }
    private static void Fields(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in value.EnumerateObject()) if (!fields.Contains(p.Name, StringComparer.Ordinal) || !seen.Add(p.Name)) throw Invalid("Unknown or duplicate saved analysis metadata property.");
        if (seen.Count != fields.Length) throw Invalid("Incomplete saved analysis metadata.");
    }
    private static void Schema(JsonElement r, string kind)
    {
        if (Text(r.GetProperty("schema"), 16) != "1" || Text(r.GetProperty("kind"), 16) != kind) throw Invalid("Unsupported saved analysis catalog schema. No overwrite or downgrade was attempted.");
    }
    private static string Text(JsonElement e, int max)
    {
        if (e.ValueKind != JsonValueKind.String) throw Invalid(); string s = e.GetString()!;
        if (!AnalysisPrivateInventoryStore.WellFormedText(s) || Encoding.UTF8.GetByteCount(s) > max) throw Invalid(); return s;
    }
    private static Guid Id(JsonElement e)
    {
        string s = Text(e, 36);
        if (!Guid.TryParseExact(s, "D", out var id) || id == Guid.Empty || id.ToString("D") != s) throw Invalid(); return id;
    }
    private static long Number(JsonElement e, long max)
    {
        string s = Text(e, 24);
        if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out long n) || n < 0 || n > max || s != n.ToString(CultureInfo.InvariantCulture)) throw Invalid(); return n;
    }
    private static T Enumeration<T>(JsonElement e) where T : struct, Enum
    {
        string s = Text(e, 64); if (!Enum.TryParse<T>(s, false, out var n) || !Enum.IsDefined(n) || n.ToString() != s) throw Invalid(); return n;
    }
    private static InvalidDataException Invalid(string message = "Invalid saved analysis metadata.") => new(message);
}
