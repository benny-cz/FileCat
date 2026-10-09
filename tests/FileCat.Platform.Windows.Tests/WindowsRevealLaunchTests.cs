using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Tools;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsRevealLaunchTests(ITestOutputHelper output)
{
    [Fact]
    public void A_current_folder_explorer_cannot_replace_the_Windows_fallback()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "The Explorer fallback is a Windows integration boundary.");
        string root = Directory.CreateTempSubdirectory("filecat-owned-reveal-selection-").FullName;
        string lure = Path.Combine(root, "explorer.exe");
        byte[] marker = "owned inert current-directory marker"u8.ToArray();
        File.WriteAllBytes(lure, marker);
        object? actual = null;
        try
        {
            var start = WindowsShellServices.RevealStartInfo(Path.Combine(root, "missing.txt"));
            start.WorkingDirectory = root;
            string systemExplorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            actual = new { Group = "windows-reveal-launch", Case = "absolute-selection", ActualExecutable = start.FileName,
                ActualFullyQualified = Path.IsPathFullyQualified(start.FileName), ActualWindowsExplorer = systemExplorer,
                ActualSystemExplorerExists = File.Exists(systemExplorer), ActualOwnedCurrentFolder = root,
                ActualInertLure = lure, ActualInertLureSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(lure))),
                ActualUseShellExecute = start.UseShellExecute, ActualCoreSHA256 = Hash(typeof(ShellQuoting).Assembly.Location),
                ActualPlatformSHA256 = Hash(typeof(WindowsShellServices).Assembly.Location), NoProcessOrRealExplorerStarted = true };
            Assert.True(Path.IsPathFullyQualified(start.FileName));
            Assert.Equal(systemExplorer, start.FileName, ignoreCase: true);
            Assert.True(File.Exists(start.FileName));
            Assert.NotEqual(lure, start.FileName);
            Assert.False(start.UseShellExecute);
            Assert.Equal(marker, File.ReadAllBytes(lure));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { Observation = actual, ActualOwnedFixtureRemoved = !Directory.Exists(root) }));
        }
    }

    [Theory]
    [InlineData("local")]
    [InlineData("spaces-unicode")]
    [InlineData("trailing-folder")]
    [InlineData("double-trailing-folder")]
    [InlineData("drive-root")]
    [InlineData("unc-root")]
    [InlineData("relative")]
    public void The_fallback_preserves_one_select_argument_including_root_separators(string mode)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "The Explorer fallback is a Windows integration boundary.");
        string root = Directory.CreateTempSubdirectory("filecat-owned-reveal-argument-").FullName;
        string path = mode switch
        {
            "local" => Path.Combine(root, "owned.txt"),
            "spaces-unicode" => Path.Combine(root, "owned folder", "název 文件.txt"),
            "trailing-folder" => Path.Combine(root, "owned-folder") + "\\",
            "double-trailing-folder" => Path.Combine(root, "owned-folder") + "\\\\",
            "drive-root" => Path.GetPathRoot(Environment.SystemDirectory)!,
            "unc-root" => "\\\\owned.invalid\\share\\",
            "relative" => "owned-relative.txt",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        object? actual = null;
        try
        {
            var start = WindowsShellServices.RevealStartInfo(path);
            string trimmed = Path.TrimEndingDirectorySeparator(path), expected = "/select," + trimmed;
            string[] decoded = start.ArgumentList.Count > 0 ? start.ArgumentList.ToArray()
                : Decode("owned.exe " + start.Arguments).Skip(1).ToArray();
            actual = new { Group = "windows-reveal-launch", Case = mode, ActualInputPath = path, ActualTrimmedPath = trimmed,
                ActualExpectedSingleArgument = expected, ActualStartInfoArgumentVector = decoded, ActualLegacyArgumentsParsedWithWindowsApi = start.ArgumentList.Count == 0, ActualTypedArguments = start.ArgumentList.ToArray(),
                ActualLegacyArguments = start.Arguments, ActualExecutable = start.FileName, ActualUseShellExecute = start.UseShellExecute,
                ActualOwnedFixtureRoot = root, ActualCoreSHA256 = Hash(typeof(ShellQuoting).Assembly.Location),
                ActualPlatformSHA256 = Hash(typeof(WindowsShellServices).Assembly.Location), NoProcessRootNetworkOrRealExplorerAccess = true };
            Assert.Equal([expected], decoded);
            Assert.False(start.UseShellExecute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { Observation = actual, ActualOwnedFixtureRemoved = !Directory.Exists(root) }));
        }
    }

    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string[] Decode(string commandLine)
    {
        nint values = CommandLineToArgvW(commandLine, out int count);
        if (values == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(values, i * nint.Size))!).ToArray(); }
        finally { LocalFree(values); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
