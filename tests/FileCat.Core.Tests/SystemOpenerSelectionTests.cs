using FileCat.Core.Platform;

namespace FileCat.Core.Tests;

[CollectionDefinition("System opener selection", DisableParallelization = true)]
public sealed class SystemOpenerSelectionCollection;

[Collection("System opener selection")]
public sealed class SystemOpenerSelectionTests
{
    [Fact]
    public void Implicit_desktop_helpers_ignore_an_absolute_writable_PATH_entry()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Portable Unix desktop helpers are not used on Windows.");
        string[] names = OperatingSystem.IsMacOS() ? ["open", "osascript"] : ["xdg-open", "x-terminal-emulator"];
        string root = Path.Combine(Path.GetTempPath(), "filecat-system-opener-tests", Guid.NewGuid().ToString("N"));
        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        Directory.CreateDirectory(root);
        try
        {
            foreach (string name in names) File.WriteAllText(Path.Combine(root, name), "owned lookup-only bait; never executed\n");
            Environment.SetEnvironmentVariable("PATH", root + Path.PathSeparator + originalPath);
            int checkedTools = 0;
            foreach (string name in names)
            {
                string? expected = new[] { "/usr/bin", "/bin", "/usr/sbin", "/sbin" }
                    .Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
                if (expected is null) continue;
                string selected = PortableShellServices.ResolveSystemTool(name);
                Assert.Equal(expected, selected);
                Assert.NotEqual(Path.Combine(root, name), selected);
                checkedTools++;
            }
            Assert.True(checkedTools > 0, "A native system opener is required for this control.");
            Assert.Throws<System.ComponentModel.Win32Exception>(() => PortableShellServices.ResolveSystemTool("../xdg-open"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root));
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
