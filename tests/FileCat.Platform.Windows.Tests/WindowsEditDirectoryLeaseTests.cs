using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsEditDirectoryLeaseTests
{
    [Theory,InlineData("roundtrip"),InlineData("public-parent"),InlineData("bad-leaves"),InlineData("wrong-directory-id"),
        InlineData("parent-disposed"),InlineData("canceled"),InlineData("parent-redirect"),InlineData("collision"),InlineData("wrong-file-id"),InlineData("inherited-acl"),
        InlineData("stream-outlives"),InlineData("file-substitution"),InlineData("publish"),InlineData("publish-collision"),
        InlineData("publish-wrong-source"),InlineData("publish-wrong-bytes")]
    public void Native_edit_namespace_creates_private_children_and_retains_exact_read_authority(string scenario)
    {
        if(!OperatingSystem.IsWindows())Assert.Skip("Windows native directory handles required.");
        ArchiveEditDirectoryCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot,handle=>ProtectedHexFile.Identity(handle).ToString());
    }
}
