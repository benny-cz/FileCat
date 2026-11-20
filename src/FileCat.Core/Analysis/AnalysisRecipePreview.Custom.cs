using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisRecipePreviews
{
    private readonly AnalysisSavedRecipe.ScopedFilter? _saved;
    private readonly bool _stale;

    internal AnalysisRecipePreviews(AnalysisSavedRecipe recipe, ImmutableArray<RootBinding> roots, SessionIdentity identity,
        AnalysisFreshness freshness)
    {
        _saved = recipe.CompileFor(identity, roots);
        _stale = freshness != AnalysisFreshness.Observed;
        _rootIds = recipe.RootIds;
        Metric = recipe.PresentationMetric == AnalysisMetric.Files ? AnalysisMetric.FileSize : recipe.PresentationMetric;
        string query = AnalysisNames.Display(recipe.Query, 640);
        _definition = new(AnalysisMetadataRecipe.CustomFilter, "custom-filter", 1, AnalysisNames.Display(recipe.Name, 120),
            "Saved filter — " + query + (recipe.CaseSensitive ? " · Case sensitive." : " · Case insensitive."),
            "Only definite matches in the saved captured locations appear as candidates.");
    }

    private AnalysisRecipeFinding EvaluateSaved(AnalysisNode node, AnalysisObject? obj, AnalysisMeasurement measured,
        AnalysisMeasurement allocation, Func<ulong, AnalysisNode?> readPath)
    {
        if (!node.IncludedByPolicy || node.MetadataState == MeasurementState.Excluded || node.Cloud == true)
            return new(AnalysisRecipeMatch.Excluded);
        var path = NeedsRelativePath ? RelativePath(node, readPath) : "";
        var state = node.MetadataState == MeasurementState.Known ? measured.State : node.MetadataState;
        var item = new AnalysisFilterItem(node, obj, path ?? "", allocation, Stale: _stale,
            Unreadable: node.MetadataState == MeasurementState.Failed || state == MeasurementState.Failed,
            ActiveState: state, RelativePathTextAvailable: path is not null);
        return _saved!.Evaluate(item) switch
        {
            AnalysisTruth.True when node.MetadataState == MeasurementState.Known => new(AnalysisRecipeMatch.Candidate, "Recorded metadata matches the saved filter."),
            AnalysisTruth.False => new(AnalysisRecipeMatch.NoMatch),
            _ => new(AnalysisRecipeMatch.Unknown)
        };
    }

    private static string? RelativePath(AnalysisNode node, Func<ulong, AnalysisNode?> readPath)
    {
        var parts = new Stack<string>();
        int characters = 0;
        while (node.ParentId != 0)
        {
            if (parts.Count >= AnalysisLimits.Depth) throw new InvalidDataException("The recipe path exceeds the captured depth limit.");
            characters += node.Name.Length + 1;
            if (characters > AnalysisLimits.LocatorBytes / 2 || !AnalysisRawNames.HasLosslessText(node)) return null;
            parts.Push(node.Name);
            var parent = readPath(node.ParentId);
            if (parent is null || parent.RootId != node.RootId || parent.Kind != AnalysisKind.Directory || parent.Id != node.ParentId)
                return null;
            node = parent;
        }
        return string.Join('/', parts);
    }
}
