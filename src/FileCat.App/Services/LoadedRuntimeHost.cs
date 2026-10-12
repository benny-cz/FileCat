using System.Runtime.InteropServices;

namespace FileCat.App.Services;

/// <summary>The host for the .NET runtime already loaded by this process, never a new PATH search.</summary>
internal static class LoadedRuntimeHost
{
    internal static string? Current()
    {
        string? process = Environment.ProcessPath;
        if (process is not null && Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && File.Exists(process)) return process;
        return FromRuntimeDirectory(RuntimeEnvironment.GetRuntimeDirectory());
    }

    internal static string? FromRuntimeDirectory(string runtimeDirectory)
    {
        var framework = new DirectoryInfo(runtimeDirectory).Parent;
        var shared = framework?.Parent;
        var root = shared?.Parent;
        if (framework?.Name != "Microsoft.NETCore.App" || shared?.Name != "shared" || root is null) return null;
        string host = Path.Combine(root.FullName, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(host) ? host : null;
    }
}
