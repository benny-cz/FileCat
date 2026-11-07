using System.Reflection;
using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

internal static class AnalysisBackendCloseProbe
{
    internal static async Task<int> RunAsync(string root)
    {
        if (AnalysisAdmission.Refusal() is not null || AnalysisNativeHandles.Count != 0) return 111;
        bool armed = false; int faults = 0, calls = 0;
        Func<nint, bool> close = value =>
        {
            calls++; bool confirmed = AnalysisNativeHandle.CloseNativeFile(value);
            if (!armed) return confirmed;
            armed = false; faults++; return false;
        };
        using var io = new DeviceIoScheduler();
        var backend = await AnalysisIo.Run(io, "backend-close-probe", _ =>
        {
            if (!OperatingSystem.IsWindows()) return (IAnalysisBackend)new UnixAnalysisBackend(root, close);
            var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "FileCat.Platform.Windows.dll"));
            var type = assembly.GetType("FileCat.Platform.Windows.Analysis.WindowsAnalysisBackend", throwOnError: true)!;
            var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(string), typeof(Func<nint, bool>)], null)!;
            return (IAnalysisBackend)constructor.Invoke([root, close]);
        });
        armed = true;
        try
        {
            await AnalysisIo.Run(io, backend.IoDeviceKey, _ =>
            { foreach (var entry in backend.Children(backend.RootEntry, CancellationToken.None)) { } return true; });
            return 112;
        }
        catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { }
        if (faults != 1) return 113;
        int afterFault = calls;
        try
        {
            await AnalysisIo.Run(io, backend.IoDeviceKey, _ =>
            { foreach (var entry in backend.Children(backend.RootEntry, CancellationToken.None)) { } return true; });
            return 114;
        }
        catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { }
        int newNativeCallsAfterFault = calls - afterFault;
        if (newNativeCallsAfterFault != 0) return 115;
        try { await AnalysisIo.ReleaseAsync(io, backend); return 116; }
        catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { }
        if (AnalysisNativeHandles.Count != 1) return 117;
        Console.WriteLine("BACKEND_READY " + JsonSerializer.Serialize(new { retainedSlots = AnalysisNativeHandles.Count,
            faults, newNativeCallsAfterFault, backendWorkRefused = true, backendCloseRetainedFailure = true }));
        Console.Out.Flush();
        return Console.ReadLine() == "EXIT" ? 0 : 118;
    }
}
