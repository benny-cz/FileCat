using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Already computed accounting for display. Geometry cannot query a source or capture members.</summary>
public interface IAnalysisChartData
{
    AnalysisMetric Metric { get; }
    DateTimeOffset ObservedUtc { get; }
    ImmutableDictionary<string, AnalysisTreeItem> Items { get; }
    string ParentOf(string key);
    string NameOf(string key);
    UInt128 ScopeValue(string key);
    ImmutableArray<AnalysisTreeItem> ChildrenOf(string key);
}
