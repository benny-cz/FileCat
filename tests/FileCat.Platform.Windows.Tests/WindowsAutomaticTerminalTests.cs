using System.Security.Cryptography;
using FileCat.Platform.Windows;

namespace FileCat.Platform.Windows.Tests;

[CollectionDefinition("Windows automatic terminal selection", DisableParallelization = true)]
public sealed class WindowsAutomaticTerminalSelectionCollection;

[Collection("Windows automatic terminal selection")]
public sealed class WindowsAutomaticTerminalTests
{
    [Fact]
    public void Automatic_terminal_ignores_absolute_writable_PATH_replacements()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows terminal selection.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-terminal-path-" + Guid.NewGuid().ToString("N"))).FullName;
        string wtBait = Path.Combine(root, "wt.exe");
        string pwshBait = Path.Combine(root, "pwsh.exe");
        string cmdBait = Path.Combine(root, "cmd.exe");
        string? oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            File.WriteAllText(wtBait, "owned lookup-only Windows Terminal replacement; never executed\n");
            File.WriteAllText(pwshBait, "owned lookup-only PowerShell replacement; never executed\n");
            File.WriteAllText(cmdBait, "owned lookup-only command processor replacement; never executed\n");
            byte[] wtHash = SHA256.HashData(File.ReadAllBytes(wtBait));
            byte[] pwshHash = SHA256.HashData(File.ReadAllBytes(pwshBait));
            byte[] cmdHash = SHA256.HashData(File.ReadAllBytes(cmdBait));
            Environment.SetEnvironmentVariable("PATH", root);

            Assert.Equal(wtBait, WindowsShellServices.FindOnPath("wt.exe", root));
            Assert.Equal(pwshBait, WindowsShellServices.FindOnPath("pwsh.exe", root));

            string? package = WindowsShellServices.AutomaticWindowsTerminal;
            string? systemPwsh = WindowsShellServices.SystemPowerShell7;
            Assert.NotEqual(wtBait, package);
            Assert.NotEqual(pwshBait, systemPwsh);
            if (package is not null) Assert.True(File.Exists(package), package);
            if (systemPwsh is not null) Assert.True(File.Exists(systemPwsh), systemPwsh);
            Assert.Equal(package is not null ? "wt" : systemPwsh is not null ? "pwsh" : "powershell",
                WindowsShellServices.ResolveShell("auto"));
            string commandArguments = WindowsShellServices.WindowsTerminalCommandArguments(root, "echo FileCat-owned-input");
            Assert.Contains('"' + Path.Combine(Environment.SystemDirectory, "cmd.exe") + '"', commandArguments);
            Assert.DoesNotContain(" cmd.exe /K", commandArguments);
            Assert.Equal(wtHash, SHA256.HashData(File.ReadAllBytes(wtBait)));
            Assert.Equal(pwshHash, SHA256.HashData(File.ReadAllBytes(pwshBait)));
            Assert.Equal(cmdHash, SHA256.HashData(File.ReadAllBytes(cmdBait)));
            TestContext.Current.TestOutputHelper!.WriteLine($"package={package ?? "<none>"}; systemPwsh={systemPwsh ?? "<none>"}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root));
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
