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
        Secrets = new WindowsCredentialStore();
        Core.Jobs.Junctions.CreateHandler = Junction.Create;
        Core.Jobs.JobExecutors.ExtraExecutors[Core.Jobs.JobKind.Elevated] =
            (job, _, _, journal) => new Elevation.ElevatedJobExecutor(job, journal);
        Core.Jobs.JobExecutors.ExtraExecutors[Core.Jobs.JobKind.Registry] =
            (job, _, _, journal) => new RegistryExecutor(job, journal);
    }

    /// <summary>Owner window for Shell warnings (e.g. the permanent-deletion warning during recycle).</summary>
    public static void SetOwnerWindow(nint hwnd) => WindowsFileOperations.OwnerWindow = hwnd;

    /// <summary>
    /// "Windows 11 (build 26220, x64)": Windows 11 still reports version 10.0, which read as Windows 10. An x64 FileCat on
    /// an ARM64 computer says it runs emulated there, where the ARM64 package runs natively (D-48).
    /// </summary>
    public override string Name
    {
        get
        {
            var v = Environment.OSVersion.Version;
            string product = v.Major == 10 ? v.Build >= 22000 ? "Windows 11" : "Windows 10" : $"Windows {v.Major}.{v.Minor}";
            var process = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
            var os = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            string architecture = process == os ? Architecture(process) : $"{Architecture(process)} emulated on {Architecture(os)}";
            return $"{product} (build {v.Build}, {architecture})";
        }
    }

    private static string Architecture(System.Runtime.InteropServices.Architecture architecture) => architecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        _ => architecture.ToString(),
    };

    public override void RegisterProviders(ProviderRegistry registry)
    {
        base.RegisterProviders(registry);
        registry.Register(new WindowsComputerProvider());
        registry.Register(new NetworkShareProvider());
        registry.Register(new WindowsRegistryProvider());
        // Phones and cameras over MTP (P8), with uploads, deletes, renames, and folders as jobs.
        registry.Register(new Mtp.MtpProvider());
        Mtp.MtpJobs.Register();
    }

    protected override LocalFileSystemProvider CreateFileSystemProvider() => new WindowsFileSystemProvider();
}
