using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.Core.Analysis;

internal static partial class AnalysisCapacityRetirementProbe
{
    internal static int Run(string profileText, string mode, string? parent)
    {
        if (AnalysisAdmission.Refusal() is not null) return 131;
        var profile = Guid.ParseExact(profileText, "N"); int failureCalls = 0;
        AnalysisProfileQuota Create(Guid id) => OperatingSystem.IsWindows()
            ? AnalysisProfileQuota.Open(id, name => new AnalysisNativeMutex(name,
                close: mode == "kernel-close" ? value => { failureCalls++; AnalysisNativeHandle.CloseNativeFile(value); return false; } : null,
                beforeRelease: mode != "kernel-close" ? () => { failureCalls++; throw new IOException("Injected release refusal before the native call."); } : null))
            : AnalysisProfileQuota.Open(id);
        void Fault(AnalysisProfileQuota quota)
        {
            if (!quota.TryReserve(1)) throw new IOException("The owned fixture could not acquire capacity.");
            if (!OperatingSystem.IsWindows() && mode != "file")
            {
                // The exact ID comes from this freshly created, retained component. Never
                // enumerate IPC objects or remove an ID obtained from a persisted name/PID.
                var component = typeof(AnalysisProfileQuota).GetField("_unix", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(quota)!;
                int id = (int)component.GetType().GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
                if (Remove(id) != 0) throw new IOException("The exact owned IPC set could not be removed.");
                failureCalls++;
            }
            try { quota.Release(1); throw new InvalidOperationException("Retirement unexpectedly succeeded."); }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { }
            try { quota.TryReserve(1); throw new InvalidOperationException("Faulted admission unexpectedly succeeded."); }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { }
            try { quota.Dispose(); throw new InvalidOperationException("Final retirement unexpectedly succeeded."); }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { }
            quota.Dispose(); // Root-owner closure is idempotent, without a second native attempt.
            if (!quota.WorkerAlive || quota.ReservedBytes != 1 || quota.CapacityBytes != AnalysisProfileQuota.QuantumBytes)
                throw new IOException("The failed quota lost its worker, byte charge or capacity owner.");
        }
        if (mode == "kernel-limit")
        {
            for (int i = 0; i < AnalysisLimits.Sessions; i++) Fault(Create(Guid.NewGuid()));
            try { AnalysisProfileQuota.Open(Guid.NewGuid()); return 132; } catch (IOException) { }
            if (AnalysisProfileQuota.ActiveWorkers != AnalysisLimits.Sessions || AnalysisNativeHandles.Count != AnalysisLimits.Sessions || failureCalls != AnalysisLimits.Sessions) return 133;
            Console.WriteLine("CAPACITY_READY " + JsonSerializer.Serialize(new { workerAlive = true,
                workerCount = AnalysisProfileQuota.ActiveWorkers, nativeCount = AnalysisNativeHandles.Count,
                retainedBytes = AnalysisLimits.Sessions, capacityBytes = AnalysisProfileQuota.QuantumBytes * AnalysisLimits.Sessions,
                failureCalls, newOwnerRefused = true }));
        }
        else
        {
            FaultDirectory? directory = mode == "file" ? new(AnalysisQuotaHold.OpenDirectory(parent!)) : null;
            var quota = directory is null ? Create(profile) : AnalysisProfileQuota.Open(profile, directory);
            Fault(quota);
            int actualCalls = directory?.CloseCalls ?? failureCalls;
            if (actualCalls != 1 || directory?.Closed == true || AnalysisProfileQuota.ActiveWorkers != 1) return 134;
            Console.WriteLine("CAPACITY_READY " + JsonSerializer.Serialize(new { workerAlive = quota.WorkerAlive,
                workerCount = AnalysisProfileQuota.ActiveWorkers, nativeCount = AnalysisNativeHandles.Count,
                retainedBytes = quota.ReservedBytes, capacityBytes = quota.CapacityBytes, failureCalls = actualCalls,
                directoryClosed = directory?.Closed ?? false, newOwnerRefused = false }));
        }
        Console.Out.Flush(); return Console.ReadLine() == "EXIT" ? 0 : 135;
    }
    private sealed class FaultDirectory(AnalysisPrivateDirectory inner) : AnalysisPrivateDirectory
    {
        internal bool Closed; internal int CloseCalls;
        internal override void Verify() => inner.Verify();
        internal override (UInt128 Capacity, UInt128 Available) Space() => inner.Space();
        internal override AnalysisPrivateFile CreateStaging() => inner.CreateStaging();
        internal override AnalysisPrivateFile OpenRead(string name)
        {
            var file = inner.OpenRead(name);
            return AnalysisPrivateNames.IsCapacityToken(name) ? new FaultFile(this, file) : file;
        }
        public override void Dispose() { Closed = true; inner.Dispose(); }
        private sealed class FaultFile(FaultDirectory owner, AnalysisPrivateFile inner) : AnalysisPrivateFile
        {
            public override Stream Stream => inner.Stream;
            public override void Verify() => inner.Verify();
            public override void Flush() => inner.Flush();
            public override void PublishNew(string name) => inner.PublishNew(name);
            internal override bool TryLockCapacity() => inner.TryLockCapacity();
            public override void Dispose()
            { owner.CloseCalls++; throw new IOException("Injected refusal before native token close; the actual lock is still held."); }
        }
    }
    private static int Remove(int id) => OperatingSystem.IsMacOS()
        ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? RemoveArm(id, 0, 0, 0, 0, 0, 0, 0, 0) : RemoveMac(id, 0, 0, 0)
        : RemoveUnix(id, 0, 0, 0);
    [LibraryImport("libc", EntryPoint = "semctl", SetLastError = true)] private static partial int RemoveUnix(int id, int number, int command, nint argument);
    [LibraryImport("libc", EntryPoint = "semctl$UNIX2003", SetLastError = true)] private static partial int RemoveMac(int id, int number, int command, nint argument);
    [LibraryImport("libc", EntryPoint = "semctl", SetLastError = true)] private static partial int RemoveArm(int id, int number, int command, int a, int b, int c, int d, int e, nint argument);
}
