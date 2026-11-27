using System.Security.Principal;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using FileCat.PrivilegedHost;
using BrokerProgram = FileCat.PrivilegedHost.Program;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// I689: on Windows a FileCat without administrator rights cannot query other accounts' processes, so it leaves a drive
/// read's census of other FileCat processes to the administrator helper. The helper takes it before consent and refuses
/// the read unless it is clear; other plans never take it.
/// </summary>
public sealed class BrokerRecoveryCensusTests
{
    [Theory]
    [InlineData("clear", true)]
    [InlineData("running", true)]
    [InlineData("unknown", true)]
    [InlineData("clear", false)]
    [InlineData("running", false)]
    public void A_drive_read_waits_for_a_clear_census_taken_before_consent(string census, bool reading)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The broker and its secure exchange require Windows.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-broker-census-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        DateTime now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        int asks = 0, reads = 0, censuses = 0;
        var refusals = new List<string>();
        using var identity = WindowsIdentity.GetCurrent();
        var plan = new ElevationPlan
        {
            Nonce = ElevationPlanCodec.NewNonce(), CreatedUtc = now,
            UserSid = identity.User!.Value, UserName = identity.Name, RequesterProcessId = Environment.ProcessId,
            Title = "Owned broker census control",
            Steps = [reading
                ? new ElevatedStep(ElevatedVerb.ReadDevice) { Path = @"\\.\PhysicalDrive999", Name = "FileCat-read-" + ElevationPlanCodec.NewNonce() }
                : new ElevatedStep(ElevatedVerb.CreateDirectory) { Destination = ElevationPaths.ToVolumePath(root), Name = "made" }],
        };
        try
        {
            using var request = ElevationExchange.Create(Path.Combine(root, "exchange"), plan);
            using var exchange = BrokerExchange.Open(request.VolumePlanPath);
            var services = new BrokerProgram.RunServices(
                () => now, (_, _) => null, (_, _) => true,
                (_, _, _, _) => { asks++; return true; },
                refusals.Add,
                (_, _, _, _) => { reads++; return "Owned read callback; no device opened."; },
                ProcessCensus: (requester, directory) =>
                {
                    censuses++;
                    Assert.Equal(plan.RequesterProcessId, requester);
                    Assert.Equal("owned-broker-directory", directory);
                    // The census is taken before the person is asked anything.
                    Assert.Equal(0, asks);
                    return census switch { "clear" => null, "running" => RecoveryCensus.Running, _ => RecoveryCensus.Unknown };
                });
            int code = BrokerProgram.Run(exchange, request.Hash, "owned-broker-directory", services);
            var result = request.ReadResult();
            Assert.NotNull(result);
            Assert.Equal(reading ? 1 : 0, censuses);
            if (!reading || census == "clear")
            {
                Assert.Equal(0, code); Assert.Equal(1, asks); Assert.True(result.Consented); Assert.Null(result.Refused);
                Assert.Equal(reading ? 1 : 0, reads);
                return;
            }
            string expected = census == "running" ? RecoveryCensus.Running : RecoveryCensus.Unknown;
            Assert.Equal(2, code); Assert.Equal(0, asks); Assert.Equal(0, reads);
            Assert.False(result.Consented); Assert.Equal(expected, result.Refused); Assert.Equal([expected], refusals);
        }
        finally
        {
            string temporaryParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(temporaryParent, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Only_a_positive_clear_reply_for_this_nonce_lets_the_read_go_on()
    {
        string nonce = new('A', 32), other = new('B', 32);
        Assert.Null(RecoveryCensus.Verdict($"FileCatRecoveryProcessCensusV1:{nonce}:clear\r\n", nonce, 0));
        Assert.Equal(RecoveryCensus.Running, RecoveryCensus.Verdict($"FileCatRecoveryProcessCensusV1:{nonce}:running\n", nonce, 0));
        Assert.Equal(RecoveryCensus.Unknown, RecoveryCensus.Verdict($"FileCatRecoveryProcessCensusV1:{nonce}:clear", nonce, 3));
        Assert.Equal(RecoveryCensus.Unknown, RecoveryCensus.Verdict($"FileCatRecoveryProcessCensusV1:{other}:clear", nonce, 0));
        Assert.Equal(RecoveryCensus.Unknown, RecoveryCensus.Verdict($"FileCatRecoveryProcessCensusV1:{nonce}:clear\nextra", nonce, 0));
        Assert.Equal(RecoveryCensus.Unknown, RecoveryCensus.Verdict("", nonce, 0));
    }

    [Fact]
    public void A_missing_installed_FileCat_is_an_unknown_census_not_a_clear_one()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator helper runs on Windows.");
        string empty = Directory.CreateTempSubdirectory("filecat-census-no-app-").FullName;
        try { Assert.Equal(RecoveryCensus.Unknown, RecoveryCensus.Refusal(Environment.ProcessId, empty)); }
        finally { Directory.Delete(empty, recursive: true); }
    }
}
