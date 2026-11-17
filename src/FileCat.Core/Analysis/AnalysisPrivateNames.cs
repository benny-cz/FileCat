namespace FileCat.Core.Analysis;

/// <summary>Closed managed-store namespace. These data names never select a source or grant action authority.</summary>
internal static partial class AnalysisPrivateNames
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
    internal static string PendingDeletion(Guid catalog, Guid inventory, Guid operation) =>
        catalog != Guid.Empty && inventory != Guid.Empty && operation != Guid.Empty ? $"delete-{catalog:N}-{inventory:N}-{operation:N}.json" : throw new ArgumentException("Generated maintenance identities are required.");
    internal static bool TryPendingDeletion(string name, out Guid catalog, out Guid inventory, out Guid operation)
    {
        catalog = inventory = operation = Guid.Empty;
        return name.Length == 110 && name.StartsWith("delete-", StringComparison.Ordinal) && name[39] == '-' && name[72] == '-' && name.EndsWith(".json", StringComparison.Ordinal) &&
            Guid.TryParseExact(name.AsSpan(7, 32), "N", out catalog) && Guid.TryParseExact(name.AsSpan(40, 32), "N", out inventory) && Guid.TryParseExact(name.AsSpan(73, 32), "N", out operation) &&
            catalog != Guid.Empty && inventory != Guid.Empty && operation != Guid.Empty && name == PendingDeletion(catalog, inventory, operation);
    }
    internal static bool IsPendingDeletion(string name) => TryPendingDeletion(name, out _, out _, out _);
    internal static bool IsCatalogEntry(string name) =>
        name.Length == 78 && name.StartsWith("catalog-", StringComparison.Ordinal) && name[40] == '-' && name.EndsWith(".json", StringComparison.Ordinal) &&
        Guid.TryParseExact(name.AsSpan(8, 32), "N", out var catalog) && catalog != Guid.Empty &&
        Guid.TryParseExact(name.AsSpan(41, 32), "N", out var inventory) && inventory != Guid.Empty && name == CatalogEntry(catalog, inventory);
    internal static string Recipe(Guid id) => id != Guid.Empty ? $"recipe-{id:N}.json" : throw new ArgumentException("A generated recipe ID is required.", nameof(id));
    internal static bool TryRecipe(string name, out Guid id)
    {
        id = Guid.Empty;
        return name.Length == 44 && name.StartsWith("recipe-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal) &&
            Guid.TryParseExact(name.AsSpan(7, 32), "N", out id) && id != Guid.Empty && name == Recipe(id);
    }
    internal static bool NeedsReadLease(string name) => !IsMetadata(name) || IsCatalogEntry(name) || IsPendingDeletion(name) || IsPendingReplacement(name);
    internal static void Maintenance(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (IsCatalogEntry(name) || IsPendingDeletion(name) || IsPendingReplacement(name) || IsReplacementPayload(name)) return;
        if (name.EndsWith(".fcdsc", StringComparison.Ordinal)) AnalysisPrivateInventoryStore.ValidateComparisonName(name);
        else AnalysisPrivateInventoryStore.ValidateName(name);
    }
    internal static void MaintenanceMove(string source, string destination)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(destination);
        if (TryPendingReplacement(destination, out var replacementCatalog, out var previous, out _, out _) && source == CatalogEntry(replacementCatalog, previous) ||
            TryPendingReplacement(source, out replacementCatalog, out previous, out _, out _) && destination == CatalogEntry(replacementCatalog, previous)) return;
        if (TryPendingDeletion(destination, out var catalog, out var inventory, out _) && source == CatalogEntry(catalog, inventory) ||
            TryPendingDeletion(source, out catalog, out inventory, out _) && destination == CatalogEntry(catalog, inventory)) return;
        throw new ArgumentException("Only one generated catalog entry can move to or from its pending-deletion name.");
    }
    internal static bool IsMetadata(string name) => name is Catalog or PrivateState or CatalogWriteLock || IsCapacityToken(name) ||
        IsCatalogEntry(name) || IsPendingDeletion(name) || IsPendingReplacement(name) || TryRecipe(name, out _);
    internal static void Metadata(string name)
    {
        if (name is null || !IsMetadata(name)) throw new ArgumentException("A reserved generated analysis metadata name is required.", nameof(name));
    }
    internal static void Entry(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (IsReplacementPayload(name)) return;
        if (!IsMetadata(name))
        {
            if (name.EndsWith(".fcdsc", StringComparison.Ordinal)) AnalysisPrivateInventoryStore.ValidateComparisonName(name);
            else AnalysisPrivateInventoryStore.ValidateName(name);
        }
    }
}
