using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using FileCat.Platform.Windows;

namespace FileCat.App.Services;

/// <summary>Git states shown on file icons. A directory inherits the strongest state beneath it.</summary>
internal enum GitStatusKind { None, Clean, Untracked, Added, Modified, Conflict }

/// <summary>A snapshot for the immediate children of one displayed local folder.</summary>
internal sealed class GitStatusSnapshot(Dictionary<string, GitStatusKind> entries)
{
    internal GitStatusKind ForName(string name) => entries.GetValueOrDefault(name);

    internal static GitStatusSnapshot Parse(string tracked, string status)
    {
        var entries = new Dictionary<string, GitStatusKind>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        static string Child(string path)
        {
            int separator = path.IndexOf('/');
            return separator < 0 ? path : path[..separator];
        }
        void Add(string path, GitStatusKind kind)
        {
            string child = Child(path);
            if (child.Length == 0 || child == ".") return;
            if (!entries.TryGetValue(child, out var old) || kind > old) entries[child] = kind;
        }

        foreach (var path in tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries)) Add(path, GitStatusKind.Clean);
        int position = 0;
        while (position < status.Length)
        {
            int end = status.IndexOf('\0', position);
            if (end < 0) break;
            string record = status[position..end];
            position = end + 1;
            if (record.Length < 4 || record[2] != ' ') continue;
            char x = record[0], y = record[1];
            var kind = x == 'U' || y == 'U' || x == 'A' && y == 'A' || x == 'D' && y == 'D'
                ? GitStatusKind.Conflict
                : x == '?' && y == '?' ? GitStatusKind.Untracked
                : x == 'A' || y == 'A' ? GitStatusKind.Added
                : GitStatusKind.Modified;
            Add(record[3..], kind);
            // In -z porcelain output a rename/copy has a second NUL-terminated old pathname.
            if (x is 'R' or 'C' || y is 'R' or 'C')
            {
                int oldEnd = status.IndexOf('\0', position);
                if (oldEnd < 0) break;
                Add(status[position..oldEnd], GitStatusKind.Modified);
                position = oldEnd + 1;
            }
        }
        return new GitStatusSnapshot(entries);
    }
}

/// <summary>
/// Reads the same underlying Git state that overlay extensions represent, without loading third-party Shell code into
/// the file manager. Work is bounded and cancellable; unavailable Git or very large output simply leaves icons plain.
/// </summary>
internal static class GitStatusReader
{
    private const int MaxOutputChars = 4_000_000;
    private static readonly SemaphoreSlim Gate = new(2);

    internal static async Task<GitStatusSnapshot?> ReadAsync(string folder, CancellationToken cancellationToken, string gitExecutable = "git")
    {
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder) || OperatingSystem.IsWindows() && !WindowsIcons.IsLocal(folder)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                string? status = await RunAsync(folder, timeout.Token, gitExecutable, "status", "--porcelain=v1", "-z", "--untracked-files=normal", "--", ".").ConfigureAwait(false);
                if (status is null) return null;
                string? tracked = await RunAsync(folder, timeout.Token, gitExecutable, "ls-files", "--cached", "-z", "--", ".").ConfigureAwait(false);
                return tracked is null ? null : GitStatusSnapshot.Parse(tracked, status);
            }
            finally { Gate.Release(); }
        }
        catch (OperationCanceledException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (Win32Exception) { return null; } // Git is optional.
    }

    private static async Task<string?> RunAsync(string folder, CancellationToken cancellationToken, string gitExecutable, params string[] arguments)
    {
        var start = new ProcessStartInfo(gitExecutable)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.fsmonitor=false");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("status.relativePaths=true");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        bool started = false;
        try
        {
            if (!process.Start()) return null;
            started = true;
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            string output = await ReadLimitedAsync(process.StandardOutput, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            return process.ExitCode == 0 ? output : null;
        }
        catch (InvalidDataException) { return null; }
        finally
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (text.Length + count > MaxOutputChars) throw new InvalidDataException("Git status output exceeds the icon budget.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
