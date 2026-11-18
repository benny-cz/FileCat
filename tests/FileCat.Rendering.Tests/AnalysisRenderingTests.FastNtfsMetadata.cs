using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Fact]
    public void FastNtfsMetadataScopeAcceptsOnlyTheCanonicalMetadataContract()
    {
        const string volume = @"\\?\Volume{7c9e6a08-7b1d-466a-b9e5-38efabacab26}\";
        var scope = new ElevatedNtfsMetadataScope(volume, 5.ToString("X16"), 6.ToString("X32"), 7, 1, "S-1-5-5-9-10",
            8.ToString("X48"), NtfsMetadataProtocol.Version, AnalysisLimits.Observations, ElevationPlanCodec.MetadataFields);
        var step = new ElevatedStep(ElevatedVerb.EnumerateNtfsMetadata) { NtfsMetadata = scope };
        Assert.Null(ElevationPlanCodec.MetadataStepProblem(step));
        var refused = new[]
        {
            step with { Path = @"\\.\PhysicalDrive0" }, step with { Destination = @"C:\output" },
            step with { Name = "other", ReplaceExisting = true }, step with { Verb = ElevatedVerb.ReadDevice },
            step with { NtfsMetadata = scope with { Volume = volume + "folder" } },
            step with { NtfsMetadata = scope with { Volume = @"\\server\volume\" } },
            step with { NtfsMetadata = scope with { RequesterLogonSid = "S-1-5-5-09-10" } },
            step with { NtfsMetadata = scope with { MaximumRecords = int.MaxValue, Fields = "RawRead" } },
            step with { NtfsMetadata = scope with { RootFileId = new string('0', 32) } },
        };
        foreach (var variant in refused) Assert.NotNull(ElevationPlanCodec.MetadataStepProblem(variant));
        var plan = new ElevationPlan { Nonce = NtfsMetadataProtocol.NewNonce(), UserSid = "S-1-5-21-1-2-3-1001",
            CreatedUtc = DateTime.UnixEpoch, RequesterProcessId = 9, Title = ElevationPlanCodec.MetadataTitle, Steps = [step] };
        byte[] encoded = ElevationPlanCodec.Serialize(plan);
        Assert.Equal(step, Assert.Single(ElevationPlanCodec.Parse(encoded).Steps));
        string set = Encoding.UTF8.GetString(encoded);
        var duplicate = set.Replace("\"MaximumRecords\":", "\"MaximumRecords\":1,\"MaximumRecords\":", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ElevationPlanCodec.Parse(Encoding.UTF8.GetBytes(duplicate)));
        var numeric = set.Replace("\"EnumerateNtfsMetadata\"", ((int)ElevatedVerb.EnumerateNtfsMetadata).ToString(), StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ElevationPlanCodec.Parse(Encoding.UTF8.GetBytes(numeric)));
    }

    [Fact]
    public void FastNtfsMetadataPagePreservesRawNamesAndRejectsMalformedRecords()
    {
        byte[] page = new byte[80];
        BinaryPrimitives.WriteUInt64LittleEndian(page, 17);
        var record = page.AsSpan(8);
        BinaryPrimitives.WriteUInt32LittleEndian(record, 72);
        BinaryPrimitives.WriteUInt16LittleEndian(record[4..], 2);
        BinaryPrimitives.WriteUInt64LittleEndian(record[8..], 9);
        BinaryPrimitives.WriteUInt64LittleEndian(record[16..], 6);
        BinaryPrimitives.WriteUInt32LittleEndian(record[52..], 32);
        BinaryPrimitives.WriteUInt16LittleEndian(record[56..], 6);
        BinaryPrimitives.WriteUInt16LittleEndian(record[58..], 60);
        byte[] raw = [(byte)'a', 0, 0, 0xd8, (byte)'z', 0];
        raw.CopyTo(record[60..]);
        string rootId = 6.ToString("X32");
        var parsed = NtfsMetadataReader.ParsePage(page, 5, rootId);
        Assert.Equal(17ul, parsed.NextReference);
        Assert.Equal(new NtfsMetadataRecord(9.ToString("X32"), rootId, Convert.ToBase64String(raw), 32), Assert.Single(parsed.Records));
        Assert.Empty(NtfsMetadataReader.ParsePage(page, 5, 9.ToString("X32")).Records);
        Assert.Throws<InvalidDataException>(() => NtfsMetadataReader.ParsePage(page, 17, rootId));
        Assert.Throws<InvalidDataException>(() => NtfsMetadataReader.ParsePage(page[..^1], 5, rootId));
        page[8 + 60] = (byte)'\\';
        Assert.Throws<InvalidDataException>(() => NtfsMetadataReader.ParsePage(page, 5, rootId));
    }

    [Fact]
    public void FastNtfsMetadataWholeVolumeSelectionChecksLiveIdentity()
    {
        OrdinaryPreparation();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() => NtfsMetadataVolume.CaptureSelection("/", CancellationToken.None));
            return;
        }
        var ct = TestContext.Current.CancellationToken;
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        var scope = NtfsMetadataVolume.CaptureSelection(root, ct);
        Assert.Equal(scope.GuidPath, NtfsMetadataVolume.CanonicalGuid(scope.GuidPath));
        Assert.Equal(scope, NtfsMetadataVolume.CaptureSelection(scope.GuidPath, ct));
        using var reopened = scope.ReopenRoot(ct);
        Assert.Equal(scope, NtfsMetadataVolume.Native.Capture(scope.GuidPath, reopened));
        Assert.Throws<AnalysisSourceChangedException>(() => (scope with { Serial = new string('F', 16) }).ReopenRoot(ct));
        Assert.Throws<NotSupportedException>(() => NtfsMetadataVolume.CaptureSelection(Path.Combine(root, "Windows"), ct));
        Assert.Throws<NotSupportedException>(() => NtfsMetadataVolume.CaptureSelection(@"\\?\Volume{c52f967a-01da-4e9a-b201-cffa5f25f2f9}\", ct));
        using var heldRoot = new WindowsAnalysisBackend(root);
        Assert.Equal(scope, heldRoot.CaptureDiscoveryVolume(ct));
        Assert.Equal(NtfsMetadataProtocol.ParseId(scope.RootFileId), heldRoot.DiscoveryPrivateBoundaryId(heldRoot));
    }

    [Fact]
    public async Task FastNtfsDiscoveryIndexesRawNamesPrivatelyAndKeepsDimensionsUnmeasured()
    {
        OrdinaryPreparation(); using var profile = new NtfsFixture();
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var identity = new SessionIdentity(Guid.NewGuid(), 1, 1, "ntfs-discovery-test", now, now);
        var root = new RootBinding("1", "file", profile.Root, profile.Root, "root", IdentityAssurance.Verified, "volume")
            { NameRules = AnalysisNameRules.Windows };
        const string basis = AnalysisComponents.WindowsPrimaryAllocationBasis;
        var policy = new ScanPolicy { BasisIds = [basis] };
        string device = PathUtil.GetDeviceKey(profile.Root);
        AnalysisPrivateDirectory Private(string name) => OperatingSystem.IsWindows()
            ? new WindowsAnalysisPrivateDirectory(profile.Root, name) : new UnixAnalysisPrivateDirectory(profile.Root, name);
        await using (var ordinary = await AnalysisObservationWriter.CreateAsync(profile.Io, device, () => Private("ordinary"), identity, [root], policy, ct))
            await Assert.ThrowsAsync<InvalidOperationException>(() => ordinary.CreateNtfsDiscoveryAsync(1, ct));
        await using var writer = await AnalysisObservationWriter.CreateAsync(profile.Io, device, () => Private("discovery"),
            identity, [root], policy, AnalysisTaint.PrivilegedDerived, ct);
        await using (var discovery = await writer.CreateNtfsDiscoveryAsync(1, ct))
        {
            byte[] raw = [(byte)'x', 0, 0, 0xd8, (byte)'z', 0];
            var observations = new List<NtfsMetadataRecord>
            {
                new(4.ToString("X32"), 2.ToString("X32"), Convert.ToBase64String(raw), (uint)FileAttributes.SparseFile),
                Row(30, 31, "disconnected"), Row(31, 30, "cycle", FileAttributes.Directory),
                Row(5, 1, "dup"), Row(6, 1, "dup"), Row(7, 1, "link", FileAttributes.Directory | FileAttributes.ReparsePoint),
                Row(8, 7, "outside"), Row(2, 1, "folder", FileAttributes.Directory),
            };
            for (int i = 0; i < 255; i++) observations.Add(Row(100 + i, 1, $"a{i:D3}"));
            var frames = new Queue<NtfsMetadataFrame>([
                new(NtfsMetadataMessage.ObservationBatch, JsonSerializer.SerializeToElement(new { Records = observations })),
                new(NtfsMetadataMessage.Complete, JsonSerializer.SerializeToElement(new { Seen = observations.Count, Outcome = "Finished" })),
            ]);
            var outcome = await NtfsMetadataClient.CaptureAsync(_ => ValueTask.FromResult(frames.Dequeue()), discovery, null, ct);
            Assert.Equal(NtfsMetadataCompletion.Finished, outcome);
            var first = await discovery.ReadChildrenAsync(1, 0, ct);
            Assert.Equal(255, first.Observations.Length);
            Assert.Equal(1, first.AmbiguousNames);
            Assert.Equal(256ul, first.NextOffset);
            var page = await discovery.ReadChildrenAsync(1, first.NextOffset!.Value, ct);
            Assert.Equal(1, page.AmbiguousNames);
            Assert.Equal(new UInt128[] { 2, 7 }, page.Observations.Select(row => row.ObjectId));
            Assert.Null(page.NextOffset);
            var child = Assert.Single((await discovery.ReadChildrenAsync(2, 0, ct)).Observations).ToEntry(now);
            Assert.Equal(raw, child.RawName.ToArray());
            Assert.Equal(MeasurementState.Unsupported, child.LogicalLength.State);
            Assert.Equal(MeasurementState.Unsupported, child.Allocation.State);
            Assert.Equal(MeasurementState.Unsupported, child.Links.State);
            Assert.Null(child.Identity);
            Assert.Null(child.Ancestry);
            Assert.Equal(AnalysisKind.Unknown, page.Observations[1].ToEntry(now).Kind);
            Assert.False(page.Observations[1].ToEntry(now).IncludedByPolicy);
            var folder = page.Observations[0].ToEntry(now);
            await writer.AppendBatchAsync([new(folder with { Name = "volume", RawName = [] }, 0, "1", basis),
                new(folder, 1, "1", basis), new(child, 2, "1", basis)], ct);
        }
        await using var result = await writer.CompleteAsync(identity with { Revision = 2, ObservedUtc = DateTimeOffset.UtcNow },
            AnalysisPhase.Finished, NamespaceCoverage.Partial, ct);
        await using var lease = await result.AcquireAsync(ct);
        Assert.Equal(AnalysisPrivacy.Join(AnalysisTaint.PrivilegedDerived), lease.Descriptor.Taint);
        Assert.Equal(NamespaceCoverage.Partial, lease.Descriptor.Coverage);
        Assert.Equal(0xd800, (await lease.ReadPathAsync(3, ct)).Name[1]);
        Assert.Equal(MeasurementState.Unsupported, Assert.Single((await lease.ReadFactsAsync(0, cancellation: ct)).Rows).Measurement.State);
        Assert.Equal(MeasurementState.Unsupported, Assert.Single((await lease.ReadObjectsAsync(0, cancellation: ct)).Rows).LinkCount.State);

        static NtfsMetadataRecord Row(int id, int parent, string name, FileAttributes attributes = FileAttributes.Normal) =>
            new(id.ToString("X32"), parent.ToString("X32"), Convert.ToBase64String(AnalysisRawNames.Encode("utf16le", name)), (uint)attributes);
    }

    [Fact]
    public async Task FastNtfsDiscoveryRejectsAmbiguousIdentitiesAndUnfinishedInputBeforePublication()
    {
        OrdinaryPreparation(); using var profile = new NtfsFixture();
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var root = new RootBinding("1", "file", profile.Root, profile.Root, "root", IdentityAssurance.Verified, "volume");
        await using var writer = await AnalysisObservationWriter.CreateAsync(profile.Io, PathUtil.GetDeviceKey(profile.Root),
            () => OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(profile.Root, "refuse") : new UnixAnalysisPrivateDirectory(profile.Root, "refuse"),
            new(Guid.NewGuid(), 1, 1, "ntfs-discovery-test", now, now), [root], new(), AnalysisTaint.PrivilegedDerived, ct);
        var name = ImmutableArray.Create<byte>(65, 0);
        await using (var duplicate = await writer.CreateNtfsDiscoveryAsync(1, ct))
        {
            await duplicate.AppendAsync([new(2, 1, 16, name), new(2, 3, 16, name)], ct);
            await Assert.ThrowsAsync<InvalidDataException>(() => duplicate.SealAsync(2, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => duplicate.ReadChildrenAsync(1, 0, ct));
        }
        await using (var invalid = await writer.CreateNtfsDiscoveryAsync(1, ct))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => invalid.AppendAsync([new(2, 1, 16, name), new(3, 1, 16, [46, 0])], ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => invalid.SealAsync(1, ct));
        }
        await using var interrupted = await writer.CreateNtfsDiscoveryAsync(1, ct);
        await interrupted.AppendAsync([new(2, 1, 16, name)], ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => interrupted.SealAsync(2, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => interrupted.ReadChildrenAsync(1, 0, ct));
    }
}
