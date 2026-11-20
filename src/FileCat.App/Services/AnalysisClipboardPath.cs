using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.App.Services;

/// <summary>Formats inert captured paths without resolving them through a provider.</summary>
internal static class AnalysisClipboardPath
{
    internal readonly record struct Part(string Name, string Encoding, ImmutableArray<byte> Raw, bool IsRoot);

    internal static (string? Path, string Refusal) Format(RootBinding root, IEnumerable<Part> leafToRoot)
    {
        const string tooLong = "The captured path exceeds the clipboard limit. The inventory retains its raw names.";
        const string incomplete = "The full captured path is unavailable.";
        string locator = root.CanonicalLocator;
        if (locator.Length == 0) return (null, "No location was recorded for this entry.");
        using var parts = leafToRoot.GetEnumerator();
        if (!parts.MoveNext()) return (null, incomplete);

        bool windows = root.NameRules == AnalysisNameRules.Windows || root.NameRules == AnalysisNameRules.Provider &&
            (locator.StartsWith("\\\\", StringComparison.Ordinal) || locator.Length > 1 && locator[1] == ':');
        char separator = windows ? '\\' : '/';
        string prefix = parts.Current.IsRoot ? locator : windows ? locator.TrimEnd('/', '\\') : locator.TrimEnd('/');
        int bytes = Encoding.UTF8.GetByteCount(prefix);
        if (bytes > AnalysisLimits.LocatorBytes) return (null, tooLong);

        var names = new Stack<string>();
        do
        {
            var part = parts.Current;
            if (!AnalysisRawNames.HasLosslessText(part.Encoding, part.Raw.AsSpan(), part.Name))
                return (null, "Raw name has no lossless clipboard path.");
            if (part.IsRoot) return (names.Count == 0 ? locator : prefix + separator + string.Join(separator, names), "");
            if (names.Count == AnalysisLimits.Depth) return (null, incomplete);
            bytes += Encoding.UTF8.GetByteCount(part.Name) + 1;
            if (bytes > AnalysisLimits.LocatorBytes) return (null, tooLong);
            names.Push(part.Name);
        } while (parts.MoveNext());
        return (null, incomplete);
    }
}
