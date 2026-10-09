using System.Diagnostics;
using System.Text;
using FileCat.Core.State;

namespace FileCat.Core.Tools;

public sealed class ToolLaunchException : Exception
{
    public ToolLaunchException(string message) : base(message) { }
    public ToolLaunchException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Values for tool placeholders; paths are absolute.</summary>
public sealed record ToolContext(IReadOnlyList<string> Files, string Directory, string? TargetDirectory = null, string? Prompt = null);

public sealed record ToolLaunchResult(IReadOnlyList<string> Invocations, string? Warning);

/// <summary>
/// Launches external programs with structured arguments under Windows' invocation rules (plan §14.2,
/// TV-17): real executables only, batch targets refused when arguments carry cmd.exe metacharacters,
/// absolute paths with <c>--</c> where supported, and bounded batching or list files under the process/interpreter command-line limit.
/// </summary>
public static class ToolLauncher
{
    public const int WindowsCommandLineLimit = 32_767;
    private const int WindowsBatchCommandLineLimit = 8_191;
    private const string CmdMetacharacters = "&|<>^%!\"\r\n()";

    /// <summary>Tokens: {file} focused/first file, {files} all files as separate arguments, {listfile} a UTF-8 list,
    /// {dir} current folder, {target} target panel folder, {name} file name, {prompt} runtime prompt text.</summary>
    public static IReadOnlyList<string> Placeholders { get; } = ["{file}", "{files}", "{listfile}", "{dir}", "{target}", "{name}", "{prompt}"];

