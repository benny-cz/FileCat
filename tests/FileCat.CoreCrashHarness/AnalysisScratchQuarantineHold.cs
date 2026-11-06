using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

internal static class AnalysisScratchQuarantineHold
{
    internal static async Task<int> RunAsync(string profileText, string parent)
    {
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        using var io = new DeviceIoScheduler(); AnalysisProfileQuota? quota = null; AnalysisScratchWorkspace? workspace = null;
        AnalysisScratchArena? arena = null; long retained = 0;
        await AnalysisIo.Run(io, "quarantine-owned-child", _ =>
        {
            quota = AnalysisProfileQuota.Open(Guid.ParseExact(profileText, "N"), AnalysisQuotaHold.OpenDirectory(parent));
            workspace = new(new FaultDirectory(ScratchScale.Directory(parent)), AnalysisLimits.ScratchBytes, new(AnalysisLimits.ProfileScratchBytes), quota);
            arena = workspace.CreateArena(); arena.Append(new byte[64 * 1024]); retained = arena.ReservedBytes;
            quota.Dispose(); // Only scratch's runtime retention owns this capacity now.
            return true;
        });
        try { await AnalysisIo.Run(io, "quarantine-owned-child", _ => { AnalysisIo.CloseOnWorker(arena!); return true; }); return 81; }
        catch (AnalysisCloseUnconfirmedException) { }
        await AnalysisIo.ReleaseScratchAsync(io, "quarantine-owned-child", workspace!);
        if (workspace!.ReservedBytes != retained || quota!.ReservedBytes != retained || quota.CapacityBytes != AnalysisProfileQuota.QuantumBytes || AnalysisResourceQuarantine.Shared.Count == 0)
            return 82;
        Console.WriteLine("QUARANTINE_READY " + JsonSerializer.Serialize(new { retainedBytes = retained, capacityBytes = quota.CapacityBytes, pid = Environment.ProcessId })); Console.Out.Flush();
        if (Console.ReadLine() != "EXIT") return 83;
        // Quarantine intentionally has no Dispose/retry path. Process exit releases the
        // actual kernel locks; the parent independently verifies reuse only after exit.
        return 0;
    }
    private sealed class FaultDirectory(AnalysisPrivateDirectory inner) : AnalysisPrivateDirectory
    {
        internal override void Verify() => inner.Verify();
        internal override (UInt128 Capacity, UInt128 Available) Space() => inner.Space();
        internal override AnalysisPrivateScratchFile CreateScratch() => new FaultFile(inner.CreateScratch());
        internal override AnalysisPrivateFile CreateStaging() => inner.CreateStaging();
        internal override AnalysisPrivateFile OpenRead(string name) => inner.OpenRead(name);
        public override void Dispose() => inner.Dispose();
    }
    private sealed class FaultFile(AnalysisPrivateScratchFile inner) : AnalysisPrivateScratchFile
    {
        public override long Length => inner.Length;
        public override void Verify() => inner.Verify();
        public override int ReadAt(Span<byte> buffer, long offset) => inner.ReadAt(buffer, offset);
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset) => inner.WriteAt(buffer, offset);
        public override void Dispose() { inner.Dispose(); throw new IOException("Injected unconfirmed scratch-close result."); }
    }
}
