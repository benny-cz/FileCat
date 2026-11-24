using FileCat.Core.State;

namespace FileCat.Tests;

/// <summary>
/// I655: the two sides of the one "elevated or root" decision (<see cref="ProcessAuthority"/>). <see cref="Ordinary"/>
/// takes the process to be an ordinary one, so the controls hold even in an elevated or root test host.
/// <see cref="ElevatedOrRoot"/> assumes an administrator window or root on any platform, or, unassumed, keeps the
/// process's own answer and runs only where that is one: a test host started as root (sudo) or elevated.
/// </summary>
internal static class ProcessAuthorityScopes
{
    internal const string RealOnly = "Needs a test host that runs elevated (Windows) or as root (Linux, macOS); the assumed case runs everywhere.";

    internal static IDisposable Ordinary() => ProcessAuthority.Assume(false);

    internal static IDisposable ElevatedOrRoot(bool assumed)
    {
        if (assumed) return ProcessAuthority.Assume(true);
        Assert.SkipUnless(ProcessAuthority.IsElevatedOrRoot, RealOnly);
        return new Unchanged();
    }

    /// <summary>The wording such a process uses on this platform (<see cref="ProcessAuthority.RunningAs"/>).</summary>
    internal static string RunningAs => OperatingSystem.IsWindows() ? "as administrator" : "as root";

    /// <summary>The advice such a process gives on this platform (<see cref="ProcessAuthority.UsualProcessAdvice"/>).</summary>
    internal static string Advice => OperatingSystem.IsWindows() ? "Reopen it without elevation" : "Start it as your usual user";

    private sealed class Unchanged : IDisposable
    {
        public void Dispose() { }
    }
}
