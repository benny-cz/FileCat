using System.Collections.Immutable;
using FileCat.Core.FileSystem;

namespace FileCat.Core.HiddenData;

public enum HiddenInventoryState { Complete, Partial, NotInspected, Denied, Unsupported, Changed, Failed }
public enum HiddenMetadataRole { Ordinary, DownloadMark, StorageBacking, SecurityMetadata, RequiredMetadata, Unqualified, StoragePolicy }
public sealed record HiddenInventoryRequest(bool IncludeEaValues = false, bool ShowCompressionBacking = true);
public sealed record HiddenCategoryInventory(HiddenKind Kind, HiddenInventoryState State, int Count, string Scope, string? Reason = null);

/// <summary>Names, types and lengths from one explicit acquisition; no value excerpts are retained.</summary>
public sealed record HiddenInventory(string Path, DateTimeOffset ObservedAt, ImmutableArray<HiddenItem> Items,
    ImmutableArray<HiddenCategoryInventory> Categories, string Source)
{
    public const int MaxEntries = 1024, MaxNameBytes = 1024 * 1024;
    public FileIdentity? Identity { get; init; }
    public bool BoundToObject { get; init; }
    public bool ActionsAvailable { get; init; } = true;
    public bool CanAct => BoundToObject && ActionsAvailable;
    public bool DeletionAvailable { get; init; } = true;
    public bool CanDelete => CanAct && DeletionAvailable;
    public bool ValueBearingQueryIssued { get; init; }
    public string? Notice { get; init; }
    /// <summary>
    /// Why this observation's values are not read, copied or deleted, for F3, F5, Delete and Enter to say (I651); null
    /// where they are. <see cref="ValueRefusalSummary"/> is its short form on each row.
    /// </summary>
    public string? ValueRefusal { get; init; }
    public string? ValueRefusalSummary { get; init; }
    /// <summary>What a value action says when this observation cannot act on its values.</summary>
    public string ActionRefusalText => ValueRefusal ?? Notice ?? "FileCat could not bind this list to its object. Refresh it and try again.";
    public bool IsComplete => Categories.Length > 0 && Categories.All(c => c.State == HiddenInventoryState.Complete);
    public int? ExactCount => IsComplete ? Items.Length : null;
    /// <summary>At least this many: the listed items, or the categories' counts where a category knows of more than it lists.</summary>
    public int LowerBound => Math.Max(Items.Length, Categories.Sum(c => c.Count));
    /// <summary>
    /// An exact count, a lower bound ("2+"), or what kept a count from being known: no category qualified here ("—"),
    /// access denied, a failed read, a change while it was read, or a category deliberately left unread ("Not inspected").
    /// </summary>
    public string CountText => ExactCount is { } n ? n.ToString(System.Globalization.CultureInfo.CurrentCulture)
        : LowerBound > 0 ? LowerBound.ToString(System.Globalization.CultureInfo.CurrentCulture) + "+"
        : Categories.Any(c => c.State == HiddenInventoryState.Denied) ? "no access"
        : Categories.Any(c => c.State == HiddenInventoryState.Failed) ? "error"
        : Categories.Any(c => c.State == HiddenInventoryState.Changed) ? "changed"
        : Categories.Length > 0 && Categories.All(c => c.State is HiddenInventoryState.Unsupported or HiddenInventoryState.Complete) ? "—"
        : "Not inspected";
    public string Description => string.Join("; ", DescriptionLines);
    /// <summary>Each category on a line of its own, a reason kept to one line: what the Extra data tooltip lists.</summary>
    public IEnumerable<string> DescriptionLines => Categories.Select(c => c.State == HiddenInventoryState.Complete
        ? $"{c.Count} {CategoryName(c.Kind)} ({c.Scope})"
        : $"{CategoryName(c.Kind)}: {StateText(c.State)}{(c.Count > 0 ? $", at least {c.Count}" : "")}{(c.Reason is { } reason ? " — " + reason.ReplaceLineEndings(" ") : "")}");
    public static string CategoryName(HiddenKind kind) => kind switch
    { HiddenKind.Stream => "named streams", HiddenKind.NtfsAttribute => "extended attributes", HiddenKind.ResourceFork => "resource forks", _ => "attributes" };
    public static string StateText(HiddenInventoryState state) => state switch
    { HiddenInventoryState.NotInspected => "not inspected", _ => state.ToString().ToLowerInvariant() };
    public static HiddenInventory Unavailable(string path, HiddenInventoryState state, string reason) => new(path, DateTimeOffset.UtcNow, [],
        [new(HiddenKind.Attribute, state, 0, "not qualified", reason)], "FileCat") { Notice = reason };
}

