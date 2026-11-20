using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.Analysis;

internal static partial class AnalysisQuotaHold
{
    internal static int Run(string profileText, string byteText, string session, string? privateParent = null)
    {
        if (session == "new" && !OperatingSystem.IsWindows() && SetSid() < 0 && GetSid(0) != Environment.ProcessId)
            throw new IOException("The owned child could not start its independent session.");
        AnalysisPrivateDirectory? directory = privateParent is null ? null : OpenDirectory(privateParent);
        AnalysisProfileQuota owned;
        try { owned = directory is null ? AnalysisProfileQuota.Open(Guid.ParseExact(profileText, "N")) : AnalysisProfileQuota.Open(Guid.ParseExact(profileText, "N"), directory); }
        catch { directory?.Dispose(); throw; }
        using var quota = owned;
        if (!quota.TryReserve(long.Parse(byteText, CultureInfo.InvariantCulture))) return 73;
        Console.WriteLine("QUOTA_READY"); Console.Out.Flush();
        while (Console.ReadLine() is { } command && command != "EXIT")
        {
            if (command == "INFO")
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { pid = Environment.ProcessId,
                    ipc = OperatingSystem.IsLinux() ? new FileInfo("/proc/self/ns/ipc").LinkTarget : null })); Console.Out.Flush(); continue;
            }
            if (command != "PING") throw new InvalidDataException("Unknown owned quota control.");
            Console.WriteLine("QUOTA_HELD"); Console.Out.Flush();
        }
        return 0;
    }
    internal static int Initialize(string parent)
    {
        using var store = new AnalysisPrivateInventoryStore(OpenDirectory(parent));
        Console.WriteLine(new AnalysisPrivateProfileState(store).LoadOrCreate().ToString("N")); return 0;
    }
    internal static AnalysisPrivateDirectory OpenDirectory(string parent) => OperatingSystem.IsWindows()
        ? (AnalysisPrivateDirectory)Activator.CreateInstance(System.Reflection.Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "FileCat.Platform.Windows.dll"))
            .GetType("FileCat.Platform.Windows.WindowsAnalysisPrivateDirectory", throwOnError: true)!, parent, "capacity")!
        : new UnixAnalysisPrivateDirectory(parent, "capacity");
    [LibraryImport("libc", EntryPoint = "setsid", SetLastError = true)] private static partial int SetSid();
    [LibraryImport("libc", EntryPoint = "getsid", SetLastError = true)] private static partial int GetSid(int pid);
}