    public static ToolDefinition DetectEditor()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var candidate in new[]
                     {
                         Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
                         Path.Combine(pf, "Microsoft VS Code", "Code.exe"),
                         Path.Combine(pf, "Notepad++", "notepad++.exe"),
                         Path.Combine(local, "Programs", "Zed", "zed.exe"),
                     })
            {
                if (File.Exists(candidate)) return new ToolDefinition { Name = Path.GetFileNameWithoutExtension(candidate), Executable = candidate, Arguments = ["{files}"] };
            }
            return new ToolDefinition { Name = "Notepad", Executable = Path.Combine(Environment.SystemDirectory, "notepad.exe"), Arguments = ["{file}"] };
        }
        foreach (var name in new[] { "code", "gnome-text-editor", "gedit", "kate", "xed" })
        {
            var p = FindOnPath(name);
            if (p is not null) return new ToolDefinition { Name = name, Executable = p, Arguments = ["{files}"] };
        }
        return OperatingSystem.IsMacOS()
            ? new ToolDefinition { Name = "TextEdit", Executable = "/usr/bin/open", Arguments = ["-e", "{files}"] }
            : new ToolDefinition { Name = "xdg-open", Executable = "/usr/bin/xdg-open", Arguments = ["{file}"] };
    }

    /// <summary>
    /// A program by full path from the absolute entries of PATH (<paramref name="path"/> in tests). A relative entry
    /// ("." and the like) would follow FileCat's current directory, where a program could have been planted (release
    /// plan I16): such entries are not searched.
    /// </summary>
    public static string? FindOnPath(string name, string? path = null)
    {
        foreach (var entry in (path ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string dir = entry.Trim('"');
            if (!Path.IsPathFullyQualified(dir)) continue;
            foreach (var ext in OperatingSystem.IsWindows() ? new[] { ".exe" } : [""])
            {
                try
                {
                    var p = Path.Combine(dir, name + ext);
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }

    /// <summary>Builds (and validates) the invocations without starting anything; used for previews and tests.</summary>
    public static IReadOnlyList<(string Executable, IReadOnlyList<string> Arguments)> Plan(ToolDefinition tool, ToolContext ctx, string tempDirectory, out string? warning)
        => PlanOwned(tool, ctx, tempDirectory, out warning, out _);

    private static IReadOnlyList<(string Executable, IReadOnlyList<string> Arguments)> PlanOwned(
        ToolDefinition tool, ToolContext ctx, string tempDirectory, out string? warning, out List<string> listFiles)
    {
        listFiles = [];
        try
        {
            warning = null;
            var exe = ResolveExecutable(tool.Executable);
            bool isBatch = exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            int commandLineLimit = CommandLineLimit(exe);
            var files = ctx.Files.Select(Path.GetFullPath).ToList();
            foreach (var f in files)
            {
                if (!Path.IsPathFullyQualified(f)) throw new ToolLaunchException("Tool arguments must be absolute paths.");
            }
            var args = new List<string>();
            var resolved = new List<(bool Files, IReadOnlyList<string> Arguments)>();
            foreach (var token in tool.Arguments)
            {
                var values = new List<string>();
                switch (token)
                {
                    case "{files}": values.AddRange(files.Select(ProtectOptionLike)); break;
                    case "{file}": if (files.Count > 0) values.Add(ProtectOptionLike(files[0])); break;
                    case "{listfile}": values.Add(WriteListFile(files, tempDirectory, listFiles)); break;
                    default: values.Add(Substitute(token, ctx, files)); break;
                }
                resolved.Add((token == "{files}", values));
                args.AddRange(values);
            }
            if (isBatch && !tool.ShellMode && args.Any(a => a.IndexOfAny(CmdMetacharacters.ToCharArray()) >= 0))
                throw new ToolLaunchException($"\"{Path.GetFileName(exe)}\" is a batch file, and cmd.exe would interpret special characters in the arguments (BatBadBut). Point the tool to the real program (for VS Code: Code.exe), or enable shell mode for this tool if you accept that risk.");
            var result = new List<(string, IReadOnlyList<string>)>();
            if (CommandLineLength(exe, args) <= commandLineLimit)
            {
                result.Add((exe, args));
                return result;
            }
            // Too long for one command line: split per file when the tool takes {files}, else require a list file.
            if (!tool.Arguments.Contains("{files}"))
                throw new ToolLaunchException("The selection is too long for one command line. Use the {listfile} token for this tool.");
            List<string> BatchArguments(IReadOnlyList<string> selected) => resolved
                .SelectMany(part => part.Files ? selected : part.Arguments).ToList();
            if (CommandLineLength(exe, BatchArguments([])) > commandLineLimit)
                throw new ToolLaunchException("The fixed tool arguments are too long for one command line.");
            var batch = new List<string>();
            foreach (var f in files)
            {
                var candidate = BatchArguments(batch.Append(ProtectOptionLike(f)).ToList());
                if (CommandLineLength(exe, candidate) > commandLineLimit)
                {
                    if (batch.Count > 0)
                    {
                        result.Add((exe, BatchArguments(batch)));
                        batch.Clear();
                    }
                    candidate = BatchArguments([ProtectOptionLike(f)]);
                    if (CommandLineLength(exe, candidate) > commandLineLimit)
                        throw new ToolLaunchException("A selected file cannot fit one command line. Use the {listfile} token for this tool.");
                }
                batch.Add(ProtectOptionLike(f));
            }
            if (batch.Count > 0) result.Add((exe, BatchArguments(batch)));
            warning = $"The selection exceeds the Windows command-line limit, so {result.Count} separate invocations were used.";
            return result;
        }
        catch
        {
            RetireLists(listFiles);
            throw;
        }
    }

    public static ToolLaunchResult Launch(ToolDefinition tool, ToolContext ctx, string tempDirectory)
    {
        var plan = PlanOwned(tool, ctx, tempDirectory, out var warning, out var listFiles);
        var summaries = new List<string>();
        bool started = false;
        try
        {
            foreach (var (exe, args) in plan)
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Substitute(tool.WorkingDirectory, ctx, ctx.Files.ToList()) is { Length: > 0 } wd && Directory.Exists(wd) ? wd : ctx.Directory,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                try
                {
                    using var process = Process.Start(psi);
                    started |= process is not null;
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new ToolLaunchException($"Could not start \"{tool.Name}\": {ex.Message}");
                }
                summaries.Add(exe + " " + string.Join(" ", args));
            }
            return new ToolLaunchResult(summaries, warning);
        }
        catch
        {
            // An earlier child may still need its list; only an entirely unstarted launch owns cleanup.
            if (!started) RetireLists(listFiles);
            throw;
        }
    }

    /// <summary>A real program file: a full path that exists, or a bare name found on PATH (never the browsed folder).</summary>
    public static string ResolveExecutable(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) throw new ToolLaunchException("No program is configured for this tool.");
        var expanded = Environment.ExpandEnvironmentVariables(exe.Trim().Trim('"'));
        if (Path.IsPathFullyQualified(expanded))
        {
            if (!File.Exists(expanded)) throw new ToolLaunchException($"The program \"{expanded}\" does not exist.");
            return expanded;
        }
        // Bare names are resolved to a real file on PATH before launch; nothing runs through a shell.
        return FindOnPath(Path.GetFileNameWithoutExtension(expanded)) ?? throw new ToolLaunchException($"The program \"{exe}\" was not found on PATH.");
    }

    /// <summary>A file name beginning with '-' could be read as an option; absolute paths never start with '-'.</summary>
    private static string ProtectOptionLike(string absolutePath) => absolutePath;

    private static string Substitute(string token, ToolContext ctx, IReadOnlyList<string> files) => token
        .Replace("{dir}", ctx.Directory, StringComparison.Ordinal)
        .Replace("{target}", ctx.TargetDirectory ?? string.Empty, StringComparison.Ordinal)
        .Replace("{name}", files.Count > 0 ? Path.GetFileName(files[0]) : string.Empty, StringComparison.Ordinal)
        .Replace("{prompt}", ctx.Prompt ?? string.Empty, StringComparison.Ordinal);

    private static string WriteListFile(IReadOnlyList<string> files, string tempDirectory, List<string> listFiles)
    {
        try
        {
            Directory.CreateDirectory(tempDirectory);
            var path = Path.Combine(tempDirectory, $"filelist-{Guid.NewGuid():N}.txt");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            listFiles.Add(path);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(string.Join("\r\n", files) + "\r\n");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ToolLaunchException("Could not create the tool's list file: " + ex.Message, ex);
        }
    }

    private static void RetireLists(IEnumerable<string> listFiles)
    {
        foreach (string path in listFiles)
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static int CommandLineLimit(string executable)
    {
        bool batch = executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows() || !batch) return WindowsCommandLineLimit;
        string commandProcessor = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } configured
            ? configured : Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return WindowsBatchCommandLineLimit - commandProcessor.Length - 5;
    }

    /// <summary>Length of the command line Windows builds from an argument vector (quotes and escapes included).</summary>
    public static int CommandLineLength(string exe, IReadOnlyList<string> args)
    {
        int len = exe.Length + 3;
        foreach (var a in args) len += 1 + QuotedLength(a);
        return len;
    }

    private static int QuotedLength(string a)
    {
        if (a.Length > 0 && a.IndexOfAny([' ', '\t', '"']) < 0) return a.Length;
        int n = 2;
        int backslashes = 0;
        foreach (var c in a)
        {
            if (c == '\\') backslashes++;
            else if (c == '"')
            {
                n += backslashes + 1;
                backslashes = 0;
            }
            else backslashes = 0;
            n++;
        }
        return n + backslashes;
    }
}
