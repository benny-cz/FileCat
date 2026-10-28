using System.Diagnostics;

namespace FileCat.App.Services;

/// <summary>
/// Starts the macOS picture worker under the OS Seatbelt facility. The worker still has the ordinary user's
/// filesystem rights, but hostile decoder input cannot create a descendant or communicate over the network.
/// </summary>
internal static class MacPictureSandbox
{
    internal const string Executable = "/usr/bin/sandbox-exec";
    internal const string Profile = "(version 1)(allow default)(deny process-fork)(deny network*)";

    internal static ProcessStartInfo StartInfo(string executable, IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("The macOS picture sandbox is available only on macOS.");
        if (!File.Exists(Executable))
            throw new InvalidOperationException("The macOS picture sandbox is unavailable; FileCat will not decode the picture outside it.");
        if (!Path.IsPathFullyQualified(executable))
            throw new InvalidOperationException("The picture worker executable must have a full path before entering the macOS sandbox.");
        var start = new ProcessStartInfo(Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add(Profile);
        start.ArgumentList.Add(executable);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
}
