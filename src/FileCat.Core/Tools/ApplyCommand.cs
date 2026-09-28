using System.Diagnostics;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tools;

/// <summary>
/// A command run once per item (FAR's Apply command, plan §14.2): a program with arguments, split into tokens before
/// any name is substituted, or an explicit shell command line in which every name is quoted for that shell.
/// </summary>
public sealed record ApplyCommandSpec(string CommandLine, bool ShellMode = false, string Shell = "");

/// <summary>One planned run; <see cref="Problem"/> blocks it (and, in the dialog, the whole plan).</summary>
public sealed record ApplyInvocation(ItemRef Item, string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory, string Display, string? Problem);

public static class ApplyCommandPlanner
{
    public static IReadOnlyList<string> Placeholders { get; } = ["{file}", "{name}", "{stem}", "{ext}", "{dir}", "{target}", "{index}"];

    private const string CmdMetacharacters = "&|<>^%!\"\r\n()";

    /// <summary>
    /// Splits a command line into tokens: whitespace separates, double quotes group (a doubled quote inside quotes is
    /// one literal quote). Backslashes are literal, so Windows paths need no escaping.
    /// </summary>
    public static IReadOnlyList<string> Split(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
                any = true;
            }
            else if (!quoted && char.IsWhiteSpace(c))
            {
                if (any) tokens.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }
        if (any) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>The shell FileCat uses for shell mode on this platform: cmd on Windows, /bin/sh elsewhere.</summary>
    public static string DefaultShell => OperatingSystem.IsWindows() ? "cmd" : "sh";

    public static IReadOnlyList<ApplyInvocation> Plan(ApplyCommandSpec spec, IReadOnlyList<ItemRef> items, string? targetDirectory,
        Func<string, string> resolveProgram)
    {
        var rows = new List<ApplyInvocation>(items.Count);
        var tokens = spec.ShellMode ? [] : Split(spec.CommandLine);
        string? programProblem = null;
        string program = "";
        if (spec.CommandLine.Trim().Length == 0) programProblem = "Enter a command.";
        else if (!spec.ShellMode)
        {
            try { program = resolveProgram(tokens[0]); }
            catch (ToolLaunchException ex) { programProblem = ex.Message + " Commands built into the shell (such as copy or ren) need \"Run through the shell\"."; }
        }
        string shell = spec.Shell.Length > 0 ? spec.Shell : DefaultShell;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.FileSystemPath is not { } path)
            {
                rows.Add(new ApplyInvocation(item, "", [], "", item.Name, "Commands run only for files and folders on disk."));
                continue;
            }
            string dir = Path.GetDirectoryName(path) ?? path;
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["{file}"] = path,
                ["{name}"] = item.Name,
                ["{stem}"] = item.IsContainer ? item.Name : Path.GetFileNameWithoutExtension(item.Name),
                ["{ext}"] = item.IsContainer ? "" : Path.GetExtension(item.Name).TrimStart('.'),
                ["{dir}"] = dir,
                ["{target}"] = targetDirectory ?? "",
                ["{index}"] = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            if (spec.ShellMode)
            {
                string line = Substitute(spec.CommandLine, values, v => ShellQuoting.Quote(v, shell));
                var (exe, args) = ShellInvocation(shell, line);
                string? problem = programProblem ?? (targetDirectory is null && spec.CommandLine.Contains("{target}", StringComparison.Ordinal) ? "There is no target panel folder for {target}." : null);
                rows.Add(new ApplyInvocation(item, exe, args, dir, line, problem ?? LengthProblem(exe, args)));
                continue;
            }
            var arguments = new List<string>();
            string? argumentProblem = programProblem;
            foreach (var token in tokens.Skip(1))
            {
                string value = Substitute(token, values, v => v);
                // A bare name that starts with '-' could be read as an option; the full path never can (plan §14.2).
                if (value.StartsWith('-') && (token.StartsWith("{name}", StringComparison.Ordinal) || token.StartsWith("{stem}", StringComparison.Ordinal) || token.StartsWith("{ext}", StringComparison.Ordinal)))
                    argumentProblem ??= $"\"{value}\" starts with '-', so the program could read it as an option; use {{file}} (the full path) instead.";
                arguments.Add(value);
            }
            if (targetDirectory is null && spec.CommandLine.Contains("{target}", StringComparison.Ordinal)) argumentProblem ??= "There is no target panel folder for {target}.";
            // A shell run as the program parses names itself: only plain names pass; shell mode quotes the rest.
            if (IsShell(program) && tokens.Skip(1).Any(t => t.Contains('{')))
            {
                foreach (var (placeholder, value) in values)
                {
                    if (tokens.Skip(1).Any(t => t.Contains(placeholder, StringComparison.Ordinal)) && !IsPlain(value))
                    {
                        argumentProblem ??= $"\"{Path.GetFileName(program)}\" is a shell and would interpret special characters or spaces in \"{value}\". Tick \"Run through the shell\" so names are quoted for it.";
                        break;
                    }
                }
            }
            bool isBatch = program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || program.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            if (isBatch && arguments.Any(a => a.IndexOfAny(CmdMetacharacters.ToCharArray()) >= 0))
                argumentProblem ??= $"\"{Path.GetFileName(program)}\" is a batch file and cmd.exe would interpret special characters in this name (BatBadBut). Use the real program, or \"Run through the shell\".";
            rows.Add(new ApplyInvocation(item, program, arguments, dir, Display(program.Length > 0 ? program : tokens.FirstOrDefault() ?? "", arguments),
                argumentProblem ?? (program.Length > 0 ? LengthProblem(program, arguments) : null)));
        }
        return rows;
    }

    private static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase) { "cmd", "powershell", "pwsh", "bash", "sh", "zsh", "dash", "ksh", "fish", "wsl" };

    private static bool IsShell(string program) => program.Length > 0 && Shells.Contains(Path.GetFileNameWithoutExtension(program));

    private static bool IsPlain(string value) => value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '\\' or '/' or ':');

    private static string? LengthProblem(string exe, IReadOnlyList<string> args) =>
        OperatingSystem.IsWindows() && ToolLauncher.CommandLineLength(exe, args) > ToolLauncher.WindowsCommandLineLimit
            ? "The command line would exceed Windows' limit of 32,767 characters." : null;

    private static (string Exe, IReadOnlyList<string> Args) ShellInvocation(string shell, string line) => shell.ToLowerInvariant() switch
    {
        "powershell" or "pwsh" => (ToolLauncher.FindOnPath(shell == "pwsh" ? "pwsh" : "powershell") ?? "powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", line]),
        "cmd" or "cmd.exe" => (Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/s", "/c", line]),
        _ => ("/bin/sh", ["-c", line]),
    };

    private static string Substitute(string text, IReadOnlyDictionary<string, string> values, Func<string, string> quote)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '{')
            {
                int close = text.IndexOf('}', i);
                if (close > i && values.TryGetValue(text[i..(close + 1)], out var value))
                {
                    sb.Append(quote(value));
                    i = close + 1;
                    continue;
                }
            }
            sb.Append(text[i++]);
        }
        return sb.ToString();
    }

    /// <summary>How the invocation looks on a command line (for the preview; the launch passes the tokens as they are).</summary>
    public static string Display(string exe, IReadOnlyList<string> args) =>
        string.Join(" ", new[] { exe }.Concat(args).Select(a => a.Length == 0 || a.IndexOfAny([' ', '\t']) >= 0 ? "\"" + a + "\"" : a));
}

