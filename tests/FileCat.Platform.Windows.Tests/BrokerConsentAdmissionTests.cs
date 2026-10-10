using System.Security.Principal;
using System.Text.Json;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using BrokerProgram = FileCat.PrivilegedHost.Program;

namespace FileCat.Platform.Windows.Tests;

public sealed class BrokerConsentAdmissionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("expires", false)]
    [InlineData("expires", true)]
    [InlineData("requester-exits", false)]
    [InlineData("requester-exits", true)]
    [InlineData("stop-during-consent", false)]
    [InlineData("stop-during-consent", true)]
    [InlineData("healthy", false)]
    [InlineData("healthy", true)]
    [InlineData("declined", false)]
    [InlineData("declined", true)]
    [InlineData("initially-expired", false)]
    [InlineData("initially-expired", true)]
    [InlineData("initially-missing-requester", false)]
    [InlineData("initially-missing-requester", true)]
    [InlineData("replay", false)]
    [InlineData("replay", true)]
    [InlineData("changed-digest", false)]
    [InlineData("changed-digest", true)]
    public void Admission_is_current_when_consent_returns(string scenario, bool reading)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The broker and its secure exchange require Windows.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-broker-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string marker = Path.Combine(root, "unchanged.txt");
        File.WriteAllBytes(marker, "owned-broker-control"u8.ToArray());
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        DateTime initial = now;
        int asks = 0, checks = 0, claims = 0, reads = 0;
        bool requesterAlive = scenario != "initially-missing-requester";
        var refusals = new List<string>();
        using var identity = WindowsIdentity.GetCurrent();
        var plan = new ElevationPlan
        {
            Nonce = ElevationPlanCodec.NewNonce(), CreatedUtc = now - (scenario == "initially-expired" ? TimeSpan.FromMinutes(16) : TimeSpan.Zero),
            UserSid = identity.User!.Value, UserName = identity.Name, RequesterProcessId = Environment.ProcessId,
            Title = "Owned broker admission control",
            Steps = [reading
                ? new ElevatedStep(ElevatedVerb.ReadDevice) { Path = @"\\.\PhysicalDrive999", Name = "FileCat-read-" + ElevationPlanCodec.NewNonce() }
                : new ElevatedStep(ElevatedVerb.CreateDirectory) { Destination = ElevationPaths.ToVolumePath(root), Name = "made" }],
        };
        bool expectedEffect = scenario == "healthy";
        int expectedCode = expectedEffect ? 0 : scenario is "declined" or "stop-during-consent" ? 1 : 2;
        try
        {
            using var request = ElevationExchange.Create(Path.Combine(root, "exchange"), plan);
            using var exchange = BrokerExchange.Open(request.VolumePlanPath);
            var services = new BrokerProgram.RunServices(
                () => now,
                (_, _) => { checks++; return requesterAlive ? null : "The owned requester has exited."; },
                (_, _) => { claims++; return scenario != "replay"; },
                (title, content, pages, footer) =>
                {
                    asks++;
                    Assert.Equal(plan.Title, title);
                    Assert.Single(pages);
                    Assert.Contains(reading ? "read" : "create", pages[0], StringComparison.OrdinalIgnoreCase);
                    Assert.NotEmpty(content);
                    Assert.NotEmpty(footer);
                    if (scenario == "expires") now = initial + TimeSpan.FromMinutes(16);
                    if (scenario == "requester-exits") requesterAlive = false;
                    if (scenario == "stop-during-consent") request.RequestStop();
                    return scenario != "declined";
                },
                refusals.Add,
                (path, name, sid, pid) =>
                {
                    reads++;
                    Assert.Equal(plan.Steps[0].Path, path);
                    Assert.Equal(plan.Steps[0].Name, name);
                    Assert.Equal(plan.UserSid, sid);
                    Assert.Equal(plan.RequesterProcessId, pid);
                    return "Owned read callback; no device opened.";
                });
            int code = BrokerProgram.Run(exchange, scenario == "changed-digest" ? new string('0', 64) : request.Hash, "owned-broker-directory", services);
            var result = request.ReadResult();
            bool made = Directory.Exists(Path.Combine(root, "made"));
            output.WriteLine("BROKER_CONSENT_ADMISSION " + JsonSerializer.Serialize(new
            {
                scenario, reading, code, expectedCode, asks, checks, claims, reads, made, expectedEffect,
                initial, now, requesterAlive, result, refusals, root,
                unchanged = File.ReadAllBytes(marker).AsSpan().SequenceEqual("owned-broker-control"u8),
                actualSecureExchangeAndWriteRunner = true, controlledClockRequesterNonceAndConsent = true,
                noNativeUIOrDeviceOrHKLMNonce = true,
            }));
            Assert.Equal(expectedCode, code);
            Assert.NotNull(result);
            Assert.Equal(plan.Nonce, result.Nonce);
            Assert.Equal(expectedEffect, result.Consented);
            Assert.Equal(expectedEffect, made || reads == 1);
            Assert.Equal(expectedEffect && reading ? 1 : 0, reads);
            Assert.Equal(expectedEffect && !reading, made);
            Assert.True(File.ReadAllBytes(marker).AsSpan().SequenceEqual("owned-broker-control"u8));
            if (expectedEffect)
            {
                Assert.True(result.Finished);
                Assert.Null(result.Refused);
                Assert.Single(result.Steps);
                Assert.Equal(ElevatedOutcome.Committed, result.Steps[0].Outcome);
                Assert.Equal(1, asks);
                Assert.Equal(1, claims);
                Assert.InRange(checks, 1, 2);
            }
            else
            {
                Assert.False(result.Finished);
                Assert.Empty(result.Steps);
                Assert.NotNull(result.Refused);
                if (expectedCode == 1) Assert.Equal(ElevationMessages.Declined, result.Refused);
            }
        }
        finally
        {
            string temporaryParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(temporaryParent, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
            output.WriteLine("BROKER_CONSENT_RESTORATION " + JsonSerializer.Serialize(new { root, absent = !Directory.Exists(root), noDeviceOpened = true }));
        }
    }
}
