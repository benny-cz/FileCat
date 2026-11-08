using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
    internal static async Task<AnalysisIndexedAcquisition> PrepareExportAsync(AppServices services, CancellationToken cancellation)
    {
        var owner = await PrepareAsync(services, cancellation).ConfigureAwait(false);
        try
        {
            if (OperatingSystem.IsMacOS() && owner._ancestorGuard[1])
                await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(owner._privatePaths[1]), token =>
                {
                    // Explicit export can qualify a fresh owner-private scratch leaf even
                    // when an unrelated corrupt profile forced acquisition into memory.
                    owner.CheckPreparation(1, token); var scratch = OpenDirectory(owner._privatePaths[1]); IAnalysisBackend? current = null;
                    try
                    {
                        current = PrepareRoot(owner._privatePaths[1]); RequireSamePrivateRoot(scratch, current);
                        var previous = owner._protection[1]; owner._protection[1] = current; current = null; owner._ancestorGuard[1] = false;
                        if (previous is not null) DisposePreparedOnWorker(previous);
                    }
                    finally { try { if (current is not null) DisposePreparedOnWorker(current); } finally { DisposePreparedOnWorker(scratch); } }
                    return true;
                }, cancellation).ConfigureAwait(false);
            return owner;
        }
        catch { await owner.DisposeAsync().ConfigureAwait(false); throw; }
    }
    internal Task<AnalysisExportReceipt> ExportInventoryAsync(AnalysisSnapshot source, string folder, string name, CancellationToken cancellation) =>
        ExportCoreAsync(folder, InventoryName(name), (factory, devices) => AnalysisInventoryFormat.ExportSnapshotNewAsync(source, Guid.NewGuid(), _services.Io, devices, name, factory, cancellation), cancellation);
    internal Task<AnalysisExportReceipt> ExportInventoryAsync(AnalysisDatasetLease source, string folder, string name, CancellationToken cancellation) =>
        ExportCoreAsync(folder, InventoryName(name), (factory, devices) => source.ExportInventoryNewAsync(name, Guid.NewGuid(), devices, factory, cancellation), cancellation);
    private static string InventoryName(string name) { AnalysisPrivateInventoryStore.ValidateName(name); return name; }
    internal Task<AnalysisExportReceipt> ExportReportAsync(AnalysisCapturedDatasetReport report, string folder, string name, bool redactPaths, CancellationToken cancellation) =>
        ExportCoreAsync(folder, name, (factory, devices) => report.ExportNewAsync(name, devices, factory, redactPaths, cancellation), cancellation);

    private async Task<AnalysisExportReceipt> ExportCoreAsync(string folder, string name,
        Func<Func<AnalysisExportDestination>, AnalysisIoDevices, Task<AnalysisExportReceipt>> export, CancellationToken cancellation)
    {
        AnalysisExportDestination.ValidateFolder(folder); AnalysisExportDestination.ValidateName(name);
        IDisposable actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_close is not null, this);
            if (_session is not null || _importStarted) throw new InvalidOperationException("This preparation already owns an acquisition or inventory operation.");
            actual = _imports.Enter(); _importStarted = true;
        }
        using (actual)
        {
            // Explicit exports do not update or claim ownership of a named profile.
            // Their native output owns its own private staging; APFS independently
            // qualifies same-volume private scratch below before accepting bytes.
            var protectedIds = new HashSet<string>(StringComparer.Ordinal);
            var protectedPaths = _services.Paths.WriteFolders.Select(item => item.Folder).Append(AppContext.BaseDirectory)
                .Concat(_services.Paths.DataRoot is { } data ? new[] { data } : []).Select(Path.GetFullPath).Distinct(PathUtil.SafetyComparer).ToArray();
            if (protectedPaths.Length > 64) throw new NotSupportedException("The application's protected output boundary exceeds its limit.");
            foreach (string protectedPath in protectedPaths)
            {
                string path = protectedPath;
                await AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(path), token =>
                {
                    for (int depth = 0; depth <= AnalysisLimits.Depth; depth++)
                    {
                        token.ThrowIfCancellationRequested(); IAnalysisBackend? native = null;
                        try { native = PrepareRoot(path); }
                        catch (Exception error) when (StorageUnavailable(error)) { }
                        if (native is not null)
                        {
                            try
                            {
                                if (native.RootEntry.Kind != AnalysisKind.Directory || native.Root.Assurance != IdentityAssurance.Verified || native.RootEntry.Identity is not { } identity)
                                    throw new IOException("A protected FileCat artifact boundary cannot be qualified.");
                                native.ValidateReadScope(native.RootEntry); protectedIds.Add(identity); return true;
                            }
                            finally { DisposePreparedOnWorker(native); }
                        }
                        path = Path.GetDirectoryName(path) ?? throw new IOException("A protected FileCat boundary is unavailable.");
                    }
                    throw new IOException("A protected FileCat boundary exceeds the ancestry limit.");
                }, cancellation).ConfigureAwait(false);
            }
            var devices = new AnalysisIoDevices(PathUtil.GetDeviceKey(folder), PathUtil.GetDeviceKey(_privatePaths[1]));
            return await export(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                return OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder, protectedIds)
                    : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () =>
                    {
                        CheckPreparation(1, cancellation); var scratch = OpenDirectory(_privatePaths[1]);
                        try { RequireSamePrivateRoot(scratch, _protection[1] ?? throw new IOException("The private export staging boundary is unavailable.")); return scratch; }
                        catch { DisposePreparedOnWorker(scratch); throw; }
                    }, protectedIds);
            }, devices).ConfigureAwait(false);
        }
    }
}
