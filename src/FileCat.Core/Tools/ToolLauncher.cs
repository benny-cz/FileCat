using System.Diagnostics;
using System.Text;
using FileCat.Core.State;

namespace FileCat.Core.Tools;

public sealed class ToolLaunchException(string message) : Exception(message);

/// <summary>Values for tool placeholders; paths are absolute.</summary>
public sealed record ToolContext(IReadOnlyList<string> Files, string Directory, string? TargetDirectory = null, string? Prompt = null);

public sealed record ToolLaunchResult(IReadOnlyList<string> Invocations, string? Warning);

/// <summary>
/// Launches external programs with structured arguments under Windows' invocation rules (plan §14.2,
/// TV-17): real executables only, batch targets refused when arguments carry cmd.exe metacharacters,
/// absolute paths with <c>--</c> where supported, and list files when a command line would exceed 32,767 characters.
/// </summary>
public static class ToolLauncher
{
    public const int WindowsCommandLineLimit = 32_767;
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

    public static string? FindOnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
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
    {
        warning = null;
        var exe = ResolveExecutable(tool.Executable);
        bool isBatch = exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        var files = ctx.Files.Select(Path.GetFullPath).ToList();
        foreach (var f in files)
        {
            if (!Path.IsPathFullyQualified(f)) throw new ToolLaunchException("Tool arguments must be absolute paths.");
        }
        var args = new List<string>();
        foreach (var token in tool.Arguments)
        {
            switch (token)
            {
                case "{files}": args.AddRange(files.Select(ProtectOptionLike)); break;
                case "{file}": if (files.Count > 0) args.Add(ProtectOptionLike(files[0])); break;
                case "{listfile}": args.Add(WriteListFile(files, tempDirectory)); break;
                default: args.Add(Substitute(token, ctx, files)); break;
            }
        }
        if (isBatch && !tool.ShellMode && args.Any(a => a.IndexOfAny(CmdMetacharacters.ToCharArray()) >= 0))
            throw new ToolLaunchException($"\"{Path.GetFileName(exe)}\" is a batch file, and cmd.exe would interpret special characters in the arguments (BatBadBut). Point the tool to the real program (for VS Code: Code.exe), or enable shell mode for this tool if you accept that risk.");
        var result = new List<(string, IReadOnlyList<string>)>();
        if (CommandLineLength(exe, args) <= WindowsCommandLineLimit)
        {
            result.Add((exe, args));
            return result;
        }
        // Too long for one command line: split per file when the tool takes {files}, else require a list file.
        if (!tool.Arguments.Contains("{files}"))
            throw new ToolLaunchException("The selection is too long for one command line. Use the {listfile} token for this tool.");
        var fixedArgs = tool.Arguments.Where(t => t != "{files}").Select(t => Substitute(t, ctx, files)).ToList();
        var batch = new List<string>();
        foreach (var f in files)
        {
            var candidate = fixedArgs.Concat(batch).Append(ProtectOptionLike(f)).ToList();
            if (CommandLineLength(exe, candidate) > WindowsCommandLineLimit && batch.Count > 0)
            {
                result.Add((exe, fixedArgs.Concat(batch).ToList()));
                batch.Clear();
            }
            batch.Add(ProtectOptionLike(f));
        }
        if (batch.Count > 0) result.Add((exe, fixedArgs.Concat(batch).ToList()));
        warning = $"The selection exceeds the Windows command-line limit, so {result.Count} separate invocations were used.";
        return result;
    }

    public static ToolLaunchResult Launch(ToolDefinition tool, ToolContext ctx, string tempDirectory)
    {
        var plan = Plan(tool, ctx, tempDirectory, out var warning);
        var summaries = new List<string>();
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
                Process.Start(psi)?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new ToolLaunchException($"Could not start \"{tool.Name}\": {ex.Message}");
            }
            summaries.Add(exe + " " + string.Join(" ", args));
        }
        return new ToolLaunchResult(summaries, warning);
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

    private static string WriteListFile(IReadOnlyList<string> files, string tempDirectory)
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, $"filelist-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, string.Join("\r\n", files) + "\r\n", new UTF8Encoding(false));
        return path;
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
