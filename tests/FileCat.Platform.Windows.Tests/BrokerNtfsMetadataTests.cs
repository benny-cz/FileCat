using System.Security.Principal;
using FileCat.Core.Analysis;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Elevation;
using BrokerProgram = FileCat.PrivilegedHost.Program;

namespace FileCat.Platform.Windows.Tests;

public sealed class BrokerNtfsMetadataTests
{
    [Theory]
    [InlineData("approved")]
    [InlineData("declined")]
    [InlineData("expired")]
    [InlineData("changed")]
    [InlineData("replayed")]
    [InlineData("wrong-hash")]
    [InlineData("mixed")]
    public void FastNtfsMetadata_uses_its_own_held_consent_and_never_dispatches_raw_recovery(string scenario)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The broker exchange requires Windows.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-metadata-consent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        DateTime now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        using var identity = WindowsIdentity.GetCurrent();
        var selected = NtfsMetadataVolume.CaptureSelection(Path.GetPathRoot(root)!, TestContext.Current.CancellationToken);
        var scope = new ElevatedNtfsMetadataScope(selected.GuidPath, selected.Serial, selected.RootFileId, 1, 2, "S-1-5-5-3-4",
            5.ToString("X48"), NtfsMetadataProtocol.Version, AnalysisLimits.Observations, ElevationPlanCodec.MetadataFields);
        var metadata = new ElevatedStep(ElevatedVerb.EnumerateNtfsMetadata) { NtfsMetadata = scope };
        var plan = new ElevationPlan
        {
            CreatedUtc = now, Nonce = NtfsMetadataProtocol.NewNonce(), UserSid = identity.User!.Value,
            RequesterProcessId = Environment.ProcessId, Title = ElevationPlanCodec.MetadataTitle,
            Steps = scenario == "mixed" ? [metadata, new(ElevatedVerb.ReadDevice) { Path = @"\\.\PhysicalDrive0", Name = "FileCat-read-" + ElevationPlanCodec.NewNonce() }] : [metadata],
        };
        var lease = new OwnedAdmission(scenario == "changed");
        int prepare = 0, asked = 0, claimed = 0;
        try
        {
            using var request = ElevationExchange.Create(Path.Combine(root, "exchange"), plan);
            using var exchange = BrokerExchange.Open(request.VolumePlanPath);
            var services = new BrokerProgram.RunServices(
                () => now,
                (_, _) => throw new InvalidOperationException("The generic requester check ran."),
                (_, _) => { claimed++; return scenario != "replayed"; },
                (_, _, _, _) => throw new InvalidOperationException("The generic consent ran."),
                _ => { }, (_, _, _, _) => throw new InvalidOperationException("The raw device route ran."),
                (approved, _) => { Assert.Equal(scope, Assert.Single(approved.Steps).NtfsMetadata); prepare++; return lease; },
                (_, _, pages, summary) =>
                {
                    asked++;
                    Assert.Contains("whole volume", Assert.Single(pages), StringComparison.Ordinal);
                    Assert.Contains(selected.GuidPath.TrimEnd('\\'), pages[0], StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("no cleanup", summary, StringComparison.Ordinal);
                    if (scenario == "expired") now = now.AddMinutes(16);
                    return scenario != "declined";
                });
            int code = BrokerProgram.Run(exchange, scenario == "wrong-hash" ? new string('0', 64) : request.Hash, root, services);
            bool ran = scenario == "approved", considered = scenario is not ("wrong-hash" or "mixed");
            Assert.Equal(ran ? 0 : scenario == "declined" ? 1 : 2, code);
            var report = request.ReadResult();
            Assert.NotNull(report);
            Assert.Equal(ran, report.Consented);
            Assert.Equal(considered ? 1 : 0, prepare);
            Assert.Equal(considered ? 1 : 0, claimed);
            Assert.Equal(considered && scenario != "replayed" ? 1 : 0, asked);
            Assert.Equal(ran ? 1 : 0, lease.Runs);
            Assert.Equal(considered, lease.Disposed);
            if (ran) Assert.Equal(ElevatedOutcome.Committed, Assert.Single(report.Steps).Outcome);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FastNtfsMetadata_elevated_native_reader_observes_owned_fixture_with_no_content_access()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_FAST_NTFS_NATIVE_PROBE") != "1")
            Assert.Skip("Requires the owned elevated NTFS validation run.");
        var ct = TestContext.Current.CancellationToken;
        string root = Path.Combine(Path.GetTempPath(), "filecat-ntfs-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string leaf = Path.GetFileName(root) + ".bin";
        string path = Path.Combine(root, leaf);
        File.WriteAllBytes(path, [1, 3, 7]);
        try
        {
            var volume = NtfsMetadataVolume.CaptureSelection(Path.GetPathRoot(path)!, ct);
            using var reader = NtfsMetadataReader.Open(volume, ct);
            string name = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(leaf));
            for (int i = 0; i < 8192; i++)
            {
                var page = reader.ReadNext(ct);
                if (page is null) break;
                if (page.Records.Any(record => record.NameUtf16 == name))
                {
                    Assert.Equal(new byte[] { 1, 3, 7 }, File.ReadAllBytes(path));
                    return;
                }
            }
            Assert.Fail("The owned fixture's name was not returned by the NTFS metadata enumeration.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FastNtfsMetadata_elevated_native_host_completes_a_live_volume_stream()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_FAST_NTFS_NATIVE_PROBE") != "1")
            Assert.Skip("Requires the owned elevated NTFS validation run.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        string root = Path.Combine(Path.GetTempPath(), "filecat-ntfs-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string leaf = Path.GetFileName(root) + ".bin";
        File.WriteAllBytes(Path.Combine(root, leaf), [1, 3, 7]);
        Task<ElevatedStepResult>? serving = null;
        try
        {
            var volume = NtfsMetadataVolume.CaptureSelection(Path.GetPathRoot(root)!, timeout.Token);
            using var peer = NtfsMetadataPeer.HoldSelf(elevated: true);
            string nonce = NtfsMetadataProtocol.NewNonce();
            serving = NtfsMetadataHost.RunAsync(volume, nonce, peer, () => timeout.IsCancellationRequested);
            using var client = await NtfsMetadataPipe.ConnectAsync(nonce, peer, timeout.Token);
            await client.SendAsync(new(NtfsMetadataMessage.Hello, System.Text.Json.JsonSerializer.SerializeToElement(new { })), timeout.Token);
            var accepted = await client.ReceiveAsync(timeout.Token);
            Assert.Equal(NtfsMetadataMessage.Accepted, accepted.Kind);
            Assert.Equal(volume.RootFileId, accepted.Payload.GetProperty("RootId").GetString());
            string wanted = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(leaf));
            bool found = false;
            int observed = 0;
            while (true)
            {
                var frame = await client.ReceiveAsync(timeout.Token);
                Assert.NotEqual(NtfsMetadataMessage.Refused, frame.Kind);
                if (frame.Kind == NtfsMetadataMessage.Complete)
                {
                    Assert.Equal("Finished", frame.Payload.GetProperty("Outcome").GetString());
                    Assert.Equal(observed, frame.Payload.GetProperty("Seen").GetInt32());
                    break;
                }
                if (frame.Kind != NtfsMetadataMessage.ObservationBatch) continue;
                var records = frame.Payload.GetProperty("Records");
                observed += records.GetArrayLength();
                found |= records.EnumerateArray().Any(record => record.GetProperty("NameUtf16").GetString() == wanted);
            }
            Assert.True(found, "The owned fixture did not appear in the metadata stream.");
            var result = await serving.WaitAsync(timeout.Token);
            Assert.Equal(ElevatedOutcome.Committed, result.Outcome);
            Assert.Equal(observed, result.ItemsDone);
        }
        finally
        {
            timeout.Cancel();
            if (serving is not null) try { _ = await serving.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
            catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException) { }
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class OwnedAdmission(bool changed) : INtfsMetadataAdmission
    {
        internal int Runs { get; private set; }
        internal bool Disposed { get; private set; }
        public void Verify() { if (changed) throw new IOException("Owned source witness changed."); }
        public ElevatedStepResult Run(Func<bool> stopRequested)
        {
            Assert.False(stopRequested());
            Runs++;
            return new(0, ElevatedOutcome.Committed, "Owned metadata endpoint completed.", 1);
        }
        public void Dispose() => Disposed = true;
    }
}
