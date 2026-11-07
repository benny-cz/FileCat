namespace FileCat.Core.Analysis;

internal static partial class AnalysisPrivateNames
{
    internal static string PendingReplacement(Guid catalog, Guid previous, Guid incoming, Guid operation) =>
        catalog != Guid.Empty && previous != Guid.Empty && incoming != Guid.Empty && incoming != previous && operation != Guid.Empty
            ? $"replace-{catalog:N}-{previous:N}-{incoming:N}-{operation:N}.json" : throw new ArgumentException("Distinct generated replacement identities are required.");
    internal static bool TryPendingReplacement(string name, out Guid catalog, out Guid previous, out Guid incoming, out Guid operation)
    {
        catalog = previous = incoming = operation = Guid.Empty;
        return name.Length == 144 && name.StartsWith("replace-", StringComparison.Ordinal) && name[40] == '-' && name[73] == '-' && name[106] == '-' && name.EndsWith(".json", StringComparison.Ordinal) &&
            Guid.TryParseExact(name.AsSpan(8, 32), "N", out catalog) && Guid.TryParseExact(name.AsSpan(41, 32), "N", out previous) &&
            Guid.TryParseExact(name.AsSpan(74, 32), "N", out incoming) && Guid.TryParseExact(name.AsSpan(107, 32), "N", out operation) &&
            catalog != Guid.Empty && previous != Guid.Empty && incoming != Guid.Empty && incoming != previous && operation != Guid.Empty && name == PendingReplacement(catalog, previous, incoming, operation);
    }
    internal static bool IsPendingReplacement(string name) => TryPendingReplacement(name, out _, out _, out _, out _);
    internal static string ReplacementIncoming(Guid incoming, Guid operation) => ReplacementPayload("incoming-", incoming, operation);
    internal static string ReplacementPrevious(Guid previous, Guid operation) => ReplacementPayload("previous-", previous, operation);
    private static string ReplacementPayload(string prefix, Guid inventory, Guid operation) => inventory != Guid.Empty && operation != Guid.Empty
        ? $"{prefix}{inventory:N}-{operation:N}.payload" : throw new ArgumentException("Generated inventory and operation identities are required.");
    internal static bool IsReplacementPayload(string name)
    {
        if (name.Length != 82 || !(name.StartsWith("incoming-", StringComparison.Ordinal) || name.StartsWith("previous-", StringComparison.Ordinal)) || name[41] != '-' || !name.EndsWith(".payload", StringComparison.Ordinal)) return false;
        return Guid.TryParseExact(name.AsSpan(9, 32), "N", out var inventory) && inventory != Guid.Empty &&
            Guid.TryParseExact(name.AsSpan(42, 32), "N", out var operation) && operation != Guid.Empty &&
            name == ReplacementPayload(name[..9], inventory, operation);
    }
    internal static void ReplacementData(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!IsReplacementPayload(name)) AnalysisPrivateInventoryStore.ValidateName(name);
    }
    internal static void ReplacementTarget(string source, string destination)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(destination);
        if (!IsReplacementPayload(source)) throw new ArgumentException("The incoming object must have a generated private replacement name.");
        AnalysisPrivateInventoryStore.ValidateName(destination);
    }
}
