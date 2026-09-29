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

    /// <summary>A whole work tree's state from its "git status": clean, or its strongest change.</summary>
    internal static GitStatusKind Summary(string status) => Parse(string.Empty, status).Strongest();

    private GitStatusKind Strongest() => entries.Count == 0 ? GitStatusKind.Clean : entries.Values.Max();

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
    /// <summary>Repositories among a folder's children that get a badge; more are left plain.</summary>
    private const int MostRepositories = 24;
    private static readonly SemaphoreSlim Gate = new(2);
    private static readonly Lazy<string?> s_git = new(() => FindGit(Environment.GetEnvironmentVariable("PATH")));

    /// <param name="gitExecutable">Git by full path; by default the one on PATH.</param>
    internal static async Task<GitStatusSnapshot?> ReadAsync(string folder, CancellationToken cancellationToken, string? gitExecutable = null)
    {
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder) || OperatingSystem.IsWindows() && !WindowsIcons.IsLocal(folder)) return null;
        if ((gitExecutable ?? s_git.Value) is not { } git) return null;
        gitExecutable = git;
        // Outside a repository, the child folders that are repositories show their work trees' state; inside one whose
        // configuration names programs, running Git would run them.
        if (SafeRepository(folder) is null) return await ReadRepositoriesAsync(folder, git, cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                // Submodules are not entered: their configurations are not checked here.
                string? status = await RunAsync(folder, timeout.Token, gitExecutable, "status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignore-submodules=all", "--", ".").ConfigureAwait(false);
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

    /// <summary>
    /// The child folders of a folder outside any repository that are repositories themselves (a ".git" folder or
    /// file), each with its work tree's state: clean, or the strongest change in it, as TortoiseGit badges a
    /// repository's folder. Repositories whose configuration names programs are left plain, as inside them.
    /// </summary>
    private static async Task<GitStatusSnapshot?> ReadRepositoriesAsync(string folder, string git, CancellationToken cancellationToken)
    {
        List<string> repositories;
        try
        {
            repositories = new DirectoryInfo(folder).EnumerateDirectories()
                .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0 &&
                            (Directory.Exists(Path.Join(d.FullName, ".git")) || File.Exists(Path.Join(d.FullName, ".git"))))
                .Select(d => d.FullName).Take(MostRepositories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
        if (repositories.Count == 0) return null;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var states = new Dictionary<string, GitStatusKind>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string repository in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(SafeRepository(repository), repository, comparison)) continue;
            // Each within moments: a huge or slow one is left plain, not the others with it.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    string? status = await RunAsync(repository, timeout.Token, git, "status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignore-submodules=all").ConfigureAwait(false);
                    if (status is not null) states[Path.GetFileName(repository)] = GitStatusSnapshot.Summary(status);
                }
                finally { Gate.Release(); }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
        return states.Count == 0 ? null : new GitStatusSnapshot(states);
    }

    /// <summary>
    /// The work tree <paramref name="folder"/> belongs to, found as Git finds it, when Git may read it without running
    /// the repository's own programs; null outside a repository. A downloaded folder controls its repository's
    /// configuration, and "git status" runs the clean filters it names while comparing files, and reads the files its
    /// include sections name; such repositories get no badges. (core.fsmonitor is overridden on Git's command line, the
    /// index is not rewritten, so no hooks run, and submodules are not entered.)
    /// </summary>
    internal static string? SafeRepository(string folder)
    {
        try
        {
            for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
            {
                string dotGit = Path.Join(dir.FullName, ".git");
                if (Directory.Exists(dotGit)) return ConfigFiles(dotGit).All(IsHarmless) ? dir.FullName : null;
                if (File.Exists(dotGit)) return LinkedGitDir(dotGit, dir.FullName) is { } linked && ConfigFiles(linked).All(IsHarmless) ? dir.FullName : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return null;
    }

    /// <summary>A work tree's ".git" file (linked work trees, submodules): "gitdir: path", relative to the work tree.</summary>
    private static string? LinkedGitDir(string dotGitFile, string workTree)
    {
        if (new FileInfo(dotGitFile).Length > 4096) return null;
        string text = File.ReadAllText(dotGitFile).Trim();
        if (!text.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
        string path = text["gitdir:".Length..].Trim();
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Join(workTree, path));
        return Directory.Exists(full) ? full : null;
    }

    /// <summary>The repository's own configuration files, and those of the repository a linked work tree shares.</summary>
    private static IEnumerable<string> ConfigFiles(string gitDir)
    {
        yield return Path.Join(gitDir, "config");
        yield return Path.Join(gitDir, "config.worktree");
        string commonDir = Path.Join(gitDir, "commondir");
        if (File.Exists(commonDir) && new FileInfo(commonDir).Length <= 4096)
        {
            string common = File.ReadAllText(commonDir).Trim();
            string full = Path.GetFullPath(Path.IsPathRooted(common) ? common : Path.Join(gitDir, common));
            yield return Path.Join(full, "config");
            yield return Path.Join(full, "config.worktree");
        }
    }

    /// <summary>
    /// No [filter …] and no [include]/[includeIf …] section (names are case-insensitive; "[filter.x]" is the old
    /// spelling of a subsection). A missing file is harmless; an outsized one is not a configuration to trust.
    /// </summary>
    internal static bool IsHarmless(string configPath)
    {
        var info = new FileInfo(configPath);
        if (!info.Exists) return true;
        if (info.Length > 1_000_000) return false;
        foreach (string raw in File.ReadLines(configPath))
        {
            var line = raw.AsSpan().TrimStart();
            if (line.Length == 0 || line[0] != '[') continue;
            var name = line[1..].TrimStart();
            if (name.StartsWith("include", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.StartsWith("filter", StringComparison.OrdinalIgnoreCase) && (name.Length == 6 || !char.IsLetterOrDigit(name[6]))) return false;
        }
        return true;
    }

    /// <summary>
    /// Git by full path from the absolute entries of <paramref name="path"/> (PATH). Started by bare name, Windows would
    /// first look in FileCat's current directory, where a git.exe could have been planted.
    /// </summary>
    internal static string? FindGit(string? path)
    {
        string name = OperatingSystem.IsWindows() ? "git.exe" : "git";
        foreach (string entry in (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string dir = entry.Trim('"');
            if (!Path.IsPathFullyQualified(dir)) continue; // "." and other relative entries follow the current directory
            string candidate = Path.Join(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
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
