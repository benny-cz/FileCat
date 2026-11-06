using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Tests;

internal static class AnalysisInventoryPathMapCases
{
    internal static async Task NativeAsync(Func<string, string, AnalysisPrivateDirectory> open)
    {
        var cancellation = TestContext.Current.CancellationToken;
        string parent = Path.Combine(Environment.CurrentDirectory, ".tmp", "inventory-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent); string folder = Path.Combine(parent, "private");
        using var io = new DeviceIoScheduler(); const string device = "native-inventory-map";
        AnalysisScratchWorkspace? workspace = null;
        try
        {
            workspace = await AnalysisIo.Run(io, device, _ => new AnalysisScratchWorkspace(open(parent, "private")), cancellation);
            static ulong Id(ulong row) => row == 5201 ? 1 : ulong.MaxValue - (row - 1) * 7919;
            IEnumerable<AnalysisInventoryPathIdentity> Source()
            { for (ulong row = 1; row <= 5201; row++) yield return new(Id(row), row == 1 ? 0 : Id(row / 2)); }
            using var source = Source().GetEnumerator();
            await using (var map = await AnalysisInventoryPathMap.BuildAsync(workspace, io, device, source, cancellation, 4096))
            {
                await AnalysisIo.ReleaseScratchAsync(io, device, workspace);
                foreach (ulong row in new[] { 1UL, 2UL, 17UL, 2600UL, 5201UL })
                {
                    Assert.Equal(row, await map.ResolveAsync(Id(row), cancellation));
                    Assert.Equal(new AnalysisInventoryPathMapping(row, Id(row), row == 1 ? 0 : Id(row / 2), row / 2), await map.ReadAsync(row, cancellation));
                }
                Assert.Null(await map.ResolveAsync(2, cancellation)); Assert.Equal(5201UL, map.Count);
            }
            Assert.Equal(0, workspace.ReservedBytes); Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        }
        finally
        {
            try { if (workspace is not null) await AnalysisIo.ReleaseScratchAsync(io, device, workspace); }
            finally { Directory.Delete(parent, recursive: true); }
        }
    }
}
