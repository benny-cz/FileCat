namespace FileCat.Core.Analysis;

/// <summary>Closed managed-store namespace. These data names never select a source or grant action authority.</summary>
internal static class AnalysisPrivateNames
{
    internal const int MetadataBytes = 8 * 1024 * 1024, DirectoryEntries = 20_004, DirectoryNameBytes = 8 * 1024 * 1024;
    internal const string Catalog = "catalog.json", PrivateState = "private-state.json";
    internal static string CatalogEntry(Guid catalog, Guid inventory) =>
        catalog != Guid.Empty && inventory != Guid.Empty ? $"catalog-{catalog:N}-{inventory:N}.json" : throw new ArgumentException("Generated catalog and inventory IDs are required.");
    internal static bool IsMetadata(string name) => name is Catalog or PrivateState ||
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
