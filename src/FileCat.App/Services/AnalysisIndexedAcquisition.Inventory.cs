using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
    internal async Task<AnalysisCatalogSnapshot> ReadCatalogAsync(CancellationToken cancellation = default)
    {
        IDisposable actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_close is not null, this);
            if (_session is not null || _importStarted) throw new InvalidOperationException("This preparation already owns an acquisition or inventory operation.");
            actual = _imports.Enter(); _importStarted = true;
        }
        using (actual)
        {
            if (MemoryPreparationNotice is not null || _profile == Guid.Empty || Profile is null) throw new NotSupportedException("Secure private profile storage is required to read saved analyses.");
            return await AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(_privatePaths[0]), token =>
            {
                CheckPreparation(0, token); var directory = OpenDirectory(_privatePaths[0]);
                try { RequireSamePrivateRoot(directory, _protection[0] ?? throw new IOException("The prepared private profile is unavailable.")); }
                catch { DisposePreparedOnWorker(directory); throw; }
                AnalysisCatalogSnapshot? result = null;
                try
                {
                    using var store = new AnalysisPrivateInventoryStore(directory);
                    result = new AnalysisSavedCatalog(store).Read(token); return result;
                }
                catch { result?.Dispose(); throw; }
            }, cancellation).ConfigureAwait(false);
        }
    }
    internal async Task<AnalysisInventoryReceipt> SaveInventoryAsync(AnalysisDatasetLease source, string name, AnalysisSavedView view,
        CancellationToken cancellation = default)
    {
        IDisposable actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_close is not null, this);
            if (_session is not null || _importStarted) throw new InvalidOperationException("This preparation already owns an acquisition or inventory operation.");
            actual = _imports.Enter(); _importStarted = true;
        }
        using (actual)
        {
            if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
            if (MemoryPreparationNotice is not null || _profile == Guid.Empty || Profile is null)
                throw new NotSupportedException("Secure private profile storage is required to save a named inventory.");
            return await source.SaveNamedAsync(name, Guid.NewGuid(), view, PathUtil.GetDeviceKey(_privatePaths[0]), () =>
            {
                CheckPreparation(0, cancellation); var directory = OpenDirectory(_privatePaths[0]);
                try { RequireSamePrivateRoot(directory, _protection[0] ?? throw new IOException("The prepared private profile is unavailable.")); return directory; }
                catch { DisposePreparedOnWorker(directory); throw; }
            }, cancellation).ConfigureAwait(false);
        }
    }
    internal async Task<AnalysisIndexedInventory> LoadInventoryAsync(string name, AnalysisSavedEntry? reviewedEntry = null,
        CancellationToken cancellation = default)
    {
        IDisposable actualWork;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_close is not null, this);
            if (_session is not null || _importStarted) throw new InvalidOperationException("This indexed preparation already owns an acquisition or inventory load.");
            actualWork = _imports.Enter(); _importStarted = true;
        }
        using (actualWork)
        {
            if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
            if (!_indexedStorage || MemoryPreparationNotice is not null || _profile == Guid.Empty || Profile is null)
                throw new NotSupportedException("Secure private scratch and profile capacity storage are required to open an indexed inventory.");
            var devices = new AnalysisIoDevices(PathUtil.GetDeviceKey(_privatePaths[1]), PathUtil.GetDeviceKey(_privatePaths[0]));
            return await AnalysisIndexedInventory.LoadPrivateAsync(_services.Io, devices, PathUtil.GetDeviceKey(_privatePaths[0]), name,
                () =>
                {
                    CheckPreparation(0, cancellation); CheckPreparation(1, cancellation);
                    return Interlocked.Exchange(ref _scratch, null) ?? throw new IOException("Private scratch ownership has already transferred.");
                }, OpenProfileQuota,
                () =>
                    {
                        CheckPreparation(0, cancellation);
                        var directory = OpenDirectory(_privatePaths[0]);
                        try { RequireSamePrivateRoot(directory, _protection[0] ?? throw new IOException("The prepared private profile is unavailable.")); return directory; }
                        catch { DisposePreparedOnWorker(directory); throw; }
                    }, reviewedEntry, cancellation).ConfigureAwait(false);
        }
    }
}
