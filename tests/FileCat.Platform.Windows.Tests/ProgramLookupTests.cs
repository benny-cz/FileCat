namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Release plan I16: the programs FileCat starts on Windows (terminals, shells) are found by full path. Started by bare
/// name, or found through a relative PATH entry, they would be looked for in the current directory first.
/// </summary>
public sealed class ProgramLookupTests
{
    [Fact]
    public void Terminals_and_shells_are_found_by_full_path_never_through_a_relative_PATH_entry()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' program lookup.");
        string name = "fc-planted-" + Guid.NewGuid().ToString("N")[..8] + ".exe";
        string folder = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, Path.GetFileNameWithoutExtension(name))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, name), "");
            string relative = Path.GetRelativePath(Environment.CurrentDirectory, folder);
            Assert.False(Path.IsPathFullyQualified(relative));
            Assert.Null(WindowsShellServices.FindOnPath(name, relative));
            Assert.Equal(Path.Combine(folder, name), WindowsShellServices.FindOnPath(name, relative + ";\"" + folder + "\""));
            Assert.True(Path.IsPathFullyQualified(WindowsShellServices.WindowsPowerShell));
            Assert.True(File.Exists(WindowsShellServices.WindowsPowerShell), WindowsShellServices.WindowsPowerShell);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
