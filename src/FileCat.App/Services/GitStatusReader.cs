using System.ComponentModel;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.State;
using FileCat.Platform.Windows;

namespace FileCat.App.Services;

/// <summary>
/// Git states shown on file icons. A directory inherits the strongest state beneath it (<see cref="GitStatusSnapshot.Rank"/>).
/// Unknown (I647): Git called the file changed only after comparing its contents without the clean filter its attributes
/// select, a program automatic badges never run, so it is shown neither clean nor changed.
/// </summary>
internal enum GitStatusKind { None, Clean, Untracked, Added, Modified, Conflict, Unknown }

/// <summary>A snapshot for the immediate children of one displayed local folder.</summary>
internal sealed class GitStatusSnapshot(Dictionary<string, GitStatusKind> entries)
{
    private readonly Dictionary<string, GitStatusKind> _entries = entries;

    internal GitStatusKind ForName(string name) => _entries.GetValueOrDefault(name);

    /// <summary>Whether two snapshots give every name the same state (null: no repository).</summary>
    internal static bool Same(GitStatusSnapshot? a, GitStatusSnapshot? b) =>
        ReferenceEquals(a, b) || a is not null && b is not null && a._entries.Count == b._entries.Count &&
        a._entries.All(e => b._entries.TryGetValue(e.Key, out var kind) && kind == e.Value);

    /// <summary>A whole work tree's state from its "git status": clean, or its strongest change.</summary>
    internal static GitStatusKind Summary(string status, IReadOnlySet<string>? undecided = null) =>
        Parse(string.Empty, status, StringComparer.Ordinal, undecided).Strongest();

    private GitStatusKind Strongest() => _entries.Count == 0 ? GitStatusKind.Clean : _entries.Values.MaxBy(Rank);

    /// <summary>
    /// How strong a state is where a folder shows the strongest one beneath it. Unknown ranks above clean, so a folder is not
    /// called clean for a file Git could not decide, and below every decided change, which it does not hide.
    /// </summary>
    internal static int Rank(GitStatusKind kind) => kind switch
    {
        GitStatusKind.None => 0,
        GitStatusKind.Clean => 1,
        GitStatusKind.Unknown => 2,
        GitStatusKind.Untracked => 3,
        GitStatusKind.Added => 4,
        GitStatusKind.Modified => 5,
        _ => 6,
    };

    /// <param name="tracked">"git ls-files -z" of the folder: paths from the folder.</param>
    /// <param name="status">"git status --porcelain=v1 -z": paths from the work tree's top, whatever status.relativePaths says.</param>
    /// <param name="names">How the folder compares its children's names.</param>
    /// <param name="undecided">
    /// Paths as <paramref name="status"/> prints them whose " M" needed a filter automatic badges do not run: Unknown.
    /// </param>
    /// <param name="prefix">
    /// I657: the folder's path from the work tree's top, as "git rev-parse --show-prefix" prints it ("sub/"; empty at the
    /// top). Status records below it are filed under the folder's children; others are not the folder's.
    /// </param>
    internal static GitStatusSnapshot Parse(string tracked, string status, StringComparer names, IReadOnlySet<string>? undecided = null, string prefix = "")
    {
        var entries = new Dictionary<string, GitStatusKind>(names);
        var lookup = entries.GetAlternateLookup<ReadOnlySpan<char>>();
        void Add(ReadOnlySpan<char> path, GitStatusKind kind)
        {
            int separator = path.IndexOf('/');
            ReadOnlySpan<char> child = separator < 0 ? path : path[..separator];
            if (child.IsEmpty || child.SequenceEqual(".")) return;
            if (!lookup.TryGetValue(child, out var old) || Rank(kind) > Rank(old)) entries[child.ToString()] = kind;
        }
        // Git limits the records to the folder ("-- ."), matching its letter case as core.ignorecase says, so the
        // prefix is compared ignoring case and then cut by its length.
        void AddBelowPrefix(ReadOnlySpan<char> path, GitStatusKind kind)
        {
            if (path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) Add(path[prefix.Length..], kind);
        }

        int trackedPosition = 0;
        while (trackedPosition < tracked.Length)
        {
            int end = tracked.IndexOf('\0', trackedPosition);
            if (end < 0) end = tracked.Length;
            if (end > trackedPosition) Add(tracked.AsSpan(trackedPosition, end - trackedPosition), GitStatusKind.Clean);
            trackedPosition = end + 1;
        }
        int position = 0;
        while (position < status.Length)
        {
            int end = status.IndexOf('\0', position);
            if (end < 0) break;
            ReadOnlySpan<char> record = status.AsSpan(position, end - position);
            position = end + 1;
            if (record.Length < 4 || record[2] != ' ') continue;
            char x = record[0], y = record[1];
            var kind = x == 'U' || y == 'U' || x == 'A' && y == 'A' || x == 'D' && y == 'D'
                ? GitStatusKind.Conflict
                : x == '?' && y == '?' ? GitStatusKind.Untracked
                : x == 'A' || y == 'A' ? GitStatusKind.Added
                : x == ' ' && y == 'M' && undecided is { Count: > 0 } && undecided.Contains(record[3..].ToString()) ? GitStatusKind.Unknown
                : GitStatusKind.Modified;
            AddBelowPrefix(record[3..], kind);
            // In -z porcelain output a rename/copy has a second NUL-terminated old pathname.
            if (x is 'R' or 'C' || y is 'R' or 'C')
            {
                int oldEnd = status.IndexOf('\0', position);
                if (oldEnd < 0) break;
                AddBelowPrefix(status.AsSpan(position, oldEnd - position), GitStatusKind.Modified);
                position = oldEnd + 1;
            }
        }
        return new GitStatusSnapshot(entries);
    }
}

