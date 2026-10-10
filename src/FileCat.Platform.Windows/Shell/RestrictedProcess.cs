using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Shell;

/// <summary>
/// Starts a helper that runs third-party code (plan §6.2, §8.2, TV-16) at low integrity where Windows allows it, inside
/// a job object that ends it together with FileCat, allows it no child processes, caps its memory, and denies it the
/// clipboard, global atoms, and desktop, display, or shutdown changes. Only its two pipe ends are inherited, and it
/// starts suspended, so none of its code runs outside the job.
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

    public static RestrictedProcess Start(string executable, string arguments, bool lowIntegrity, long memoryLimitBytes)
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
            var pi = low ? TryCreate(executable, arguments, childIn, childOut, lowToken: true) : null;
            if (pi is null)
            {
                // CreateProcessW inherits this process's token; an elevated FileCat must never turn a
                // failed low-integrity start into an administrator parser/handler. Unknown also refuses.
                if (lowIntegrity && !OrdinaryFallbackAllowed())
                    throw new Win32Exception("The Shell helper could not start at low integrity, so it was not started with administrator rights.");
                low = false;
                pi = TryCreate(executable, arguments, childIn, childOut, lowToken: false)
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

    /// <summary>The ordinary fallback requires a positively queried process token at most medium integrity.</summary>
    internal static bool CanUseFallbackToken(nint token)
    {
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
            UIRestrictionsClass = JOB_OBJECT_UILIMIT_DESKTOP | JOB_OBJECT_UILIMIT_DISPLAYSETTINGS | JOB_OBJECT_UILIMIT_EXITWINDOWS |
                                  JOB_OBJECT_UILIMIT_GLOBALATOMS | JOB_OBJECT_UILIMIT_READCLIPBOARD | JOB_OBJECT_UILIMIT_WRITECLIPBOARD |
                                  JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS,
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

    /// <summary>Creates the helper suspended with only the pipe ends inherited; null when Windows refuses.</summary>
    private static PROCESS_INFORMATION? TryCreate(string executable, string arguments, nint stdIn, nint stdOut, bool lowToken)
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
            var env = EnvironmentBlock();
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

    /// <summary>A copy of FileCat's own token lowered to low integrity (S-1-16-4096), or 0.</summary>
    private static nint LowIntegrityToken()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT, out var own)) return 0;
        try
        {
            if (!DuplicateTokenEx(own, TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT, 0, SecurityImpersonation, TokenPrimary, out var copy))
                return 0;
            if (!ConvertStringSidToSidW("S-1-16-4096", out var sid))
            {
                CloseHandle(copy);
                return 0;
            }
            try
            {
                var label = new TOKEN_MANDATORY_LABEL { Sid = sid, Attributes = SE_GROUP_INTEGRITY };
                if (!SetTokenInformation(copy, TokenIntegrityLevel, &label, (uint)(sizeof(TOKEN_MANDATORY_LABEL) + GetLengthSid(sid))))
                {
                    CloseHandle(copy);
                    return 0;
                }
                return copy;
            }
            finally
            {
                LocalFree(sid);
            }
        }
        finally
        {
            CloseHandle(own);
        }
    }

    /// <summary>FileCat's environment without diagnostics endpoints for the helper.</summary>
    private static char[] EnvironmentBlock()
    {
        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            if (e.Key is string k && k.Length > 0 && !k.Contains('=')) vars[k] = e.Value as string ?? string.Empty;
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
    private const uint JOB_OBJECT_UILIMIT_READCLIPBOARD = 0x2, JOB_OBJECT_UILIMIT_WRITECLIPBOARD = 0x4, JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS = 0x8,
        JOB_OBJECT_UILIMIT_DISPLAYSETTINGS = 0x10, JOB_OBJECT_UILIMIT_GLOBALATOMS = 0x20, JOB_OBJECT_UILIMIT_DESKTOP = 0x40, JOB_OBJECT_UILIMIT_EXITWINDOWS = 0x80;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x1, TOKEN_DUPLICATE = 0x2, TOKEN_QUERY = 0x8, TOKEN_ADJUST_DEFAULT = 0x80;
    private const int SecurityImpersonation = 2, TokenPrimary = 1, TokenIntegrityLevel = 25;
    private const uint SE_GROUP_INTEGRITY = 0x20;

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

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthority(nint sid, uint index);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthorityCount(nint sid);
}
