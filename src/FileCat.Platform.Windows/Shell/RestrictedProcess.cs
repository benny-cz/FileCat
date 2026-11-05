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
/// its standard pipe ends (plus the archive-only diagnostics and cancellation ends) are inherited, and it starts
/// suspended, so none of its code runs outside the job. This is a
/// reduced-authority worker, not a verified sandbox: it keeps the filesystem and network reach of an ordinary user.
/// </summary>
internal sealed unsafe partial class RestrictedProcess : IDisposable
{
    private readonly nint _job;
    private readonly nint _process;
    private bool _disposed;
    private FileStream? _cancellation;

    private RestrictedProcess(nint job, nint process, int id, FileStream input, FileStream output, bool lowIntegrity,
        FileStream? error, FileStream? cancellation)
    {
        _job = job;
        _process = process;
        ProcessId = id;
        Input = input;
        Output = output;
        LowIntegrity = lowIntegrity;
        Error = error;
        _cancellation = cancellation;
    }

    /// <summary>Written to the helper's standard input.</summary>
    public FileStream Input { get; }

    /// <summary>Read from the helper's standard output.</summary>
    public FileStream Output { get; }

    internal FileStream? Error { get; }

    public int ProcessId { get; }

    /// <summary>True when the helper was started with a low-integrity token.</summary>
    public bool LowIntegrity { get; }

    public bool HasExited => WaitForSingleObject(_process, 0) == 0;

    internal uint ExitCode => GetExitCodeProcess(_process, out uint code) ? code : throw new Win32Exception(Marshal.GetLastPInvokeError());

    internal TimeSpan TotalProcessorTime => GetProcessTimes(_process, out _, out _, out long kernel, out long user)
        ? TimeSpan.FromTicks(checked(kernel + user)) : throw new Win32Exception(Marshal.GetLastPInvokeError());

    /// <summary>Ends the helper and anything in its job at once.</summary>
    public void Kill() => TerminateJobObject(_job, 1);
    internal void RequestCancellation() => Interlocked.Exchange(ref _cancellation, null)?.Dispose();

