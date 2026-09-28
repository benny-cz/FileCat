using FileCat.Core.FileSystem;
using FileCat.Core.Platform;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

/// <summary>The Windows adapter set (AI-04): native shell, file operations, drives, and SMB shares.</summary>
public sealed class WindowsPlatform : PortablePlatform
{
    public WindowsPlatform()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Shell = new WindowsShellServices();
        FileOperations = new WindowsFileOperations();
        Core.Jobs.Junctions.CreateHandler = Junction.Create;
        Core.Jobs.JobExecutors.ExtraExecutors[Core.Jobs.JobKind.Elevated] =
            (job, _, _, journal) => new Elevation.ElevatedJobExecutor(job, journal);
        Core.Jobs.JobExecutors.ExtraExecutors[Core.Jobs.JobKind.Registry] =
            (job, _, _, journal) => new RegistryExecutor(job, journal);
    }

    /// <summary>Owner window for Shell warnings (e.g. the permanent-deletion warning during recycle).</summary>
    public static void SetOwnerWindow(nint hwnd) => WindowsFileOperations.OwnerWindow = hwnd;

    public override string Name => $"Windows {Environment.OSVersion.Version}";

    public override void RegisterProviders(ProviderRegistry registry)
    {
        base.RegisterProviders(registry);
        registry.Register(new WindowsComputerProvider());
        registry.Register(new NetworkShareProvider());
        registry.Register(new WindowsRegistryProvider());
    }

    protected override LocalFileSystemProvider CreateFileSystemProvider() => new WindowsFileSystemProvider();
}
