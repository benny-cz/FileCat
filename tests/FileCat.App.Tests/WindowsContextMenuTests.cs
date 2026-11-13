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

    [Fact]
    public async Task A_New_command_reports_its_item_on_the_helpers_last_line_whatever_the_code_page()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The context-menu lifecycle helper is Windows-only.");
        // Czech Windows names it so: the name travels as base64, so no console code page can change it.
        const string name = "Nová složka (2)";
        string ready = WindowsContextMenu.ReadyMessage, invoke = WindowsContextMenu.InvokeMessage;
        using var child = StartFixture($"echo {ready}& echo {invoke}& echo an extension's own line& echo {WindowsContextMenu.CreatedLine(name)}& exit /b 19");
        var report = new WindowsContextMenu.HostReport();

        var outcome = await WindowsContextMenu.MonitorHostAsync(child,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), report);
        await report.Drained.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(WindowsContextMenu.HostOutcome.Exited, outcome);
        Assert.Equal(19, child.ExitCode);
        Assert.Equal(name, WindowsContextMenu.CreatedName(report.Created));
    }

    [Fact]
    public async Task A_created_report_counts_only_as_the_last_line_and_overlong_output_is_not_kept()
    {
        string created = WindowsContextMenu.CreatedLine("New folder");
        Assert.Null(await DrainedReport(created + "\r\nan extension wrote after it\r\n"));
        Assert.Equal("New folder", WindowsContextMenu.CreatedName(await DrainedReport("noise\r\n" + created + "\r\n")));
        Assert.Equal("New folder", WindowsContextMenu.CreatedName(await DrainedReport("noise\n" + created)));
        // A line longer than any report is read through in pieces and counts as an ordinary line.
        Assert.Null(await DrainedReport(created + new string('A', 1 << 20) + "\n"));
        Assert.Null(await DrainedReport(created + "\n" + new string('B', 1 << 20)));

        static async Task<string?> DrainedReport(string output)
        {
            using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(output), writable: false));
            var report = new WindowsContextMenu.HostReport();
            await WindowsContextMenu.DrainAsync(reader, report);
            return report.Created;
        }
    }

    [Theory]
    [InlineData("New folder", true)]
    [InlineData("Nový textový dokument.txt", true)]
    [InlineData("New Text Document (2).txt", true)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("   ", false)]
    [InlineData("sub\\item", false)]
    [InlineData("sub/item", false)]
    [InlineData("C:item", false)]
    [InlineData("wild*card", false)]
    [InlineData("bell\u0007name", false)]
    public void Only_one_plain_name_is_taken_from_a_helpers_report(string name, bool accepted) =>
        Assert.Equal(accepted ? name : null, WindowsContextMenu.CreatedName(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name))));

    [Fact]
    public void A_report_that_is_not_base64_of_strict_UTF8_or_is_too_long_names_nothing()
    {
        Assert.Null(WindowsContextMenu.CreatedName(null));
        Assert.Null(WindowsContextMenu.CreatedName(""));
        Assert.Null(WindowsContextMenu.CreatedName("not base64!"));
        Assert.Null(WindowsContextMenu.CreatedName(Convert.ToBase64String([0xC3, 0x28])));
        Assert.Null(WindowsContextMenu.CreatedName(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(new string('n', 256)))));
    }

    [Theory]
    [InlineData("NewFolder", true)]
    [InlineData("newfolder", true)]
    [InlineData(".txt", true)]
    [InlineData(".zip", true)]
    [InlineData("NewLink", false)]
    [InlineData("paste", false)]
    [InlineData("properties", false)]
    [InlineData(".", false)]
    [InlineData("..\\evil", false)]
    [InlineData(null, false)]
    public void Only_New_commands_that_make_one_named_item_are_followed_by_renaming(string? verb, bool newItem) =>
        Assert.Equal(newItem, WindowsContextMenu.IsNewItemVerb(verb));

    [Fact]
    public void Only_New_entries_that_make_their_item_by_themselves_are_offered_for_renaming()
    {
        Assert.True(WindowsContextMenu.NewMakesItemItself("NewFolder"));
        // New → Shortcut runs a wizard that names its own result; shortcut-like types are never offered.
        Assert.False(WindowsContextMenu.NewMakesItemItself(".lnk"));
        Assert.False(WindowsContextMenu.NewMakesItemItself(".url"));
        Assert.False(WindowsContextMenu.NewMakesItemItself("NewLink"));
        Assert.False(WindowsContextMenu.NewMakesItemItself("..\\evil"));
        if (!OperatingSystem.IsWindows()) return;
        // A text file is made by New from an empty file or a template: its classic ShellNew key (NullFile), or on Windows 11
        // Notepad's packaged entry, which has no key here. Only a program or handler run by New would make it otherwise.
        bool runsSomething = false;
        using (var txt = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(".txt"))
            foreach (string sub in txt?.GetSubKeyNames().Prepend("") ?? [])
            {
                using var shellNew = txt!.OpenSubKey(sub.Length == 0 ? "ShellNew" : sub + "\\ShellNew");
                runsSomething |= shellNew is not null && (shellNew.GetValue("Command") is not null || shellNew.GetValue("Handler") is not null);
            }
        Assert.Equal(!runsSomething, WindowsContextMenu.NewMakesItemItself(".txt"));
    }

    [Fact]
    public void A_folders_own_menu_needs_one_full_path()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows Shell menus are Windows-only.");
        Assert.True(WindowsContextMenu.CanShowBackground(Path.GetTempPath()));
        Assert.False(WindowsContextMenu.CanShowBackground("relative\\folder"));
        Assert.False(WindowsContextMenu.CanShowBackground(null));
        Assert.False(WindowsContextMenu.CanShowBackground(Path.Combine(Path.GetTempPath(), "bad\0name")));
    }

    /// <summary>
    /// The real Shell, in the real helper: a folder's own menu lists New (with New → Folder) and Properties, and New →
    /// Folder makes one folder that the helper reports by name. Only an ordinary token may start the helper.
    /// </summary>
    [Fact]
    public async Task The_folders_own_Windows_menu_offers_New_and_reports_the_folder_New_makes()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows Shell menus are Windows-only.");
        if (!WindowsContextMenu.CanStartHost()) Assert.Skip("The Shell helper starts only from an ordinary (not elevated) token.");
        if (WindowsContextMenu.HostStartInfo() is null) Assert.Skip("The FileCat apphost is not beside the tests.");
        // The helper names a New item only in folders on this computer's fixed drives.
        if (new DriveInfo(Path.GetPathRoot(Path.GetTempPath())!).DriveType is not (DriveType.Fixed or DriveType.Ram))
            Assert.Skip("The temporary folder is not on a fixed drive.");
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-background-menu", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var (probeExit, probe, _) = await RunHostAsync(WindowsContextMenu.VerbProbeOption, WindowsContextMenu.BackgroundOption, folder);
            var lines = probe.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();
            TestContext.Current.TestOutputHelper?.WriteLine(probe);
            Assert.Equal(0, probeExit);
            Assert.Contains(lines, line => !line.StartsWith('\t') && line.EndsWith("\tNew", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith('\t') && line.EndsWith("\tNewFolder", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.EndsWith("\tproperties", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(WindowsContextMenu.ReadyMessage, lines[^1]);
            Assert.Empty(Directory.EnumerateFileSystemEntries(folder));

            var (newExit, output, _) = await RunHostAsync(WindowsContextMenu.NewProbeOption, "NewFolder", folder);
            string? report = output.Split('\n').Select(line => line.TrimEnd('\r'))
                .LastOrDefault(line => line.StartsWith(WindowsContextMenu.CreatedMessage + " ", StringComparison.Ordinal));
            Assert.Equal(19, newExit);
            string? name = WindowsContextMenu.CreatedName(report?[(WindowsContextMenu.CreatedMessage.Length + 1)..]);
            Assert.NotNull(name);
            Assert.Equal(Path.Combine(folder, name), Assert.Single(Directory.EnumerateFileSystemEntries(folder)));
            Assert.True(Directory.Exists(Path.Combine(folder, name)));

            // New → Text Document, where Windows offers it (a classic ShellNew entry or, on Windows 11, Notepad's packaged
            // one) and makes the file by itself (no program or handler of an editor runs instead): the text file is
            // reported too, beside the folder already there.
            if (lines.Any(line => line.StartsWith('\t') && line.EndsWith("\t.txt", StringComparison.OrdinalIgnoreCase)) &&
                WindowsContextMenu.NewMakesItemItself(".txt"))
            {
                var (textExit, textOutput, _) = await RunHostAsync(WindowsContextMenu.NewProbeOption, ".txt", folder);
                string? textReport = textOutput.Split('\n').Select(line => line.TrimEnd('\r'))
                    .LastOrDefault(line => line.StartsWith(WindowsContextMenu.CreatedMessage + " ", StringComparison.Ordinal));
                Assert.Equal(19, textExit);
                string? text = WindowsContextMenu.CreatedName(textReport?[(WindowsContextMenu.CreatedMessage.Length + 1)..]);
                Assert.NotNull(text);
                Assert.EndsWith(".txt", text, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(Path.Combine(folder, text)));
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>
    /// A folder deleted (or renamed, or unplugged) before its menu is built: the helper answers with exit code 1 and the
    /// failure's type, as it does for every failure, instead of crashing with an error dialog; FileCat then shows its own
    /// menu.
    /// </summary>
    [Fact]
    public async Task A_folder_gone_before_its_menu_is_built_is_answered_not_crashed_on()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows Shell menus are Windows-only.");
        if (!WindowsContextMenu.CanStartHost()) Assert.Skip("The Shell helper starts only from an ordinary (not elevated) token.");
        if (WindowsContextMenu.HostStartInfo() is null) Assert.Skip("The FileCat apphost is not beside the tests.");
        string gone = Path.Combine(Path.GetTempPath(), "filecat-background-menu", Guid.NewGuid().ToString("N"));
        foreach (string mode in new[] { WindowsContextMenu.ProbeOption, WindowsContextMenu.VerbProbeOption })
        {
            var (exit, output, errors) = await RunHostAsync(mode, WindowsContextMenu.BackgroundOption, gone);
            Assert.Equal(1, exit);
            Assert.DoesNotContain(WindowsContextMenu.ReadyMessage, output, StringComparison.Ordinal);
            Assert.Matches(@"^\w+ 0x[0-9A-F]{8}", errors.Trim());
        }
        Assert.False(Directory.Exists(gone));
    }

    private static async Task<(int ExitCode, string Output, string Errors)> RunHostAsync(params string[] arguments)
    {
        var start = WindowsContextMenu.HostStartInfo()!;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.ArgumentList.Add(WindowsContextMenu.HostArgument);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        Task<string> output = child.StandardOutput.ReadToEndAsync(), errors = child.StandardError.ReadToEndAsync();
        try
        {
            // Its own thread, generously bounded: a hung Shell extension must fail the test, not hold the run.
            await Task.Factory.StartNew(() => child.WaitForExit(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                .WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
        }
        // A program a Shell extension started could hold the pipes open after the helper ended: bounded too.
        string text = await output.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        string problems = await errors.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine(problems);
        return (child.ExitCode, text, problems);
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
