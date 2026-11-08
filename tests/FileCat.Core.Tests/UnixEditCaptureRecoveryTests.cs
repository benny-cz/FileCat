using FileCat.Core.Edit;
using FileCat.Tests;
namespace FileCat.Core.Tests;
public sealed class UnixEditCaptureRecoveryTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditCaptureRecoveryCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Fresh_recovery_reviews_bounded_capture_evidence_without_modifying_files(string scenario)
    {if(OperatingSystem.IsWindows())Assert.Skip("Run native Windows capture recovery.");ArchiveEditCaptureRecoveryCases.Run(scenario,UnixEditDirectoryLease.OpenRoot);}
}
