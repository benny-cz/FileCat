using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FileCat.App;

/// <summary>
/// A Linux desktop user cannot inspect many root-owned executable images. A negative ordinary-user
/// census therefore needs a narrowly scoped administrator check before device recovery can proceed.
/// Failure, cancellation, an unexpected reply, or an incomplete root census all remain unknown.
/// </summary>
internal static class RecoveryProcessCensus
{
    internal const string Argument = "--filecat-recovery-process-census";
    private const string ReplyPrefix = "FileCatRecoveryProcessCensusV1";
    private const int ReplyLimit = 4096;
    private static readonly TimeSpan AuthorizationLimit = TimeSpan.FromSeconds(90);

    internal static bool? OtherFileCatRunning()
    {
        bool? ordinary = SingleInstance.OtherFileCatRunning();
        if (ordinary is not null || !OperatingSystem.IsLinux()) return ordinary;
        return AskAdministrator();
    }

    internal static int RunHelper(string[] args)
    {
        if (!OperatingSystem.IsLinux() || Core.FileSystem.UnixPermissions.CurrentUserId != 0 ||
            args is not [Argument, var pidText, var nonce] ||
            !int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int parentPid) ||
            parentPid <= 1 || parentPid == Environment.ProcessId || !ValidNonce(nonce)) return 2;

        try
        {
            using var parent = Process.GetProcessById(parentPid);
            if (parent.HasExited) return 2;
            // A short-lived desktop/helper process can disappear between the process snapshot and its
            // executable lookup. Retry the complete inventory; a persistent unknown still refuses.
            bool? running = RepeatUncertain(() => SingleInstance.OtherFileCatRunning(null, null, parentPid));
            if (running is null) return 3;
            Console.Out.WriteLine($"{ReplyPrefix}:{nonce}:{(running.Value ? "running" : "clear")}");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            return 3;
        }
    }

    internal static bool? ParseReply(string reply, string nonce, int exitCode)
    {
        if (exitCode != 0 || !ValidNonce(nonce)) return null;
        return reply.TrimEnd('\r', '\n') switch
        {
            var line when line == $"{ReplyPrefix}:{nonce}:clear" => false,
            var line when line == $"{ReplyPrefix}:{nonce}:running" => true,
            _ => null,
        };
    }

    internal static bool? RepeatUncertain(Func<bool?> census, Action? delay = null)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            bool? result = census();
            if (result is not null) return result;
            if (attempt < 2) (delay ?? (() => Thread.Sleep(100)))();
        }
        return null;
    }

    private static bool ValidNonce(string nonce) => nonce.Length == 32 && nonce.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static bool? AskAdministrator()
    {
        if (!File.Exists("/usr/bin/pkexec") || Environment.ProcessPath is not { } executable) return null;
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var start = new ProcessStartInfo("/usr/bin/pkexec")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(executable);
        // Framework-dependent developer builds are launched through dotnet; packages use the apphost directly.
        if (Path.GetFileName(executable) == "dotnet")
        {
            string? assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(assembly) || !Path.IsPathFullyQualified(assembly)) return null;
            start.ArgumentList.Add(assembly);
        }
        start.ArgumentList.Add(Argument);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(nonce);

        using var process = new Process { StartInfo = start };
        using var timeout = new CancellationTokenSource(AuthorizationLimit);
        try
        {
            if (!process.Start()) return null;
            var stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
            Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr).GetAwaiter().GetResult();
            return ParseReply(stdout.Result, nonce, process.ExitCode);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException
                                   or OperationCanceledException or InvalidDataException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception kill) when (kill is Win32Exception or InvalidOperationException) { }
            return null;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellation)
    {
        var result = new StringBuilder();
        var buffer = new char[512];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
        {
            if (result.Length + count > ReplyLimit) throw new InvalidDataException("Recovery census reply exceeded its bound.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
