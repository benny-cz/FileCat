using Avalonia;
using FileCat.App;
using FileCat.App.Services;

namespace FileCat.ArchiveQualificationHost;

/// <summary>
/// Never shipped. Usage: FileCat.ArchiveQualificationHost --data &lt;throwaway folder&gt; --worker &lt;published FileCat executable&gt;
/// [--ui-budget &lt;report.json&gt;] [FileCat arguments]. The interface is the application's own; only its archive backends are
/// composed explicitly, through each platform's production worker boundary, and <c>--ui-budget</c> adds the UI-thread budget
/// probe (<see cref="UiBudgetProbe"/>). On macOS the published application's sandbox launcher must sit beside the worker.
/// </summary>
internal static class QualificationHost
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? data = null, worker = null, budget = null; var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data" && i + 1 < args.Length) data = args[++i];
            else if (args[i] == "--worker" && i + 1 < args.Length) worker = args[++i];
            else if (args[i] == "--ui-budget" && i + 1 < args.Length) budget = args[++i];
            else rest.Add(args[i]);
        }
        if (budget is not null && !Path.IsPathFullyQualified(budget))
        {
            Console.Error.WriteLine("--ui-budget needs a full report path.");
            return 2;
        }
        if (data is null || !Path.IsPathFullyQualified(data) || worker is null || !File.Exists(worker))
        {
            Console.Error.WriteLine("Usage: FileCat.ArchiveQualificationHost --data <throwaway folder> --worker <published FileCat executable> [FileCat arguments]");
            return 2;
        }
        Directory.CreateDirectory(data);
        // On Windows the worker runs in FileCat's archive AppContainer, which may read only a provisioned bundle; an
        // installer grants that once, and this test-only host does it for the published worker it is given.
        if (OperatingSystem.IsWindows())
            Platform.Windows.Shell.ArchiveAppContainer.ProvisionRuntimeReadAccess(Path.GetDirectoryName(worker)!, Path.GetFileName(worker));
        App.App.StartupOptions = StartupOptions.Parse([.. rest, "--data", data]);
        App.App.QualificationServices = startup => AppServices.InitializeForQualification(
            Core.State.AppPaths.Resolve(startup.Profile, dataRoot: startup.DataRoot),
            AppServices.QualificationBackends(worker));
        var builder = App.Program.BuildAvaloniaApp();
        if (budget is not null) builder = builder.AfterSetup(_ => UiBudgetProbe.Start(budget));
        return builder.StartWithClassicDesktopLifetime([.. rest]);
    }
}
