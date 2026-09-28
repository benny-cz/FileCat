using System.Diagnostics;
using FileCat.Core.Resources;

namespace FileCat.Core.Platform;

/// <summary>
/// Desktop integration an OS adapter provides (plan §8.2). Every method must be safe to call from the UI
/// thread only when documented as non-blocking; others are called from background threads.
/// </summary>
public interface IShellServices
{
    /// <summary>Opens a file or folder through the OS association, with its normal security prompts.</summary>
    void Open(string path);

    /// <summary>Opens the OS file manager with the item selected.</summary>
    void Reveal(string path);

    /// <summary>Shows the OS properties dialog, where one exists.</summary>
    bool ShowProperties(string path);

    /// <summary>Extension-only icon as 32-bit BGRA pixels (never reads the file; SHGFI_USEFILEATTRIBUTES).</summary>
    bool TryGetTypeIcon(string name, bool isDirectory, int size, out int width, out int height, out byte[] bgra);

    /// <summary>Human-readable type ("Text Document") from the extension only.</summary>
    string? GetTypeName(string name, bool isDirectory);

    /// <summary>Opens a terminal window in a directory.</summary>
    void OpenTerminal(string directory, string shell);

    /// <summary>
    /// Runs a user-typed command in the configured shell in a visible terminal (plan §14.2). The command is
    /// the user's own text in explicit shell mode; FileCat never builds it from file names implicitly.
    /// </summary>
    void RunInTerminal(string directory, string command, string shell);

    /// <summary>Keeps the system awake while jobs run (optional setting).</summary>
    void SetKeepAwake(bool keepAwake);

    /// <summary>Names running work when the user signs out or shuts down (null clears it).</summary>
    void SetShutdownBlock(nint owner, string? reason);

    /// <summary>True when FileCat runs with administrator rights (worth a warning: plan §13).</summary>
    bool IsElevated { get; }

    /// <summary>UNC form of a path on a mapped network drive; other paths are returned unchanged.</summary>
    string ToUncPath(string path);

    /// <summary>Shows the OS "map network drive" dialog; returns an error or null.</summary>
    string? ConnectNetworkDrive(nint owner);

    /// <summary>Shows the OS "disconnect network drive" dialog; returns an error or null.</summary>
    string? DisconnectNetworkDrive(nint owner);

    /// <summary>Prompts for credentials for a server through the OS networking UI; returns an error or null.</summary>
    string? SignIn(string server, nint owner);
}

/// <summary>Portable fallback using xdg-open / open.</summary>
public class PortableShellServices : IShellServices
{
    public virtual void Open(string path)
    {
        var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        Start(opener, path);
    }

    public virtual void Reveal(string path)
    {
        if (OperatingSystem.IsMacOS()) Start("open", "-R", path);
        else Start("xdg-open", Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path);
    }

    public virtual bool ShowProperties(string path) => false;

    public virtual bool TryGetTypeIcon(string name, bool isDirectory, int size, out int width, out int height, out byte[] bgra)
    {
        width = height = 0;
        bgra = [];
        return false;
    }

    public virtual string? GetTypeName(string name, bool isDirectory) =>
        isDirectory ? "Folder" : NameParts.GetExtension(name) is { Length: > 0 } ext ? ext.ToUpperInvariant() + " file" : "File";

