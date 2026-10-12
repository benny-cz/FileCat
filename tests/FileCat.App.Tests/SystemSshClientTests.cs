using System.Security.Cryptography;
using System.Text.Json;
using FileCat.App.Services;

namespace FileCat.App.Tests;

[CollectionDefinition("System SSH client selection", DisableParallelization = true)]
public sealed class SystemSshClientSelectionCollection;

[Collection("System SSH client selection")]
public sealed class SystemSshClientTests
{
    [Fact]
    public void Implicit_ssh_terminal_ignores_an_absolute_writable_PATH_entry()
    {
        string parent = Path.Combine(Path.GetTempPath(), "filecat-system-ssh-tests");
        string root = Directory.CreateDirectory(Path.Combine(parent, Guid.NewGuid().ToString("N"))).FullName;
        string bait = Path.Combine(root, OperatingSystem.IsWindows() ? "ssh.exe" : "ssh");
        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            File.WriteAllText(bait, "owned lookup-only ssh bait; never executed\n");
            byte[] hash = SHA256.HashData(File.ReadAllBytes(bait));
            Environment.SetEnvironmentVariable("PATH", root);
            string? selected = SystemSshClient.Find();
            string? expected = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh.exe")
                : new[] { "/usr/bin/ssh", "/bin/ssh" }.FirstOrDefault(File.Exists);
            if (expected is not null && !File.Exists(expected)) expected = null;
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { selected, expected, bait, root }));
            Assert.Equal(expected, selected);
            Assert.NotEqual(bait, selected);
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(bait)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Assert.StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, Path.GetFullPath(root));
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
