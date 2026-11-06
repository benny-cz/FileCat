using FileCat.Core.Analysis;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    private static void FileQuotaOrdinary() => Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs an ordinary Windows process.");
    [Fact]
    public void NativePrivateFilesShareCapacityAndRetainItAfterOwnerClose()
    { FileQuotaOrdinary(); AnalysisFileQuotaCases.SharedAndRetained((parent, name) => new WindowsAnalysisPrivateDirectory(parent, name)); }
    [Fact]
    public void NativePrivateFilesPreserveForeignTokensAndRefuseAChangedProfile()
    { FileQuotaOrdinary(); AnalysisFileQuotaCases.ForeignAndMismatched((parent, name) => new WindowsAnalysisPrivateDirectory(parent, name)); }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePrivateFilesCoordinateOwnedChildrenThroughActualExit(bool crash)
    { FileQuotaOrdinary(); await AnalysisFileQuotaCases.CrossProcess((parent, name) => new WindowsAnalysisPrivateDirectory(parent, name), crash); }
    [Fact]
    public async Task QuarantinedNativeScratchKeepsCrossProcessCapacityUntilActualProcessExit()
    { FileQuotaOrdinary(); await AnalysisFileQuotaCases.QuarantinedScratch((parent, name) => new WindowsAnalysisPrivateDirectory(parent, name)); }
}
