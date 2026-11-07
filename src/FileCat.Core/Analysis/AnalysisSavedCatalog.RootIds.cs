using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisSavedCatalog
{
    private static AnalysisRootDisplayIds ReadRootDisplayIds(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("Invalid saved root identifiers.");
        var result = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (result.Count >= AnalysisLimits.Roots || !result.TryAdd(property.Name, Text(property.Value, 256)))
                throw Invalid("Duplicate or excessive saved root identifiers.");
        }
        for (int i = 1; i <= result.Count; i++)
            if (!result.ContainsKey(i.ToString(CultureInfo.InvariantCulture))) throw Invalid("Invalid saved root namespace.");
        return new(result);
    }
}
