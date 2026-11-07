namespace FileCat.Core.Analysis;

/// <summary>Closed managed-store namespace. These data names never select a source or grant action authority.</summary>
internal static class AnalysisPrivateNames
{
    internal const int MetadataBytes = 8 * 1024 * 1024, DirectoryEntries = 20_036, DirectoryNameBytes = 8 * 1024 * 1024;
    internal const string Catalog = "catalog.json", PrivateState = "private-state.json";
    internal const string CatalogWriteLock = "write-lock.state";
    internal static string CapacityToken(int slot) => slot is >= 0 and < 32
        ? "capacity-" + slot.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + ".state"
        : throw new ArgumentOutOfRangeException(nameof(slot));
    internal static bool IsCapacityToken(string name) => name.Length == 17 && name.StartsWith("capacity-", StringComparison.Ordinal) && name.EndsWith(".state", StringComparison.Ordinal) &&
        name[9] is >= '0' and <= '3' && name[10] is >= '0' and <= '9' && (name[9] - '0') * 10 + name[10] - '0' < 32;
    internal static string CatalogEntry(Guid catalog, Guid inventory) =>
        catalog != Guid.Empty && inventory != Guid.Empty ? $"catalog-{catalog:N}-{inventory:N}.json" : throw new ArgumentException("Generated catalog and inventory IDs are required.");
    internal static bool IsMetadata(string name) => name is Catalog or PrivateState or CatalogWriteLock || IsCapacityToken(name) ||
        name.Length == 78 && name.StartsWith("catalog-", StringComparison.Ordinal) && name[40] == '-' && name.EndsWith(".json", StringComparison.Ordinal) &&
        Guid.TryParseExact(name.AsSpan(8, 32), "N", out var catalog) && catalog != Guid.Empty &&
        Guid.TryParseExact(name.AsSpan(41, 32), "N", out var inventory) && inventory != Guid.Empty &&
        name == CatalogEntry(catalog, inventory);
    internal static void Metadata(string name)
    {
        if (name is null || !IsMetadata(name)) throw new ArgumentException("A reserved generated analysis metadata name is required.", nameof(name));
    }
    internal static void Entry(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!IsMetadata(name)) AnalysisPrivateInventoryStore.ValidateName(name);
    }
}
