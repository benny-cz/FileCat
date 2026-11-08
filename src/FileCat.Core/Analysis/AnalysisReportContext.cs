using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed record AnalysisReportFilterContext(string Query, bool? CaseSensitive, bool UnknownOnly, bool? RootScoped);
public sealed record AnalysisReportBases(ImmutableArray<string> Labels, bool DetailLimited)
{
    internal static AnalysisReportBases Capture(IEnumerable<string> source)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal); int characters = 0; bool limited = false;
        foreach (string label in source)
        {
            if (labels.Contains(label)) continue;
            if (label.Length > 256) { limited = true; continue; }
            if (labels.Count == AnalysisLimits.Components || label.Length > 65536 - characters) { limited = true; break; }
            labels.Add(label); characters += label.Length;
        }
        return new(labels.Order(StringComparer.Ordinal).ToImmutableArray(), limited);
    }
}
