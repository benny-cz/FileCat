using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed class UnixLegacyEditAdoptionTests
{
    [Theory,InlineData("copy"),InlineData("edited"),InlineData("origin"),InlineData("pending-failure"),
        InlineData("tampered-link"),InlineData("discard")]
    public void Native_copy_preserves_old_session_and_durable_adoption_state(string scenario)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(),"Unix native edit admission required.");
        ArchiveEditLegacyAdoptionCases.Run(scenario,new UnixFileOperations(),UnixEditDirectoryLease.OpenRoot,
            (root,record,ct)=>UnixLegacyEditSource.Open(root,record,ct));
    }
}
