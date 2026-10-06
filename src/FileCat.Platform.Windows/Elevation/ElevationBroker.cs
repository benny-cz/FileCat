using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.Jobs;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Native.NativeMethods;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>
/// The per-plan administrator broker (ADR-14): FileCat.PrivilegedHost.exe beside FileCat.exe in Program Files. One UAC
/// consent launches it for one plan; it displays the plan, runs it, and exits. It is unavailable in portable mode and
/// in any build that is not installed in an administrator-protected folder.
/// </summary>
public static partial class ElevationBroker
{
    public const string ExecutableName = "FileCat.PrivilegedHost.exe";
    public const string RequesterExecutableName = "FileCat.exe";
    private const string NonceKey = @"SOFTWARE\FileCat\PrivilegedHost\UsedPlans";

    /// <summary>The installed broker, or null and why administrator retry is unavailable here.</summary>
    public static string? Locate(bool portable, out string? reason)
    {
        reason = null;
        if (!OperatingSystem.IsWindows())
        {
            reason = "Administrator retry is available on Windows.";
            return null;
        }
        if (portable)
        {
            reason = "Portable FileCat cannot run operations as administrator: its files are in a folder you can change, so a helper there could be replaced (ADR-14). The installed FileCat offers administrator retry.";
            return null;
        }
        string broker = Path.Combine(AppContext.BaseDirectory, ExecutableName);
        if (!File.Exists(broker))
        {
            reason = "This FileCat build has no administrator helper; administrator retry is part of the installed FileCat.";
            return null;
        }
        if (!IsProtectedLocation(broker))
        {
            reason = "FileCat's administrator helper runs only from the installed program folder (Program Files), where other programs cannot replace it.";
            return null;
        }
        return broker;
    }

    /// <summary>True when the file really lives under Program Files (links on the way are resolved first).</summary>
    public static bool IsProtectedLocation(string file)
    {
        string? final = FinalDosPath(file);
        if (final is null) return false;
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string root = Environment.GetFolderPath(folder);
            if (root.Length == 0) continue;
            string finalRoot = FinalDosPath(root) ?? root;
            if (final.StartsWith(finalRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? FinalDosPath(string path)
    {
        using var handle = CreateFileForQuery(path, 0, 7, 0, 3, 0x02000000, 0);
        if (handle.IsInvalid) return null;
        string? final = ElevationPaths.FinalPath(handle, 0);
        if (final is null || final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return null;
        return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
    }

    /// <summary>
    /// Defense in depth for the broker: the plan must come from the installed FileCat of the user it names. A same-user
    /// program can still ask; the displayed plan is the consent boundary (ADR-14), not this check.
    /// </summary>
    public static string? CheckRequester(ElevationPlan plan, string brokerDirectory)
    {
        using var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)plan.RequesterProcessId);
        if (process.IsInvalid) return "The FileCat window that asked for this operation is no longer running.";
        string? image = ImagePath(process);
        if (image is null) return "The requesting program could not be identified.";
        string expected = Path.Combine(brokerDirectory, RequesterExecutableName);
        if (!string.Equals(image, expected, StringComparison.OrdinalIgnoreCase))
            return "The request did not come from the installed FileCat.";
        if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out var token)) return "The requesting user could not be identified.";
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return string.Equals(identity.User?.Value, plan.UserSid, StringComparison.OrdinalIgnoreCase)
                ? null : "The plan names a different user than the FileCat that asked for it.";
    }

    private static unsafe string? ImagePath(SafeProcessHandle process)
    {
        var image = new char[1024];
        uint size = (uint)image.Length;
        bool ok;
        fixed (char* chars = image) ok = QueryFullProcessImageName(process, 0, chars, ref size);
        return ok ? new string(image, 0, (int)size) : null;
    }

    /// <summary>A plan runs at most once: its identifier is recorded in HKLM, which only administrators can change.</summary>
    public static bool TryClaimNonce(string nonce, DateTime nowUtc)
    {
        using var key = Registry.LocalMachine.CreateSubKey(NonceKey, writable: true);
        foreach (var name in key.GetValueNames())
            if (key.GetValue(name) is long stamp && DateTime.FromFileTimeUtc(stamp) < nowUtc.AddDays(-2)) key.DeleteValue(name, throwOnMissingValue: false);
        if (key.GetValue(nonce) is not null) return false;
        key.SetValue(nonce, nowUtc.ToFileTimeUtc(), RegistryValueKind.QWord);
        return true;
    }

    /// <summary>
    /// Starts the broker through UAC with the plan's volume path and hash. Throws <see cref="OperationCanceledException"/>
    /// when the user declines the Windows prompt (nothing was changed).
    /// </summary>
    public static ElevatedProcess Launch(string broker, string volumePlanPath, string hash, nint owner)
    {
        string arguments = $"--plan \"{volumePlanPath}\" --sha256 {hash}";
        var verb = Marshal.StringToHGlobalUni("runas");
        var file = Marshal.StringToHGlobalUni(broker);
        var parameters = Marshal.StringToHGlobalUni(arguments);
        // The broker starts in its own folder, never in a folder the requester chose.
        var directory = Marshal.StringToHGlobalUni(Path.GetDirectoryName(broker)!);
        try
        {
            var info = new SHELLEXECUTEINFOW
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFOW>(),
                fMask = 0x00000040 /* SEE_MASK_NOCLOSEPROCESS */ | SEE_MASK_NOASYNC,
                hwnd = owner,
                lpVerb = verb,
                lpFile = file,
                lpParameters = parameters,
                lpDirectory = directory,
                nShow = 1,
            };
            if (!ShellExecuteEx(ref info))
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == 1223) throw new OperationCanceledException("Windows did not get administrator approval, so nothing was changed.");
                throw new Win32Exception(error);
            }
            if (info.hProcess == 0) throw new IOException("The administrator helper did not start.");
            return new ElevatedProcess(new SafeProcessHandle(info.hProcess, ownsHandle: true));
        }
        finally
        {
            Marshal.FreeHGlobal(verb);
            Marshal.FreeHGlobal(file);
            Marshal.FreeHGlobal(parameters);
            Marshal.FreeHGlobal(directory);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileForQuery(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* name, ref uint size);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}

/// <summary>The running broker process; it is elevated, so FileCat can only wait for it and ask it to stop.</summary>
public sealed partial class ElevatedProcess(SafeProcessHandle handle) : IDisposable
{
    public bool WaitForExit(int milliseconds) => WaitForSingleObject(handle, (uint)milliseconds) == 0;

    public int ExitCode => GetExitCodeProcess(handle, out uint code) ? unchecked((int)code) : -1;

    /// <summary>Authenticate a read-session server before sending it any protocol request.</summary>
    internal void VerifyReadServer(SafePipeHandle pipe)
    {
        // Keep the runas process handle alive: a name/nonce or a separately opened PID is not the launched peer.
        uint expected = GetProcessId(handle);
        if (expected == 0 || WaitForSingleObject(handle, 0) != 0x102 /* WAIT_TIMEOUT */ ||
            !GetNamedPipeServerProcessId(pipe, out uint actual) || actual != expected)
            throw new IOException("The administrator helper's read session could not be verified. Nothing was read.");
    }

    public void Dispose() => handle.Dispose();

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeProcessHandle handle, out uint code);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetProcessId(SafeProcessHandle handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
