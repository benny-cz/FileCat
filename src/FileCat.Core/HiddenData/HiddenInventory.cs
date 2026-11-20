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
    public bool IsComplete => Categories.Length > 0 && Categories.All(c => c.State == HiddenInventoryState.Complete);
    public int? ExactCount => IsComplete ? Items.Length : null;
    public string CountText => ExactCount is { } n ? n.ToString(System.Globalization.CultureInfo.CurrentCulture)
        : Items.Length > 0 ? Items.Length.ToString(System.Globalization.CultureInfo.CurrentCulture) + "+" : "Not inspected";
    public string Description => string.Join("; ", Categories.Select(c => c.State == HiddenInventoryState.Complete
        ? $"{c.Count} {CategoryName(c.Kind)} ({c.Scope})"
        : $"{CategoryName(c.Kind)}: {StateText(c.State)}{(c.Count > 0 ? $", at least {c.Count}" : "")}{(c.Reason is { } reason ? " — " + reason : "")}"));
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
