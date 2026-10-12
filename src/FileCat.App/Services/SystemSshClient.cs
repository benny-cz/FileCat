namespace FileCat.App.Services;

/// <summary>Implicit SSH terminal client; an absolute PATH entry may still be writable.</summary>
internal static class SystemSshClient
{
    internal static string? Find()
    {
        if (OperatingSystem.IsWindows())
        {
            string windows = Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh.exe");
            return File.Exists(windows) ? windows : null;
        }
        return new[] { "/usr/bin/ssh", "/bin/ssh" }.FirstOrDefault(File.Exists);
    }
}