    /// <summary>
    /// Linux terminals FileCat knows, in the order it tries them: $TERMINAL first, then the distribution's default and the
    /// common desktops' own. Each knows how to start in a folder and how to run a command.
    /// </summary>
    private static IEnumerable<(string Exe, string[] Open, string[] Run)> LinuxTerminals(string directory, string script)
    {
        string[] sh = ["sh", "-c", script];
        if (Environment.GetEnvironmentVariable("TERMINAL") is { Length: > 0 } preferred) yield return (preferred, [], ["-e", .. sh]);
        yield return ("x-terminal-emulator", [], ["-e", .. sh]);
        yield return ("gnome-terminal", ["--working-directory=" + directory], ["--working-directory=" + directory, "--", .. sh]);
        yield return ("konsole", ["--workdir", directory], ["--workdir", directory, "-e", .. sh]);
        yield return ("xfce4-terminal", ["--working-directory=" + directory], ["--working-directory=" + directory, "-x", .. sh]);
        yield return ("kitty", ["--directory", directory], ["--directory", directory, .. sh]);
        yield return ("alacritty", ["--working-directory", directory], ["--working-directory", directory, "-e", .. sh]);
        yield return ("wezterm", ["start", "--cwd", directory], ["start", "--cwd", directory, "--", .. sh]);
        yield return ("foot", ["--working-directory=" + directory], ["--working-directory=" + directory, .. sh]);
        yield return ("xterm", [], ["-e", .. sh]);
    }

    /// <summary>Starts the first Linux terminal that exists; false when none does.</summary>
    private static bool StartLinuxTerminal(string directory, string? script)
    {
        foreach (var (exe, open, run) in LinuxTerminals(directory, script ?? string.Empty))
        {
            var psi = new ProcessStartInfo(exe) { WorkingDirectory = directory, UseShellExecute = false };
            foreach (var a in script is null ? open : run) psi.ArgumentList.Add(a);
            try
            {
                Process.Start(psi)?.Dispose();
                return true;
            }
            catch (System.ComponentModel.Win32Exception) { } // not installed: try the next one
        }
        return false;
    }

    public virtual void OpenTerminal(string directory, string shell)
    {
        if (OperatingSystem.IsMacOS()) Start("open", "-a", "Terminal", directory);
        else if (!StartLinuxTerminal(directory, null))
            throw new FileNotFoundException("No terminal was found. Set the TERMINAL environment variable to your terminal program.");
    }

    public virtual void RunInTerminal(string directory, string command, string shell)
    {
        var script = command + "; exec \"${SHELL:-sh}\"";
        if (OperatingSystem.IsMacOS())
        {
            var escaped = ("cd " + Tools.ShellQuoting.QuotePosix(directory) + " && " + command).Replace("\\", "\\\\").Replace("\"", "\\\"");
            Start("osascript", "-e", $"tell application \"Terminal\" to do script \"{escaped}\"");
            return;
        }
        if (!StartLinuxTerminal(directory, script))
            throw new FileNotFoundException("No terminal was found. Set the TERMINAL environment variable to your terminal program.");
    }

    private readonly object _awakeLock = new();
    private Process? _awake;

    /// <summary>macOS: caffeinate for as long as FileCat runs work; Linux: a systemd sleep inhibitor. Best effort.</summary>
    public virtual void SetKeepAwake(bool keepAwake)
    {
        lock (_awakeLock)
        {
            if (!keepAwake)
            {
                try { _awake?.Kill(); } catch (InvalidOperationException) { }
                _awake?.Dispose();
                _awake = null;
                return;
            }
            if (_awake is { HasExited: false }) return;
            var psi = OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("caffeinate", ["-i", "-w", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)])
                : new ProcessStartInfo("systemd-inhibit", ["--what=idle:sleep", "--who=FileCat", "--why=File operations are running", "--mode=block", "sleep", "infinity"]);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = psi.RedirectStandardError = true;
            try { _awake = Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception) { _awake = null; }
        }
    }

    public virtual void SetShutdownBlock(nint owner, string? reason) { }

    public virtual bool IsElevated => !OperatingSystem.IsWindows() && Environment.UserName == "root";

    public virtual string ToUncPath(string path) => path;

    public virtual string? ConnectNetworkDrive(nint owner) => "Mount network shares with your desktop's tools; they then appear under Computer.";

    public virtual string? DisconnectNetworkDrive(nint owner) => "Unmount network shares with your desktop's tools.";

    public virtual string? SignIn(string server, nint owner) => "Direct sign-in to servers is not available on this platform; mount the share first.";

    protected static void Start(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }
}
