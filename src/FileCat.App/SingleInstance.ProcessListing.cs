using System.Globalization;
using System.Text;

namespace FileCat.App;

/// <summary>
/// Linux: whether the recovery census sees every process that could write to a disk (I658). It lists processes through
/// /proc, as .NET does, and .NET leaves out a process whose /proc entry it cannot read. Mounted with hidepid, /proc hides
/// other accounts' processes from an ordinary user (invisible, ptraceable) or withholds them (noaccess), so they are not
/// listed at all; a PID namespace other than the system's shows only its own processes. A census that cannot see every
/// process cannot say that none of them is FileCat: its "none" is unknown, and on Linux the root census then decides.
/// </summary>
public static partial class SingleInstance
{
    // The system's own namespaces have these fixed inode numbers (PROC_PID_INIT_INO and PROC_USER_INIT_INO in the
    // kernel's include/linux/proc_ns.h); every other namespace gets another.
    private const string SystemPidNamespace = "pid:[4026531836]", SystemUserNamespace = "user:[4026531837]";

    // CAP_SYS_PTRACE: a process that has it in the system's user namespace may inspect every process, so hidepid hides none.
    private const int SysPtraceCapability = 19;

    private const int ProcFileLimit = 4 * 1024 * 1024;

    /// <summary>Linux: why this process's /proc may not list every process, or null when it lists them all.</summary>
    internal static string? LinuxProcessListingLimit()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux process listings.");
        return LinuxProcessListingLimit(ReadProcText("/proc/self/mountinfo"), ReadProcText("/proc/self/status"),
            ReadProcLink("/proc/self/ns/pid"), ReadProcLink("/proc/self/ns/user"));
    }

    /// <summary>Linux: why a process with these facts may not see every process in /proc, or null when it sees them all.</summary>
    /// <param name="mountInfo">The process's /proc/self/mountinfo; null when it could not be read.</param>
    /// <param name="status">Its /proc/self/status; null when it could not be read.</param>
    /// <param name="pidNamespace">The target of its /proc/self/ns/pid link; null when it could not be read.</param>
    /// <param name="userNamespace">The target of its /proc/self/ns/user link; null when it could not be read.</param>
    internal static string? LinuxProcessListingLimit(string? mountInfo, string? status, string? pidNamespace, string? userNamespace)
    {
        // Containers and sandboxes with a PID namespace of their own: nothing outside it can be seen, by any account.
        if (pidNamespace != SystemPidNamespace)
            return pidNamespace is null ? "this process's PID namespace could not be read"
                : $"this process runs in a PID namespace of its own ({pidNamespace}), which shows no process outside it";
        if (mountInfo is null) return "/proc/self/mountinfo could not be read";
        var mounts = new List<(string Type, string[] Options)>();
        foreach (string line in mountInfo.Split('\n'))
        {
            // proc(5): ID, parent ID, major:minor, root, mount point, mount options, optional fields, "-", file system type,
            // source, super options. Fields are separated by single spaces; spaces within a field are escaped (\040).
            string[] fields = line.Split(' ');
            if (fields.Length < 5 || fields[4] != "/proc") continue;
            int separator = fields.Length > 6 ? Array.IndexOf(fields, "-", 6) : -1;
            if (separator < 0 || separator + 3 >= fields.Length) return "the /proc mount's line in /proc/self/mountinfo could not be read";
            mounts.Add((fields[separator + 1], [.. fields[5].Split(','), .. fields[separator + 3].Split(',')]));
        }
        if (mounts.Count == 0) return "no /proc mount is listed in /proc/self/mountinfo";

        // Only in the system's user namespace do this process's capabilities and group numbers mean what the kernel
        // checks: /proc prints its gid= option in the system's numbering, and a capability held in another user
        // namespace does not reach processes outside it.
        bool systemUsers = userNamespace == SystemUserNamespace;
        var (inspectsAll, groups) = LinuxCredentials(status);
        // Mounts stacked on /proc each say how they list processes; every one of them must list all.
        foreach (var (type, options) in mounts)
        {
            if (type != "proc") return $"/proc is a {type} mount, not the proc file system";
            string? hidepid = null;
            uint? exempt = null;
            foreach (string option in options)
            {
                if (option.StartsWith("hidepid=", StringComparison.Ordinal)) hidepid = option["hidepid=".Length..];
                else if (option.StartsWith("gid=", StringComparison.Ordinal) &&
                         uint.TryParse(option["gid=".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out uint gid)) exempt = gid;
            }
            // The kernel prints the names since Linux 5.8, the numbers before.
            string? hiding = hidepid switch
            {
                null or "0" or "off" => null,
                "1" or "noaccess" => "noaccess",
                "2" or "invisible" => "invisible",
                "4" or "ptraceable" => "ptraceable",
                _ => "",
            };
            if (hiding is null) continue;
            if (hiding.Length == 0) return $"/proc is mounted with hidepid={hidepid}, which FileCat does not know";
            if (systemUsers && inspectsAll) continue;
            // gid= exempts its group from noaccess and invisible, but not from ptraceable (has_pid_permissions).
            if (hiding != "ptraceable" && systemUsers && exempt is { } group && groups.Contains(group)) continue;
            return hiding == "noaccess"
                ? "/proc is mounted with hidepid=noaccess: other accounts' processes cannot be read there, so they are not listed"
                : $"/proc is mounted with hidepid={hiding}: other accounts' processes are hidden from this one";
        }
        return null;
    }

    // Whether the process has CAP_SYS_PTRACE in effect, and the groups /proc's gid= exemption checks: its file-system group
    // and its supplementary groups (in_group_p).
    private static (bool InspectsAll, HashSet<uint> Groups) LinuxCredentials(string? status)
    {
        bool inspectsAll = false;
        var groups = new HashSet<uint>();
        foreach (string line in (status ?? string.Empty).Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string[] values = line[(colon + 1)..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            switch (line[..colon])
            {
                case "CapEff" when values.Length == 1 &&
                                   ulong.TryParse(values[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong effective):
                    inspectsAll = (effective & (1UL << SysPtraceCapability)) != 0;
                    break;
                case "Gid" when values.Length == 4 && uint.TryParse(values[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint fileSystem):
                    groups.Add(fileSystem);
                    break;
                case "Groups":
                    foreach (string value in values)
                        if (uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint supplementary)) groups.Add(supplementary);
                    break;
            }
        }
        return (inspectsAll, groups);
    }

    private static string? ReadProcText(string path)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1);
            using var copy = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int count;
            while ((count = input.Read(buffer)) > 0)
            {
                if (copy.Length + count > ProcFileLimit) return null;
                copy.Write(buffer, 0, count);
            }
            return Encoding.UTF8.GetString(copy.GetBuffer(), 0, (int)copy.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string? ReadProcLink(string path)
    {
        try { return new FileInfo(path).LinkTarget; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
