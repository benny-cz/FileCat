using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;
using Xunit;

internal static class AnalysisNativeMemoryFallbackCases
{
    internal static async Task Run(Func<string, IAnalysisBackend> prepare)
    {
        Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
        var cancellation = TestContext.Current.CancellationToken;
        string parent = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".tmp"));
        string fixture = Path.Combine(parent, "native-memory-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        string snapshots = Path.Combine(fixture, "snapshots"), scratch = Path.Combine(fixture, "scratch");
        Directory.CreateDirectory(snapshots); Directory.CreateDirectory(scratch);
        string savedCanary = Path.Combine(snapshots, "PRIVATE_SNAPSHOT_CANARY.bin"), scratchCanary = Path.Combine(scratch, "PRIVATE_SCRATCH_CANARY.bin");
        byte[] canary = [17, 39, 91, 121]; File.WriteAllBytes(savedCanary, canary); File.WriteAllBytes(scratchCanary, canary);
        string[] files = Enumerable.Range(0, 64).Select(n => Path.Combine(fixture, $"source-{n:D2}.bin")).ToArray();
        foreach (string file in files) File.WriteAllBytes(file, [23, 47]);
        using var io = new DeviceIoScheduler(); IAnalysisBackend? source = null;
        try
        {
            string device = PathUtil.GetDeviceKey(fixture);
            source = await AnalysisIo.Run(io, device, _ => prepare(fixture), cancellation);
            var storage = new AnalysisStorageOptions(device, () => throw new IOException("Controlled secure scratch unavailability."),
                [new(device, () => prepare(snapshots)), new(device, () => prepare(scratch))], PreviewPaths: 4) { AllowMemoryFallback = true };
            using var session = new AnalysisCoordinator(io).Start([source], storage: storage); source = null;
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30), cancellation);
            Assert.True(session.UsesMemoryFallback); Assert.Null(session.DatasetDescriptor); Assert.Equal(0UL, session.SourceCloseFailures);
            var snapshot = session.Snapshot(); AnalysisValidation.Validate(snapshot, cancellation);
            Assert.Equal(67, snapshot.Nodes.Length); Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, snapshot.Coverage);
            Assert.DoesNotContain(snapshot.Nodes, n => n.Name.Contains("PRIVATE_", StringComparison.Ordinal));
            Assert.Equal(2, snapshot.Nodes.Count(n => n.Kind == AnalysisKind.Directory && !n.IncludedByPolicy && n.MetadataState == MeasurementState.Excluded && n.ObservedIdentity is null));
            Assert.Equal(64, snapshot.Objects.Length);
            Assert.Equal(canary, File.ReadAllBytes(savedCanary)); Assert.Equal(canary, File.ReadAllBytes(scratchCanary));
            Assert.Equal(new[] { savedCanary }, Directory.GetFiles(snapshots)); Assert.Equal(new[] { scratchCanary }, Directory.GetFiles(scratch));
            foreach (string file in files) Assert.Equal(new byte[] { 23, 47 }, File.ReadAllBytes(file));
        }
        finally
        {
            if (source is not null) await AnalysisIo.ReleaseAsync(io, source);
            string relative = Path.GetRelativePath(parent, fixture);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("The generated fallback fixture escaped its owned parent.");
            Directory.Delete(fixture, recursive: true);
        }
    }
}
