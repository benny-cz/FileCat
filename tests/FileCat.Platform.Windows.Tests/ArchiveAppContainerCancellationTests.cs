using FileCat.Core.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory]
    [InlineData("archive-assess", false)]
    [InlineData("archive-verify", false)]
    [InlineData("archive-transform", false)]
    [InlineData("archive-assess", true)]
    [InlineData("archive-verify", true)]
    [InlineData("archive-transform", true)]
    [InlineData("archive-cancel-ignore", false)]
    [InlineData("archive-cancel-output", false)]
    public Task Actual_LPAC_cancellation_exits_cooperatively_or_uses_the_two_second_deadline(string role, bool application)
    {
        string executable = Probe();
        if (application)
        {
            string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
            Assert.True(assets is not null && Path.IsPathFullyQualified(assets), "Supply a dedicated self-contained application bundle.");
            ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); executable = Path.Combine(assets!, "FileCat.exe");
        }
        return ArchiveWorkerCancellationAssertions.Run((nonce, cpu) => application ? role switch
        {
            "archive-assess" => ArchiveAppContainer.StartAssessment(executable, nonce, cpu),
            "archive-verify" => ArchiveAppContainer.StartVerification(executable, nonce, cpu),
            _ => ArchiveAppContainer.StartTransform(executable, nonce, cpu),
        } : ArchiveAppContainer.Start(executable, $"{role} {nonce:D}", cpu), role);
    }
}
