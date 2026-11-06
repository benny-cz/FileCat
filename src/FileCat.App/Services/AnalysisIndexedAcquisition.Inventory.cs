using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
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
                        catch { directory.Dispose(); throw; }
                    }, reviewedEntry, cancellation).ConfigureAwait(false);
        }
    }
}
