using FileCat.Platform.Windows.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Fact]
    public Task NativeSourcesExcludePrivateRootsWhenSecureScratchIsUnavailable() =>
        AnalysisNativeMemoryFallbackCases.Run(path => new WindowsAnalysisBackend(path));
}
