using System.Diagnostics;
using FileCat.Core.State;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class WindowsContextMenuTests
{
    [Fact]
    public void A_high_integrity_process_cannot_become_the_Shell_parser_host()
    {
        Assert.Equal(OperatingSystem.IsWindows() && !WindowsTokenAuthority.IsHighIntegrity,
            WindowsContextMenu.CanStartHost());
    }

    [Fact]
    public async Task Helper_stderr_is_fully_drained_while_only_the_logged_prefix_is_retained()
    {
        const int retained = 240;
        string prefix = new('A', retained);
        byte[] data = System.Text.Encoding.UTF8.GetBytes(prefix + new string('B', 4 << 20) + "END");
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new StreamReader(stream);

        string details = await WindowsContextMenu.ReadErrorPrefixAsync(reader);

        Assert.Equal(prefix, details);
        Assert.Equal(data.Length, stream.Position);
    }

    [Fact]
    public void Files_in_one_folder_use_the_shell_even_when_access_checks_would_fail()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows Shell menus are Windows-only.");
        string folder = Path.Combine(Path.GetTempPath(), "filecat-menu-check");
        Assert.True(WindowsContextMenu.CanShow([Path.Combine(folder, "first"), Path.Combine(folder, "second")]));
        Assert.False(WindowsContextMenu.CanShow([Path.Combine(folder, "first"), Path.Combine(folder, "subfolder", "second")]));
        Assert.False(WindowsContextMenu.CanShow([Path.Combine(folder, "bad\0name")]));
    }

    [Fact]
    public async Task A_context_host_that_never_becomes_ready_is_ended()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The context-menu lifecycle helper is Windows-only.");
        using var child = StartFixture("ping -n 30 127.0.0.1 >nul");

        var outcome = await WindowsContextMenu.MonitorHostAsync(child,
            TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        Assert.Equal(WindowsContextMenu.HostOutcome.Ended, outcome);
        Assert.True(child.HasExited);
    }

    [Fact]
    public async Task A_verb_that_outlasts_its_deadline_is_left_to_finish_its_work()
    {
        // Review finding R-M8: a synchronous verb, such as a paste of a large folder, was ended at its 20-second deadline,
        // leaving a truncated destination.
        if (!OperatingSystem.IsWindows()) Assert.Skip("The context-menu lifecycle helper is Windows-only.");
        string ready = WindowsContextMenu.ReadyMessage, invoke = WindowsContextMenu.InvokeMessage;
        using var child = StartFixture($"echo {ready}& ping -n 2 127.0.0.1 >nul& echo {invoke}& ping -n 30 127.0.0.1 >nul");
        try
        {
            var outcome = await WindowsContextMenu.MonitorHostAsync(child,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(300));

            Assert.Equal(WindowsContextMenu.HostOutcome.StillRunning, outcome);
            Assert.False(child.HasExited);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_line_a_Shell_extension_writes_while_its_verb_runs_does_not_end_the_helper()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The context-menu lifecycle helper is Windows-only.");
        string ready = WindowsContextMenu.ReadyMessage, invoke = WindowsContextMenu.InvokeMessage;
        using var child = StartFixture($"echo {ready}& echo {invoke}& echo a Shell extension's own line& ping -n 2 127.0.0.1 >nul& exit /b 0");

        var outcome = await WindowsContextMenu.MonitorHostAsync(child,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));

        Assert.Equal(WindowsContextMenu.HostOutcome.Exited, outcome);
        Assert.Equal(0, child.ExitCode);
    }

    [Fact]
    public async Task A_ready_context_host_can_cancel_cleanly_without_invoking_a_verb()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The context-menu lifecycle helper is Windows-only.");
        using var child = StartFixture($"echo {WindowsContextMenu.ReadyMessage}& exit /b 10");

        var outcome = await WindowsContextMenu.MonitorHostAsync(child,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        Assert.Equal(WindowsContextMenu.HostOutcome.Exited, outcome);
        Assert.Equal(10, child.ExitCode);
    }

    private static Process StartFixture(string command)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = "/d /s /c \"" + command + "\"",
        };
        return Process.Start(start)!;
    }
}
