using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Platform;
using FileCat.Core.Tools;
using static FileCat.Platform.Windows.Native.NativeMethods;

namespace FileCat.Platform.Windows;

/// <summary>
/// Windows Shell integration without running third-party handlers on file content (AI-14): icons and
/// type names come from the extension only (SHGFI_USEFILEATTRIBUTES), and nothing here parses files.
/// </summary>
public sealed unsafe class WindowsShellServices : PortableShellServices
{
    public override void Open(string path)
    {
        // The OS applies its normal security prompts (e.g. Mark-of-the-Web warnings) when opening.
        var psi = new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty };
        Process.Start(psi)?.Dispose();
    }

    public override void Reveal(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        if (SHParseDisplayName(trimmed, 0, out var pidl, 0, out _) == 0 && pidl != 0)
        {
            try
            {
                if (SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0) return;
            }
            finally
            {
                CoTaskMemFree(pidl);
            }
        }
        var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false, Arguments = $"/select,\"{trimmed}\"" };
        Process.Start(psi)?.Dispose();
    }

    public override bool ShowProperties(string path)
    {
        var verb = Marshal.StringToHGlobalUni("properties");
        var file = Marshal.StringToHGlobalUni(path);
        try
        {
            var info = new SHELLEXECUTEINFOW
            {
                cbSize = sizeof(SHELLEXECUTEINFOW),
                fMask = SEE_MASK_INVOKEIDLIST | SEE_MASK_FLAG_NO_UI,
                lpVerb = verb,
                lpFile = file,
                nShow = 1,
            };
            return ShellExecuteEx(ref info);
        }
        finally
        {
            Marshal.FreeHGlobal(verb);
            Marshal.FreeHGlobal(file);
        }
    }

    public override bool TryGetTypeIcon(string name, bool isDirectory, int size, out int width, out int height, out byte[] bgra)
    {
        width = height = 0;
        bgra = [];
        var info = new SHFILEINFOW();
        uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var r = SHGetFileInfo(name, attrs, ref info, (uint)sizeof(SHFILEINFOW), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
        if (r == 0 || info.hIcon == 0) return false;
        try
        {
            return IconToBgra(info.hIcon, out width, out height, out bgra);
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static bool IconToBgra(nint hIcon, out int width, out int height, out byte[] bgra)
    {
        width = height = 0;
        bgra = [];
        if (!GetIconInfo(hIcon, out var ii)) return false;
        try
        {
            if (ii.hbmColor == 0) return false;
            BITMAP bm;
            if (GetObject(ii.hbmColor, sizeof(BITMAP), &bm) == 0) return false;
            int w = bm.bmWidth, h = bm.bmHeight;
            var pixels = new byte[w * h * 4];
            var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var hdc = GetDC(0);
            try
            {
                fixed (byte* p = pixels)
                {
                    if (GetDIBits(hdc, ii.hbmColor, 0, (uint)h, p, &header, 0) == 0) return false;
                }
                bool hasAlpha = false;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0) { hasAlpha = true; break; }
                }
                if (!hasAlpha && ii.hbmMask != 0)
                {
                    var mask = new byte[w * h * 4];
                    var mh = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                    fixed (byte* m = mask)
                    {
                        GetDIBits(hdc, ii.hbmMask, 0, (uint)h, m, &mh, 0);
                    }
                    for (int i = 0; i < w * h; i++) pixels[i * 4 + 3] = mask[i * 4] == 0 ? (byte)255 : (byte)0;
                }
            }
            finally
            {
                ReleaseDC(0, hdc);
            }
            width = w;
            height = h;
            bgra = pixels;
            return true;
        }
        finally
        {
            if (ii.hbmColor != 0) DeleteObject(ii.hbmColor);
            if (ii.hbmMask != 0) DeleteObject(ii.hbmMask);
        }
    }

    public override string? GetTypeName(string name, bool isDirectory)
    {
        var info = new SHFILEINFOW();
        uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        if (SHGetFileInfo(name, attrs, ref info, (uint)sizeof(SHFILEINFOW), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES) == 0) return base.GetTypeName(name, isDirectory);
        return new string(info.szTypeName);
    }

    /// <summary>
    /// "auto" as it stands on this computer: Windows Terminal when installed (it opens the user's default profile,
    /// PowerShell unless they chose otherwise), else PowerShell 7, else Windows PowerShell.
    /// </summary>
    public static string ResolveShell(string shell) => shell != "auto" ? shell
        : FindOnPath("wt.exe") is not null ? "wt"
        : FindOnPath("pwsh.exe") is not null ? "pwsh"
        : "powershell";

    /// <summary>Windows PowerShell where Windows keeps it: started by bare name it would be looked for in the current directory first (I16).</summary>
    internal static string WindowsPowerShell => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>A program found by full path, or the error starting a missing one would give (never a bare name, I16).</summary>
    private static string Installed(string exe, string what) =>
        FindOnPath(exe) ?? throw new System.ComponentModel.Win32Exception(2, $"{what} ({exe}) was not found on this computer.");

    public override void OpenTerminal(string directory, string shell)
    {
        ProcessStartInfo psi;
        switch (ResolveShell(shell))
        {
            case "powershell":
                psi = new ProcessStartInfo(WindowsPowerShell) { ArgumentList = { "-NoExit", "-NoLogo" } };
                break;
            case "pwsh":
                psi = new ProcessStartInfo(Installed("pwsh.exe", "PowerShell 7")) { ArgumentList = { "-NoExit", "-NoLogo" } };
                break;
            case "wt":
                // As separate arguments: a quoted folder ending in a backslash ("C:\dir\") would escape its own quote.
                psi = new ProcessStartInfo(Installed("wt.exe", "Windows Terminal")) { ArgumentList = { "-d", directory } };
                break;
            default:
                psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
                break;
        }
        psi.WorkingDirectory = directory;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = false;
        Process.Start(psi)?.Dispose();
    }

    public override void RunInTerminal(string directory, string command, string shell)
    {
        ProcessStartInfo psi;
        // A typed command line is cmd's language: "auto" keeps it so (inside Windows Terminal when it is there).
        switch (shell == "auto" ? FindOnPath("wt.exe") is not null ? "wt" : "cmd" : shell)
        {
            case "powershell":
            case "pwsh":
                // -EncodedCommand avoids every layer of command-line quoting for the user's text.
                var exe = shell == "pwsh" ? Installed("pwsh.exe", "PowerShell 7") : WindowsPowerShell;
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                psi = new ProcessStartInfo(exe) { Arguments = "-NoExit -NoLogo -EncodedCommand " + encoded };
                break;
            case "wt":
                psi = new ProcessStartInfo(Installed("wt.exe", "Windows Terminal")) { Arguments = $"-d \"{directory.TrimEnd('\\')}\\\\\" cmd.exe /K {command}" };
                break;
            default:
                // Explicit shell mode: the user's command line goes to cmd.exe verbatim.
                psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { Arguments = "/K " + command };
                break;
        }
        psi.WorkingDirectory = directory;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = false;
        Process.Start(psi)?.Dispose();
    }

    public override void SetKeepAwake(bool keepAwake) =>
        SetThreadExecutionState(keepAwake ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);

    public override void SetShutdownBlock(nint owner, string? reason)
    {
        if (owner == 0) return;
        if (reason is null) ShutdownBlockReasonDestroy(owner);
        else ShutdownBlockReasonCreate(owner, reason);
    }

    public override bool IsElevated
    {
        get
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }

    private ProcessAccount? _account;

    /// <summary>
    /// The process token's account and rights: elevated (UAC's full token, the built-in Administrator, SYSTEM), an
    /// administrator's filtered token (UAC's "limited" elevation type: not elevated), or a standard user. A local
    /// account is named alone; a domain account, or NT AUTHORITY's, with its domain.
    /// </summary>
    public override ProcessAccount Account => _account ??= ReadAccount();

    private static ProcessAccount ReadAccount()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        string name = identity.Name;
        int slash = name.IndexOf('\\');
        if (slash > 0 && string.Equals(name[..slash], Environment.MachineName, StringComparison.OrdinalIgnoreCase)) name = name[(slash + 1)..];
        int type = 0;
        bool limited = GetTokenInformation(identity.Token, 18 /* TokenElevationType */, &type, sizeof(int), out _) && type == 3 /* TokenElevationTypeLimited */;
        var rights = Environment.IsPrivilegedProcess ? AccountRights.Elevated : limited ? AccountRights.AdministratorNotElevated : AccountRights.Standard;
        return new ProcessAccount(name, rights);
    }

    public override string ToUncPath(string path)
    {
        if (path.Length >= 2 && path[1] == ':' && WindowsNetwork.GetRemoteName(path[..2]) is { } unc)
            return unc.TrimEnd('\\') + path[2..];
        return path;
    }

    public override string? ConnectNetworkDrive(nint owner) => WindowsNetwork.ShowConnectDriveDialog(owner);

    public override string? DisconnectNetworkDrive(nint owner) => WindowsNetwork.ShowDisconnectDriveDialog(owner);

    public override string? SignIn(string server, nint owner) => WindowsNetwork.ConnectInteractive(server, owner);

    /// <summary>A program by full path from PATH's absolute entries (a relative one would follow the current directory, I16).</summary>
    internal static string? FindOnPath(string exe, string? pathVariable = null)
    {
        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string dir = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", exe);
        return File.Exists(local) ? local : null;
    }

    internal static string QuoteForDisplay(string s) => ShellQuoting.QuoteCmd(s);
}
