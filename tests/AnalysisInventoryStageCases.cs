using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Tests;

internal static class AnalysisInventoryStageCases
{
    internal static ulong Id(int row, int files) => row == files ? 1 : ulong.MaxValue - (ulong)row * 7919;
    internal static byte[] Bytes(int files, bool largeRecord = false)
    {
        var nodes = ImmutableArray.CreateBuilder<AnalysisNode>(); nodes.Add(new(Id(0, files), "r", 0, "Root", AnalysisKind.Directory));
        for (int n = 1; n <= files; n++) nodes.Add(new(Id(n, files), "r", Id(0, files), "file-" + n, AnalysisKind.File, "X"));
        var snapshot = new AnalysisSnapshot(new(Guid.NewGuid(), 1, 1, "fixture", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            [new("r", "file", "Root", "Root", "device", IdentityAssurance.Verified, "Root")], nodes.ToImmutable(),
            [new("X", "device:X", AnalysisMeasurement.Known(7), AnalysisMeasurement.Known((uint)files))],
            [new("f", "X", null, "primary", "fixture-v1", AnalysisMeasurement.Known(4096), "fixture", DateTimeOffset.UnixEpoch, [])],
            [], new(), AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy);
        using var stream = new MemoryStream(); AnalysisInventoryFormat.Write(stream, snapshot, Guid.NewGuid(), TestContext.Current.CancellationToken);
        byte[] result = stream.ToArray();
        if (!largeRecord) return result;
        var lines = Encoding.UTF8.GetString(result).TrimEnd('\n').Split('\n'); lines[1] = lines[1].PadRight(AnalysisLimits.RecordBytes, ' ');
        return WithTrailer(lines);
    }
    internal static byte[] WithTrailer(string[] lines)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(string.Join('\n', lines[..^1]) + "\n");
        var trailer = System.Text.Json.Nodes.JsonNode.Parse(lines[^1])!; trailer["sha256"] = Convert.ToHexStringLower(SHA256.HashData(prefix));
        return [.. prefix, .. Encoding.UTF8.GetBytes(trailer.ToJsonString() + "\n")];
    }
    internal static async Task NativeAsync(Func<string, string, AnalysisPrivateDirectory> open)
    {
        var token = TestContext.Current.CancellationToken; const int files = 5200;
        string parent = Path.Combine(Environment.CurrentDirectory, ".tmp", "inventory-stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(parent);
        string file = Path.Combine(parent, "input", "source.fcdsa");
        using var io = new DeviceIoScheduler(); AnalysisScratchWorkspace? workspace = null; TrackingDirectory? input = null; AnalysisInventoryPathMap? map = null; AnalysisDataset? dataset = null;
        try
        {
            byte[] bytes = Bytes(files, largeRecord: true);
            await AnalysisIo.Run(io, "native-stage", _ =>
            {
                using var directory = open(parent, "input");
                using var output = directory.CreateStaging(); output.Stream.Write(bytes); output.Flush(); output.PublishNew("source.fcdsa");
                workspace = new(open(parent, "scratch")); return true;
            }, token);
            string before = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            await using (var stage = await AnalysisInventoryFormat.StagePrivateAsync(workspace!, io, "native-stage", "native-stage", "source.fcdsa", () => input = new(open(parent, "input")), token))
            {
                Assert.True(input!.Closed); await AnalysisIo.ReleaseScratchAsync(io, "native-stage", workspace!);
                Assert.Equal((ulong)files + 1, stage.Count(AnalysisInventoryFormat.RecordKind.Path));
                var header = await stage.ReadHeaderAsync(token); Assert.Equal(AnalysisOrigin.ImportedInventory, header.Origin); Assert.True(header.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
                var first = await stage.ReadAsync(AnalysisInventoryFormat.RecordKind.Path, 1, token);
                var last = await stage.ReadAsync(AnalysisInventoryFormat.RecordKind.Path, (ulong)files + 1, token);
                Assert.Equal(ulong.MaxValue, first.Path!.Id); Assert.Equal(1UL, last.Path!.Id); Assert.Equal(ulong.MaxValue, last.Path.ParentId);
                Assert.Equal((UInt128)7, (await stage.ReadAsync(AnalysisInventoryFormat.RecordKind.Object, 1, token)).Object!.LogicalLength.Value);
                map = await stage.BuildPathMapAsync(token);
                dataset = await stage.BuildDatasetAsync(token);
            }
            Assert.Equal((ulong)files + 1, await map.ResolveAsync(1, token)); Assert.Equal(1UL, (await map.ReadAsync((ulong)files + 1, token)).ParentRow);
            await map.DisposeAsync(); map = null;
            await using (var lease = await dataset.AcquireAsync(token))
            {
                await dataset.DisposeAsync(); dataset = null;
                Assert.Equal(ulong.MaxValue, (await lease.ReadPathsAsync(0, 1, token)).Rows[0].Id);
                Assert.Equal(1UL, (await lease.ReadPathsAsync((ulong)files, 1, token)).Rows[0].Id);
                Assert.Equal((UInt128)4096, (await lease.SummarizeAsync(AnalysisMetric.OnDisk, token)).Totals.Known);
                await using (var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("name:file-5200"), token))
                    Assert.Equal(1UL, Assert.Single((await hierarchy.ReadMatchingFilesAsync(cancellation: token)).Rows).Path.Id);
                await lease.SaveNewAsync("saved.fcdsa", Guid.NewGuid(), "native-stage", () => open(parent, "output"), token);
                using var saved = File.OpenRead(Path.Combine(parent, "output", "saved.fcdsa"));
                using var reopened = AnalysisInventoryFormat.Read(saved, token);
                Assert.Equal(ulong.MaxValue, reopened.Snapshot.Nodes[0].Id); Assert.Equal(1UL, reopened.Snapshot.Nodes[^1].Id);
                Assert.Equal(ulong.MaxValue, reopened.Snapshot.Nodes[^1].ParentId); Assert.NotNull(reopened.Snapshot.CoverageDetails!.Qualification);
            }
            Assert.Equal(0, workspace!.ReservedBytes); Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(parent, "scratch")));
            Assert.Equal(before, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        }
        finally
        {
            try { try { if (map is not null) await map.DisposeAsync(); } finally { if (dataset is not null) await dataset.DisposeAsync(); } }
            finally { try { if (workspace is not null) await AnalysisIo.ReleaseScratchAsync(io, "native-stage", workspace); } finally { Directory.Delete(parent, true); } }
        }
    }
    private sealed class TrackingDirectory(AnalysisPrivateDirectory inner) : AnalysisPrivateDirectory
    {
        internal bool Closed;
        internal override void Verify() => inner.Verify();
        internal override (UInt128 Capacity, UInt128 Available) Space() => inner.Space();
        internal override AnalysisPrivateScratchFile CreateScratch() => inner.CreateScratch();
        internal override AnalysisPrivateFile CreateStaging() => inner.CreateStaging();
        internal override AnalysisPrivateFile OpenRead(string name) => inner.OpenRead(name);
        public override void Dispose() { inner.Dispose(); Closed = true; }
    }
}
