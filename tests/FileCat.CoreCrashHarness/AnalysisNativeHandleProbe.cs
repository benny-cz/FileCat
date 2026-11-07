using System.Text.Json;
using FileCat.Core.Analysis;

internal static class AnalysisNativeHandleProbe
{
    internal static int Run(string source)
    {
        if (AnalysisAdmission.Refusal() is not null || AnalysisNativeHandles.Count != 0) return 91;
        var owners = new List<AnalysisNativeHandle>(); int failedCloseCalls = 0; bool realCloseConfirmed = false;
        var quarantine = new AnalysisResourceQuarantine();
        try
        {
            for (int i = 0; i < AnalysisLimits.NativeHandles; i++)
            {
                var slot = AnalysisNativeHandles.Acquire();
                var file = File.OpenHandle(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Func<nint, bool>? injected = i == 0 ? value =>
                { failedCloseCalls++; realCloseConfirmed = AnalysisNativeHandle.CloseNativeFile(value); return false; } : null;
                owners.Add(AnalysisNativeHandle.Adopt(file, slot, quarantine, injected));
            }
            if (AnalysisNativeHandles.Count != AnalysisLimits.NativeHandles || !Full()) return 92;
            nint releasedNumber = owners[0].DangerousGetHandle();
            try { owners[0].CloseChecked(); return 93; } catch (AnalysisCloseUnconfirmedException) { }
            if (!realCloseConfirmed || failedCloseCalls != 1) return 94;
            // This later handle can reuse the old native number. No failed-close retry may
            // affect it, regardless of the platform's actual reuse choice on this run.
            using var neighbor = File.OpenHandle(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool reused = neighbor.DangerousGetHandle() == releasedNumber;
            for (int i = 0; i < 4; i++)
                try { owners[0].CloseChecked(); return 95; } catch (AnalysisCloseUnconfirmedException) { }
            if (RandomAccess.GetLength(neighbor) != 3 || failedCloseCalls != 1) return 96;
            for (int i = 1; i < owners.Count; i++) owners[i].CloseChecked();
            if (AnalysisNativeHandles.Count != 1 || quarantine.Count != 1) return 97;
            var replacements = new List<IDisposable>();
            try
            {
                for (int i = 1; i < AnalysisLimits.NativeHandles; i++) replacements.Add(AnalysisNativeHandles.Acquire());
                if (!Full()) return 98;
            }
            finally { foreach (var slot in replacements) slot.Dispose(); }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            if (AnalysisNativeHandles.Count != 1 || failedCloseCalls != 1) return 99;
            Console.WriteLine("HANDLE_READY " + JsonSerializer.Serialize(new { nativeLimit = AnalysisLimits.NativeHandles,
                retainedSlots = AnalysisNativeHandles.Count, failedCloseCalls, realCloseConfirmed, nativeNumberReused = reused }));
            Console.Out.Flush();
            return Console.ReadLine() == "EXIT" ? 0 : 100;
        }
        finally
        {
            // Successful owners are idempotent. The failed owner deliberately stays charged
            // until process exit; no cleanup loop retries its native close.
            for (int i = 1; i < owners.Count; i++) owners[i].CloseChecked();
        }
    }
    private static bool Full()
    {
        try { using var unexpected = AnalysisNativeHandles.Acquire(); return false; }
        catch (IOException) { return true; }
    }
}
