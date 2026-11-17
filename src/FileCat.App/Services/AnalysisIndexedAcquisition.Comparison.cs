using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
    private IDisposable BeginComparisonOperation(bool scratch)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_close is not null, this);
            if (_session is not null || _importStarted) throw new InvalidOperationException("This preparation already owns an acquisition or inventory operation.");
            if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
            if (MemoryPreparationNotice is not null || _profile == Guid.Empty || Profile is null || scratch && !_indexedStorage)
                throw new NotSupportedException("Secure private profile storage and scratch capacity are required for this comparison operation.");
            var actual = _imports.Enter(); _importStarted = true; return actual;
        }
    }
    private AnalysisPrivateDirectory OpenComparisonDirectory(CancellationToken cancellation)
    {
        CheckPreparation(0, cancellation); var directory = OpenDirectory(_privatePaths[0]);
        try { RequireSamePrivateRoot(directory, _protection[0] ?? throw new IOException("The prepared private profile is unavailable.")); return directory; }
        catch { DisposePreparedOnWorker(directory); throw; }
    }
    private AnalysisPrivateDirectory TakeComparisonScratch(CancellationToken cancellation)
    {
        CheckPreparation(0, cancellation); CheckPreparation(1, cancellation);
        return Interlocked.Exchange(ref _scratch, null) ?? throw new IOException("Private scratch ownership has already transferred.");
    }
    internal async Task<AnalysisComparisonReceipt> SaveComparisonAsync(AnalysisCapturedIndexedComparisonBundle captured,
        string name, CancellationToken cancellation)
    {
        using var actual = BeginComparisonOperation(false);
        return await captured.SaveNamedNewAsync(name, PathUtil.GetDeviceKey(_privatePaths[0]),
            () => OpenComparisonDirectory(cancellation), cancellation).ConfigureAwait(false);
    }
    internal async Task<ImmutableArray<string>> ReadComparisonNamesAsync(CancellationToken cancellation)
    {
        using var actual = BeginComparisonOperation(false);
        return await AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(_privatePaths[0]), token =>
        {
            using var store = new AnalysisPrivateInventoryStore(OpenComparisonDirectory(token));
            return store.ComparisonNames(token);
        }, cancellation).ConfigureAwait(false);
    }
    internal async Task<AnalysisSavedIndexedComparison> LoadSavedComparisonAsync(string name, CancellationToken cancellation)
    {
        AnalysisPrivateInventoryStore.ValidateComparisonName(name); using var actual = BeginComparisonOperation(true);
        var devices = new AnalysisIoDevices(PathUtil.GetDeviceKey(_privatePaths[1]), PathUtil.GetDeviceKey(_privatePaths[0]));
        return await AnalysisComparisonFormat.ReadPrivateIndexedAsync(_services.Io, devices, PathUtil.GetDeviceKey(_privatePaths[0]), name,
            () => TakeComparisonScratch(cancellation), OpenProfileQuota, () => OpenComparisonDirectory(cancellation), cancellation).ConfigureAwait(false);
    }
    internal async Task<AnalysisSavedIndexedComparison> LoadExternalComparisonAsync(string path, CancellationToken cancellation)
    {
        AnalysisInventoryInput.ValidateChosenPath(path, comparison: true); using var actual = BeginComparisonOperation(true);
        var devices = new AnalysisIoDevices(PathUtil.GetDeviceKey(_privatePaths[1]), PathUtil.GetDeviceKey(_privatePaths[0]));
        return await AnalysisComparisonFormat.ReadExternalIndexedAsync(_services.Io, devices, PathUtil.GetDeviceKey(path),
            () => TakeComparisonScratch(cancellation), OpenProfileQuota,
            () => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenChosenComparison(path)
                : UnixAnalysisPrivateDirectory.OpenChosenComparison(path), cancellation).ConfigureAwait(false);
    }
    internal Task<AnalysisExportReceipt> ExportComparisonAsync(AnalysisCapturedIndexedComparisonBundle captured,
        string folder, string name, CancellationToken cancellation) =>
        ExportCoreAsync(folder, name, (factory, devices) => captured.ExportNewAsync(name, devices, factory, false, cancellation), cancellation);
}
