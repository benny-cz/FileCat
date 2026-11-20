using System.Collections.Immutable;
using System.Globalization;

namespace FileCat.Core.Analysis;

public static partial class AnalysisComparisonFormat
{
    private sealed record ParsedHeader(Guid Id, Guid PreviousId, Guid CurrentId, long PreviousBytes, long CurrentBytes,
        string Label, DateTimeOffset SavedUtc, AnalysisTaint Taint, AnalysisComparisonView View,
        ImmutableDictionary<string, string>? Roots, ImmutableArray<string> PreviousRootIds, ImmutableArray<string> CurrentRootIds);

    private static ParsedHeader ParseHeader(byte[] header)
    {
        using var document = Document(header); var root = document.RootElement;
        Fields(root, "kind", "schema", "comparison", "previous", "current", "previousBytes", "currentBytes", "savedUtc", "previousLabel", "taint", "roots", "view", "previousRootIds", "currentRootIds");
        if (Text(root, "kind") != "comparison-manifest" || Text(root, "schema") != "1") throw Invalid("Unsupported saved comparison schema.");
        Guid id = Id(root, "comparison"), beforeId = Id(root, "previous"), nowId = Id(root, "current");
        if (beforeId == nowId || beforeId == id || nowId == id) throw Invalid("Conflicting saved comparison identities.");
        long beforeBytes = Count(root, "previousBytes"), nowBytes = Count(root, "currentBytes");
        if (beforeBytes > AnalysisLimits.InventoryBytes - header.Length - nowBytes - 256) throw Invalid("Saved comparison size limit reached.");
        string label = Text(root, "previousLabel"); Label(label);
        var saved = DateTimeOffset.TryParseExact(Text(root, "savedUtc"), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp)
            ? stamp : throw Invalid("Invalid comparison save time.");
        var taint = (AnalysisTaint)Integer(root, "taint");
        if (taint == 0 || (taint & ~(AnalysisTaint.OrdinarySensitive | AnalysisTaint.PrivilegedDerived | AnalysisTaint.ImportedUnverified)) != 0)
            throw Invalid("Invalid saved comparison privacy flags.");
        return new(id, beforeId, nowId, beforeBytes, nowBytes, label, saved, taint, ReadView(root.GetProperty("view")),
            ReadRoots(root.GetProperty("roots")), ReadRootIds(root.GetProperty("previousRootIds")), ReadRootIds(root.GetProperty("currentRootIds")));
    }
}
