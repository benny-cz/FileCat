using FileCat.Core.State;
using FileCat.Tests;

namespace FileCat.Core.Tests;

/// <summary>
/// I645's neighbour at start: on Linux and macOS FileCat makes its folders its user's alone (0700). chmod follows a link,
/// so root, whose HOME may be the user's, changes no folder that is a link; an ordinary FileCat tightens its folders
/// wherever they lead, as before.
/// </summary>
public sealed class AppPathsOwnFolderTests : IDisposable
{
    private const UnixFileMode Shared = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private readonly TemporaryLinkFixture _links = new();

    public void Dispose() => _links.Dispose();

    private string SharedFolder(string name)
    {
        string folder = _links.Dir(name);
        File.SetUnixFileMode(folder, Shared);
        return folder;
    }

    [Fact]
    public void Root_changes_the_mode_of_no_folder_that_is_a_link()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Folder modes are Linux's and macOS's.");
        string target = SharedFolder("target"), scratchTarget = SharedFolder("scratch target"), plain = SharedFolder("plain");
        string link = _links.FolderLink(Path.Combine(_links.Root, "linked state"), target);
        string scratchLink = _links.FolderLink(Path.Combine(_links.Root, "linked scratch"), scratchTarget);

        AppPaths.TightenOwnFolders([link, plain], scratchLink, privileged: true);

        Assert.Equal(Shared, File.GetUnixFileMode(target));
        Assert.Equal(Shared, File.GetUnixFileMode(scratchTarget));
        Assert.Equal(Private, File.GetUnixFileMode(plain)); // a folder that is no link is still made private

        AppPaths.TightenOwnFolders([link], scratchLink, privileged: false);

        Assert.Equal(Private, File.GetUnixFileMode(target));
        Assert.Equal(Private, File.GetUnixFileMode(scratchTarget));
    }
}
