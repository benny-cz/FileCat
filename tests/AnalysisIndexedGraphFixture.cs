using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Tests;

internal static class AnalysisIndexedGraphFixture
{
    internal static async Task VerifyNative(Func<AnalysisPrivateDirectory> create, string parent, CancellationToken cancellation)
    {
        using var io = new DeviceIoScheduler(); string device = "native-graph-" + Guid.NewGuid().ToString("N");
        var workspace = await AnalysisIo.Run(io, device, _ => new AnalysisScratchWorkspace(create()), cancellation);
        AnalysisIndexedGraph? graph = null; AnalysisIndexedGraphRevision? revision = null; AnalysisIndexedGraphReader? reader = null;
        string foreign = Path.Combine(parent, "scratch", "foreign.txt");
        try
        {
            var start = DateTimeOffset.UtcNow;
            var header = new AnalysisIndexedGraphHeader(new(Guid.NewGuid(), 1, 0, "native-fixture", start, start) { TimeZoneId = "UTC" },
                [new("root", "file", parent, parent, null, IdentityAssurance.Unknown, "fixture") { NameRules = AnalysisNameRules.Unix }],
                new(), AnalysisPhase.Scanning, NamespaceCoverage.Partial, Origin: AnalysisOrigin.ImportedInventory);
            graph = await AnalysisIo.Run(io, device, _ => new AnalysisIndexedGraph(workspace.CreateArena(), header), cancellation);
            await AnalysisIo.Run(io, device, token =>
            {
                graph.AppendPath(new(1, "root", 0, "fixture", AnalysisKind.Directory), token);
                for (int n = 249; n >= 0; n--)
                {
                    string id = "object-é-" + n;
                    graph.AppendObject(new(id, "identity-" + n, AnalysisMeasurement.Known((UInt128)n), AnalysisMeasurement.Known(4)), token);
                    graph.AppendFact(new("fact-" + n, id, null, "reported-data", "fixture", AnalysisMeasurement.Known((UInt128)(n * 4096)), "native-fixture", start, []), token);
                }
                return true;
            }, cancellation);
            for (int first = 1; first <= 1000; first += 250)
            {
                int begin = first;
                await AnalysisIo.Run(io, device, token =>
                {
                    for (int n = begin; n < begin + 250; n++)
                    {
                        int owner = (n - 1) / 4;
                        graph.AppendPath(new((ulong)n + 1, "root", 1, "file-" + (1001 - n), AnalysisKind.File, "object-é-" + owner)
                            { ObservedIdentity = "identity-" + owner, Times = new(start, null, 1) }, token);
                        if (n % 100 == 0) graph.AppendIssue(new("sampled-issue", (ulong)n + 1) { RootId = "root" }, token);
                    }
                    return true;
                }, cancellation);
            }
            revision = await graph.SealAsync(workspace, io, device, header.Identity with { Revision = 1, ObservedUtc = DateTimeOffset.UtcNow },
                AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, cancellation);
            reader = await AnalysisIo.Run(io, device, _ => revision.Acquire(), cancellation);
            await AnalysisIo.ReleaseScratchAsync(io, device, graph); graph = null;
            await AnalysisIo.ReleaseScratchAsync(io, device, revision); revision = null;
            await AnalysisIo.ReleaseScratchAsync(io, device, workspace);
            File.WriteAllText(foreign, "preserve foreign neighbor");
            for (int first = 1; first <= 1000; first += 100)
            {
                int begin = first;
                await AnalysisIo.Run(io, device, token =>
                {
                    Assert.Equal(1000UL, reader.Children(0, 1, token).Count); Assert.Equal(AnalysisOrigin.ImportedInventory, reader.Header.Origin);
                    for (int n = begin; n < begin + 100; n++)
                    {
                        string id = "object-é-" + ((n - 1) / 4); var path = reader.ReadPath((ulong)n + 1, token);
                        Assert.Equal("file-" + (1001 - n), path.Name); Assert.Equal(id, path.ObjectId);
                        Assert.Equal(4UL, reader.Aliases(id, token).Count);
                        var objects = reader.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: token);
                        Assert.Equal(1UL, objects.Count); var obj = reader.ReadObject(reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, objects.First, token).Value, token);
                        Assert.Equal((UInt128)((n - 1) / 4), obj.LogicalLength.Value);
                    }
                    return true;
                }, cancellation);
            }
            Assert.True(workspace.ReservedBytes > 0);
            await AnalysisIo.ReleaseScratchAsync(io, device, reader); reader = null;
            Assert.Equal(0, workspace.ReservedBytes); Assert.Equal("preserve foreign neighbor", File.ReadAllText(foreign));
            Assert.Equal(new[] { foreign }, Directory.GetFiles(Path.GetDirectoryName(foreign)!));
        }
        finally
        {
            if (reader is not null) await AnalysisIo.ReleaseScratchAsync(io, device, reader);
            if (revision is not null) await AnalysisIo.ReleaseScratchAsync(io, device, revision);
            if (graph is not null) await AnalysisIo.ReleaseScratchAsync(io, device, graph);
            await AnalysisIo.ReleaseScratchAsync(io, device, workspace);
        }
    }
}
