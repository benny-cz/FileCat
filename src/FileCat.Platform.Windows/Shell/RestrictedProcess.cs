using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Shell;

/// <summary>
/// Starts a helper that runs third-party code (plan §6.2, §8.2, TV-16) at low integrity where Windows allows it, on a
/// token that holds no administrative group and no privilege but SeChangeNotifyPrivilege even when FileCat itself runs
/// elevated, inside a job object that ends it together with FileCat, allows it no child processes, caps its memory,
/// and denies it FileCat's own windows, the clipboard, global atoms, and desktop, display, or shutdown changes. Only
/// its two pipe ends are inherited, and it starts suspended, so none of its code runs outside the job. This is a
/// reduced-authority worker, not a verified sandbox: it keeps the filesystem and network reach of an ordinary user.
/// </summary>
internal sealed unsafe partial class RestrictedProcess : IDisposable
{
    private readonly nint _job;
    private readonly nint _process;
    private bool _disposed;

    private RestrictedProcess(nint job, nint process, int id, FileStream input, FileStream output, bool lowIntegrity)
    {
        _job = job;
        _process = process;
        ProcessId = id;
        Input = input;
        Output = output;
        LowIntegrity = lowIntegrity;
    }

    /// <summary>Written to the helper's standard input.</summary>
    public FileStream Input { get; }

    /// <summary>Read from the helper's standard output.</summary>
    public FileStream Output { get; }

    public int ProcessId { get; }

    /// <summary>True when the helper was started with a low-integrity token.</summary>
    public bool LowIntegrity { get; }

    public bool HasExited => WaitForSingleObject(_process, 0) == 0;

    /// <summary>Ends the helper and anything in its job at once.</summary>
    public void Kill() => TerminateJobObject(_job, 1);

