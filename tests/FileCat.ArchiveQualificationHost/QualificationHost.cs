using Avalonia;
using FileCat.App;
using FileCat.App.Services;

namespace FileCat.ArchiveQualificationHost;

/// <summary>
/// Never shipped. Starts the real interface with the selected native candidate backends and an isolated profile.
/// On macOS the published application's sandbox launcher must sit beside an explicitly selected archive worker.
/// </summary>
internal static class QualificationHost
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (App.Program.TryRunStorageCloudQueryWorker(args) is { } workerExit) return workerExit;
        string? data = null, worker = null, budget = null;
        bool qualifyNtfsStorage = false, nativeArm64Storage = false;
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data" && i + 1 < args.Length) data = args[++i];
            else if (args[i] == "--worker" && i + 1 < args.Length) worker = args[++i];
            else if (args[i] == "--ui-budget" && i + 1 < args.Length) budget = args[++i];
            else if (args[i] == "--ntfs-storage-review") qualifyNtfsStorage = true;
            else if (args[i] == "--ntfs-storage-arm64-review") qualifyNtfsStorage = nativeArm64Storage = true;
            else rest.Add(args[i]);
        }
        if (budget is not null && !Path.IsPathFullyQualified(budget))
        {
            Console.Error.WriteLine("--ui-budget needs a full report path.");
            return 2;
        }
        if (data is null || !Path.IsPathFullyQualified(data) || (worker is null ? !qualifyNtfsStorage : !File.Exists(worker)))
        {
            Console.Error.WriteLine("Usage: FileCat.ArchiveQualificationHost --data <throwaway folder> [--worker <published FileCat executable>] [--ntfs-storage-review | --ntfs-storage-arm64-review] [--ui-budget <report.json>] [FileCat arguments]");
            return 2;
        }
        if (qualifyNtfsStorage && !AppServices.CanQualifyNtfsStorage(nativeArm64Storage))
        {
            Console.Error.WriteLine("NTFS storage review qualification requires a non-elevated Windows 11 process on the selected native architecture.");
            return 2;
        }
        Directory.CreateDirectory(data);
        // On Windows the worker runs in FileCat's archive AppContainer, which may read only a provisioned bundle; an
        // installer grants that once, and this test-only host does it for the published worker it is given.
        if (worker is not null && OperatingSystem.IsWindows())
            Platform.Windows.Shell.ArchiveAppContainer.ProvisionRuntimeReadAccess(Path.GetDirectoryName(worker)!, Path.GetFileName(worker));
        App.App.StartupOptions = StartupOptions.Parse([.. rest, "--data", data]);
        App.App.QualificationServices = startup => AppServices.InitializeForQualification(
            Core.State.AppPaths.Resolve(startup.Profile, dataRoot: startup.DataRoot),
            worker is null ? Core.Archives.ArchiveJobBackends.Empty : AppServices.QualificationBackends(worker), qualifyNtfsStorage, nativeArm64Storage);
        using var storageCloudQuery = qualifyNtfsStorage ? App.Program.StartStorageCloudQuery(typeof(QualificationHost)) : null;
        var builder = App.Program.BuildAvaloniaApp();
        if (budget is not null) builder = builder.AfterSetup(_ => UiBudgetProbe.Start(budget));
        return builder.StartWithClassicDesktopLifetime([.. rest]);
    }
}