    public static RestrictedProcess Start(string executable, string arguments, bool lowIntegrity, long memoryLimitBytes,
        bool inheritParentEnvironment = true, nint appContainerSid = 0, long cpuTimeTicks = 0)
    {
        if (appContainerSid != 0 && (!lowIntegrity || inheritParentEnvironment))
            throw new ArgumentException("An archive AppContainer requires a restricted token and a clean environment.");
        nint job = CreateJob(memoryLimitBytes, cpuTimeTicks);
        nint childIn = 0, parentIn = 0, parentOut = 0, childOut = 0, parentError = 0, childError = 0, childCancel = 0, parentCancel = 0;
        nint process = 0, thread = 0;
        FileStream? input = null, output = null, errorStream = null, cancellationStream = null;
        try
        {
            var inherit = new SECURITY_ATTRIBUTES { nLength = sizeof(SECURITY_ATTRIBUTES), bInheritHandle = 1 };
            if (!CreatePipe(out childIn, out parentIn, &inherit, 0) || !CreatePipe(out parentOut, out childOut, &inherit, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Shell helper's pipes could not be created.");
            if (appContainerSid != 0 && !CreatePipe(out parentError, out childError, &inherit, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The archive worker's error pipe could not be created.");
            if (appContainerSid != 0 && !CreatePipe(out childCancel, out parentCancel, &inherit, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The archive worker's cancellation pipe could not be created.");
            // FileCat's own ends are never inherited.
            if (!SetHandleInformation(parentIn, HANDLE_FLAG_INHERIT, 0) ||
                !SetHandleInformation(parentOut, HANDLE_FLAG_INHERIT, 0) ||
                (parentError != 0 && !SetHandleInformation(parentError, HANDLE_FLAG_INHERIT, 0)) ||
                (parentCancel != 0 && !SetHandleInformation(parentCancel, HANDLE_FLAG_INHERIT, 0)))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The parent's worker pipe ends could not be protected.");

            if (childCancel != 0) arguments += " " + FileCat.Core.Archives.ArchiveWorkerCancellation.Argument + " " +
                childCancel.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bool low = lowIntegrity;
            var pi = low ? TryCreate(executable, arguments, childIn, childOut, lowToken: true, inheritParentEnvironment,
                appContainerSid, childError, childCancel) : null;
            if (pi is null)
            {
                if (appContainerSid != 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    throw new Win32Exception(error, $"The archive AppContainer could not be started (Windows error {error}). No unrestricted worker was started.");
                }
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
            process = info.hProcess;
            thread = info.hThread;
            if (appContainerSid != 0 && !VerifyArchiveContainerToken(info.hProcess, appContainerSid, out string tokenRefusal))
            {
                throw new Win32Exception($"The suspended archive worker's boundary could not be verified: {tokenRefusal}");
            }
            if (!AssignProcessToJobObject(job, info.hProcess))
            {
                int error = Marshal.GetLastPInvokeError();
                throw new Win32Exception(error, "The Shell helper could not be restricted, so it was not started.");
            }
            if (ResumeThread(info.hThread) == uint.MaxValue)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new Win32Exception(error, "The restricted worker could not be resumed.");
            }
            CloseHandle(info.hThread);
            thread = 0;
            CloseHandle(childIn);
            CloseHandle(childOut);
            childIn = childOut = 0;
            if (childError != 0) { CloseHandle(childError); childError = 0; }
            if (childCancel != 0) { CloseHandle(childCancel); childCancel = 0; }
            input = TakePipe(ref parentIn, FileAccess.Write);
            output = TakePipe(ref parentOut, FileAccess.Read);
            errorStream = parentError == 0 ? null : TakePipe(ref parentError, FileAccess.Read);
            cancellationStream = parentCancel == 0 ? null : TakePipe(ref parentCancel, FileAccess.Write);
            return new RestrictedProcess(job, info.hProcess, (int)info.dwProcessId, input, output, low, errorStream, cancellationStream);
        }
        catch
        {
            if (process != 0) TerminateProcess(process, 1);
            foreach (var h in new[] { childIn, parentIn, parentOut, childOut, parentError, childError, childCancel, parentCancel }) if (h != 0) CloseHandle(h);
            try { input?.Dispose(); } catch (IOException) { }
            try { output?.Dispose(); } catch (IOException) { }
            try { errorStream?.Dispose(); } catch (IOException) { }
            try { cancellationStream?.Dispose(); } catch (IOException) { }
            if (thread != 0) CloseHandle(thread);
            CloseHandle(job);
            if (process != 0) { WaitForSingleObject(process, 5000); CloseHandle(process); }
            throw;
        }
    }

    private static FileStream TakePipe(ref nint raw, FileAccess access)
    {
        var handle = new SafeFileHandle(raw, ownsHandle: true);
        raw = 0;
        try { return new FileStream(handle, access, 1); }
        catch { handle.Dispose(); throw; }
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

    private static nint CreateJob(long memoryLimitBytes, long cpuTimeTicks = 0)
    {
        nint job = CreateJobObjectW(0, 0);
        if (job == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Shell helper's job object could not be created.");
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION |
                             JOB_OBJECT_LIMIT_ACTIVE_PROCESS | JOB_OBJECT_LIMIT_PROCESS_MEMORY |
                             (cpuTimeTicks > 0 ? JOB_OBJECT_LIMIT_PROCESS_TIME : 0),
                ActiveProcessLimit = 1,
                PerProcessUserTimeLimit = cpuTimeTicks,
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
        bool lowToken, bool inheritParentEnvironment, nint appContainerSid = 0, nint stdError = 0, nint cancellation = 0)
    {
        nint token = 0, attributes = 0;
        bool attributesInitialized = false;
        try
        {
            if (lowToken && (token = appContainerSid != 0 ? CreateWorkerToken(appContainerBase: true) : LowIntegrityToken()) == 0)
            {
                if (appContainerSid != 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The archive AppContainer base token could not be prepared.");
                return null;
            }
            nint size = 0;
            int attributeCount = appContainerSid == 0 ? 1 : 4;
            InitializeProcThreadAttributeList(0, attributeCount, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributes, attributeCount, 0, ref size)) return null;
            attributesInitialized = true;
            var handles = stackalloc nint[4] { stdIn, stdOut, stdError, cancellation };
            if (!UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, handles,
                    (cancellation != 0 ? 4 : stdError == 0 ? 2 : 3) * sizeof(nint), 0, 0)) return null;
            var capabilities = new SECURITY_CAPABILITIES { AppContainerSid = appContainerSid };
            uint allPackagesPolicy = 1, childPolicy = 1;
            if (appContainerSid != 0 &&
                (!UpdateProcThreadAttribute(attributes, 0, 0x20009, &capabilities, sizeof(SECURITY_CAPABILITIES), 0, 0) ||
                 !UpdateProcThreadAttribute(attributes, 0, 0x2000f, &allPackagesPolicy, sizeof(uint), 0, 0) ||
                 !UpdateProcThreadAttribute(attributes, 0, 0x2000e, &childPolicy, sizeof(uint), 0, 0)))
                return null;
            var si = new STARTUPINFOEXW
            {
                StartupInfo = new STARTUPINFOW
                {
                    cb = sizeof(STARTUPINFOEXW),
                    dwFlags = STARTF_USESTDHANDLES,
                    hStdInput = stdIn,
                    hStdOutput = stdOut,
                    hStdError = stdError,
                },
                lpAttributeList = attributes,
            };
            string commandLine = $"\"{executable}\" {arguments}";
            var cmd = (commandLine + '\0').ToCharArray(); // CreateProcess requires a writable, NUL-terminated buffer.
            var env = EnvironmentBlock(inheritParentEnvironment, appContainerSid != 0);
            string? workingDirectory = appContainerSid == 0 ? null : Path.GetDirectoryName(executable);
            uint flags = CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT;
            PROCESS_INFORMATION pi;
            fixed (char* c = cmd)
            fixed (char* e = env)
            {
                bool ok = lowToken
                    ? CreateProcessAsUserW(token, executable, c, 0, 0, true, flags, e, workingDirectory, &si, &pi)
                    : CreateProcessW(executable, c, 0, 0, true, flags, e, workingDirectory, &si, &pi);
                if (!ok && appContainerSid != 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    throw new Win32Exception(error, $"CreateProcessAsUser refused the archive AppContainer (Windows error {error}).");
                }
                return ok ? pi : null;
            }
        }
        finally
        {
            int creationError = Marshal.GetLastPInvokeError();
            if (attributes != 0)
            {
                if (attributesInitialized) DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
            if (token != 0) CloseHandle(token);
            Marshal.SetLastPInvokeError(creationError);
        }
    }

    /// <summary>
    /// A worker token with no administrator authority: an elevated FileCat's own UAC-filtered token where Windows
    /// offers one, with every privileged group made deny-only and every privilege but SeChangeNotifyPrivilege
    /// removed, then lowered to low integrity (S-1-16-4096). Zero unless that result verifies, because low
    /// integrity alone restricts writes: a token that still carries an enabled Administrators SID reads whatever
    /// administrators may read, and one that keeps its privileges can debug processes and bypass file security.
    /// </summary>
    private static nint LowIntegrityToken() => CreateWorkerToken(appContainerBase: false);

    private static nint CreateWorkerToken(bool appContainerBase)
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
            if (!SetIntegrity(worker, appContainerBase ? "S-1-16-8192" : LowIntegritySid) ||
                !VerifyNoAdministratorAuthority(worker, appContainerBase ? 8192u : LowIntegrityLevel)) return 0;
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

    private static bool SetIntegrity(nint token, string integritySid)
    {
        if (!ConvertStringSidToSidW(integritySid, out var sid)) return false;
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
    internal static bool NoAdministratorAuthority(nint token) => VerifyNoAdministratorAuthority(token, LowIntegrityLevel);

    private static bool VerifyNoAdministratorAuthority(nint token, uint maximumIntegrity) =>
        VerifyNoAdministratorAuthority(token, maximumIntegrity, out _);

    private static bool VerifyNoAdministratorAuthority(nint token, uint maximumIntegrity, out string refusal, long? permittedNotifyPrivilege = null)
    {
        refusal = "The token integrity could not be read.";
        if (!TokenInformation(token, TokenIntegrityLevel, out nint level, out _)) return false;
        try
        {
            nint sid = Marshal.ReadIntPtr(level);
            byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            if (count == 0) return false;
            uint integrity = unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))));
            if (integrity > maximumIntegrity) { refusal = $"The token integrity is {integrity}, above {maximumIntegrity}."; return false; }
        }
        finally { Marshal.FreeHGlobal(level); }

        refusal = "The token groups could not be read.";
        if (!TokenInformation(token, TokenGroups, out nint groups, out _)) return false;
        try
        {
            int count = Marshal.ReadInt32(groups);
            for (int i = 0; i < count; i++)
            {
                nint entry = groups + sizeof(nint) + i * (2 * sizeof(nint));
                if ((Marshal.ReadInt32(entry, sizeof(nint)) & SE_GROUP_ENABLED) != 0 && IsPrivilegedGroup(Marshal.ReadIntPtr(entry)))
                { refusal = "The token retained an enabled administrative group."; return false; }
            }
        }
        finally { Marshal.FreeHGlobal(groups); }

        long notify = 0;
        refusal = "The token privileges could not be read.";
        if (!TokenInformation(token, TokenPrivileges, out nint privileges, out uint privilegeBytes)) return false;
        try
        {
            if (privilegeBytes < sizeof(int)) return false;
            int count = Marshal.ReadInt32(privileges);
            if (count < 0 || count > (privilegeBytes - sizeof(int)) / 12) return false;
            // A real AppContainer can have no privileges and cannot resolve LSA privilege names. Its empty
            // list already proves this condition; no privileged lookup is needed to verify an empty set.
            if (count == 0) { refusal = ""; return true; }
            refusal = "The worker's one permitted privilege could not be resolved.";
            if (permittedNotifyPrivilege is { } known) notify = known;
            else if (!LookupPrivilegeValueW(null, "SeChangeNotifyPrivilege", out notify)) return false;
            for (int i = 0; i < count; i++)
            {
                // DWORD PrivilegeCount, then LUID_AND_ATTRIBUTES: two DWORDs of LUID and one of attributes.
                nint entry = privileges + sizeof(int) + i * 12;
                long luid = (uint)Marshal.ReadInt32(entry) | ((long)Marshal.ReadInt32(entry, 4) << 32);
                if (luid != notify) { refusal = $"The token retained an unexpected privilege (LUID {luid})."; return false; }
            }
            refusal = "";
            return true;
        }
        finally { Marshal.FreeHGlobal(privileges); }
    }

    internal static bool VerifyArchiveContainerToken(nint process, nint expectedSid, out string refusal, long? permittedNotifyPrivilege = null)
    {
        refusal = "The suspended process token could not be opened.";
        if (!OpenProcessToken(process, TOKEN_QUERY | TOKEN_DUPLICATE, out nint token)) return false;
        try
        {
            int appContainer = 0;
            refusal = "The process is not a verified AppContainer.";
            if (!GetTokenInformation(token, 29, (nint)(&appContainer), sizeof(int), out uint size) ||
                size != sizeof(int) || appContainer != 1) return false;
            refusal = "The process AppContainer identity does not match the archive profile.";
            if (!TokenInformation(token, 31, out nint container, out uint identityBytes)) return false;
            try { if (identityBytes < sizeof(nint) || !EqualSid(Marshal.ReadIntPtr(container), expectedSid)) return false; }
            finally { Marshal.FreeHGlobal(container); }
            refusal = "The process capabilities could not be read.";
            if (!TokenInformation(token, 30, out nint capabilities, out uint bytes)) return false;
            try
            {
                if (bytes < sizeof(uint)) return false;
                int count = Marshal.ReadInt32(capabilities);
                if (count != 0) { refusal = $"The process has {count} unexpected capabilities."; return false; }
            }
            finally { Marshal.FreeHGlobal(capabilities); }
            uint mandatoryPolicy = 0;
            refusal = "The archive worker does not enforce mandatory no-write-up policy.";
            if (!GetTokenInformation(token, 27, (nint)(&mandatoryPolicy), sizeof(uint), out size) ||
                size != sizeof(uint) || (mandatoryPolicy & 1) == 0) return false;
            return VerifyNoAdministratorAuthority(token, LowIntegrityLevel, out refusal, permittedNotifyPrivilege) &&
                VerifyLessPrivilegedAccess(token, expectedSid, out refusal);
        }
        finally { CloseHandle(token); }
    }

    internal static long NotifyPrivilege()
    {
        if (!LookupPrivilegeValueW(null, "SeChangeNotifyPrivilege", out long value)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return value;
    }

    internal static bool VerifyCurrentArchiveJob()
    {
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = default;
        JOBOBJECT_BASIC_UI_RESTRICTIONS ui = default;
        uint required = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_ACTIVE_PROCESS |
            JOB_OBJECT_LIMIT_PROCESS_MEMORY | JOB_OBJECT_LIMIT_PROCESS_TIME;
        return QueryInformationJobObject(0, JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION), out uint bytes) &&
            bytes == sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION) &&
            (limits.BasicLimitInformation.LimitFlags & required) == required && limits.BasicLimitInformation.ActiveProcessLimit == 1 &&
            limits.ProcessMemoryLimit <= (nuint)FileCat.Core.Archives.ArchiveWritePolicy.MaxWorkerMemoryBytes && limits.ProcessMemoryLimit > 0 &&
            limits.BasicLimitInformation.PerProcessUserTimeLimit is > 0 && limits.BasicLimitInformation.PerProcessUserTimeLimit <= TimeSpan.FromHours(2).Ticks &&
            QueryInformationJobObject(0, JobObjectBasicUIRestrictions, &ui, (uint)sizeof(JOBOBJECT_BASIC_UI_RESTRICTIONS), out bytes) &&
            bytes == sizeof(JOBOBJECT_BASIC_UI_RESTRICTIONS) && (ui.UIRestrictionsClass & 0xff) == 0xff;
    }

    /// <summary>Reads one variable-length token information class into unmanaged memory the caller frees.</summary>
    private static bool TokenInformation(nint token, int information, out nint data, out uint size)
    {
        data = 0;
        GetTokenInformation(token, information, 0, 0, out size);
        if (size == 0 || size > 1 << 20) return false;
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
    private static char[] EnvironmentBlock(bool inheritParentEnvironment, bool appContainer = false)
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
            if (appContainer)
            {
                // Windows constructs the AppContainer's redirected profile/temp locations from these OS-known
                // folders. They carry no file authority and are not inherited loader/credential variables.
                vars["USERPROFILE"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                vars["LOCALAPPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                vars["APPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                vars["TEMP"] = vars["TMP"] = Path.GetTempPath();
            }
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
        try { RequestCancellation(); } catch (IOException) { }
        try { Input.Dispose(); } catch (IOException) { }
        try { Output.Dispose(); } catch (IOException) { }
        try { Error?.Dispose(); } catch (IOException) { }
        CloseHandle(_process);
        CloseHandle(_job);
    }

    // ---- Win32 ----------------------------------------------------------------------------------------------

    private const uint HANDLE_FLAG_INHERIT = 1;
    private const uint CREATE_SUSPENDED = 0x4, CREATE_UNICODE_ENVIRONMENT = 0x400, EXTENDED_STARTUPINFO_PRESENT = 0x80000, CREATE_NO_WINDOW = 0x08000000;
    private const int STARTF_USESTDHANDLES = 0x100;
    private const nint PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x20002;
    private const int JobObjectBasicUIRestrictions = 4, JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_PROCESS_TIME = 0x2, JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x8, JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x100,
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
    private struct SECURITY_CAPABILITIES
    {
        public nint AppContainerSid;
        public nint Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
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
    private static partial bool QueryInformationJobObject(nint job, int informationClass, void* information, uint length, out uint returned);

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
    private static partial bool GetExitCodeProcess(nint process, out uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

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

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EqualSid(nint first, nint second);
}
