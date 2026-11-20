using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisIndexedGraphTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SealedIdentityUniquenessIsDerivedRatherThanAcceptedFromTheSuppliedHeader(bool duplicate)
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler();
        var header = Header with { UniqueObjectIdentities = duplicate };
        using var graph = await Create(workspace, io, header);
        graph.AppendPath(new(1, "root", 0, "source", AnalysisKind.Directory), Cancellation);
        graph.AppendPath(new(2, "root", 1, "z", AnalysisKind.File, "z"), Cancellation);
        graph.AppendPath(new(3, "root", 1, "a", AnalysisKind.File, "a"), Cancellation);
        graph.AppendObject(Object("z", duplicate ? "same" : "first"), Cancellation);
        graph.AppendObject(Object("a", duplicate ? "same" : "second"), Cancellation);
        using var revision = await graph.SealAsync(workspace, io, Device, header.Identity with { Revision = 1 }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, Cancellation);
        using var reader = revision.Acquire(); Assert.Equal(!duplicate, reader.Header.UniqueObjectIdentities);
        Assert.Equal(2UL, reader.ObjectCohortCount);
        var first = reader.ReadObjectCohort(1, Cancellation);
        Assert.Equal(AnalysisGraphKeys.ObjectIdentity(reader.ReadObject(first.Value, Cancellation)), first.Key);
        var second = reader.ReadObjectCohort(2, Cancellation);
        Assert.Equal(AnalysisGraphKeys.ObjectIdentity(reader.ReadObject(second.Value, Cancellation)), second.Key);
        if (duplicate) Assert.Equal(first.Key, second.Key);
        else Assert.Equal("a", reader.ReadObject(first.Value, Cancellation).Id);
    }

    [Fact]
    public async Task InternalTraversalUsesCapturedRowsWhilePublicChildrenRetainExactNameOrder()
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler(); var header = Header;
        using var graph = await Create(workspace, io, header);
        graph.AppendPath(new(1, "root", 0, "source", AnalysisKind.Directory), Cancellation);
        graph.AppendPath(new(2, "root", 1, "z", AnalysisKind.File, "z"), Cancellation);
        graph.AppendPath(new(3, "root", 1, "a", AnalysisKind.File, "a"), Cancellation);
        graph.AppendObject(Object("z", "native-z"), Cancellation); graph.AppendObject(Object("a", "native-a"), Cancellation);
        using var revision = await graph.SealAsync(workspace, io, Device, header.Identity with { Revision = 1 }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, Cancellation);
        using var reader = revision.Acquire();
        var names = reader.Children(0, 1, Cancellation); var captured = reader.TraversalChildren(0, 1, Cancellation);
        Assert.Equal(2UL, names.Count); Assert.Equal(2UL, captured.Count);
        Assert.Equal(3UL, reader.ReadIndex(AnalysisIndexedGraphRevision.Children, names.First, Cancellation).Value);
        Assert.Equal(2UL, reader.ReadIndex(AnalysisIndexedGraphRevision.Children, names.First + 1, Cancellation).Value);
        Assert.Equal(2UL, reader.ReadIndex(AnalysisIndexedGraphRevision.TraversalChildren, captured.First, Cancellation).Value);
        Assert.Equal(3UL, reader.ReadIndex(AnalysisIndexedGraphRevision.TraversalChildren, captured.First + 1, Cancellation).Value);
    }
}
