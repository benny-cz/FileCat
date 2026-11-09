using FileCat.Platform.Windows.Archives;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsLegacyEditAdoptionTests
{
    [Theory,InlineData("marker"),InlineData("pending"),InlineData("ready")]
    public Task Process_kill_preserves_old_edit_and_classifies_native_copy(string point)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(),"Windows native edit admission required.");
        return ArchiveEditLegacyAdoptionCrashCases.RunAsync(point,new WindowsFileOperations(),WindowsEditDirectoryLease.OpenRoot,
            (root,record,ct)=>WindowsLegacyEditSource.Open(root,record,ct),typeof(WindowsFileOperations).Assembly.Location);
    }

    [Theory,InlineData("copy"),InlineData("edited"),InlineData("origin"),InlineData("pending-failure"),
        InlineData("tampered-link"),InlineData("discard"),InlineData("source-changed"),InlineData("native-changed"),InlineData("native-origin-changed"),InlineData("duplicate"),InlineData("future-old")]
    public void Native_copy_preserves_old_session_and_durable_adoption_state(string scenario)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(),"Windows native edit admission required.");
        ArchiveEditLegacyAdoptionCases.Run(scenario,new WindowsFileOperations(),WindowsEditDirectoryLease.OpenRoot,
            (root,record,ct)=>WindowsLegacyEditSource.Open(root,record,ct));
    }
}
