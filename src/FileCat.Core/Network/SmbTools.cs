using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace FileCat.Core.Network;

/// <summary>A user name, an optional domain or workgroup, and a password for a share (D-54).</summary>
public sealed record NetworkCredentials(string User, string? Domain, string Password);

/// <summary>A share on a server, as a listing names it.</summary>
public sealed record SmbShare(string Name, string? Comment)
{
    /// <summary>Administrative and hidden shares end with '$'.</summary>
    public bool IsHidden => Name.EndsWith('$');
}

/// <summary>The server wants a user name and password before it answers.</summary>
public sealed class SmbSignInRequiredException(string message) : UnauthorizedAccessException(message);

/// <summary>
/// The system's own SMB tools on Linux and macOS (D-54): share lists from gvfs (gio) or Samba's smbclient on Linux and
/// smbutil on macOS; shares mounted by gvfs on Linux and by NetFS on macOS (see <see cref="MacNetFS"/>), and found
/// again where they are already mounted. Tools are run by full path, never through the current directory, and their
/// output is bounded.
/// </summary>
public static partial class SmbTools
{
    private static readonly string[] ToolFolders = ["/usr/bin", "/bin", "/usr/local/bin", "/opt/homebrew/bin", "/usr/sbin", "/sbin"];

    /// <summary>A tool by full path, from the absolute entries of PATH and the usual folders; null when it is not there.</summary>
    public static string? Find(string name)
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathFullyQualified).Concat(ToolFolders);
        foreach (string folder in folders)
        {
            string candidate = Path.Join(folder, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Runs a tool with a time limit; its exit code and output (at most a megabyte each).</summary>
    public static async Task<(int Code, string Output, string Errors)> RunAsync(string tool, IEnumerable<string> arguments, TimeSpan limit, CancellationToken ct,
        string? input = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        // Answers in English whatever the desktop's language, so that they can be read.
        start.Environment["LC_ALL"] = "C";
        start.Environment["LANG"] = "C";
        if (environment is not null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new IOException($"{Path.GetFileName(tool)} could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var errors = ReadBoundedAsync(process.StandardError, timeout.Token);
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), await errors.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"{Path.GetFileName(tool)} did not answer within {limit.TotalSeconds:0} seconds.");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            if (text.Length < 1 << 20) text.Append(buffer, 0, read);
        return text.ToString();
    }

    // ---- Share lists ------------------------------------------------------------------------------------------

    /// <summary>
    /// The disk shares of a server: gvfs (as the desktop's file manager lists them), else smbclient, on Linux;
    /// smbutil on macOS. With <paramref name="credentials"/>, as that user.
    /// </summary>
    public static async Task<IReadOnlyList<SmbShare>> ListSharesAsync(string server, NetworkCredentials? credentials, CancellationToken ct)
    {
        if (OperatingSystem.IsMacOS()) return await ListWithSmbutilAsync(server, credentials, ct).ConfigureAwait(false);
        Exception? failure = null;
        if (credentials is null && Find("gio") is { } gio)
        {
            var (code, output, errors) = await RunAsync(gio, ["list", $"smb://{server}/"], TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            if (code == 0) return ParseGioList(output);
            failure = Failure(server, errors);
        }
        if (Find("smbclient") is { } smbclient)
        {
            var arguments = new List<string> { "-L", $"//{server}", "-g" };
            Dictionary<string, string>? environment = null;
            if (credentials is null) arguments.Add("-N");
            else
            {
                arguments.AddRange(["-U", credentials.User]);
                if (!string.IsNullOrEmpty(credentials.Domain)) arguments.AddRange(["-W", credentials.Domain]);
                // Through the environment, never on the command line where other users could read it.
                environment = new() { ["PASSWD"] = credentials.Password };
            }
            var (code, output, errors) = await RunAsync(smbclient, arguments, TimeSpan.FromSeconds(20), ct, environment: environment).ConfigureAwait(false);
            var shares = ParseSmbclient(output);
            if (code == 0 || shares.Count > 0) return shares;
            failure = Failure(server, errors + output);
        }
        throw failure ?? new IOException("Listing a server's shares needs gvfs (the gvfs-backends package) or Samba's smbclient.");
    }

    private static async Task<IReadOnlyList<SmbShare>> ListWithSmbutilAsync(string server, NetworkCredentials? credentials, CancellationToken ct)
    {
        string smbutil = Find("smbutil") ?? throw new IOException("smbutil, which lists a Mac's view of a server's shares, is missing.");
        // As a guest, then anonymously, never asking on a terminal (-N); a user's password goes through NetFS instead.
        Exception? failure = null;
        foreach (string how in credentials is null ? new[] { "-g", "-A" } : ["-g"])
        {
            var (code, output, errors) = await RunAsync(smbutil, ["view", "-N", how, $"//{server}"], TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            var shares = ParseSmbutil(output);
            if (code == 0) return shares;
            failure = Failure(server, errors + output);
        }
        throw failure!;
    }

    /// <summary>Why a tool could not list or mount, as the exception FileCat reports it by.</summary>
    internal static Exception Failure(string server, string said)
    {
        string text = said.Trim();
        if (Regex.IsMatch(text, "ACCESS_DENIED|LOGON_FAILURE|Password required|Authentication|authentication|EAUTH|syserr = Authentication|Permission denied", RegexOptions.IgnoreCase))
            return new SmbSignInRequiredException($"{server} wants a user name and password.");
        if (Regex.IsMatch(text, "HOST_UNREACHABLE|Connection to .* failed|could not connect|Could not connect|No route to host|NT_STATUS_IO_TIMEOUT|Name or service not known|not found|Unable to find|timed out|failed to resolve", RegexOptions.IgnoreCase))
            return new DirectoryNotFoundException($"{server} is not reachable, or it does not share files over SMB.");
        string line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "no answer";
        return new IOException($"{server}: {line}");
    }

    /// <summary>"gio list smb://server/": one share per line.</summary>
    internal static List<SmbShare> ParseGioList(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name.Length > 0 && !name.Contains('/') && !name.Equals("IPC$", StringComparison.OrdinalIgnoreCase))
            .Select(name => new SmbShare(name, null)).ToList();

    /// <summary>"smbclient -L //server -g": "Disk|name|comment" lines (IPC and printers left out).</summary>
    internal static List<SmbShare> ParseSmbclient(string output)
    {
        var shares = new List<SmbShare>();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|');
            if (parts.Length >= 2 && parts[0] == "Disk" && parts[1].Length > 0)
                shares.Add(new SmbShare(parts[1], parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null));
        }
        return shares;
    }

    /// <summary>"smbutil view //server": a table of share, type, and comment (disk shares kept).</summary>
    internal static List<SmbShare> ParseSmbutil(string output)
    {
        var shares = new List<SmbShare>();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = SmbutilRow().Match(line.TrimEnd());
            if (match.Success && match.Groups["type"].Value == "Disk")
                shares.Add(new SmbShare(match.Groups["name"].Value.Trim(), match.Groups["comment"].Value.Trim() is { Length: > 0 } c ? c : null));
        }
        return shares;
    }

    [GeneratedRegex(@"^(?<name>\S.*?)\s+(?<type>Disk|Pipe|Printer|Comm|Device)(?:\s+(?<comment>.*))?$")]
    private static partial Regex SmbutilRow();

    // ---- Mounted shares ---------------------------------------------------------------------------------------

    /// <summary>Where a share is mounted already (a gvfs mount or cifs on Linux, smbfs on macOS), or null.</summary>
    public static string? FindMount(string server, string share) =>
        Mounts().FirstOrDefault(m => m.Server.Equals(server, StringComparison.OrdinalIgnoreCase) && m.Share.Equals(share, StringComparison.OrdinalIgnoreCase)).Path;

    /// <summary>Every SMB share mounted now: its server, share, and mount point.</summary>
    public static List<(string Server, string Share, string Path)> Mounts()
    {
        var mounts = new List<(string, string, string)>();
        try
        {
            if (OperatingSystem.IsLinux())
            {
                foreach (string line in File.ReadLines("/proc/self/mounts"))
                {
                    var fields = line.Split(' ');
                    if (fields.Length < 3 || fields[2] is not ("cifs" or "smb3" or "smbfs")) continue;
                    if (ParseUnc(Unescape(fields[0])) is { } unc) mounts.Add((unc.Server, unc.Share, Unescape(fields[1])));
                }
                // gvfs: $XDG_RUNTIME_DIR/gvfs/smb-share:server=NAME,share=SHARE[,user=...]
                string gvfs = Path.Join(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? $"/run/user/{getuid()}", "gvfs");
                if (Directory.Exists(gvfs))
                    foreach (string dir in Directory.EnumerateDirectories(gvfs, "smb-share:*"))
                        if (ParseGvfsName(Path.GetFileName(dir)) is { } g) mounts.Add((g.Server, g.Share, dir));
            }
            else if (OperatingSystem.IsMacOS() && Find("mount") is { } mount)
            {
                var run = RunAsync(mount, [], TimeSpan.FromSeconds(5), CancellationToken.None).GetAwaiter().GetResult();
                foreach (string line in run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    if (ParseMacMount(line) is { } m) mounts.Add(m);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException) { }
        return mounts;
    }

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern uint getuid();

    /// <summary>"//server/share" (or "//user@server/share"), else null.</summary>
    internal static (string Server, string Share)? ParseUnc(string device)
    {
        if (!device.StartsWith("//", StringComparison.Ordinal)) return null;
        var parts = device[2..].Split('/', 2);
        if (parts.Length < 2 || parts[1].Length == 0) return null;
        string server = parts[0][(parts[0].LastIndexOf('@') + 1)..];
        return server.Length == 0 ? null : (server, Uri.UnescapeDataString(parts[1].TrimEnd('/')));
    }

    /// <summary>"smb-share:server=nas,share=public,user=me" (gvfs's mount folder name), else null.</summary>
    public static (string Server, string Share)? ParseGvfsName(string name)
    {
        if (!name.StartsWith("smb-share:", StringComparison.Ordinal)) return null;
        string? server = null, share = null;
        foreach (string pair in name["smb-share:".Length..].Split(','))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string key = pair[..eq], value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            if (key == "server") server = value;
            else if (key == "share") share = value;
        }
        return server is null || share is null ? null : (server, share);
    }

    /// <summary>A line of macOS's "mount": "//user@server/share on /Volumes/share (smbfs, …)", else null.</summary>
    internal static (string Server, string Share, string Path)? ParseMacMount(string line)
    {
        int on = line.IndexOf(" on ", StringComparison.Ordinal);
        int type = line.LastIndexOf(" (smbfs", StringComparison.Ordinal);
        if (on < 0 || type < on) return null;
        return ParseUnc(line[..on]) is { } unc ? (unc.Server, unc.Share, line[(on + 4)..type]) : null;
    }

    /// <summary>Mount-table escapes ("\040" for a space).</summary>
    private static string Unescape(string field) =>
        Regex.Replace(field, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString());

    // ---- Mounting on Linux ------------------------------------------------------------------------------------

    /// <summary>
    /// Mounts a share with gvfs (as the desktop's file manager does) and gives the folder where its files are: as a
    /// guest first, then as <paramref name="credentials"/>' user (the password on gio's input, never its command line).
    /// </summary>
    public static async Task<string> MountWithGvfsAsync(string server, string share, NetworkCredentials? credentials, CancellationToken ct)
    {
        string gio = Find("gio") ?? throw new IOException("Opening a share needs gvfs (the gvfs-backends and gvfs-fuse packages); or mount it with mount.cifs and open its folder.");
        string uri = $"smb://{server}/{Uri.EscapeDataString(share)}";
        (int Code, string Output, string Errors) run;
        if (credentials is null) run = await RunAsync(gio, ["mount", "-a", uri], TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        else
        {
            string user = (string.IsNullOrEmpty(credentials.Domain) ? "" : Uri.EscapeDataString(credentials.Domain) + ";") + Uri.EscapeDataString(credentials.User);
            run = await RunAsync(gio, ["mount", $"smb://{user}@{server}/{Uri.EscapeDataString(share)}"], TimeSpan.FromSeconds(30), ct, input: credentials.Password + "\n").ConfigureAwait(false);
        }
        if (run.Code != 0 && !run.Errors.Contains("already mounted", StringComparison.OrdinalIgnoreCase)) throw Failure(server, run.Errors + run.Output);
        var info = await RunAsync(gio, ["info", uri], TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        foreach (string line in info.Output.Split('\n'))
            if (line.TrimStart().StartsWith("local path:", StringComparison.Ordinal))
                return line[(line.IndexOf(':') + 1)..].Trim();
        return FindMount(server, share)
               ?? throw new IOException("The share is open, but gvfs shows no folder for it: its FUSE part (the gvfs-fuse package) is not running.");
    }
}
