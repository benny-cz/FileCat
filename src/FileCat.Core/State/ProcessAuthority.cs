namespace FileCat.Core.State;

/// <summary>
/// The one "elevated or root" decision (I655; R-M12 made it the shell's): a high-integrity token on Windows, an effective
/// user ID of 0 on Linux and macOS. Such a process keeps out of the saved state of the profile it finds, which the
/// ordinary account can change: it loads the defaults and saves nothing, keeps no log or crash report, and reads, writes
/// and acts on no edit sessions, journals, verification records or trust stores there. Root does so whether HOME is the
/// caller's (sudo -E, or sudo where its configuration keeps HOME) or root's own, as an administrator window does whichever
/// account's profile it opens.
/// </summary>
public static class ProcessAuthority
{
    private static readonly AsyncLocal<bool?> Assumed = new();

    /// <summary>True for a high-integrity token on Windows and for root (an effective user ID of 0) on Linux and macOS.</summary>
    public static bool IsElevatedOrRoot =>
        Assumed.Value ?? (OperatingSystem.IsWindows() ? WindowsTokenAuthority.IsHighIntegrity : Environment.IsPrivilegedProcess);

    /// <summary>How messages name such a process: "as administrator" on Windows, "as root" on Linux and macOS.</summary>
    public static string RunningAs => RunningAsOn(OperatingSystem.IsWindows());

    /// <summary>
    /// What to do instead, as a sentence without its full stop: "Reopen it without elevation" on Windows, "Start it as
    /// your usual user" on Linux and macOS (an administrator window comes from an ordinary one; root from sudo).
    /// </summary>
    public static string UsualProcessAdvice => UsualProcessAdviceOn(OperatingSystem.IsWindows(), "it");

    /// <summary><see cref="UsualProcessAdvice"/> naming what is reopened or started ("FileCat").</summary>
    public static string UsualProcessAdviceFor(string subject) => UsualProcessAdviceOn(OperatingSystem.IsWindows(), subject);

    internal static string RunningAsOn(bool windows) => windows ? "as administrator" : "as root";

    internal static string UsualProcessAdviceOn(bool windows, string subject) =>
        windows ? $"Reopen {subject} without elevation" : $"Start {subject} as your usual user";

    /// <summary>The advice in the middle of a sentence ("…; reopen it without elevation to …").</summary>
    internal static string LowerFirst(string sentence) =>
        sentence.Length == 0 ? sentence : char.ToLowerInvariant(sentence[0]) + sentence[1..];

    /// <summary>
    /// Tests: within this asynchronous flow, until disposed, take the process to be elevated or root (or not) whatever its
    /// token or user ID, on any platform. Other tests' flows keep the real answer.
    /// </summary>
    internal static IDisposable Assume(bool elevatedOrRoot)
    {
        bool? previous = Assumed.Value;
        Assumed.Value = elevatedOrRoot;
        return new Restore(previous);
    }

    private sealed class Restore(bool? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Assumed.Value = previous;
        }
    }
}
