using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public enum AnalysisColorDimension { FileType, HierarchyDepth, ModifiedAge }
public sealed record AnalysisColorCategory(string Key, string Label);

/// <summary>Recorded-data color meanings only; no filesystem access, cleanup advice or accounting changes.</summary>
public static class AnalysisChartColors
{
    public const int RuleVersion = 1;
    public static string Title(AnalysisColorDimension dimension) => dimension switch
    {
        AnalysisColorDimension.FileType => "File-type family",
        AnalysisColorDimension.HierarchyDepth => "Depth from all locations",
        AnalysisColorDimension.ModifiedAge => "Modification age at capture",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };
    private static readonly ImmutableArray<AnalysisColorCategory> Special = [new("shared", "Shared files"),
        new("directory-allocation", "Directory allocation"), new("other", "Aggregated / unknown")];
    private static readonly ImmutableArray<AnalysisColorCategory> Types = [new("folder", "Folders / mixed contents"), new("text", "Text"), new("code", "Code"),
            new("image", "Images"), new("audio", "Audio"), new("video", "Video"), new("archive", "Archives"),
            new("executable", "Applications"), new("document", "Documents"), new("other-type", "Other / no extension"),
            new("unknown", "Unknown / special entry"), .. Special];
    private static readonly ImmutableArray<AnalysisColorCategory> Depths = [new("depth-0", "All locations"), new("depth-1", "Depth 1–2"), new("depth-3", "Depth 3–5"),
            new("depth-6", "Depth 6–10"), new("depth-11", "Depth 11+"), new("unknown", "Unknown depth"), .. Special];
    private static readonly ImmutableArray<AnalysisColorCategory> Ages = [new("age-7", "Up to 7 days"), new("age-30", "Over 7–30 days"), new("age-180", "Over 30–180 days"),
            new("age-365", "Over 180–365 days"), new("age-old", "Over 365 days"), new("age-future", "Future timestamp"),
            new("unknown", "Unknown / uncertain modification age"), .. Special];
    public static ImmutableArray<AnalysisColorCategory> Legend(AnalysisColorDimension dimension) => dimension switch
    {
        AnalysisColorDimension.FileType => Types,
        AnalysisColorDimension.HierarchyDepth => Depths,
        AnalysisColorDimension.ModifiedAge => Ages,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };
    public static AnalysisColorCategory Category(IAnalysisChartData tree, AnalysisTreeItem item, AnalysisColorDimension dimension)
    {
        if (item.Family is "shared" or "directory-allocation" || item.Family == "other" && item.Synthetic)
            return Find(item.Family);
        string key = dimension switch
        {
            AnalysisColorDimension.FileType => item.Kind == AnalysisKind.Directory ? "folder" : item.Kind != AnalysisKind.File ? "unknown" :
                AnalysisClassification.Families.Contains(item.Family) ? item.Family == "other" ? "other-type" : item.Family : "unknown",
            AnalysisColorDimension.HierarchyDepth => Depth(tree, item),
            AnalysisColorDimension.ModifiedAge => item.MetadataState != MeasurementState.Known || !item.IncludedByPolicy ? "unknown" : Age(item.ModifiedUtc, item.ModifiedPrecisionTicks, tree.ObservedUtc),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
        return Find(key);
        AnalysisColorCategory Find(string value) => Legend(dimension).First(x => x.Key == value);
    }
    private static string Depth(IAnalysisChartData tree, AnalysisTreeItem item)
    {
        if (item.Key == AnalysisTree.RootKey) return "depth-0";
        string parent = item.ParentKey; int depth = 1;
        while (parent != AnalysisTree.RootKey)
        {
            if (++depth > AnalysisLimits.Depth + 1 || !tree.Items.ContainsKey(parent)) return "unknown";
            parent = tree.ParentOf(parent);
        }
        return depth <= 2 ? "depth-1" : depth <= 5 ? "depth-3" : depth <= 10 ? "depth-6" : "depth-11";
    }
    internal static string Age(DateTimeOffset? modified, ulong precision, DateTimeOffset observed)
    {
        if (modified is null || precision == 0) return "unknown";
        UInt128 first = (ulong)modified.Value.UtcTicks, last = checked(first + precision - 1), reference = (ulong)observed.UtcTicks;
        if (last > (ulong)DateTimeOffset.MaxValue.UtcTicks) return "unknown";
        if (first > reference) return "age-future";
        if (last > reference) return "unknown";
        string youngest = Band(reference - last), oldest = Band(reference - first);
        return youngest == oldest ? youngest : "unknown";
        static string Band(UInt128 ticks) => ticks <= (UInt128)(7L * TimeSpan.TicksPerDay) ? "age-7" :
            ticks <= (UInt128)(30L * TimeSpan.TicksPerDay) ? "age-30" : ticks <= (UInt128)(180L * TimeSpan.TicksPerDay) ? "age-180" :
            ticks <= (UInt128)(365L * TimeSpan.TicksPerDay) ? "age-365" : "age-old";
    }
}
