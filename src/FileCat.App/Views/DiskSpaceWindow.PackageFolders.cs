using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private static readonly string[] MacPackageExtensions = [".app", ".bundle", ".framework", ".plugin", ".kext"];

    // A display hint from captured names and the macOS basis. This does not inspect
    // package content, query application associations, or change traversal or actions.
    private static bool HasMacPackageName(AnalysisTreeItem item, ScanPolicy? policy, string? rawName = null)
    {
        if (item is not { NodeId: not null, Kind: AnalysisKind.Directory, IncludedByPolicy: true, MetadataState: MeasurementState.Known } ||
            policy?.BasisIds.Contains(AnalysisComponents.DarwinTotalAllocationBasis) != true)
            return false;
        string name = rawName ?? item.CapturedOrderName ?? item.Name;
        return MacPackageExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string MacPackageContents(ScanPolicy policy) => policy.PackagePolicy == "contents-included"
        ? "The name has a standard macOS package extension. The recorded scan policy includes package contents where accessible."
        : "The name has a standard macOS package extension. Check the captured scope for what was included.";
}
