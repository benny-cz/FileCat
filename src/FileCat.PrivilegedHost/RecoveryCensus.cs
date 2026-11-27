using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FileCat.PrivilegedHost;

/// <summary>
/// I689: the census of other FileCat processes for a drive read, taken with this helper's administrator rights. A FileCat
/// without them cannot query other accounts' processes on Windows, so its own census never completes, and it leaves the
/// decision here. The installed FileCat beside this helper takes it (<c>FileCat --filecat-recovery-process-census</c>, as
/// polkit's pkexec runs it on Linux), leaving out the FileCat that asked. Anything but a positive "clear" refuses the read.
/// </summary>
internal static class RecoveryCensus
{
    internal const string Argument = "--filecat-recovery-process-census";
    private const string ReplyPrefix = "FileCatRecoveryProcessCensusV1";
    private const int ReplyLimit = 4096;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    internal const string Running = "Other FileCat processes may still write to this disk, so it is not read yet. " +
        "Close other FileCat windows, wait for their helpers to finish, then ask for the scan again.";
    internal const string Unknown = "FileCat could not tell which FileCat processes are running, so this disk is not read yet. " +
        "Close other FileCat windows, then ask for the scan again.";

    /// <summary>Null when no other FileCat runs; otherwise why the read must wait.</summary>
    internal static string? Refusal(int requesterProcessId, string brokerDirectory)
    {
        string executable = Path.Combine(brokerDirectory, "FileCat.exe");
        if (!File.Exists(executable)) return Unknown;
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = brokerDirectory,
        };
        start.ArgumentList.Add(Argument);
        start.ArgumentList.Add(requesterProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(nonce);
        using var process = new Process { StartInfo = start };
        using var timeout = new CancellationTokenSource(Limit);
        try
        {
            if (!process.Start()) return Unknown;
            var stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
            Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr).GetAwaiter().GetResult();
            return Verdict(stdout.Result, nonce, process.ExitCode);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException
                                   or OperationCanceledException or InvalidDataException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception kill) when (kill is Win32Exception or InvalidOperationException) { }
            return Unknown;
        }
    }

    /// <summary>The census reply, as FileCat's RecoveryProcessCensus writes it; anything unexpected is unknown.</summary>
    internal static string? Verdict(string reply, string nonce, int exitCode)
    {
        if (exitCode != 0) return Unknown;
        string line = reply.TrimEnd('\r', '\n');
        if (line == $"{ReplyPrefix}:{nonce}:clear") return null;
        return line == $"{ReplyPrefix}:{nonce}:running" ? Running : Unknown;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellation)
    {
        var result = new StringBuilder();
        var buffer = new char[512];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
        {
            if (result.Length + count > ReplyLimit) throw new InvalidDataException("The recovery census reply exceeded its bound.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