/// <summary>
/// Runs <see cref="JobRequest.Invocations"/> one after another, each in its item's folder, and records each exit code.
/// Output is captured (bounded) so a failure can say why. A cancel stops before the next item and leaves a running
/// program to finish on its own.
/// </summary>
internal sealed class ApplyCommandExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    private const int OutputTail = 2000;

    public override void Execute()
    {
        var runs = Job.Request.Invocations ?? throw new InvalidOperationException("The invocations are required.");
        int succeeded = 0, failed = 0;
        Job.AddTotals(runs.Count, 0);
        try
        {
            for (int i = 0; i < runs.Count; i++)
            {
                Job.Checkpoint();
                var run = runs[i];
                string path = run.Item.FileSystemPath ?? run.Item.Name;
                Job.SetCurrent(path);
                if (run.Problem is not null)
                {
                    failed++;
                    Job.ItemFailed();
                    Job.RootFailed(i);
                    Issue(IssueSeverity.Error, path, "Not run: " + run.Problem, StepOutcome.Failed);
                    continue;
                }
                var (exitCode, output) = RunOne(run);
                if (exitCode == 0)
                {
                    succeeded++;
                    Job.ItemDone();
                    Job.RootCompleted(i);
                }
                else
                {
                    failed++;
                    Job.ItemFailed();
                    Job.RootFailed(i);
                    Issue(IssueSeverity.Error, path, exitCode is null
                        ? "The program could not be started: " + output
                        : $"The program ended with exit code {exitCode}" + (output.Length > 0 ? ": " + output : "."), StepOutcome.Failed);
                }
            }
        }
        finally
        {
            string canceled = Job.IsCancellationRequested ? $"canceled after {succeeded + failed:N0} of {runs.Count:N0}; " : "";
            Job.SetSummary(canceled + (failed == 0 ? $"{succeeded:N0} succeeded" : $"{succeeded:N0} succeeded, {failed:N0} failed"));
        }
    }

    private (int? ExitCode, string Output) RunOne(ApplyInvocation run)
    {
        var psi = new ProcessStartInfo(run.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Directory.Exists(run.WorkingDirectory) ? run.WorkingDirectory : Environment.CurrentDirectory,
        };
        foreach (var a in run.Arguments) psi.ArgumentList.Add(a);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        void Keep(StringBuilder sb, string? line)
        {
            if (line is null) return;
            lock (sb)
            {
                sb.AppendLine(line);
                if (sb.Length > OutputTail * 2) sb.Remove(0, sb.Length - OutputTail);
            }
        }
        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("No process was started.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (null, ex.Message);
        }
        using (process)
        {
            process.OutputDataReceived += (_, e) => Keep(stdout, e.Data);
            process.ErrorDataReceived += (_, e) => Keep(stderr, e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // Programs that ask for input get end-of-file instead of waiting forever.
            process.StandardInput.Close();
            while (!process.WaitForExit(200))
            {
                if (Job.IsCancellationRequested)
                {
                    Issue(IssueSeverity.Warning, run.Item.FileSystemPath ?? run.Item.Name,
                        $"\"{Path.GetFileName(run.Executable)}\" was still running for this item when the job was canceled; it was left to finish.", StepOutcome.Skipped);
                    throw new OperationCanceledException();
                }
            }
            process.WaitForExit();
            string tail;
            lock (stderr) tail = stderr.ToString().Trim();
            if (tail.Length == 0) lock (stdout) tail = stdout.ToString().Trim();
            var lastLines = tail.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).TakeLast(3);
            return (process.ExitCode, string.Join(" / ", lastLines));
        }
    }
}
