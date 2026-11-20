using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using FileCat.Core.Threading;

namespace FileCat.Core.FileSystem;

internal static partial class MacMaterializationPolicy
{
    internal const int Off = 1; // IOPOL_MATERIALIZE_DATALESS_FILES_OFF
    internal const int Required = 11; // Darwin EDEADLK when a lookup requires materialization under this policy
    internal const uint DatalessFlag = 0x40000000; // SF_DATALESS
    internal const string RequiredReason = "This metadata would require downloading the item or resolving a dataless parent.";
    private const int MaterializeDatalessFiles = 3, ThreadScope = 1;

    internal enum Failure { PolicyUnavailable, WouldMaterialize, RestoreFailed }

    internal static int Get() => GetIoPolicy(MaterializeDatalessFiles, ThreadScope);
    internal static int Set(int policy) => SetIoPolicy(MaterializeDatalessFiles, ThreadScope, policy);

    internal static T Run<T>(CancellationToken ct, Func<T> read, Func<Failure, string, T> unavailable) where T : class
    {
        ct.ThrowIfCancellationRequested();
        if (DeviceIoScheduler.IsDeviceWorker) return ReadOnWorker();

        // A direct caller needs an isolated thread so a failed restoration cannot affect its later filesystem calls.
        T? observed = null;
        ExceptionDispatchInfo? failure = null;
        var worker = new Thread(() =>
        {
            try { observed = ReadOnWorker(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        }) { IsBackground = true, Name = "FileCat macOS metadata" };
        worker.Start();
        worker.Join();
        failure?.Throw();
        return observed!;

        T ReadOnWorker()
        {
            ct.ThrowIfCancellationRequested();
            int prior;
            try
            {
                prior = Get();
                if (prior < 0 || prior != Off && Set(Off) != 0)
                    return unavailable(Failure.PolicyUnavailable, "FileCat could not disable downloads on this inspection thread.");
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return unavailable(Failure.PolicyUnavailable, "This system does not provide the non-downloading metadata policy.");
            }

            T result;
            bool restored = true;
            try
            {
                ct.ThrowIfCancellationRequested();
                result = read();
            }
            catch (IOException ex) when (ex.HResult == Required)
            {
                result = unavailable(Failure.WouldMaterialize, RequiredReason);
            }
            finally
            {
                if (prior != Off)
                {
                    restored = false;
                    try { restored = Set(prior) == 0; }
                    catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
                    if (!restored && DeviceIoScheduler.IsDeviceWorker) DeviceIoScheduler.RetireCurrentWorker();
                }
            }
            ct.ThrowIfCancellationRequested();
            return restored ? result : unavailable(Failure.RestoreFailed, "FileCat could not restore the inspection thread's original policy.");
        }
    }

    [LibraryImport("libc", EntryPoint = "getiopolicy_np", SetLastError = true)]
    private static partial int GetIoPolicy(int type, int scope);

    [LibraryImport("libc", EntryPoint = "setiopolicy_np", SetLastError = true)]
    private static partial int SetIoPolicy(int type, int scope, int policy);
}
