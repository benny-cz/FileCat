using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.App.Services;

internal static class AnalysisOwnedStoreAccess
{
    internal static async Task<OwnedAnalysisInventoryDetails> InspectAsync(AppServices services, Guid inventory,
        CancellationToken cancellation)
    {
        string path = services.Paths.AnalysisSnapshotsDirectory;
        string parent = Path.GetDirectoryName(path) ?? throw new IOException("The private profile parent is unavailable.");
        string name = Path.GetFileName(path);
        await using var owned = await OwnedAnalysisStore.OpenAsync(services.Io, PathUtil.GetDeviceKey(path),
            () => OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(parent, name) : new UnixAnalysisPrivateDirectory(parent, name), cancellation);
        return await owned.InspectAsync(inventory, cancellation);
    }
}
