using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using BrokerProgram = FileCat.PrivilegedHost.Program;

namespace FileCat.Platform.Windows.Tests;

public sealed class BrokerRequesterLifetimeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("alive")]
    [InlineData("exited")]
    [InlineData("wrong-directory")]
    [InlineData("wrong-user")]
    [InlineData("missing")]
    public void Native_requester_check_requires_a_live_matching_process(string scenario)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requester identity and process handles require Windows.");
        using var fixture = new Requester(output);
        var plan = fixture.Plan(false);
        if (scenario == "exited") fixture.Exit();
        if (scenario == "wrong-user") plan = plan with { UserSid = plan.UserSid == "S-1-5-18" ? "S-1-5-19" : "S-1-5-18" };
        if (scenario == "missing") plan = plan with { RequesterProcessId = 0 };
        string directory = scenario == "wrong-directory" ? Path.Combine(fixture.Root, "other") : fixture.Root;
        string? refusal = ElevationBroker.CheckRequester(plan, directory);
        output.WriteLine("BROKER_REQUESTER_NATIVE " + JsonSerializer.Serialize(new
        {
            scenario, refusal, actualPid = fixture.Process.Id, exited = fixture.Process.HasExited,
            retainedHandle = !fixture.Process.SafeHandle.IsClosed, expectedRefusal = scenario != "alive",
            nativeImage = fixture.Image, nativeSid = plan.UserSid,
        }));
        if (scenario == "alive") Assert.Null(refusal);
        else Assert.NotNull(refusal);
    }

    [Theory]
    [InlineData("initially-exited", false)]
    [InlineData("initially-exited", true)]
    [InlineData("exits-during-consent", false)]
    [InlineData("exits-during-consent", true)]
    [InlineData("alive", false)]
    [InlineData("alive", true)]
    [InlineData("declined", false)]
    [InlineData("declined", true)]
    public void Broker_admission_refuses_an_actual_exited_requester(string scenario, bool reading)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The broker and native requester handles require Windows.");
        using var fixture = new Requester(output);
        var plan = fixture.Plan(reading);
        if (scenario == "initially-exited") fixture.Exit();
        using var request = ElevationExchange.Create(Path.Combine(fixture.Root, "exchange"), plan);
        using var exchange = BrokerExchange.Open(request.VolumePlanPath);
        int asks = 0, reads = 0, checks = 0, claims = 0;
        var refusals = new List<string>();
        var services = new BrokerProgram.RunServices(
            () => plan.CreatedUtc,
            (p, directory) => { checks++; return ElevationBroker.CheckRequester(p, directory); },
            (_, _) => { claims++; return true; },
            (_, _, _, _) =>
            {
                asks++;
                if (scenario == "exits-during-consent") fixture.Exit();
                return scenario != "declined";
            }, refusals.Add,
            (_, _, _, _) => { reads++; return "Owned callback only; no device opened."; });
        int code = BrokerProgram.Run(exchange, request.Hash, fixture.Root, services);
        var result = request.ReadResult();
        bool made = Directory.Exists(Path.Combine(fixture.Root, "made"));
        bool expected = scenario == "alive";
        output.WriteLine("BROKER_REQUESTER_ADMISSION " + JsonSerializer.Serialize(new
        {
            scenario, reading, code, asks, checks, claims, reads, made, result, refusals,
            actualPid = fixture.Process.Id, exited = fixture.Process.HasExited,
            retainedNativeHandle = !fixture.Process.SafeHandle.IsClosed,
            nativeRequesterCheck = true, actualExchangeAndWriteRunner = true,
            controlledConsentNonceAndReadCallback = true, noUacOrNativeDialogOrSourceDevice = true,
        }));
        Assert.Equal(expected ? 0 : scenario == "declined" ? 1 : 2, code);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Consented);
        Assert.Equal(expected, result.Finished);
        Assert.Equal(expected && !reading, made);
        Assert.Equal(expected && reading ? 1 : 0, reads);
        Assert.Equal(scenario == "initially-exited" ? 0 : 1, asks);
        Assert.Equal(expected ? 2 : scenario == "exits-during-consent" ? 2 : 1, checks);
        if (!expected) { Assert.NotNull(result.Refused); Assert.Empty(result.Steps); }
    }

    private sealed class Requester : IDisposable
    {
        private readonly ITestOutputHelper _output;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "filecat-broker-requester-" + Guid.NewGuid().ToString("N"));
        public string Image => Path.Combine(Root, "FileCat.exe");
        public Process Process { get; } = null!;
        private readonly byte[] _original;

        public Requester(ITestOutputHelper output)
        {
            _output = output;
            Directory.CreateDirectory(Root);
            string input = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            _original = File.ReadAllBytes(input);
            File.WriteAllBytes(Image, _original);
            var start = new ProcessStartInfo(Image)
            {
                Arguments = "/d /q /c \"echo FILECAT_REQUESTER_READY&set /p hold=&exit\"",
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Root,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            try
            {
                Process = System.Diagnostics.Process.Start(start)!;
                _ = Process.SafeHandle; // Hold the real object even after exit; the PID remains queryable.
                var ready = Process.StandardOutput.ReadLineAsync();
                Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Owned requester did not become ready.");
                Assert.Equal("FILECAT_REQUESTER_READY", ready.Result?.Trim());
                Assert.False(Process.HasExited);
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        public ElevationPlan Plan(bool reading)
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new ElevationPlan
            {
                Nonce = ElevationPlanCodec.NewNonce(), CreatedUtc = DateTime.UtcNow,
                UserSid = identity.User!.Value, UserName = identity.Name, RequesterProcessId = Process.Id,
                Title = "Owned native requester control",
                Steps = [reading
                    ? new ElevatedStep(ElevatedVerb.ReadDevice) { Path = @"\\.\PhysicalDrive999", Name = "FileCat-read-" + ElevationPlanCodec.NewNonce() }
                    : new ElevatedStep(ElevatedVerb.CreateDirectory) { Destination = ElevationPaths.ToVolumePath(Root), Name = "made" }],
            };
        }

        public void Exit()
        {
            Process.StandardInput.WriteLine("done");
            Process.StandardInput.Flush();
            Assert.True(Process.WaitForExit(10000), "Owned requester did not exit.");
            Assert.Equal(0, Process.ExitCode);
        }

        public void Dispose()
        {
            int pid = Process.Id;
            try { Assert.True(File.ReadAllBytes(Image).AsSpan().SequenceEqual(_original)); }
            finally { Cleanup(); }
            _output.WriteLine("BROKER_REQUESTER_RESTORATION " + JsonSerializer.Serialize(new { Root, pid, exited = true, absent = true, unchangedSystemCopy = true }));
        }

        private void Cleanup()
        {
            try
            {
                if (Process is not null)
                {
                    if (!Process.HasExited) Process.Kill(entireProcessTree: true);
                    Assert.True(Process.WaitForExit(10000), "Owned requester survived fixture cleanup.");
                }
            }
            finally { Process?.Dispose(); }
            string allowed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(allowed, Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(Root, recursive: true);
            Assert.False(Directory.Exists(Root));
        }
    }
}
