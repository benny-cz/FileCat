using FileCat.Core.Analysis;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisProfileQuotaTests
{
    private static void FileOrdinary()
    {
        Ordinary(); Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows native private-file capacity cases run in AnalysisWindowsTests.");
    }
    [Fact]
    public void NativePrivateFilesShareCapacityAndRetainItAfterOwnerClose()
    { FileOrdinary(); AnalysisFileQuotaCases.SharedAndRetained((parent, name) => new UnixAnalysisPrivateDirectory(parent, name)); }
    [Fact]
    public void NativePrivateFilesPreserveForeignTokensAndRefuseAChangedProfile()
    { FileOrdinary(); AnalysisFileQuotaCases.ForeignAndMismatched((parent, name) => new UnixAnalysisPrivateDirectory(parent, name)); }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePrivateFilesCoordinateOwnedChildrenThroughActualExit(bool crash)
    { FileOrdinary(); await AnalysisFileQuotaCases.CrossProcess((parent, name) => new UnixAnalysisPrivateDirectory(parent, name), crash); }
}