/// <summary>
/// Reads the same underlying Git state that overlay extensions represent, without loading third-party Shell code into
/// the file manager. Work is bounded and cancellable; unavailable Git or very large output simply leaves icons plain.
/// Automatic badges use repository rules, excluding user-wide configuration, ignore and attribute files; of the user's
/// settings they take only those that decide how files are compared (<see cref="ComparisonSettings"/>).
/// </summary>
internal static class GitStatusReader
{
    private const int MaxOutputChars = 4_000_000;
    /// <summary>Repositories among a folder's children that get a badge; more are left plain.</summary>
    private const int MostRepositories = 24;
    private static readonly SemaphoreSlim Gate = new(2);
    private static readonly Lazy<string?> s_git = new(FindAutomaticGit);
    /// <summary>UTF-8 without a byte order mark, for everything exchanged with Git.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// I647: the settings that decide how "git status" compares a work tree with its index, and that name neither a program
    /// nor a path. Automatic badges run Git without system and global configuration (I150), which dropped these too: Git for
    /// Windows keeps core.autocrlf=true in its system file, so files Git calls clean read changed. Each is read from Git's
    /// ordinary configuration and passed back with "-c" at the value Git would use there. Names are Git's canonical ones.
    /// </summary>
    internal static readonly string[] ComparisonSettings =
    [
        "core.autocrlf",          // line endings converted before contents are compared
        "core.eol",               // the line endings of text files, which belong with core.autocrlf
        "core.symlinks",          // whether a tracked link checked out as a plain file reads type-changed
        "core.ignorecase",        // whether a name differing only in case is the tracked file or an untracked one
        "core.filemode",          // whether a different executable bit reads changed
        "core.trustctime",        // whether a changed inode-change time makes Git compare contents
        "core.checkstat",         // which recorded file times and identities make Git compare contents
        "core.precomposeunicode", // macOS: whether names are compared in composed Unicode
        "core.longpaths",         // Git for Windows: whether paths beyond 260 characters are read at all
    ];

    /// <summary>
    /// "git config --get-regexp": the comparison settings and the commands of clean filters (whose values are only noted,
    /// never passed on or run). Git matches it against canonical names, whose section and key are lower case.
    /// </summary>
    private static readonly string ConfigurationPattern =
        "^(" + string.Join('|', ComparisonSettings.Select(name => name.Replace(".", @"\.", StringComparison.Ordinal))) + @"|filter\..+\.(clean|process))$";

