namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// The cloud providers' sync roots registered for this user, read from the registry alone (on the owner's computer:
/// OneDrive, Dropbox and iCloud Drive; on a build machine usually none). Each must be a real folder, so that the marks
/// FileCat draws in it are about something that exists.
/// </summary>
public sealed class CloudSyncRootTests
{
    [Fact]
    public void Sync_roots_are_read_as_folders_with_their_providers_names()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Cloud Files sync roots are Windows'."); return; }
        var shell = new WindowsShellServices();
        var roots = shell.CloudSyncRoots;
        foreach (var root in roots)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"{root.Provider}: {root.Path}");
            Assert.False(string.IsNullOrWhiteSpace(root.Provider));
            Assert.True(Path.IsPathFullyQualified(root.Path), root.Path);
            Assert.False(root.Path.EndsWith('\\'), root.Path);
        }
        // Read once and kept: asked while drawing every row.
        Assert.Same(roots, shell.CloudSyncRoots);
    }
}
