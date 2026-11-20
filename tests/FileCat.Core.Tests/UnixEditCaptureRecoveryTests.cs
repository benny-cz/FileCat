using FileCat.Core.Edit;
using FileCat.Tests;
namespace FileCat.Core.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class UnixEditCaptureRecoveryTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditCaptureRecoveryCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Fresh_recovery_reviews_bounded_capture_evidence_without_modifying_files(string scenario)
    {if(OperatingSystem.IsWindows())Assert.Skip("Run native Windows capture recovery.");ArchiveEditCaptureRecoveryCases.Run(scenario,UnixEditDirectoryLease.OpenRoot);}
    public static IEnumerable<object[]> RemovalScenarios()=>ArchiveEditCaptureRecoveryCases.RemovalScenarios();
    [Theory,MemberData(nameof(RemovalScenarios))]
    public void One_reviewed_retained_copy_is_removed_and_everything_else_stays(string scenario)
    {if(OperatingSystem.IsWindows())Assert.Skip("Run native Windows capture recovery.");ArchiveEditCaptureRecoveryCases.RunRemoval(scenario,UnixEditDirectoryLease.OpenRoot);}
}
