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

    public virtual void OpenTerminal(string directory, string shell)
    {
        if (OperatingSystem.IsMacOS()) Start("open", "-a", "Terminal", directory);
        else
        {
            var psi = new ProcessStartInfo("x-terminal-emulator") { WorkingDirectory = directory, UseShellExecute = false };
            try { Process.Start(psi)?.Dispose(); }
            catch (Exception) { Start("gnome-terminal", "--working-directory=" + directory); }
        }
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
        var psi = new ProcessStartInfo("x-terminal-emulator") { WorkingDirectory = directory, UseShellExecute = false };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add("sh");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        Process.Start(psi)?.Dispose();
    }

    public virtual void SetKeepAwake(bool keepAwake) { }

    protected static void Start(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }
}
