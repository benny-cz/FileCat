using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace FileCat.Core.Analysis;

public static partial class AnalysisComparisonFormat
{
    private static byte[] Header(Guid id, Guid previous, Guid current, long previousBytes, long currentBytes,
        DateTimeOffset saved, string label, AnalysisTaint taint, ImmutableDictionary<string, string>? roots, AnalysisComparisonView view,
        ImmutableArray<string> previousRootIds, ImmutableArray<string> currentRootIds) => Encode(w =>
    {
        w.WriteString("kind", "comparison-manifest"); w.WriteString("schema", "1"); w.WriteString("comparison", id.ToString("D"));
        w.WriteString("previous", previous.ToString("D")); w.WriteString("current", current.ToString("D"));
        w.WriteString("previousBytes", previousBytes.ToString(CultureInfo.InvariantCulture)); w.WriteString("currentBytes", currentBytes.ToString(CultureInfo.InvariantCulture));
        w.WriteString("savedUtc", saved.ToString("O", CultureInfo.InvariantCulture)); w.WriteString("previousLabel", label);
        w.WriteString("taint", ((int)taint).ToString(CultureInfo.InvariantCulture));
        w.WriteStartArray("previousRootIds"); foreach (string root in previousRootIds) w.WriteStringValue(root); w.WriteEndArray();
        w.WriteStartArray("currentRootIds"); foreach (string root in currentRootIds) w.WriteStringValue(root); w.WriteEndArray();
        if (roots is null) w.WriteNull("roots");
        else
        {
            w.WriteStartArray("roots");
            foreach (var pair in roots.OrderBy(p => p.Key, StringComparer.Ordinal))
            { w.WriteStartObject(); w.WriteString("previous", pair.Key); w.WriteString("current", pair.Value); w.WriteEndObject(); }
            w.WriteEndArray();
        }
        w.WriteStartObject("view"); w.WriteString("metric", view.Metric.ToString()); w.WriteString("cohort", view.Cohort.ToString()); w.WriteString("order", view.Order.ToString());
        w.WriteString("query", view.Query); w.WriteBoolean("caseSensitive", view.CaseSensitive); w.WriteString("querySide", view.QuerySide.ToString());
        w.WriteBoolean("showUnknown", view.ShowUnknown); w.WriteString("chart", view.Chart.ToString()); w.WriteBoolean("previousChart", view.PreviousChart);
        w.WriteString("chartScope", view.ChartScope); w.WriteString("focusNode", view.FocusNode.ToString(CultureInfo.InvariantCulture)); w.WriteBoolean("previousFocus", view.PreviousFocus); w.WriteEndObject();
    });
    private static AnalysisComparisonView ReadView(JsonElement value)
    {
        Fields(value, "metric", "cohort", "order", "query", "caseSensitive", "querySide", "showUnknown", "chart", "previousChart", "chartScope", "focusNode", "previousFocus");
        string focus = Text(value, "focusNode");
        if (!ulong.TryParse(focus, NumberStyles.None, CultureInfo.InvariantCulture, out ulong node) || node.ToString(CultureInfo.InvariantCulture) != focus)
            throw Invalid("Invalid saved comparison focus.");
        var view = new AnalysisComparisonView
        {
            Metric = EnumValue<AnalysisMetric>(value, "metric"),
            Cohort = EnumValue<AnalysisComparisonCohort>(value, "cohort"),
            Order = EnumValue<AnalysisComparisonOrder>(value, "order"),
            Query = Text(value, "query"),
            CaseSensitive = Boolean(value, "caseSensitive"),
            QuerySide = EnumValue<AnalysisComparisonSide>(value, "querySide"),
            ShowUnknown = Boolean(value, "showUnknown"),
            Chart = EnumValue<AnalysisComparisonChartMode>(value, "chart"),
            PreviousChart = Boolean(value, "previousChart"),
            ChartScope = Text(value, "chartScope"),
            FocusNode = node,
            PreviousFocus = Boolean(value, "previousFocus")
        };
        AnalysisComparisonView.Validate(view); return view;
    }
    private static T EnumValue<T>(JsonElement value, string name) where T : struct, Enum
    {
        string text = Text(value, name);
        return Enum.TryParse<T>(text, out var result) && Enum.IsDefined(result) && result.ToString() == text ? result : throw Invalid("Unsupported saved comparison enum.");
    }
    private static bool Boolean(JsonElement value, string name) => value.GetProperty(name).ValueKind switch
    { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw Invalid("Saved comparison field must be Boolean.") };
    private static ImmutableDictionary<string, string>? ReadRoots(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > AnalysisLimits.Roots) throw Invalid("Invalid saved comparison root pairs.");
        var roots = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal); var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in value.EnumerateArray())
        {
            Fields(pair, "previous", "current"); string previous = Text(pair, "previous"), current = Text(pair, "current");
            if (!Wire(previous) || !Wire(current) || !roots.TryAdd(previous, current) || !targets.Add(current)) throw Invalid("Conflicting saved comparison root pairs.");
        }
        return roots.ToImmutable();
        static bool Wire(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number is > 0 and <= AnalysisLimits.Roots && number.ToString(CultureInfo.InvariantCulture) == text;
    }
    private static ImmutableArray<string> RootDisplayIds(ImmutableArray<RootBinding> roots, IReadOnlyDictionary<string, string>? display)
    {
        if (display is not null && (display.Count != roots.Length || roots.Any(root => !display.ContainsKey(root.Id)))) throw Invalid("Captured root identifiers do not match their input.");
        var ids = roots.Select(root => display is null ? root.Id : display[root.Id]).ToImmutableArray();
        ValidateRootIds(ids); return ids;
    }
    private static ImmutableArray<string> ReadRootIds(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > AnalysisLimits.Roots) throw Invalid("Invalid captured root identifiers.");
        var ids = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw Invalid("Invalid captured root identifier.")).ToImmutableArray();
        ValidateRootIds(ids); return ids;
    }
    private static void ValidateRootIds(ImmutableArray<string> ids)
    {
        if (ids.Length is < 1 or > AnalysisLimits.Roots || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length ||
            ids.Any(id => string.IsNullOrEmpty(id) || !AnalysisPrivateInventoryStore.WellFormedText(id) || Utf8.GetByteCount(id) > 256)) throw Invalid("Invalid captured root identifiers.");
    }
    private static ImmutableDictionary<string, string> BindRootDisplayIds(ImmutableArray<RootBinding> roots, ImmutableArray<string> ids)
    {
        if (roots.Length != ids.Length) throw Invalid("Captured root identifier count does not match its input.");
        return roots.Select((root, index) => new KeyValuePair<string, string>(root.Id, ids[index])).ToImmutableDictionary(StringComparer.Ordinal);
    }
}