public static class HiddenIdentities
{
    public static int Ordinal(HiddenKind kind) => kind switch
    { HiddenKind.Stream => 1, HiddenKind.Attribute => 2, HiddenKind.NtfsAttribute => 3, HiddenKind.ResourceFork => 4, _ => 0 };
    public static HiddenKind? Kind(int ordinal) => ordinal switch
    { 1 => HiddenKind.Stream, 2 => HiddenKind.Attribute, 3 => HiddenKind.NtfsAttribute, 4 => HiddenKind.ResourceFork, _ => null };
}

public static class HiddenMetadataRoles
{
    public static HiddenMetadataRole Classify(HiddenItem item, bool? compressed = null)
    {
        if ((item.NativeFlags & 0x80) != 0) return HiddenMetadataRole.RequiredMetadata;
        if (item.Kind == HiddenKind.NtfsAttribute && (item.Name.StartsWith("$KERNEL.", StringComparison.OrdinalIgnoreCase) ||
            item.Name.StartsWith("$CI.", StringComparison.OrdinalIgnoreCase) || item.Name.StartsWith("$LX", StringComparison.OrdinalIgnoreCase) || item.Name.Equals("LXATTRB", StringComparison.OrdinalIgnoreCase))) return HiddenMetadataRole.SecurityMetadata;
        if (item.Kind == HiddenKind.Stream && item.Name.Equals("WofCompressedData", StringComparison.OrdinalIgnoreCase) ||
            item.Kind == HiddenKind.Attribute && item.Name == "com.apple.decmpfs") return HiddenMetadataRole.StorageBacking;
        if (OperatingSystem.IsLinux() && item.Kind == HiddenKind.Attribute && item.Name == "btrfs.compression")
            return HiddenMetadataRole.StoragePolicy;
        if (item.Kind == HiddenKind.ResourceFork) return compressed switch
        { true => HiddenMetadataRole.StorageBacking, false => HiddenMetadataRole.Ordinary, null => HiddenMetadataRole.Unqualified };
        if (item.Kind == HiddenKind.Attribute && (item.Name.StartsWith("security.", StringComparison.Ordinal) ||
            item.Name.StartsWith("system.", StringComparison.Ordinal) || item.Name.StartsWith("trusted.", StringComparison.Ordinal) ||
            item.Name is "com.apple.rootless" or "com.apple.macl" or "com.apple.provenance")) return HiddenMetadataRole.SecurityMetadata;
        return DownloadMarks.IsDownloadMark(item, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()) ? HiddenMetadataRole.DownloadMark : HiddenMetadataRole.Ordinary;
    }

    public static string? DeleteRefusal(HiddenItem item) => item.Role switch
    {
        HiddenMetadataRole.StorageBacking => "This is filesystem-managed storage backing; it cannot be deleted as an attachment.",
        HiddenMetadataRole.StoragePolicy => "This attribute controls filesystem storage policy and cannot be deleted here.",
        HiddenMetadataRole.SecurityMetadata => "This is managed security or operating-system metadata; use the responsible system tool.",
        HiddenMetadataRole.RequiredMetadata => "The filesystem marks this extended attribute as required; generic deletion is unavailable.",
        HiddenMetadataRole.Unqualified => "The role of this metadata is not qualified; deletion is unavailable.",
        _ => null,
    };
}