    /// <param name="gitExecutable">Git by full path; by default an OS-installed copy for automatic badges.</param>
    internal static async Task<GitStatusSnapshot?> ReadAsync(string folder, CancellationToken cancellationToken, string? gitExecutable = null)
    {
        // Git runs what its configuration names (filters, fsmonitor), from the repository and from HOME: never for an
        // administrator window, nor for root (I655), whose HOME may be the caller's.
        if (ProcessAuthority.IsElevatedOrRoot) return null;
        if (cancellationToken.IsCancellationRequested) return null;
        if (!Path.IsPathFullyQualified(folder) || !IsLocalPath(folder) || !Directory.Exists(folder)) return null;
        if ((gitExecutable ?? s_git.Value) is not { } git) return null;
        gitExecutable = git;
        // Starting a malformed .exe can block synchronously inside Windows CreateProcess,
        // before the cancellation budget can be observed. Optional badges need no child
        // when the selected file cannot even be a Windows executable.
        if (OperatingSystem.IsWindows() && !LooksLikeWindowsExecutable(git)) return null;
        // Outside a repository, the child folders that are repositories show their work trees' state; inside one whose
        // configuration names programs, running Git would run them.
        if (SafeRepository(folder) is not { } workTree) return await ReadRepositoriesAsync(folder, git, cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                // Without the user's comparison settings Git could call unchanged files changed: no badges then.
                if (await ReadConfigurationAsync(folder, gitExecutable, timeout.Token).ConfigureAwait(false) is not { } configuration) return null;
                var run = new GitRun(configuration.Settings);
                // I657: porcelain status names paths from the work tree's top, ls-files and the listing from the folder.
                // Below the top, Git says where the folder lies, in the letter case it gives the paths.
                string prefix = string.Empty;
                if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), Path.TrimEndingDirectorySeparator(workTree), StringComparison.Ordinal))
                {
                    if (await RunAsync(folder, timeout.Token, gitExecutable, run, "rev-parse", "--show-prefix").ConfigureAwait(false) is not { } shown) return null;
                    prefix = shown.EndsWith('\n') ? shown[..^1] : shown;
                }
                // Submodules are not entered: their configurations are not checked here.
                string? status = await RunAsync(folder, timeout.Token, gitExecutable, run, "status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignore-submodules=all", "--", ".").ConfigureAwait(false);
                if (status is null) return null;
                var names = WindowsCaseSensitivity.NamesIn(folder);
                if (IsUntrackedFolder(status, prefix))
                {
                    // Git names an untracked folder as one record, and nothing below it is tracked: list its own files.
                    status = await RunAsync(folder, timeout.Token, gitExecutable, run, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=all", "--", ".").ConfigureAwait(false);
                    return status is null ? null : GitStatusSnapshot.Parse(string.Empty, status, names, prefix: prefix);
                }
                if (await UndecidedAsync(workTree, gitExecutable, timeout.Token, run, configuration.Filters, status).ConfigureAwait(false) is not { } undecided) return null;
                string? tracked = await RunAsync(folder, timeout.Token, gitExecutable, run, "ls-files", "--cached", "-z", "--", ".").ConfigureAwait(false);
                return tracked is null ? null : GitStatusSnapshot.Parse(tracked, status, names, undecided, prefix);
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
            // A directory without repositories otherwise probes every child before its optional badges return.
            // Stop discovery independently of the per-repository Git deadlines below.
            const int maxDirectories = 256;
            repositories = new List<string>(MostRepositories);
            long started = Stopwatch.GetTimestamp();
            int scanned = 0;
            foreach (var directory in new DirectoryInfo(folder).EnumerateDirectories())
            {
                if (cancellationToken.IsCancellationRequested) return null;
                if (++scanned > maxDirectories || Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(250)) break;
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                string dotGit = Path.Join(directory.FullName, ".git");
                if (!IsLocalPath(dotGit) || !Directory.Exists(dotGit) && !File.Exists(dotGit)) continue;
                repositories.Add(directory.FullName);
                if (repositories.Count == MostRepositories) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
        if (repositories.Count == 0) return null;
        var names = WindowsCaseSensitivity.NamesIn(folder);
        var states = new Dictionary<string, GitStatusKind>(names);
        foreach (string repository in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!names.Equals(SafeRepository(repository), repository)) continue;
            // Each within moments: a huge or slow one is left plain, not the others with it.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    if (await ReadConfigurationAsync(repository, git, timeout.Token).ConfigureAwait(false) is { } configuration)
                    {
                        var run = new GitRun(configuration.Settings);
                        string? status = await RunAsync(repository, timeout.Token, git, run, "status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignore-submodules=all").ConfigureAwait(false);
                        if (status is not null && await UndecidedAsync(repository, git, timeout.Token, run, configuration.Filters, status).ConfigureAwait(false) is { } undecided)
                            states[Path.GetFileName(repository)] = GitStatusSnapshot.Summary(status, undecided);
                    }
                }
                finally { Gate.Release(); }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            // Git is optional here too: an installation can disappear or become unusable after PATH discovery,
            // and a child repository can become inaccessible. Leave its icon plain and keep listing the others.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (Win32Exception) { }
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
            string? common = ReadBoundedMetadataText(commonDir, 4096);
            if (common is null) return null;
            common = common.Trim();
            string full = Path.GetFullPath(Path.IsPathRooted(common) ? common : Path.Join(gitDir, common));
            if (!IsLocalPath(full)) return null;
            dirs.Add(full);
        }
        return dirs;
    }

    /// <summary>A work tree's ".git" file (linked work trees, submodules): "gitdir: path", relative to the work tree.</summary>
    private static string? LinkedGitDir(string dotGitFile, string workTree)
    {
        string? text = ReadBoundedMetadataText(dotGitFile, 4096);
        if (text is null) return null;
        text = text.Trim();
        if (!text.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
        string path = text["gitdir:".Length..].Trim();
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Join(workTree, path));
        return IsLocalPath(full) && Directory.Exists(full) ? full : null;
    }

    private static string? ReadBoundedMetadataText(string path, int maxBytes)
    {
        var info = new FileInfo(path);
        if (info.Length > maxBytes) return null;
        // A short link or a file grown after the metadata check may lead to a much larger stream.
        using var input = OperatingSystem.IsWindows()
            ? File.OpenRead(path)
            : new FileStream(UnixFiles.OpenRegularFileForRead(path), FileAccess.Read);
        using var content = new MemoryStream((int)Math.Min(info.Length, maxBytes));
        Span<byte> buffer = stackalloc byte[4096];
        while (true)
        {
            int count = input.Read(buffer[..Math.Min(buffer.Length, maxBytes + 1 - (int)content.Length)]);
            if (count == 0) break;
            if (content.Length + count > maxBytes) return null;
            content.Write(buffer[..count]);
        }
        content.Position = 0;
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
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
        string? configuration = ReadBoundedMetadataText(configPath, 1_000_000);
        if (configuration is null) return false;
        bool core = false, remote = false, extensions = false;
        foreach (string? raw in LogicalConfigurationLines(configuration))
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
    private static IEnumerable<string?> LogicalConfigurationLines(string configuration)
    {
        using var reader = new StringReader(configuration);
        var line = new StringBuilder();
        bool quoted = false, continued = false;
        int characters = 0;
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
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
                // The metadata-only size check cannot bound a FIFO or a file changed before open.
                // Share the opened regular-file and actual-byte bound with other Git metadata.
                string? alternates = ReadBoundedMetadataText(path, maxCharacters);
                if (alternates is null) return false;
                using var reader = new StringReader(alternates);
                string? raw;
                while ((raw = reader.ReadLine()) is not null)
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

    /// <summary>
    /// Automatic folder badges must not run a Git program from a writable PATH directory. A separately supplied
    /// executable remains the caller's explicit choice; default badge discovery uses conventional OS installations.
    /// </summary>
    internal static string? FindAutomaticGit()
    {
        if (!OperatingSystem.IsWindows())
            return new[] { "/usr/bin/git", "/bin/git" }.FirstOrDefault(File.Exists);
        foreach (string programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 }.Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string subpath in new[] { @"Git\cmd\git.exe", @"Git\bin\git.exe" })
            {
                string candidate = Path.Combine(programFiles, subpath);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static bool LooksLikeWindowsExecutable(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> dos = stackalloc byte[64];
            if (file.ReadAtLeast(dos, dos.Length, throwOnEndOfStream: false) != dos.Length ||
                dos[0] != (byte)'M' || dos[1] != (byte)'Z') return false;
            int peOffset = BinaryPrimitives.ReadInt32LittleEndian(dos[0x3c..]);
            if (peOffset < dos.Length || peOffset > 1024 * 1024 || peOffset + 4L > file.Length) return false;
            file.Position = peOffset;
            Span<byte> pe = stackalloc byte[4];
            return file.ReadAtLeast(pe, pe.Length, throwOnEndOfStream: false) == pe.Length &&
                   pe.SequenceEqual("PE\0\0"u8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// I647: what automatic badges take from Git's ordinary configuration here: system and global files, the repository's
    /// own (already checked), and the files they include, as Git reads them in <paramref name="folder"/>. "git config"
    /// runs no program while it reads. Null when Git cannot read its configuration; Git itself would then fail too.
    /// </summary>
    internal static async Task<GitConfiguration?> ReadConfigurationAsync(string folder, string git, CancellationToken cancellationToken)
    {
        string? output = await RunAsync(folder, cancellationToken, git, new GitRun(OrdinaryConfiguration: true),
            "--no-pager", "config", "-z", "--includes", "--get-regexp", ConfigurationPattern).ConfigureAwait(false);
        return output is null ? null : GitConfiguration.Parse(output);
    }

    /// <summary>
    /// I657: whether "git status" in a folder below the top names that folder itself, or one holding it, as one untracked
    /// record ("?? sub/new/"). In normal untracked mode Git names an untracked folder, not the files in it.
    /// </summary>
    private static bool IsUntrackedFolder(string status, string prefix)
    {
        if (prefix.Length == 0) return false;
        for (int position = 0; position < status.Length;)
        {
            int end = status.IndexOf('\0', position);
            if (end < 0) break;
            var record = status.AsSpan(position, end - position);
            position = end + 1;
            if (record.Length < 4 || record[2] != ' ') continue;
            if (record[0] is 'R' or 'C' || record[1] is 'R' or 'C')
            {
                int oldEnd = status.IndexOf('\0', position); // a rename's or copy's old path
                if (oldEnd < 0) break;
                position = oldEnd + 1;
                continue;
            }
            if (record[0] == '?' && record[1] == '?' && record[^1] == '/' && prefix.AsSpan().StartsWith(record[3..], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// I647: the paths "git status" calls changed in the work tree alone (" M") whose comparison needed a clean filter that
    /// automatic badges never run (I150): their attributes select one that Git's ordinary configuration defines, Git LFS's
    /// for one. Git then compared the unfiltered bytes, so "changed" may be false. As in Git, which compares no contents
    /// then, a size other than the one its index recorded still proves the change. Paths are as "git status" prints them,
    /// relative to the work tree, so Git runs in <paramref name="workTree"/> here. Null when Git cannot tell.
    /// </summary>
    private static async Task<HashSet<string>?> UndecidedAsync(string workTree, string git, CancellationToken cancellationToken,
        GitRun run, IReadOnlySet<string> filters, string status)
    {
        var undecided = new HashSet<string>(StringComparer.Ordinal);
        if (filters.Count == 0) return undecided;
        var changed = new List<string>();
        int characters = 0;
        for (int position = 0; position < status.Length;)
        {
            int end = status.IndexOf('\0', position);
            if (end < 0) break;
            var record = status.AsSpan(position, end - position);
            position = end + 1;
            if (record.Length < 4 || record[2] != ' ') continue;
            if (record[0] is 'R' or 'C' || record[1] is 'R' or 'C')
            {
                int oldEnd = status.IndexOf('\0', position); // a rename's or copy's old path
                if (oldEnd < 0) break;
                position = oldEnd + 1;
                continue;
            }
            if (record[0] != ' ' || record[1] != 'M') continue;
            changed.Add(record[3..].ToString());
            characters += record.Length - 2;
        }
        if (changed.Count == 0) return undecided;
        // Too many to ask about within the output budget: none of them is called changed.
        if (changed.Count > 10_000 || characters > 1_000_000)
        {
            undecided.UnionWith(changed);
            return undecided;
        }
        // One "path\0filter\0value\0" for each path given, in the order given; the value names a filter or is "set",
        // "unset" or "unspecified".
        string? attributes = await RunAsync(workTree, cancellationToken, git, run with { Input = string.Join('\0', changed) + "\0" },
            "check-attr", "-z", "--stdin", "filter").ConfigureAwait(false);
        if (attributes is null) return null;
        string[] fields = attributes.Split('\0');
        if (fields.Length != changed.Count * 3 + 1) return null;
        for (int i = 0; i < changed.Count; i++)
            if (filters.Contains(fields[i * 3 + 2])) undecided.Add(changed[i]);
        // Git compares contents only while the size it recorded still matches, or when it recorded none (0).
        if (undecided.Count == 0 || undecided.Count > 256 || undecided.Sum(path => path.Length + 1) > 16_000) return undecided;
        string? recorded = await RunAsync(workTree, cancellationToken, git, run,
            ["--literal-pathspecs", "ls-files", "--debug", "-z", "--", .. undecided]).ConfigureAwait(false);
        if (recorded is null) return undecided;
        foreach (var (path, size) in RecordedSizes(recorded))
        {
            if (size == 0 || !undecided.Contains(path)) continue;
            try
            {
                var file = new FileInfo(Path.Join(workTree, path));
                if (file.Exists && (file.Attributes & FileAttributes.ReparsePoint) == 0 && (uint)file.Length != size) undecided.Remove(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { }
        }
        return undecided;
    }

    /// <summary>
    /// "git ls-files --debug -z": each path, a NUL, then lines of the file data Git recorded, one of them "  size: N\tflags: F".
    /// Git keeps the size modulo 2^32.
    /// </summary>
    private static List<(string Path, uint Size)> RecordedSizes(string debug)
    {
        const string sizeField = "  size: ";
        var sizes = new List<(string, uint)>();
        for (int position = 0; position < debug.Length;)
        {
            int nul = debug.IndexOf('\0', position);
            int size = nul < 0 ? -1 : debug.IndexOf(sizeField, nul + 1, StringComparison.Ordinal);
            int next = size < 0 ? -1 : debug.IndexOf('\n', size);
            if (next < 0) break;
            int value = size + sizeField.Length, tab = debug.IndexOf('\t', value, next - value);
            if (tab > value && uint.TryParse(debug.AsSpan(value, tab - value), NumberStyles.None, CultureInfo.InvariantCulture, out uint bytes))
                sizes.Add((debug[position..nul], bytes));
            position = next + 1;
        }
        return sizes;
    }

    /// <summary>What automatic badges take from Git's ordinary configuration (<see cref="ReadConfigurationAsync"/>).</summary>
    /// <param name="Settings">"-c" arguments for the <see cref="ComparisonSettings"/> found, in that order.</param>
    /// <param name="Filters">The clean filters defined there (Git LFS's in Git for Windows' system file). Never run.</param>
    internal sealed record GitConfiguration(string[] Settings, IReadOnlySet<string> Filters)
    {
        internal static GitConfiguration Parse(string output)
        {
            // "name\nvalue\0", or "name\0" for a name given without a value, which Git reads as true. Values come in
            // Git's order (system, global, repository, work tree, included files where they are included); the last is
            // the one Git uses.
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string record in output.Split('\0'))
            {
                if (record.Length == 0) continue;
                int newline = record.IndexOf('\n');
                values[newline < 0 ? record : record[..newline]] = newline < 0 ? null : record[(newline + 1)..];
            }
            var settings = new List<string>();
            foreach (string name in ComparisonSettings)
            {
                if (!values.TryGetValue(name, out string? value)) continue;
                settings.Add("-c");
                settings.Add(value is null ? name : name + "=" + value);
            }
            // filter.<driver>.clean or .process with a command: Git would run it to compare files. Drivers keep their case.
            var filters = new HashSet<string>(StringComparer.Ordinal);
            const string section = "filter.";
            foreach (var (name, value) in values)
            {
                int key = name.LastIndexOf('.');
                if (!string.IsNullOrEmpty(value) && name.StartsWith(section, StringComparison.Ordinal) && key > section.Length)
                    filters.Add(name[section.Length..key]);
            }
            return new GitConfiguration([.. settings], filters);
        }
    }

    /// <summary>How one Git child of a read runs.</summary>
    /// <param name="Settings">The imported comparison settings ("-c" arguments).</param>
    /// <param name="OrdinaryConfiguration">
    /// Only to read those settings: Git reads its ordinary system and global files, which every other run excludes.
    /// </param>
    /// <param name="Input">Text for Git's standard input: paths for "check-attr --stdin".</param>
    private readonly record struct GitRun(IReadOnlyList<string>? Settings = null, bool OrdinaryConfiguration = false, string? Input = null);

    private static async Task<string?> RunAsync(string folder, CancellationToken cancellationToken, string gitExecutable, GitRun run, params string[] arguments)
    {
        var start = new ProcessStartInfo(gitExecutable)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = run.Input is not null,
            // Git's paths are UTF-8. Left unset, once FileCat has made the legacy code pages available, Windows .NET reads
            // them in the console's code page, or with no console, as for FileCat, in the ANSI code page: a name with other
            // letters then missed its badge (I657).
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        // Paths go to Git as UTF-8, without a byte order mark that would read as one more path.
        if (run.Input is not null) start.StandardInputEncoding = Utf8;
        // A work tree's attributes can select a filter defined outside its checked configuration. Inherited Git
        // variables can also redirect the repository or write traces. Restrict only this child, preserving the
        // caller's environment and ordinary PATH/runtime settings. Global/system files and implicit user-wide
        // ignore/attribute defaults are excluded; .gitignore/.gitattributes and checked repository settings remain.
        foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        // Git's documented null path also works with Git for Windows. The Windows ARM64 runner's Git rejects "NUL".
        const string emptyFile = "/dev/null";
        if (!run.OrdinaryConfiguration)
        {
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_SYSTEM"] = emptyFile;
            start.Environment["GIT_CONFIG_GLOBAL"] = emptyFile;
        }
        start.Environment["GIT_ATTR_NOSYSTEM"] = "1";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        // Defense in depth: an automatic lookup must not acquire objects from a promisor remote.
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        // The system and global files the configuration read sees can name trace destinations: write none.
        start.Environment["GIT_TRACE2"] = "0";
        start.Environment["GIT_TRACE2_EVENT"] = "0";
        start.Environment["GIT_TRACE2_PERF"] = "0";
        // The imported settings first: none of them is one of the overrides that follow, and a later "-c" wins.
        foreach (string setting in run.Settings ?? []) start.ArgumentList.Add(setting);
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
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<string>? output = null, errors = null;
        Task? input = null;
        bool started = false;
        try
        {
            if (!process.Start()) return null;
            started = true;
            // Both redirected pipes have a budget. Drain diagnostics without keeping their text, and observe
            // either limit immediately: waiting for stdout first could leave a noisy child running until timeout.
            errors = ReadLimitedAsync(process.StandardError, reading.Token, keepText: false);
            output = ReadLimitedAsync(process.StandardOutput, reading.Token);
            if (run.Input is not null) input = WriteInputAsync(process.StandardInput, run.Input, reading.Token);
            Task<string> first = await Task.WhenAny(output, errors).ConfigureAwait(false);
            await first.ConfigureAwait(false);
            await Task.WhenAll(output, errors).ConfigureAwait(false);
            if (input is not null) await input.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            // "git config --get-regexp" exits 1 when no setting matches.
            return process.ExitCode == 0 || run.OrdinaryConfiguration && process.ExitCode == 1 ? await output.ConfigureAwait(false) : null;
        }
        catch (InvalidDataException) { return null; }
        finally
        {
            reading.Cancel();
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            // Cancellation/limit failure on one pipe must not leave the other read or its exception unobserved.
            foreach (var task in new Task?[] { output, errors, input })
            {
                if (task is null) continue;
                try { await task.ConfigureAwait(false); }
                catch (Exception ex) when (ex is OperationCanceledException or InvalidDataException or IOException) { }
            }
        }
    }

    /// <summary>Writes the whole input, then closes the pipe so Git sees its end.</summary>
    private static async Task WriteInputAsync(StreamWriter input, string text, CancellationToken cancellationToken)
    {
        try { await input.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false); }
        finally { input.Close(); }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken cancellationToken, bool keepText = true)
    {
        var text = keepText ? new StringBuilder() : null;
        var buffer = new char[8192];
        int count, characters = 0;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (count > MaxOutputChars - characters) throw new InvalidDataException("Git status output exceeds the icon budget.");
            characters += count;
            text?.Append(buffer, 0, count);
        }
        return text?.ToString() ?? string.Empty;
    }
}
