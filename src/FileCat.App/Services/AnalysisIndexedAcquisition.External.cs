using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
    internal static Task<AnalysisSavedComparison> ReadExternalComparisonAsync(AppServices services, string path, CancellationToken cancellation)
    {
        AnalysisInventoryInput.ValidateChosenPath(path, comparison: true);
        return AnalysisComparisonFormat.ReadExternalAsync(services.Io, PathUtil.GetDeviceKey(path),
            () => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenChosenComparison(path)
                : UnixAnalysisPrivateDirectory.OpenChosenComparison(path), cancellation);
    }
    internal async Task<AnalysisIndexedInventory> LoadExternalInventoryAsync(string path, CancellationToken cancellation)
    {
        AnalysisInventoryInput.ValidateChosenPath(path);
        IDisposable actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_close is not null, this);
            if (_session is not null || _importStarted) throw new InvalidOperationException("This preparation already owns an acquisition or inventory operation.");
            actual = _imports.Enter(); _importStarted = true;
        }
        using (actual)
        {
            if (!_indexedStorage || MemoryPreparationNotice is not null || _profile == Guid.Empty || Profile is null)
                throw new NotSupportedException("Secure private scratch and profile capacity are required to import an inventory.");
            var devices = new AnalysisIoDevices(PathUtil.GetDeviceKey(_privatePaths[1]), PathUtil.GetDeviceKey(_privatePaths[0]));
            return await AnalysisIndexedInventory.LoadExternalAsync(_services.Io, devices, PathUtil.GetDeviceKey(path),
                () =>
                {
                    CheckPreparation(0, cancellation); CheckPreparation(1, cancellation);
                    return Interlocked.Exchange(ref _scratch, null) ?? throw new IOException("Private scratch ownership has already transferred.");
                }, OpenProfileQuota,
                () => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenChosenInventory(path)
                    : UnixAnalysisPrivateDirectory.OpenChosenInventory(path), cancellation).ConfigureAwait(false);
        }
    }
}
