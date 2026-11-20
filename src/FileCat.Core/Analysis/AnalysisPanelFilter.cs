using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileCat.Core.Selection;

namespace FileCat.Core.Analysis;

/// <summary>Captures the resolved panel mask as display data without consulting saved-filter settings later.</summary>
internal static class AnalysisPanelFilter
{
    internal static string? Capture(Mask? mask)
    {
        var snapshot = mask?.CaptureSnapshot(allowRegex: false)?.ToJsonString();
        return snapshot is not null && Encoding.UTF8.GetByteCount(snapshot) <= AnalysisLimits.QueryBytes ? snapshot : null;
    }

    internal static Mask Restore(string snapshot)
    {
        if (snapshot.Length is 0 or > AnalysisLimits.QueryBytes || Encoding.UTF8.GetByteCount(snapshot) > AnalysisLimits.QueryBytes ||
            !AnalysisPrivateInventoryStore.WellFormedText(snapshot))
            throw new FormatException("The captured panel filter exceeds the display filter limits.");
        try
        {
            // Eight nested saved masks can add an object, term, and array at each level.
            var value = JsonNode.Parse(snapshot, documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
            if (Mask.TryRestoreSnapshot(value, out var mask, allowRegex: false) && mask is not null) return mask;
        }
        catch (JsonException) { }
        throw new FormatException("The captured panel filter could not be read.");
    }

    internal static AnalysisTruth Match(Mask mask, AnalysisNode node)
    {
        if (!AnalysisRawNames.HasLosslessText(node)) return AnalysisTruth.Unknown;
        return mask.Shows(node.Name, node.Kind == AnalysisKind.Directory) ? AnalysisTruth.True : AnalysisTruth.False;
    }
}
