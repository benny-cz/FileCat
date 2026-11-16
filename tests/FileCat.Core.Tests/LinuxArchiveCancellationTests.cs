using FileCat.Core.Archives.Linux;

namespace FileCat.Core.Tests;

public sealed partial class LinuxArchiveProcessTests
{
    [Theory]
    [InlineData("archive-assess")]
    [InlineData("archive-verify")]
    [InlineData("archive-transform")]
    [InlineData("archive-cancel-ignore")]
    [InlineData("archive-cancel-output")]
    public async Task Native_cancellation_retains_the_boundary_and_reaps_before_releasing_the_queue(string role)
    {
        string probe = Probe(); LinuxArchiveProcess? actual = null;
        await ArchiveWorkerCancellationAssertions.Run((nonce, cpu) => actual = LinuxArchiveProcess.Start(probe, nonce, cpu, [role, nonce.ToString("D")]), role);
        Assert.NotNull(actual); Assert.False(Directory.Exists(actual.ControlPath));
        diagnostics.WriteLine("Removed owned cancellation controller: " + actual.ControlPath);
    }
}
