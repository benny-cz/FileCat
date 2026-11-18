using System.Collections.Immutable;
using System.Text.Json;

namespace FileCat.Core.Analysis;

/// <summary>A literal folder path relative to one explicitly selected root, using slash separators.</summary>
public sealed record AnalysisFolderExclusion(int RootIndex, string RelativePath);

internal sealed class AnalysisFolderExclusions
{
    public const int Maximum = 32;
    public const int PathCharacters = 1024;
    public const string IssueCode = "folder-excluded-by-scan-policy";
    private const string RulePrefix = "folder-path-v1:";
    private readonly ImmutableArray<AnalysisFolderExclusion> _rules;
    public ImmutableArray<string> PolicyRules { get; }

    internal AnalysisFolderExclusions(IReadOnlyList<IAnalysisBackend> sources, ImmutableArray<AnalysisFolderExclusion> rules)
    {
        if (rules.IsDefaultOrEmpty) { _rules = []; PolicyRules = []; return; }
        if (rules.Length > Maximum) throw new ArgumentException($"Choose at most {Maximum} folder exclusions.");
        _rules = rules.Distinct().ToImmutableArray();
        var policy = ImmutableArray.CreateBuilder<string>();
        foreach (var rule in _rules)
        {
            if (rule is null || rule.RootIndex < 0 || rule.RootIndex >= sources.Count)
                throw new ArgumentException("Each folder exclusion needs a selected location.");
            var source = sources[rule.RootIndex];
            var root = source.Root;
            bool windows = root.NameRules == AnalysisNameRules.Windows;
            if (root.Scheme != "file" || source.RootEntry.Kind != AnalysisKind.Directory || root.NameRules == AnalysisNameRules.Provider)
                throw new ArgumentException("Folder exclusions require a local folder or drive location.");
            if (rule.RelativePath is not { Length: > 0 and <= PathCharacters } path ||
                path.Split('/').Any(name => !AnalysisNames.IsOrdinaryChild(name, windows) || name.Any(char.IsControl)))
                throw new ArgumentException("Enter an exact folder path relative to its location, such as Projects/Cache. Use / between folders.");
            try { _ = AnalysisRawNames.Encode("utf8", path); }
            catch (InvalidDataException error) { throw new ArgumentException("The folder exclusion needs lossless name text.", error); }

            foreach (var selected in sources)
            {
                if (!AnalysisRootPaths.TryRelative(root, selected.Root, StringComparison.Ordinal, out var relative)) continue;
                if (windows) relative = relative.Replace('\\', '/');
                if (relative == path || relative.StartsWith(path + "/", StringComparison.Ordinal))
                    throw new ArgumentException("A selected location is inside a folder exclusion. Remove the location or the exclusion before starting.");
            }
            // These are data-only literal rules, keyed by the captured locator instead of its session ordinal.
            var encoded = RulePrefix + JsonSerializer.Serialize(new[] { root.Scheme, root.CanonicalLocator, path });
            if (System.Text.Encoding.UTF8.GetByteCount(encoded) > AnalysisLimits.QueryBytes)
                throw new ArgumentException("The folder exclusion and its location are too long to record.");
            policy.Add(encoded);
        }
        PolicyRules = policy.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    internal static int RuleCount(ScanPolicy policy) => policy.Exclusions.Count(rule => rule.StartsWith(RulePrefix, StringComparison.Ordinal));

    internal IEnumerable<string> PathsFor(ImmutableArray<RootBinding> roots, int index)
    {
        var active = roots[index];
        foreach (var rule in _rules)
        {
            if (rule.RootIndex == index) yield return rule.RelativePath;
            else if (roots[rule.RootIndex] is { Disposition: AnalysisRootDisposition.Covered, CoveredRelativePath: { } prefix } covered &&
                covered.CoveredByRootId == active.Id)
            {
                if (prefix.Length == 0 && covered.ObservedObjectIdentity != active.ObservedObjectIdentity)
                    throw new InvalidOperationException("The exclusion could not be placed under its covering root.");
                yield return prefix.Length == 0 ? rule.RelativePath : prefix + "/" + rule.RelativePath;
            }
        }
    }

    internal byte[][] EncodePaths(ImmutableArray<RootBinding> roots, int index)
    {
        bool windows = roots[index].NameRules == AnalysisNameRules.Windows;
        return PathsFor(roots, index).Distinct(StringComparer.Ordinal).Select(path => windows
            ? AnalysisRawNames.Encode("utf16le", path.Replace('/', '\\'))
            : AnalysisRawNames.Encode("unix-bytes", path)).ToArray();
    }

    internal static AnalysisEntry Apply(AnalysisEntry entry, byte[][] paths)
    {
        // A failed or changed observation already blocks traversal. Keep its native coverage evidence.
        if (!entry.IncludedByPolicy || entry.Kind != AnalysisKind.Directory || entry.MetadataState != MeasurementState.Known) return entry;
        foreach (var path in paths)
        {
            if (entry.RelativeName.AsSpan().SequenceEqual(path))
                return AnalysisEntry.Unavailable(entry.RelativeName, entry.RawName, entry.NameEncoding,
                    AnalysisKind.Directory, MeasurementState.Excluded, IssueCode);
        }
        return entry;
    }
}
