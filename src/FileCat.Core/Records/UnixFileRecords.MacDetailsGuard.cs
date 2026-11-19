using System.Runtime.ExceptionServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    private const string MacMaterializationRequired = "This metadata would require downloading the item or resolving a dataless parent.";

    internal static FileDetailsReport MacDetailsWithoutMaterialization(string path, CancellationToken ct, Func<FileDetailsReport> read)
    {
        ct.ThrowIfCancellationRequested();
        if (DeviceIoScheduler.IsDeviceWorker) return ReadOnWorker();

        // The normal App and column paths already own device workers. A direct caller needs an isolated thread so
        // a failed policy restoration cannot affect its later filesystem calls.
        FileDetailsReport? observed = null;
        ExceptionDispatchInfo? failure = null;
        var worker = new Thread(() =>
        {
            try { observed = ReadOnWorker(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        }) { IsBackground = true, Name = "FileCat macOS details" };
        worker.Start();
        worker.Join();
        failure?.Throw();
        return observed!;

        FileDetailsReport ReadOnWorker()
        {
            ct.ThrowIfCancellationRequested();
            int prior;
            try
            {
                prior = MacMaterializationPolicy.Get();
                if (prior < 0 || prior != MacMaterializationPolicy.Off && MacMaterializationPolicy.Set(MacMaterializationPolicy.Off) != 0)
                    return Unavailable(FactState.Unsupported, "FileCat could not disable downloads on this inspection thread.");
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return Unavailable(FactState.Unsupported, "This system does not provide the non-downloading metadata policy.");
            }

            FileDetailsReport report;
            bool restored = true;
            try
            {
                ct.ThrowIfCancellationRequested();
                report = read();
            }
            catch (IOException ex) when (ex.HResult == MacMaterializationPolicy.Required)
            {
                report = Unavailable(FactState.NotInspected, MacMaterializationRequired);
            }
            finally
            {
                if (prior != MacMaterializationPolicy.Off)
                {
                    restored = false;
                    try { restored = MacMaterializationPolicy.Set(prior) == 0; }
                    catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
                    if (!restored && DeviceIoScheduler.IsDeviceWorker) DeviceIoScheduler.RetireCurrentWorker();
                }
            }
            ct.ThrowIfCancellationRequested();
            return restored ? report : Unavailable(FactState.Failed, "FileCat could not restore the inspection thread's original policy.");
        }

        FileDetailsReport Unavailable(FactState state, string reason) => FileDetailsReport.Metadata(new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Identity = ObservedFact<DetailsIdentity>.Unavailable(state, FactScope.Entry, "macOS metadata with materialization disabled", reason),
            Notes = [reason],
        });
    }
}
