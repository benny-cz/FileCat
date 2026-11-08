using FileCat.Platform.Windows.Archives;
using FileCat.Tests;
namespace FileCat.Platform.Windows.Tests;
[Collection("Native edit snapshot reservations")]
public sealed class WindowsEditCaptureRecoveryTests
{
    public static IEnumerable<object[]> Scenarios()=>ArchiveEditCaptureRecoveryCases.Scenarios();
    [Theory,MemberData(nameof(Scenarios))]
    public void Fresh_recovery_reviews_bounded_capture_evidence_without_modifying_files(string scenario)
    {if(!OperatingSystem.IsWindows())Assert.Skip("Windows native capture recovery required.");ArchiveEditCaptureRecoveryCases.Run(scenario,WindowsEditDirectoryLease.OpenRoot);}
}
