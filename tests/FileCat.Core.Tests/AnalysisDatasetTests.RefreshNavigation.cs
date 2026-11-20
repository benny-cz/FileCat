using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetTests
{
    private static AnalysisIndexedGraphHeader RefreshHeader() => Header() with
    {
        Roots = [new("r", "file", "/source", "/source", "physical-source", IdentityAssurance.Verified, "source")
        { NameRules = AnalysisNameRules.Unix, ObservedObjectIdentity = "physical-root" }]
    };

    private static async Task<AnalysisDataset> RefreshFixture(DeviceIoScheduler io, AnalysisIndexedGraphHeader header,
        int padding = 0, string encoding = "utf8", string change = "", bool longAncestry = false)
    {
        using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory());
        using var capture = new AnalysisObservationCapture(workspace.CreateArena(), header);
        var root = Entry("source", AnalysisKind.Directory, header.Roots[0].ObservedObjectIdentity) with
        { CreatedUtc = Start.AddDays(-5), CreatedPrecisionTicks = 1 };
        capture.Append(root, 0, "r", "basis", cancellation: Cancellation);
        ulong parent = 1;
        if (longAncestry)
            for (int n = 0; n < 4; n++) parent = capture.Append(Entry(new string((char)('a' + n), 40_000), AnalysisKind.Directory, "dir-" + n), parent, "r", "basis", cancellation: Cancellation);
        for (int n = 0; n < padding; n++) capture.Append(Entry("a-" + n.ToString("D6"), identity: "padding-" + n), parent, "r", "basis", cancellation: Cancellation);
        byte[] raw = encoding == "unix-bytes" ? [255] : AnalysisRawNames.Encode(encoding, "zz-target");
        var target = Entry(AnalysisRawNames.Decode(encoding, raw), identity: "physical-target") with
        { NameEncoding = encoding, RawName = raw.ToImmutableArray(), CreatedUtc = Start.AddDays(-1), CreatedPrecisionTicks = 1 };
        target = change switch
        {
            "identity" => target with { Identity = "replacement" },
            "birth" => target with { CreatedUtc = Start },
            "precision" => target with { CreatedPrecisionTicks = 2 },
            "missing-birth" => target with { CreatedUtc = null, CreatedPrecisionTicks = null },
            "name" => target with { Name = "other", RawName = AnalysisRawNames.Encode(encoding, "other").ToImmutableArray() },
            "raw" => target with { RawName = [254] },
            "kind" => target with { Kind = AnalysisKind.Special },
            _ => target
        };
        capture.Append(target, parent, "r", "basis", cancellation: Cancellation); capture.Publish(Cancellation);
        return new(await Complete(capture, workspace, io, header), io, Device);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("unix-bytes")]
    public async Task IndexedRefreshNavigationFindsExactRawNamesAcrossReorderedRowsAfterProducerClosure(string encoding)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var header = RefreshHeader();
        await using var old = await RefreshFixture(io, header, encoding: encoding);
        await using var current = await RefreshFixture(io, header with { Identity = header.Identity with { Epoch = 2 } }, 5003, encoding);
        await using var a = await old.AcquireAsync(Cancellation); await using var b = await current.AcquireAsync(Cancellation);
        await old.DisposeAsync(); await current.DisposeAsync();
        Assert.Equal(5005UL, await AnalysisRefreshNavigation.MapAsync(a, b, 2, Cancellation));
        Assert.Equal(1UL, await AnalysisRefreshNavigation.MapAsync(a, b, 1, Cancellation));
        Assert.Equal(0UL, await AnalysisRefreshNavigation.MapAsync(a, b, 0, Cancellation));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("birth")]
    [InlineData("precision")]
    [InlineData("missing-birth")]
    [InlineData("name")]
    [InlineData("raw")]
    [InlineData("kind")]
    [InlineData("source")]
    [InlineData("root")]
    [InlineData("session")]
    [InlineData("epoch")]
    public async Task IndexedRefreshNavigationRefusesReplacementsUnknownBirthAndChangedDomains(string change)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var header = RefreshHeader();
        var next = header with { Identity = header.Identity with { Epoch = change == "epoch" ? 3 : 2 } };
        if (change == "session") next = next with { Identity = next.Identity with { SessionId = Guid.NewGuid() } };
        if (change == "source") next = next with { Roots = [next.Roots[0] with { SourceIdentity = "replacement" }] };
        if (change == "root") next = next with { Roots = [next.Roots[0] with { ObservedObjectIdentity = "replacement-root" }] };
        await using var old = await RefreshFixture(io, header, encoding: "unix-bytes");
        await using var current = await RefreshFixture(io, next, encoding: "unix-bytes", change: change);
        await using var a = await old.AcquireAsync(Cancellation); await using var b = await current.AcquireAsync(Cancellation);
        Assert.Null(await AnalysisRefreshNavigation.MapAsync(a, b, 2, Cancellation));
    }

    [Fact]
    public async Task IndexedRefreshNavigationBoundsOptionalAncestryAndHonorsCancellationBeforeTakingReaders()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var header = RefreshHeader();
        await using var old = await RefreshFixture(io, header, longAncestry: true);
        await using var current = await RefreshFixture(io, header with { Identity = header.Identity with { Epoch = 2 } }, longAncestry: true);
        await using var a = await old.AcquireAsync(Cancellation); await using var b = await current.AcquireAsync(Cancellation);
        Assert.Null(await AnalysisRefreshNavigation.MapAsync(a, b, 6, Cancellation));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnalysisRefreshNavigation.MapAsync(a, b, 0, canceled.Token));
    }
}
