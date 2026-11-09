using FileCat.Platform.Windows.Archives;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsLegacyEditAdoptionTests
{
    [Theory,InlineData("copy"),InlineData("edited"),InlineData("origin"),InlineData("pending-failure"),
        InlineData("tampered-link"),InlineData("discard")]
    public void Native_copy_preserves_old_session_and_durable_adoption_state(string scenario)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(),"Windows native edit admission required.");
        ArchiveEditLegacyAdoptionCases.Run(scenario,new WindowsFileOperations(),WindowsEditDirectoryLease.OpenRoot,
            (root,record,ct)=>WindowsLegacyEditSource.Open(root,record,ct));
    }
}
