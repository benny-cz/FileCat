namespace FileCat.Core.Platform;

/// <summary>The rights FileCat's process has.</summary>
public enum AccountRights
{
    /// <summary>An account without administrator rights: a standard user.</summary>
    Standard,
    /// <summary>An administrator's account running without them (Windows' filtered token: not elevated).</summary>
    AdministratorNotElevated,
    /// <summary>Administrator rights: elevated on Windows, root on Linux and macOS.</summary>
    Elevated,
}

/// <summary>
/// The account FileCat runs as, as the window title says it: a second FileCat started elevated, or as another user,
/// is told apart at a glance.
/// </summary>
/// <param name="Name">The account's name; a domain account's with its domain ("CORP\jane").</param>
/// <param name="SudoFrom">On Linux and macOS, the user who started FileCat as root through sudo.</param>
public sealed record ProcessAccount(string Name, AccountRights Rights, string? SudoFrom = null)
{
    /// <summary>
    /// The main window's title: the place, FileCat, and the account. Elevated on Windows it starts with
    /// "Administrator: ", as Windows' own consoles do, so it shows where titles are cut short (Alt+Tab, the taskbar).
    /// </summary>
    public string WindowTitle(string? place)
    {
        string app = place is null ? "FileCat" : $"{place} — FileCat";
        return (Rights == AccountRights.Elevated && OperatingSystem.IsWindows() ? "Administrator: " : string.Empty) + $"{app} — {Describe()}";
    }

    /// <summary>
    /// "marek (standard user)", "marek (administrator, not elevated)", "marek (elevated)"; on Linux and macOS the name
    /// alone ("root" says it), with "(sudo from marek)" when sudo started it.
    /// </summary>
    public string Describe()
    {
        if (!OperatingSystem.IsWindows()) return SudoFrom is { Length: > 0 } from && from != Name ? $"{Name} (sudo from {from})" : Name;
        return Rights switch
        {
            AccountRights.Elevated => $"{Name} (elevated)",
            AccountRights.AdministratorNotElevated => $"{Name} (administrator, not elevated)",
            _ => $"{Name} (standard user)",
        };
    }

    /// <summary>The account as the process's environment tells it (no platform code): user name and privilege.</summary>
    public static ProcessAccount FromEnvironment()
    {
        bool privileged = Environment.IsPrivilegedProcess;
        return new ProcessAccount(Environment.UserName, privileged ? AccountRights.Elevated : AccountRights.Standard,
            privileged && !OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("SUDO_USER") : null);
    }
}
