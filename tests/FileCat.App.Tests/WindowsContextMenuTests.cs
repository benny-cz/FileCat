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

        bool completed = await WindowsContextMenu.MonitorHostAsync(child,
            TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        Assert.False(completed);
        Assert.True(child.HasExited);
    }

    [Fact]
    public async Task User_choice_time_is_distinct_from_a_bounded_hung_verb()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The context-menu lifecycle helper is Windows-only.");
        string ready = WindowsContextMenu.ReadyMessage, invoke = WindowsContextMenu.InvokeMessage;
        using var child = StartFixture($"echo {ready}& ping -n 2 127.0.0.1 >nul& echo {invoke}& ping -n 30 127.0.0.1 >nul");

        bool completed = await WindowsContextMenu.MonitorHostAsync(child,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(300));

        Assert.False(completed);
        Assert.True(child.HasExited);
    }

    [Fact]
    public async Task A_ready_context_host_can_cancel_cleanly_without_invoking_a_verb()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The context-menu lifecycle helper is Windows-only.");
        using var child = StartFixture($"echo {WindowsContextMenu.ReadyMessage}& exit /b 10");

        bool completed = await WindowsContextMenu.MonitorHostAsync(child,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        Assert.True(completed);
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
