using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisObservationCaptureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedNativeTimestampsRemainExactInMemoryAndIndexedPaths(bool missing)
    {
        Ordinary(); var header = Header;
        var entry = Entry("source", AnalysisKind.Directory, null) with
        {
            CreatedUtc = missing ? null : Start.AddDays(-2).AddTicks(3), CreatedPrecisionTicks = missing ? null : 10,
            ChangedUtc = missing ? null : Start.AddSeconds(-1).AddTicks(7), ChangedPrecisionTicks = missing ? null : 1
        };
        using var memory = new AnalysisStore(); Assert.Equal(1UL, memory.Append(entry, 0, "r", "basis"));
        var snapshot = memory.Snapshot(header.Identity, header.Roots, header.Policy, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy);
        using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler();
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        capture.Append(entry, 0, "r", "basis", cancellation: Cancellation); capture.Publish(Cancellation);
        using var revision = await Complete(capture, workspace, io, header); using var reader = revision.Acquire();
        var expected = Assert.Single(snapshot.Nodes).Times;
        Assert.Equal(entry.CreatedUtc, expected!.CreatedUtc); Assert.Equal(entry.CreatedPrecisionTicks, expected.CreatedPrecisionTicks);
        Assert.Equal(entry.ChangedUtc, expected.ChangedUtc); Assert.Equal(entry.ChangedPrecisionTicks, expected.ChangedPrecisionTicks);
        Assert.Equal(expected, reader.ReadPath(1, Cancellation).Times);
    }

    [Theory]
    [InlineData("created-value")]
    [InlineData("created-precision")]
    [InlineData("created-zero")]
    [InlineData("changed-value")]
    [InlineData("changed-precision")]
    [InlineData("changed-zero")]
    [InlineData("modified-zero")]
    public void InconsistentNativeTimestampPrecisionCannotPartlyAppendEitherStore(string change)
    {
        Ordinary(); var good = Entry("source", AnalysisKind.Directory, null);
        var bad = change switch
        {
            "created-value" => good with { CreatedUtc = Start },
            "created-precision" => good with { CreatedPrecisionTicks = 1 },
            "created-zero" => good with { CreatedUtc = Start, CreatedPrecisionTicks = 0 },
            "changed-value" => good with { ChangedUtc = Start },
            "changed-precision" => good with { ChangedPrecisionTicks = 1 },
            "changed-zero" => good with { ChangedUtc = Start, ChangedPrecisionTicks = 0 },
            "modified-zero" => good with { ModifiedPrecisionTicks = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        using var memory = new AnalysisStore();
        Assert.Throws<InvalidDataException>(() => memory.Append(bad, 0, "r", "basis")); Assert.Equal(0, memory.Count);
        Assert.Equal(1UL, memory.Append(good, 0, "r", "basis"));
        using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var capture = new AnalysisObservationCapture(workspace.CreateArena(), Header);
        Assert.Throws<InvalidDataException>(() => capture.Append(bad, 0, "r", "basis", cancellation: Cancellation)); Assert.Equal(0UL, capture.Count);
        Assert.Equal(1UL, capture.Append(good, 0, "r", "basis", cancellation: Cancellation));
    }

    [Theory]
    [InlineData(0U, 123L, 42U, false)]
    [InlineData(0x800U, 123L, 1_000_000_000U, false)]
    [InlineData(0x800U, long.MaxValue, 0U, false)]
    [InlineData(0x800U, 123L, 42U, true)]
    [InlineData(0x800U, -1L, 999_999_999U, true)]
    public void StatxBirthMaskAndTimestampValidityKeepUnsupportedCreationUnknown(uint mask, long seconds, uint ns, bool known)
    {
        var value = UnixAnalysisBackend.StatxCreatedTime(mask, seconds, ns);
        Assert.Equal(known, value.HasValue);
        if (known) Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(ns / 100), value);
    }
}
