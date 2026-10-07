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
/// Automatic badges use repository rules, excluding user-wide configuration, ignore and attribute files.
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
        if (!Path.IsPathFullyQualified(folder) || !IsLocalPath(folder) || !Directory.Exists(folder)) return null;
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
                .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0 && IsLocalPath(Path.Join(d.FullName, ".git")) &&
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
            if (!IsLocalPath(folder)) return null;
            for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
            {
                string dotGit = Path.Join(dir.FullName, ".git");
                if (!IsLocalPath(dotGit)) return null;
                if (Directory.Exists(dotGit)) return Trusted(dotGit) && TreeIsLocal(dir.FullName) ? dir.FullName : null;
                if (File.Exists(dotGit)) return LinkedGitDir(dotGit, dir.FullName) is { } linked && Trusted(linked) && TreeIsLocal(dir.FullName) ? dir.FullName : null;
            }
        }
        // A path a repository's files name can be unusable as a path at all (ArgumentException, NotSupportedException):
        // that is no reason to fail the listing it was shown in, and no repository to trust.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException) { }
        return null;
    }

    /// <summary>
    /// Whether Git may read this repository: neither its own files nor those of the repository a linked work tree
    /// shares name a program to run or a path off this computer.
    /// </summary>
    private static bool Trusted(string gitDir)
    {
        if (!IsLocalPath(gitDir)) return false;
        if (SharedWith(gitDir) is not { } dirs) return false;
        foreach (string dir in dirs)
        {
            if (!TreeIsLocal(dir)) return false;
            if (!IsHarmless(Path.Join(dir, "config"), gitDir) || !IsHarmless(Path.Join(dir, "config.worktree"), gitDir)) return false;
            if (!AlternateObjectDirectoriesAreLocal(Path.Join(dir, "objects"))) return false;
        }
        return true;
    }

    /// <summary>
    /// Git reads both repository metadata and the worktree, including files below untracked junctions. On Windows a
    /// link anywhere in either tree can send the child to a share. Refuse stable linked trees
    /// before launching Git, walking only ordinary directories with bounded optional-badge work. This admission
    /// snapshot does not prevent an attacker from swapping a path after the check.
    /// </summary>
    private static bool TreeIsLocal(string root)
    {
        if (!OperatingSystem.IsWindows()) return true;
        const int maxEntries = 10_000, maxDepth = 64, maxPathChars = 1_048_576;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        long started = Stopwatch.GetTimestamp();
        int entries = 0, pathChars = 0;
        while (pending.TryPop(out var directory))
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(250) || !IsLocalPath(directory.Path)) return false;
            foreach (var item in new DirectoryInfo(directory.Path).EnumerateFileSystemInfos())
            {
                if (++entries > maxEntries || Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(250)) return false;
                var attributes = item.Attributes; // The final link's own attributes; do not enumerate beneath it.
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                pathChars += item.FullName.Length;
                if (pathChars > maxPathChars) return false;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (directory.Depth >= maxDepth) return false;
                    pending.Push((item.FullName, directory.Depth + 1));
                }
            }
        }
        return true;
    }

    /// <summary>
    /// This repository's directory and, where a linked work tree names one ("commondir"), the directory it shares;
    /// null when that one is not on this computer (such a repository gets no badges).
    /// </summary>
    private static List<string>? SharedWith(string gitDir)
    {
        var dirs = new List<string> { gitDir };
        string commonDir = Path.Join(gitDir, "commondir");
        if (!IsLocalPath(commonDir)) return null;
        if (File.Exists(commonDir))
        {
            if (new FileInfo(commonDir).Length > 4096) return null;
            string common = File.ReadAllText(commonDir).Trim();
            string full = Path.GetFullPath(Path.IsPathRooted(common) ? common : Path.Join(gitDir, common));
            if (!IsLocalPath(full)) return null;
            dirs.Add(full);
        }
        return dirs;
    }

    /// <summary>A work tree's ".git" file (linked work trees, submodules): "gitdir: path", relative to the work tree.</summary>
    private static string? LinkedGitDir(string dotGitFile, string workTree)
    {
        if (new FileInfo(dotGitFile).Length > 4096) return null;
        string text = File.ReadAllText(dotGitFile).Trim();
        if (!text.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
        string path = text["gitdir:".Length..].Trim();
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Join(workTree, path));
        return IsLocalPath(full) && Directory.Exists(full) ? full : null;
    }

    /// <summary>
    /// A path a repository's own files name (".git" file, "commondir") is looked at only when it is on this computer:
    /// a downloaded folder can name any path there, and on Windows merely testing a network path connects to it. Decided
    /// from the path itself first. On Windows, each component's own attributes are then checked before looking beneath
    /// it: a fixed-drive spelling can still lead to a share through a junction or symbolic link. Such optional badges
    /// are refused even for local links. A missing component is safe to test after all existing ancestors pass.
    /// </summary>
    internal static bool IsLocalPath(string fullPath)
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            if (!WindowsIcons.IsLocal(fullPath)) return false;
            fullPath = Path.GetFullPath(fullPath);
            string root = Path.GetPathRoot(fullPath)!;
            string component = root;
            if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0) return false;
            foreach (string part in fullPath[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                component = Path.Join(component, part);
                // GetFileAttributes returns the final link's attributes without following its target. Never ask for
                // the next component until this one passes; checking only the complete path would already follow it.
                if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>
    /// The [core] settings whose value Git opens while it compares files: the ignore and attribute rules, the work
    /// tree, the hooks directory. (core.fsmonitor is overridden on Git's command line, so this one is never read.)
    /// </summary>
    private static readonly string[] OpenedByGit = ["excludesfile", "attributesfile", "worktree", "hookspath"];

    /// <summary>
    /// No filter/include sections, promised-object remotes or core setting that sends Git off this computer.
    /// A partial clone can fetch missing objects even during "status", so its automatic badges are left plain.
    /// Names are case-insensitive; old dotted subsection spellings are checked too. A missing file is harmless;
    /// an outsized one is not a configuration to trust.
    /// </summary>
    internal static bool IsHarmless(string configPath, string? gitDirectory = null)
    {
        if (!IsLocalPath(configPath)) return false;
        var info = new FileInfo(configPath);
        if (!info.Exists) return true;
        if (info.Length > 1_000_000) return false;
        bool core = false, remote = false, extensions = false;
        foreach (string? raw in LogicalConfigurationLines(configPath))
        {
            if (raw is null) return false;
            var line = raw.AsSpan().Trim();
            if (line.Length == 0) continue;
            if (line[0] == '[')
            {
                var name = line[1..].TrimStart();
                if (name.StartsWith("include", StringComparison.OrdinalIgnoreCase)) return false;
                if (name.StartsWith("filter", StringComparison.OrdinalIgnoreCase) && (name.Length == 6 || !char.IsLetterOrDigit(name[6]))) return false;
                core = name.StartsWith("core", StringComparison.OrdinalIgnoreCase) && (name.Length == 4 || !char.IsLetterOrDigit(name[4]));
                remote = name.StartsWith("remote", StringComparison.OrdinalIgnoreCase) && (name.Length == 6 || !char.IsLetterOrDigit(name[6]));
                extensions = name.StartsWith("extensions", StringComparison.OrdinalIgnoreCase) && (name.Length == 10 || !char.IsLetterOrDigit(name[10]));
                int close = line.IndexOf(']');
                if (close < 0) continue;
                line = line[(close + 1)..].Trim(); // Git takes a setting on the section's own line too.
                if (line.Length == 0) continue;
            }
            int equals = line.IndexOf('=');
            var key = equals < 0 ? line : line[..equals].TrimEnd();
            if (extensions && key.Equals("partialclone", StringComparison.OrdinalIgnoreCase)) return false;
            if (remote)
            {
                if (key.Equals("partialclonefilter", StringComparison.OrdinalIgnoreCase)) return false;
                if (key.Equals("promisor", StringComparison.OrdinalIgnoreCase))
                {
                    // A bare Boolean is true; only explicit false values leave an ordinary remote available.
                    string? promise = equals < 0 ? null : Value(line[(equals + 1)..]);
                    if (promise is null || !(promise.Length == 0 || promise == "0" ||
                        promise.Equals("false", StringComparison.OrdinalIgnoreCase) || promise.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                        promise.Equals("off", StringComparison.OrdinalIgnoreCase))) return false;
                }
            }
            if (!core || equals < 0) continue;
            foreach (string opened in OpenedByGit)
            {
                if (!key.Equals(opened, StringComparison.OrdinalIgnoreCase)) continue;
                // Git resolves core.worktree against the actual gitdir, including for a shared linked-worktree
                // config. A relative spelling can cross the same junction as an absolute path.
                string? relativeTo = opened == "worktree" ? gitDirectory ?? Path.GetDirectoryName(Path.GetFullPath(configPath)) : null;
                string? value = Value(line[(equals + 1)..]);
                if (value is null || !NamesThisComputer(value, relativeTo)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Git removes an unescaped backslash and newline before interpreting a continued value. Check the same logical
    /// line, keeping quoted/escaped characters intact for the value reader. Comments end the physical line: a slash
    /// in a comment must not consume a following filter/include section. Null refuses incomplete or oversized input.
    /// </summary>
    private static IEnumerable<string?> LogicalConfigurationLines(string configPath)
    {
        var line = new StringBuilder();
        bool quoted = false, continued = false;
        int characters = 0;
        foreach (string raw in File.ReadLines(configPath))
        {
            if (raw.Length > 1_000_000 - characters - 1) { yield return null; yield break; }
            characters += raw.Length + 1; // Include physical line endings so empty continued lines are bounded too.
            continued = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\\')
                {
                    if (i + 1 == raw.Length) { continued = true; break; }
                    line.Append(c).Append(raw[++i]);
                    continue;
                }
                if (c == '"') quoted = !quoted;
                if (!quoted && c is '#' or ';') break;
                line.Append(c);
            }
            if (continued) continue;
            if (quoted) { yield return null; yield break; }
            yield return line.ToString();
            line.Clear();
        }
        if (continued || quoted || line.Length != 0) yield return null;
    }

    /// <summary>Decode the whole logical value, including adjacent quoted segments and Git's five escapes.</summary>
    private static string? Value(ReadOnlySpan<char> text)
    {
        var value = new StringBuilder();
        bool quoted = false;
        int preserved = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\')
            {
                if (++i == text.Length) return null;
                c = text[i] switch
                {
                    'n' => '\n', 't' => '\t', 'b' => '\b', '\\' => '\\', '"' => '"', _ => '\0',
                };
                if (c == '\0') return null;
            }
            else if (c == '"')
            {
                quoted = !quoted;
                preserved = value.Length;
                continue;
            }
            else if (!quoted)
            {
                if (c is '#' or ';') break;
                if (c is ' ' or '\t' or '\r' or '\v' or '\f')
                {
                    if (value.Length != 0) value.Append(c);
                    continue; // Outside whitespace is retained only if more value follows.
                }
            }
            value.Append(c);
            preserved = value.Length;
        }
        return quoted ? null : value.ToString(0, preserved);
    }

    /// <summary>
    /// Whether a setting's value keeps Git on this computer. With a supplied base, relative core.worktree values
    /// resolve against the actual Git directory and receive the same local-path checks as absolute values. Other
    /// relative settings and home-relative rule paths retain their existing admission behavior. Network spellings
    /// are refused from the text before a file-system call; on Windows existing ancestors are checked for links.
    /// </summary>
    private static bool NamesThisComputer(ReadOnlySpan<char> value, string? relativeTo = null)
    {
        if (value.Length == 0) return true;
        string text = value.ToString();
        // core.worktree is an ordinary path relative to the gitdir, even when it starts with a literal "~".
        // Ignore/attribute rule paths retain their home-relative handling and command-line overrides.
        if (relativeTo is null && text.StartsWith('~')) return true;
        if (!Path.IsPathRooted(text) && relativeTo is null) return true;
        try
        {
            string full = relativeTo is null ? Path.GetFullPath(text) : Path.GetFullPath(text, relativeTo);
            return IsLocalPath(full) && (relativeTo is null || !Directory.Exists(full) || TreeIsLocal(full));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>
    /// Git resolves alternate paths against the object directory, and follows each store's own alternates too.
    /// Check the bounded graph and Windows metadata trees before looking beneath an alternate. Quoted C-style paths
    /// and HTTP fetch locations are left plain rather than interpreting them as an unchecked relative filename.
    /// </summary>
    private static bool AlternateObjectDirectoriesAreLocal(string objectsDirectory)
    {
        const int maxDirectories = 32, maxCharacters = 1_000_000;
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(Path.GetFullPath(objectsDirectory));
        long started = Stopwatch.GetTimestamp();
        int characters = 0;
        while (pending.TryDequeue(out string? directory))
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(250)) return false;
            if (!visited.Add(directory)) continue;
            if (visited.Count > maxDirectories || !IsLocalPath(directory)) return false;
            if (!Directory.Exists(directory)) continue; // Missing local stores cannot redirect a read.
            if (!TreeIsLocal(directory)) return false;
            foreach (string name in new[] { "alternates", "http-alternates" })
            {
                string path = Path.Join(directory, "info", name);
                if (!IsLocalPath(path)) return false;
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                if (info.Length > maxCharacters) return false;
                foreach (string raw in File.ReadLines(path))
                {
                    if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(250) ||
                        raw.Length >= maxCharacters - characters) return false;
                    characters += raw.Length + 1;
                    var line = raw.AsSpan();
                    if (line.Trim().Length == 0) continue;
                    // Git's alternate quoting has byte/octal rules different from its configuration values.
                    // Optional badges refuse this unqualified encoding, including leading/trailing whitespace.
                    if (name == "http-alternates" || line[0] == '"' || line.Trim().Length != line.Length) return false;
                    string text = line.ToString();
                    if (Path.IsPathRooted(text) && !Path.IsPathFullyQualified(text)) return false;
                    string full = Path.GetFullPath(text, directory);
                    if (!IsLocalPath(full) || pending.Count >= maxDirectories) return false;
                    pending.Enqueue(full);
                }
            }
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
        // A work tree's attributes can select a filter defined outside its checked configuration. Inherited Git
        // variables can also redirect the repository or write traces. Restrict only this child, preserving the
        // caller's environment and ordinary PATH/runtime settings. Global/system files and implicit user-wide
        // ignore/attribute defaults are excluded; .gitignore/.gitattributes and checked repository settings remain.
        foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        // Git's documented null path also works with Git for Windows. The Windows ARM64 runner's Git rejects "NUL".
        const string emptyFile = "/dev/null";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_SYSTEM"] = emptyFile;
        start.Environment["GIT_CONFIG_GLOBAL"] = emptyFile;
        start.Environment["GIT_ATTR_NOSYSTEM"] = "1";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        // Defense in depth: an automatic lookup must not acquire objects from a promisor remote.
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.fsmonitor=false");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("status.relativePaths=true");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.attributesFile=" + emptyFile);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.excludesFile=" + emptyFile);
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
