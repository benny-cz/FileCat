namespace FileCat.Core.Analysis;

internal static class AnalysisRootPaths
{
    // Locators suggest a proof request; they never establish containment by themselves.
    public static bool TryRelative(RootBinding parent, RootBinding child, StringComparison comparison, out string relative)
    {
        char separator = parent.NameRules == AnalysisNameRules.Windows ? '\\' : parent.NameRules == AnalysisNameRules.Unix ? '/' : Path.DirectorySeparatorChar;
        string start = parent.CanonicalLocator.TrimEnd(separator), end = child.CanonicalLocator.TrimEnd(separator);
        relative = "";
        if (start.Equals(end, comparison)) return true;
        if (parent.Kind != AnalysisKind.Directory || !end.StartsWith(start + separator, comparison)) return false;
        relative = end[(start.Length + 1)..]; return true;
    }
    public static bool Potential(RootBinding parent, RootBinding child)
    {
        var comparison = parent.NameRules == AnalysisNameRules.Windows || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return TryRelative(parent, child, comparison, out _) || parent.Kind == AnalysisKind.Directory && child.Kind == AnalysisKind.Directory &&
            parent.Assurance == IdentityAssurance.Verified && child.Assurance == IdentityAssurance.Verified &&
            parent.SourceIdentity is { } identity && identity == child.SourceIdentity;
    }
}
