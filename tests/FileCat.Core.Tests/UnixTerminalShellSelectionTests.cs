using FileCat.Core.Platform;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

[Collection("System opener selection")]
public sealed class UnixTerminalShellSelectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("/bin/true")]
    [InlineData("sh")]
    public void Terminal_command_does_not_resolve_its_nested_shell_through_writable_PATH(string? selectedShell)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("This control exercises the Linux terminal command route.");

        string root = Directory.CreateTempSubdirectory("filecat-terminal-shell-").FullName;
        string terminal = Path.Combine(root, "terminal");
        string decoy = Path.Combine(root, "sh");
        string decoyMarker = Path.Combine(root, "decoy-ran");
        string controlMarker = Path.Combine(root, "control-ran");
        string doneMarker = Path.Combine(root, "terminal-done");
        string? oldPath = Environment.GetEnvironmentVariable("PATH");
        string? oldTerminal = Environment.GetEnvironmentVariable("TERMINAL");
        string? oldShell = Environment.GetEnvironmentVariable("SHELL");
        string? oldDecoyMarker = Environment.GetEnvironmentVariable("FILECAT_TERMINAL_DECOY");
        string? oldDoneMarker = Environment.GetEnvironmentVariable("FILECAT_TERMINAL_DONE");
        try
        {
            File.WriteAllText(terminal,
                "#!/bin/sh\n[ \"$1\" = '-e' ] || exit 21\nshift\n\"$@\" </dev/null\nstatus=$?\nprintf '%s' \"$status\" > \"$FILECAT_TERMINAL_DONE\"\n");
            File.WriteAllText(decoy,
                "#!/bin/sh\nprintf 'decoy' > \"$FILECAT_TERMINAL_DECOY\"\nexec /bin/sh \"$@\"\n");
            File.SetUnixFileMode(terminal, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(decoy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", root + Path.PathSeparator + oldPath);
            Environment.SetEnvironmentVariable("TERMINAL", terminal);
            Environment.SetEnvironmentVariable("SHELL", selectedShell);
            Environment.SetEnvironmentVariable("FILECAT_TERMINAL_DECOY", decoyMarker);
            Environment.SetEnvironmentVariable("FILECAT_TERMINAL_DONE", doneMarker);

            new PortableShellServices().RunInTerminal(root,
                "printf 'control' > " + ShellQuoting.QuotePosix(controlMarker), "sh");

            Assert.True(SpinWait.SpinUntil(() => File.Exists(doneMarker), TimeSpan.FromSeconds(10)),
                "The owned terminal did not finish its nested command.");
            Assert.Equal("0", File.ReadAllText(doneMarker));
            Assert.Equal("control", File.ReadAllText(controlMarker));
            Assert.False(File.Exists(decoyMarker), "A writable PATH entry supplied FileCat's nested shell.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Environment.SetEnvironmentVariable("TERMINAL", oldTerminal);
            Environment.SetEnvironmentVariable("SHELL", oldShell);
            Environment.SetEnvironmentVariable("FILECAT_TERMINAL_DECOY", oldDecoyMarker);
            Environment.SetEnvironmentVariable("FILECAT_TERMINAL_DONE", oldDoneMarker);
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
