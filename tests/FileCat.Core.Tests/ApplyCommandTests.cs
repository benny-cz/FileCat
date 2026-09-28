using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

public sealed class ApplyCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly JobManager _jobs;

    public ApplyCommandTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _jobs = new JobManager(new PortableFileOperations(), _providers, Path.Combine(_dir.Path, "journal"));
    }

    public void Dispose() => _dir.Dispose();

    private ItemRef Item(string relative) => ItemRef.ForFileSystemPath(Path.Combine(_dir.Path, relative), EntryKind.File);

    private static string Resolve(string name) => name switch
    {
        "tool" => Path.Combine(Path.GetTempPath(), "tool.exe"),
        "run" => Path.Combine(Path.GetTempPath(), "run.cmd"),
        "cmd" => Path.Combine(Path.GetTempPath(), "cmd.exe"),
        _ => throw new ToolLaunchException($"The program \"{name}\" was not found on PATH."),
    };

    [Fact]
    public void Command_lines_split_into_tokens_before_names_are_substituted()
    {
        Assert.Equal(["tool", "a b", "c", "", "say \"hi\""], ApplyCommandPlanner.Split("tool \"a b\" c \"\" \"say \"\"hi\"\"\""));
        _dir.File("my song.flac");
        var rows = ApplyCommandPlanner.Plan(new ApplyCommandSpec("tool -i {file} --out {target}/{stem}.mp3 --n {index}"), [Item("my song.flac")],
            Path.Combine(_dir.Path, "out"), Resolve);
        var row = Assert.Single(rows);
        Assert.Null(row.Problem);
        Assert.Equal(Resolve("tool"), row.Executable);
        Assert.Equal(["-i", Path.Combine(_dir.Path, "my song.flac"), "--out", Path.Combine(_dir.Path, "out") + "/my song.mp3", "--n", "1"], row.Arguments);
        Assert.Equal(_dir.Path, row.WorkingDirectory);
    }

    [Fact]
    public void Risky_invocations_are_refused_with_a_way_out()
    {
        _dir.File("-rf");
        _dir.File("a&b.txt");
        Assert.Contains("option", ApplyCommandPlanner.Plan(new ApplyCommandSpec("tool {name}"), [Item("-rf")], null, Resolve)[0].Problem);
        Assert.Null(ApplyCommandPlanner.Plan(new ApplyCommandSpec("tool {file}"), [Item("-rf")], null, Resolve)[0].Problem);
        Assert.Contains("BatBadBut", ApplyCommandPlanner.Plan(new ApplyCommandSpec("run {file}"), [Item("a&b.txt")], null, Resolve)[0].Problem);
        Assert.Contains("is a shell", ApplyCommandPlanner.Plan(new ApplyCommandSpec("cmd /c type {file}"), [Item("a&b.txt")], null, Resolve)[0].Problem);
        Assert.Contains("Run through the shell", ApplyCommandPlanner.Plan(new ApplyCommandSpec("copy {file} x"), [Item("-rf")], null, Resolve)[0].Problem);
        Assert.Contains("target", ApplyCommandPlanner.Plan(new ApplyCommandSpec("tool {target}"), [Item("-rf")], null, Resolve)[0].Problem);

        // Shell mode quotes every name for its shell.
        var cmd = ApplyCommandPlanner.Plan(new ApplyCommandSpec("type {name} > {stem}.out", ShellMode: true, Shell: "cmd"), [Item("a&b.txt")], null, Resolve)[0];
        Assert.Null(cmd.Problem);
        Assert.Equal("type \"a&b.txt\" > \"a&b\".out", cmd.Display);
        var sh = ApplyCommandPlanner.Plan(new ApplyCommandSpec("cat {name}", ShellMode: true, Shell: "sh"), [Item("a&b.txt")], null, Resolve)[0];
        Assert.Equal("cat 'a&b.txt'", sh.Display);
        Assert.Equal(["-c", "cat 'a&b.txt'"], sh.Arguments);
    }

    [Fact]
    public async Task Each_item_runs_in_its_folder_and_exit_codes_decide_the_outcome()
    {
        _dir.File("one.txt", "1");
        _dir.File("sub/two.txt", "2");
        var items = new[] { Item("one.txt"), Item("sub/two.txt") };
        async Task<Job> Run(string command)
        {
            var rows = ApplyCommandPlanner.Plan(new ApplyCommandSpec(command, ShellMode: true), items, null, ToolLauncher.ResolveExecutable);
            Assert.All(rows, r => Assert.Null(r.Problem));
            var job = _jobs.Submit(new JobRequest { Kind = JobKind.ApplyCommand, Sources = items, Invocations = rows });
            while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
            return job;
        }

        var ok = await Run("echo x{index}> {stem}.out");
        Assert.Equal(JobState.Completed, ok.State);
        Assert.Equal("2 succeeded", ok.Summary);
        Assert.StartsWith("x1", File.ReadAllText(Path.Combine(_dir.Path, "one.out")));
        Assert.StartsWith("x2", File.ReadAllText(Path.Combine(_dir.Path, "sub", "two.out")));

        var failing = await Run("echo broken {name} 1>&2 && exit 3");
        Assert.Equal(JobState.Failed, failing.State);
        Assert.Equal("0 succeeded, 2 failed", failing.Summary);
        Assert.All(failing.Issues.Where(i => i.Severity == IssueSeverity.Error), i => Assert.Contains("exit code 3", i.Message));
        Assert.Contains(failing.Issues, i => i.Message.Contains("broken one.txt", StringComparison.Ordinal));
    }
}
