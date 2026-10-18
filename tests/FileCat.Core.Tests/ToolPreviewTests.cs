using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

public sealed class ToolPreviewTests
{
    [Fact]
    public void Preview_resolves_argument_boundaries_without_creating_a_list_file_or_starting_a_tool()
    {
        string temp = Path.Combine(Path.GetTempPath(), "filecat-tool-preview", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(temp, "example file.txt");
        string exe = Environment.ProcessPath!;
        var tool = new ToolDefinition
        {
            Name = "Editor",
            Executable = exe,
            Arguments = ["--profile", "a b", "", "{file}", "{name}", "{dir}", "{listfile}"],
        };

        var invocation = Assert.Single(ToolLauncher.Preview(tool, new ToolContext([file], temp), temp, out var warning));

        Assert.Equal(exe, invocation.Executable);
        Assert.Equal(["--profile", "a b", "", file, "example file.txt", temp,
            Path.Combine(temp, $"filelist-{Guid.Empty:N}.txt")], invocation.Arguments);
        Assert.Null(warning);
        Assert.False(Directory.Exists(temp));
    }

    [Fact]
    public void Preview_rejects_a_missing_program_without_creating_a_temporary_directory()
    {
        string temp = Path.Combine(Path.GetTempPath(), "filecat-tool-preview", Guid.NewGuid().ToString("N"));
        var tool = new ToolDefinition { Name = "Missing", Executable = Path.Combine(temp, "missing.exe"), Arguments = ["{listfile}"] };

        Assert.Throws<ToolLaunchException>(() => ToolLauncher.Preview(tool, new ToolContext([], temp), temp, out _));
        Assert.False(Directory.Exists(temp));
    }
}
