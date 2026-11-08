using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed class UnixEditDirectoryLeaseTests
{
    [Theory,InlineData("roundtrip"),InlineData("public-parent"),InlineData("bad-leaves"),InlineData("wrong-directory-id"),
        InlineData("parent-disposed"),InlineData("canceled"),InlineData("parent-redirect"),InlineData("collision"),InlineData("wrong-file-id"),InlineData("inherited-acl"),
        InlineData("stream-outlives"),InlineData("file-substitution"),InlineData("publish"),InlineData("publish-collision"),
        InlineData("publish-wrong-source"),InlineData("publish-wrong-bytes")]
    public void Native_edit_namespace_creates_private_children_and_retains_exact_read_authority(string scenario)
    {
        if(OperatingSystem.IsWindows())Assert.Skip("Run the native Windows namespace adapter lane.");
        ArchiveEditDirectoryCases.Run(scenario,UnixEditDirectoryLease.OpenRoot,handle=>UnixFiles.Stat(handle)!.Value.Identity.ToString());
    }
}