    public static RestrictedProcess Start(string executable, string arguments, bool lowIntegrity, long memoryLimitBytes,
        bool inheritParentEnvironment = true)
    {
        nint job = CreateJob(memoryLimitBytes);
        nint childIn = 0, parentIn = 0, parentOut = 0, childOut = 0;
        try
        {
            var inherit = new SECURITY_ATTRIBUTES { nLength = sizeof(SECURITY_ATTRIBUTES), bInheritHandle = 1 };
            if (!CreatePipe(out childIn, out parentIn, &inherit, 0) || !CreatePipe(out parentOut, out childOut, &inherit, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Shell helper's pipes could not be created.");
            // FileCat's own ends are never inherited.
            SetHandleInformation(parentIn, HANDLE_FLAG_INHERIT, 0);
            SetHandleInformation(parentOut, HANDLE_FLAG_INHERIT, 0);

            bool low = lowIntegrity;
            var pi = low ? TryCreate(executable, arguments, childIn, childOut, lowToken: true, inheritParentEnvironment) : null;
            if (pi is null)
            {
                // CreateProcessW inherits this process's token; an elevated FileCat must never turn a
                // failed low-integrity start into an administrator parser/handler. Unknown also refuses.
                if (lowIntegrity && !OrdinaryFallbackAllowed())
                    throw new Win32Exception("The Shell helper could not start on a low-integrity token that holds no " +
                        "administrative group or privilege, so it was not started with administrator rights instead.");
                low = false;
                pi = TryCreate(executable, arguments, childIn, childOut, lowToken: false, inheritParentEnvironment)
                     ?? throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Shell helper could not be started.");
            }
            var info = pi.Value;
            if (!AssignProcessToJobObject(job, info.hProcess))
            {
                int error = Marshal.GetLastPInvokeError();
                TerminateProcess(info.hProcess, 1);
                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
                throw new Win32Exception(error, "The Shell helper could not be restricted, so it was not started.");
            }
            ResumeThread(info.hThread);
            CloseHandle(info.hThread);
            CloseHandle(childIn);
            CloseHandle(childOut);
            childIn = childOut = 0;
            var input = new FileStream(new SafeFileHandle(parentIn, ownsHandle: true), FileAccess.Write, 1);
            var output = new FileStream(new SafeFileHandle(parentOut, ownsHandle: true), FileAccess.Read, 1);
            parentIn = parentOut = 0;
            return new RestrictedProcess(job, info.hProcess, (int)info.dwProcessId, input, output, low);
        }
        catch
        {
            foreach (var h in new[] { childIn, parentIn, parentOut, childOut }) if (h != 0) CloseHandle(h);
            CloseHandle(job);
            throw;
        }
    }

    private static bool OrdinaryFallbackAllowed()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token)) return false;
        try { return CanUseFallbackToken(token); }
        finally { CloseHandle(token); }
    }

    /// <summary>The ordinary fallback requires a non-elevated token at most medium integrity.</summary>
    internal static bool CanUseFallbackToken(nint token)
    {
        // Integrity can be lowered on a duplicated full administrator token without removing
        // its privileges. Never treat that token as safe for an ordinary worker fallback.
        int elevated = 0;
        if (!GetTokenInformation(token, TokenElevation, (nint)(&elevated), sizeof(int), out uint elevationSize) ||
            elevationSize != sizeof(int) || elevated != 0) return false;
        GetTokenInformation(token, TokenIntegrityLevel, 0, 0, out uint size);
        if (size == 0) return false;
        nint data = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, data, size, out _)) return false;
            nint sid = Marshal.ReadIntPtr(data);
            byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return count != 0 && unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)))) <= 8192;
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    private static nint CreateJob(long memoryLimitBytes)
    {
        nint job = CreateJobObjectW(0, 0);
        if (job == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Shell helper's job object could not be created.");
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION |
                             JOB_OBJECT_LIMIT_ACTIVE_PROCESS | JOB_OBJECT_LIMIT_PROCESS_MEMORY,
                ActiveProcessLimit = 1,
            },
            ProcessMemoryLimit = (nuint)memoryLimitBytes,
        };
        var ui = new JOBOBJECT_BASIC_UI_RESTRICTIONS
        {
            // HANDLES denies the worker every USER handle owned outside its job, so it cannot reach FileCat's own
            // windows: no posted messages, no reading their text, no driving the dialogs of the program that
            // started it.
            UIRestrictionsClass = JOB_OBJECT_UILIMIT_HANDLES | JOB_OBJECT_UILIMIT_DESKTOP | JOB_OBJECT_UILIMIT_DISPLAYSETTINGS |
                                  JOB_OBJECT_UILIMIT_EXITWINDOWS | JOB_OBJECT_UILIMIT_GLOBALATOMS | JOB_OBJECT_UILIMIT_READCLIPBOARD |
                                  JOB_OBJECT_UILIMIT_WRITECLIPBOARD | JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS,
        };
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)) ||
            !SetInformationJobObject(job, JobObjectBasicUIRestrictions, &ui, (uint)sizeof(JOBOBJECT_BASIC_UI_RESTRICTIONS)))
        {
            int error = Marshal.GetLastPInvokeError();
            CloseHandle(job);
            throw new Win32Exception(error, "The Shell helper's limits could not be set.");
        }
        return job;
    }

    /// <summary>
    /// Creates the helper suspended with only the pipe ends inherited; null when Windows refuses. The helper must be
    /// a windowless program: a console one needs a console, and that needs access to the window station, which a
    /// token holding no administrative group does not have in a session whose own token carries no logon SID.
    /// </summary>
    private static PROCESS_INFORMATION? TryCreate(string executable, string arguments, nint stdIn, nint stdOut,
        bool lowToken, bool inheritParentEnvironment)
    {
        nint token = 0, attributes = 0;
        try
        {
            if (lowToken && (token = LowIntegrityToken()) == 0) return null;
            nint size = 0;
            InitializeProcThreadAttributeList(0, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) return null;
            var handles = stackalloc nint[2] { stdIn, stdOut };
            if (!UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, handles, 2 * sizeof(nint), 0, 0)) return null;
            var si = new STARTUPINFOEXW
            {
                StartupInfo = new STARTUPINFOW
                {
                    cb = sizeof(STARTUPINFOEXW),
                    dwFlags = STARTF_USESTDHANDLES,
                    hStdInput = stdIn,
                    hStdOutput = stdOut,
                },
                lpAttributeList = attributes,
            };
            string commandLine = $"\"{executable}\" {arguments}";
            var cmd = commandLine.ToCharArray();
            var env = EnvironmentBlock(inheritParentEnvironment);
            uint flags = CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT;
            PROCESS_INFORMATION pi;
            fixed (char* c = cmd)
            fixed (char* e = env)
            {
                bool ok = lowToken
                    ? CreateProcessAsUserW(token, executable, c, 0, 0, true, flags, e, null, &si, &pi)
                    : CreateProcessW(executable, c, 0, 0, true, flags, e, null, &si, &pi);
                return ok ? pi : null;
            }
        }
        finally
        {
            if (attributes != 0)
            {
                DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
            if (token != 0) CloseHandle(token);
        }
    }

    /// <summary>
    /// A worker token with no administrator authority: an elevated FileCat's own UAC-filtered token where Windows
    /// offers one, with every privileged group made deny-only and every privilege but SeChangeNotifyPrivilege
    /// removed, then lowered to low integrity (S-1-16-4096). Zero unless that result verifies, because low
    /// integrity alone restricts writes: a token that still carries an enabled Administrators SID reads whatever
    /// administrators may read, and one that keeps its privileges can debug processes and bypass file security.
    /// </summary>
    private static nint LowIntegrityToken()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY, out var own)) return 0;
        nint limited = 0, restricted = 0, worker = 0;
        try
        {
            limited = FilteredToken(own);
            if (!CreateRestricted(limited != 0 ? limited : own, out restricted)) return 0;
            if (!DuplicateTokenEx(restricted, TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT,
                    0, SecurityImpersonation, TokenPrimary, out worker))
                return 0;
            if (!LowerIntegrity(worker) || !NoAdministratorAuthority(worker)) return 0;
            nint result = worker;
            worker = 0;
            return result;
        }
        finally
        {
            if (worker != 0) CloseHandle(worker);
            if (restricted != 0) CloseHandle(restricted);
            if (limited != 0) CloseHandle(limited);
            CloseHandle(own);
        }
    }

    /// <summary>The standard-user token UAC linked to an elevated one, or 0 when this session has none.</summary>
    private static nint FilteredToken(nint token)
    {
        int type = 0;
        if (!GetTokenInformation(token, TokenElevationType, (nint)(&type), sizeof(int), out uint typeSize) ||
            typeSize != sizeof(int) || type != TokenElevationTypeFull)
            return 0;
        nint linked = 0;
        if (!GetTokenInformation(token, TokenLinkedToken, (nint)(&linked), (uint)sizeof(nint), out uint size) ||
            size != sizeof(nint) || linked == 0)
            return 0;
        // The linked token is an impersonation token; a primary one is needed to start a process.
        if (!DuplicateTokenEx(linked, TOKEN_DUPLICATE | TOKEN_QUERY, 0, SecurityImpersonation, TokenPrimary, out var primary))
            primary = 0;
        CloseHandle(linked);
        return primary;
    }

    /// <summary>Drops every privilege but SeChangeNotifyPrivilege and makes the token's privileged groups deny-only.</summary>
    private static bool CreateRestricted(nint token, out nint restricted)
    {
        restricted = 0;
        var disable = PrivilegedGroups(token);
        try
        {
            var sidsToDisable = disable.ToArray();
            fixed (SID_AND_ATTRIBUTES* sids = sidsToDisable)
            {
                return CreateRestrictedToken(token, DISABLE_MAX_PRIVILEGE, (uint)sidsToDisable.Length, sids, 0, null, 0, null, out restricted);
            }
        }
        finally
        {
            foreach (var sid in disable) Marshal.FreeHGlobal(sid.Sid);
        }
    }

    /// <summary>
    /// The token's own group SIDs that grant administrative authority. Only SIDs the token actually carries are
    /// listed: CreateRestrictedToken refuses a SID that is not in the token.
    /// </summary>
    private static List<SID_AND_ATTRIBUTES> PrivilegedGroups(nint token)
    {
        var disable = new List<SID_AND_ATTRIBUTES>();
        if (!TokenInformation(token, TokenGroups, out nint groups, out _)) return disable;
        try
        {
            int count = Marshal.ReadInt32(groups);
            for (int i = 0; i < count; i++)
            {
                nint entry = groups + sizeof(nint) + i * (2 * sizeof(nint));
                nint sid = Marshal.ReadIntPtr(entry);
                if (!IsPrivilegedGroup(sid)) continue;
                uint length = GetLengthSid(sid);
                nint copy = Marshal.AllocHGlobal(checked((int)length));
                Buffer.MemoryCopy((void*)sid, (void*)copy, length, length);
                disable.Add(new SID_AND_ATTRIBUTES { Sid = copy, Attributes = 0 });
            }
            return disable;
        }
        finally { Marshal.FreeHGlobal(groups); }
    }

    /// <summary>True for the built-in groups whose members administer this computer.</summary>
    private static bool IsPrivilegedGroup(nint sid)
    {
        // S-1-5-32-<rid>: Administrators, Account/Server/Print/Backup operators, Power users, and the
        // configuration groups that can change networking, performance logging or Hyper-V.
        if (Marshal.ReadByte(sid, 1) != 2) return false; // two sub-authorities
        // SID_IDENTIFIER_AUTHORITY, six bytes most significant first: NT Authority is 5.
        for (int i = 0; i < 5; i++) if (Marshal.ReadByte(sid, 2 + i) != 0) return false;
        if (Marshal.ReadByte(sid, 7) != 5) return false;
        if (unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, 0))) != 32) return false;
        uint rid = unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, 1)));
        return rid is 544 or 548 or 549 or 550 or 551 or 547 or 556 or 557 or 559 or 578;
    }

    private static bool LowerIntegrity(nint token)
    {
        if (!ConvertStringSidToSidW(LowIntegritySid, out var sid)) return false;
        try
        {
            var label = new TOKEN_MANDATORY_LABEL { Sid = sid, Attributes = SE_GROUP_INTEGRITY };
            return SetTokenInformation(token, TokenIntegrityLevel, &label, (uint)(sizeof(TOKEN_MANDATORY_LABEL) + GetLengthSid(sid)));
        }
        finally { LocalFree(sid); }
    }

    /// <summary>
    /// Verifies the finished worker token: low integrity, no enabled administrative group and no privilege beyond
    /// SeChangeNotifyPrivilege. A worker is never started on a token that fails this.
    /// </summary>
    internal static bool NoAdministratorAuthority(nint token)
    {
        if (!TokenInformation(token, TokenIntegrityLevel, out nint level, out _)) return false;
        try
        {
            nint sid = Marshal.ReadIntPtr(level);
            byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            if (count == 0 || unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)))) > LowIntegrityLevel) return false;
        }
        finally { Marshal.FreeHGlobal(level); }

        if (!TokenInformation(token, TokenGroups, out nint groups, out _)) return false;
        try
        {
            int count = Marshal.ReadInt32(groups);
            for (int i = 0; i < count; i++)
            {
                nint entry = groups + sizeof(nint) + i * (2 * sizeof(nint));
                if ((Marshal.ReadInt32(entry, sizeof(nint)) & SE_GROUP_ENABLED) != 0 && IsPrivilegedGroup(Marshal.ReadIntPtr(entry)))
                    return false;
            }
        }
        finally { Marshal.FreeHGlobal(groups); }

        long notify = 0;
        if (!LookupPrivilegeValueW(null, "SeChangeNotifyPrivilege", out notify)) return false;
        if (!TokenInformation(token, TokenPrivileges, out nint privileges, out _)) return false;
        try
        {
            int count = Marshal.ReadInt32(privileges);
            for (int i = 0; i < count; i++)
            {
                // DWORD PrivilegeCount, then LUID_AND_ATTRIBUTES: two DWORDs of LUID and one of attributes.
                nint entry = privileges + sizeof(int) + i * 12;
                long luid = (uint)Marshal.ReadInt32(entry) | ((long)Marshal.ReadInt32(entry, 4) << 32);
                if (luid != notify) return false;
            }
            return true;
        }
        finally { Marshal.FreeHGlobal(privileges); }
    }

    /// <summary>Reads one variable-length token information class into unmanaged memory the caller frees.</summary>
    private static bool TokenInformation(nint token, int information, out nint data, out uint size)
    {
        data = 0;
        GetTokenInformation(token, information, 0, 0, out size);
        if (size == 0) return false;
        nint buffer = Marshal.AllocHGlobal(checked((int)size));
        if (!GetTokenInformation(token, information, buffer, size, out size))
        {
            Marshal.FreeHGlobal(buffer);
            return false;
        }
        data = buffer;
        return true;
    }

    /// <summary>Only the picture decoder drops the parent's variables; Shell helpers retain their existing profile.</summary>
    private static char[] EnvironmentBlock(bool inheritParentEnvironment)
    {
        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (inheritParentEnvironment)
        {
            foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
                if (e.Key is string k && k.Length > 0 && !k.Contains('=')) vars[k] = e.Value as string ?? string.Empty;
        }
        else
        {
            // An absolute apphost/host path finds its own runtime. Retain Windows' loader root, not the parent's
            // PATH, credentials, native loader overrides or user-profile settings, for hostile picture decoding.
            vars["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        }
        vars["DOTNET_EnableDiagnostics"] = "0";
        var sb = new StringBuilder();
        foreach (var (k, v) in vars) sb.Append(k).Append('=').Append(v).Append('\0');
        sb.Append('\0');
        return sb.ToString().ToCharArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Kill();
        try { Input.Dispose(); } catch (IOException) { }
        try { Output.Dispose(); } catch (IOException) { }
        CloseHandle(_process);
        CloseHandle(_job);
    }

    // ---- Win32 ----------------------------------------------------------------------------------------------

    private const uint HANDLE_FLAG_INHERIT = 1;
    private const uint CREATE_SUSPENDED = 0x4, CREATE_UNICODE_ENVIRONMENT = 0x400, EXTENDED_STARTUPINFO_PRESENT = 0x80000, CREATE_NO_WINDOW = 0x08000000;
    private const int STARTF_USESTDHANDLES = 0x100;
    private const nint PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x20002;
    private const int JobObjectBasicUIRestrictions = 4, JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x8, JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x100,
        JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x400, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const uint JOB_OBJECT_UILIMIT_HANDLES = 0x1,
        JOB_OBJECT_UILIMIT_READCLIPBOARD = 0x2, JOB_OBJECT_UILIMIT_WRITECLIPBOARD = 0x4, JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS = 0x8,
        JOB_OBJECT_UILIMIT_DISPLAYSETTINGS = 0x10, JOB_OBJECT_UILIMIT_GLOBALATOMS = 0x20, JOB_OBJECT_UILIMIT_DESKTOP = 0x40, JOB_OBJECT_UILIMIT_EXITWINDOWS = 0x80;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x1, TOKEN_DUPLICATE = 0x2, TOKEN_QUERY = 0x8, TOKEN_ADJUST_DEFAULT = 0x80;
    private const int SecurityImpersonation = 2, TokenPrimary = 1;
    private const int TokenGroups = 2, TokenPrivileges = 3, TokenElevationType = 18, TokenLinkedToken = 19,
        TokenElevation = 20, TokenIntegrityLevel = 25;
    private const int TokenElevationTypeFull = 2;
    private const uint SE_GROUP_INTEGRITY = 0x20;
    private const int SE_GROUP_ENABLED = 0x4;
    private const uint DISABLE_MAX_PRIVILEGE = 0x1;
    private const string LowIntegritySid = "S-1-16-4096";
    private const uint LowIntegrityLevel = 4096;

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public nint lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_UI_RESTRICTIONS
    {
        public uint UIRestrictionsClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOW
    {
        public int cb;
        public nint lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEXW
    {
        public STARTUPINFOW StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_MANDATORY_LABEL
    {
        public nint Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public nint Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out nint hReadPipe, out nint hWritePipe, SECURITY_ATTRIBUTES* lpPipeAttributes, uint nSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(nint hObject, uint dwMask, uint dwFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateJobObjectW(nint lpJobAttributes, nint lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint hJob, int jobObjectInformationClass, void* lpJobObjectInformation, uint cbJobObjectInformationLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(nint hJob, uint uExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint hProcess, uint uExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(nint hThread);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint lpAttributeList, int dwAttributeCount, int dwFlags, ref nint lpSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(nint lpAttributeList, uint dwFlags, nint attribute, void* lpValue, nint cbSize, nint lpPreviousValue, nint lpReturnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(nint lpAttributeList);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(string lpApplicationName, char* lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, void* lpEnvironment, string? lpCurrentDirectory,
        STARTUPINFOEXW* lpStartupInfo, PROCESS_INFORMATION* lpProcessInformation);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessAsUserW(nint hToken, string lpApplicationName, char* lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, void* lpEnvironment, string? lpCurrentDirectory,
        STARTUPINFOEXW* lpStartupInfo, PROCESS_INFORMATION* lpProcessInformation);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateTokenEx(nint hExistingToken, uint dwDesiredAccess, nint lpTokenAttributes, int impersonationLevel, int tokenType, out nint phNewToken);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSidToSidW(string stringSid, out nint sid);

    [LibraryImport("advapi32.dll")]
    private static partial uint GetLengthSid(nint pSid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetTokenInformation(nint tokenHandle, int tokenInformationClass, void* tokenInformation, uint tokenInformationLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, nint tokenInformation, uint tokenInformationLength, out uint returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateRestrictedToken(nint existingTokenHandle, uint flags, uint disableSidCount,
        SID_AND_ATTRIBUTES* sidsToDisable, uint deletePrivilegeCount, LUID_AND_ATTRIBUTES* privilegesToDelete,
        uint restrictedSidCount, SID_AND_ATTRIBUTES* sidsToRestrict, out nint newTokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? systemName, string name, out long luid);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthority(nint sid, uint index);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthorityCount(nint sid);
}
