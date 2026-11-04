using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Analysis;

/// <summary>Checks actual process authority without ever probing by acquiring higher authority.</summary>
public static partial class AnalysisAdmission
{
    public static string? Refusal()
    {
        if (Environment.IsPrivilegedProcess) return "Start an ordinary, unelevated FileCat to analyze storage.";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return identity.ImpersonationLevel != System.Security.Principal.TokenImpersonationLevel.None
                    ? "Analysis is unavailable under an impersonated identity." : null;
            }
            if (GetUid() == 0 || GetEuid() == 0 || GetGid() == 0 || GetEgid() == 0) return "Analysis requires an ordinary process identity.";
            if (OperatingSystem.IsLinux())
            {
                if (GetResUid(out var r, out var e, out var s) != 0 || r == 0 || e == 0 || s == 0 ||
                    GetResGid(out r, out e, out s) != 0 || r == 0 || e == 0 || s == 0) return "Retained process authority could not be excluded.";
                using var file = File.OpenRead("/proc/self/status");
                var bytes = new byte[65537];
                int n = 0, read;
                while (n < bytes.Length && (read = file.Read(bytes, n, bytes.Length - n)) > 0) n += read;
                if (n == bytes.Length || !UnixWorkerIdentity.CapabilitiesClear(System.Text.Encoding.ASCII.GetString(bytes, 0, n)))
                    return "Analysis is unavailable while process capabilities are retained or unknown.";
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (IsSetUGid() != 0) return "Analysis is unavailable in a set-ID process context.";
            }
            else return "No qualified analysis process adapter is available on this platform.";
            int count = GetGroups(0, []);
            if (count < 0 || count > 65536) return "Process group authority could not be checked.";
            var groups = new uint[count];
            if (GetGroups(count, groups) != count || groups.Contains(0u)) return "Analysis is unavailable with retained root-group authority.";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
        { return "Process authority could not be verified. Analysis was not started."; }
    }
    [LibraryImport("libc", EntryPoint = "getuid")] private static partial uint GetUid();
    [LibraryImport("libc", EntryPoint = "geteuid")] private static partial uint GetEuid();
    [LibraryImport("libc", EntryPoint = "getgid")] private static partial uint GetGid();
    [LibraryImport("libc", EntryPoint = "getegid")] private static partial uint GetEgid();
    [LibraryImport("libc", EntryPoint = "getresuid")] private static partial int GetResUid(out uint r, out uint e, out uint s);
    [LibraryImport("libc", EntryPoint = "getresgid")] private static partial int GetResGid(out uint r, out uint e, out uint s);
    [LibraryImport("libc", EntryPoint = "issetugid")] private static partial int IsSetUGid();
    [LibraryImport("libc", EntryPoint = "getgroups")]
    private static partial int GetGroups(int count, [Out, MarshalUsing(CountElementName = nameof(count))] uint[] groups);
}
