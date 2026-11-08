using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.App.Services;

internal static class AnalysisRollingHistoryAccess
{
    internal static Task<OwnedAnalysisStore> OpenAsync(AppServices services, CancellationToken cancellation)
    {
        string path = services.Paths.AnalysisHistoryDirectory;
        return OwnedAnalysisStore.OpenRollingAsync(services.Io, PathUtil.GetDeviceKey(path), () =>
        {
            AnalysisPrivateDirectory? history = null; IAnalysisBackend? named = null;
            try
            {
                history = OperatingSystem.IsWindows()
                    ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path))
                    : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path));
                // Native equality also catches aliases which do not have a textual/reparse prefix.
                named = OperatingSystem.IsWindows()
                    ? new WindowsAnalysisBackend(services.Paths.AnalysisSnapshotsDirectory)
                    : new UnixAnalysisBackend(services.Paths.AnalysisSnapshotsDirectory);
                if (history.MatchesRoot(named)) throw new IOException("Rolling history cannot use the named-analysis directory.");
                var completed = named; named = null; AnalysisIo.CloseOnWorker(completed);
                var result = history; history = null; return result;
            }
            finally
            {
                try { if (named is not null) AnalysisIo.CloseOnWorker(named); }
                finally { if (history is not null) AnalysisIo.CloseOnWorker(history); }
            }
        }, cancellation);
    }
}
