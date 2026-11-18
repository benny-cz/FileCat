using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Jobs;
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
    }
}
