using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Buffers.Binary;
using System.Globalization;
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
                if (identity.ImpersonationLevel != System.Security.Principal.TokenImpersonationLevel.None)
                    return "Analysis is unavailable under an impersonated identity.";
                return State.WindowsTokenAuthority.IsHighIntegrity || RetainsWindowsAuthority(identity.Token)
                    ? "Analysis requires a normal user token without retained administrative file authority." : null;
            }
            if (GetUid() == 0 || GetGid() == 0 || GetUid() != GetEuid() || GetGid() != GetEgid()) return "Analysis requires a consistent ordinary process identity.";
            if (OperatingSystem.IsLinux())
            {
                if (GetResUid(out var r, out var e, out var s) != 0 || r == 0 || r != e || e != s ||
                    GetResGid(out r, out e, out s) != 0 || r == 0 || r != e || e != s) return "Retained process authority could not be excluded.";
                using var file = File.OpenRead("/proc/thread-self/status");
                var bytes = new byte[65537];
                int n = 0, read;
                while (n < bytes.Length && (read = file.Read(bytes, n, bytes.Length - n)) > 0) n += read;
                if (n == bytes.Length || !OrdinaryLinuxStatus(System.Text.Encoding.ASCII.GetString(bytes, 0, n)))
                    return "Analysis is unavailable while thread filesystem identity or capabilities are retained or unknown.";
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
    internal static bool OrdinaryLinuxStatus(string status)
    {
        if (!UnixWorkerIdentity.CapabilitiesClear(status)) return false;
        int seen = 0;
        foreach (string line in status.Split('\n'))
        {
            int bit = line.StartsWith("Uid:", StringComparison.Ordinal) ? 1 : line.StartsWith("Gid:", StringComparison.Ordinal) ? 2 : 0;
            if (bit == 0) continue;
            if ((seen & bit) != 0) return false;
            seen |= bit;
            var fields = line[4..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 4 || !uint.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint expected) || expected == 0) return false;
            foreach (string field in fields)
                if (!uint.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) || id != expected) return false;
        }
        return seen == 3;
    }
    private static readonly Lazy<HashSet<ulong>> DangerousWindowsLuids = new(() =>
    {
        string[] names = ["SeBackupPrivilege", "SeRestorePrivilege", "SeTakeOwnershipPrivilege", "SeDebugPrivilege",
            "SeImpersonatePrivilege", "SeAssignPrimaryTokenPrivilege", "SeCreateTokenPrivilege", "SeTcbPrivilege",
            "SeSecurityPrivilege", "SeRelabelPrivilege", "SeLoadDriverPrivilege", "SeManageVolumePrivilege",
            "SeCreateSymbolicLinkPrivilege", "SeTrustedCredManAccessPrivilege", "SeDelegateSessionUserImpersonatePrivilege"];
        var luids = new HashSet<ulong>();
        foreach (string name in names)
        {
            if (LookupPrivilegeValue(null, name, out ulong luid)) luids.Add(luid);
            else if (Marshal.GetLastPInvokeError() != 1313) throw new IOException("Process privilege policy could not be resolved.");
        }
        return luids;
    });
    private static unsafe bool RetainsWindowsAuthority(nint token)
    {
        GetTokenInformation(token, 3, null, 0, out int needed);
        if (needed is < 4 or > 65536) return true;
        byte[] bytes = new byte[needed];
        fixed (byte* p = bytes)
            if (!GetTokenInformation(token, 3, p, needed, out needed) || needed > bytes.Length || needed < 4) return true;
        return HasRetainedPrivileges(bytes.AsSpan(0, needed), DangerousWindowsLuids.Value);
    }
    internal static bool HasRetainedPrivileges(ReadOnlySpan<byte> bytes, IReadOnlySet<ulong> dangerous)
    {
        if (bytes.Length < 4) return true;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (count > 4096 || 4L + count * 12L > bytes.Length) return true;
        for (int i = 0; i < count; i++)
        {
            var entry = bytes.Slice(4 + i * 12, 12);
            ulong luid = BinaryPrimitives.ReadUInt64LittleEndian(entry);
            uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            if (dangerous.Contains(luid) && (attributes & 4) == 0) return true;
        }
        return false;
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
    [LibraryImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool GetTokenInformation(nint token, int kind, byte* data, int length, out int needed);
    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool LookupPrivilegeValue(string? system, string name, out ulong luid);
}
