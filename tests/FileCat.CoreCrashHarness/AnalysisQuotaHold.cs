using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.Analysis;

internal static partial class AnalysisQuotaHold
{
    internal static int Run(string profileText, string byteText, string session)
    {
        if (session == "new" && !OperatingSystem.IsWindows() && SetSid() < 0 && GetSid(0) != Environment.ProcessId)
            throw new IOException("The owned child could not start its independent session.");
        using var quota = AnalysisProfileQuota.Open(Guid.ParseExact(profileText, "N"));
        if (!quota.TryReserve(long.Parse(byteText, CultureInfo.InvariantCulture))) return 73;
        Console.WriteLine("QUOTA_READY"); Console.Out.Flush();
        while (Console.ReadLine() is { } command && command != "EXIT")
        {
            if (command != "PING") throw new InvalidDataException("Unknown owned quota control.");
            Console.WriteLine("QUOTA_HELD"); Console.Out.Flush();
        }
        return 0;
    }
    [LibraryImport("libc", EntryPoint = "setsid", SetLastError = true)] private static partial int SetSid();
    [LibraryImport("libc", EntryPoint = "getsid", SetLastError = true)] private static partial int GetSid(int pid);
}
