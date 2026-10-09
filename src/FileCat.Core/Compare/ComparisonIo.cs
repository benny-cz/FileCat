using System.Runtime.ExceptionServices;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Compare;

/// <summary>Directory comparisons share the device workers with listings and viewers.</summary>
public sealed class ComparisonIo(DeviceIoScheduler io, ProviderRegistry providers)
{
    private void Check(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (io.IsStopped) throw new OperationCanceledException("Comparison admission stopped.");
    }

    private async Task<T> Run<T>(ResourceProvider provider, Location location, CancellationToken ct, Func<T> work)
    {
        Check(ct);
        // Do not cancel the wait for an active call: its content and buffers remain owned until it returns.
        // Cancellation is checked before queued calls start, and at the boundary after every completed call.
        T value = await io.Run(provider.GetDeviceKey(location), IoPriority.Normal, _ => { Check(ct); return work(); }).ConfigureAwait(false);
        Check(ct);
        return value;
    }

    private async Task<IContentSource?> Open(ResourceProvider provider, ItemRef item, CancellationToken ct)
    {
        Check(ct);
        var source = await io.Run(provider.GetDeviceKey(item.Parent), IoPriority.Normal, _ =>
        {
            Check(ct);
            return provider.OpenContent(item);
        }).ConfigureAwait(false);
        try { Check(ct); return source; }
        catch
        {
            try { source?.Dispose(); }
            catch (Exception ex) { AppLog.Warn("Could not close comparison content after admission stopped", ex); }
            throw;
        }
    }

    public async Task<bool?> ContentEqualAsync(ItemRef left, ItemRef right, CancellationToken ct)
    {
        var lp = providers.For(left.Parent);
        var rp = providers.For(right.Parent);
        IContentSource? a = null, b = null;
        Exception? originalFailure = null;
        try
        {
            a = await Open(lp, left, ct).ConfigureAwait(false);
            if (a is null) return null;
            b = await Open(rp, right, ct).ConfigureAwait(false);
            if (b is null) return null;
            return await DirectoryCompare.ContentEqualAsync(() => Run(lp, left.Parent, ct, () => DirectoryCompare.ReadState(a, ct)),
                () => Run(rp, right.Parent, ct, () => DirectoryCompare.ReadState(b, ct)),
                (offset, buffer, start) => Run(lp, left.Parent, ct, () => a.Read(offset, buffer.AsSpan(start))),
                (offset, buffer, start) => Run(rp, right.Parent, ct, () => b.Read(offset, buffer.AsSpan(start))), ct).ConfigureAwait(false);
        }
        catch (Exception ex) { originalFailure = ex; throw; }
        finally
        {
            Exception? closeFailure = null;
            void Close(IContentSource? source)
            {
                try { source?.Dispose(); }
                catch (Exception ex)
                {
                    // Retire both owners, but keep the operation error (or the first standalone close error).
                    if (originalFailure is null && closeFailure is null) closeFailure = ex;
                    else AppLog.Warn("Could not close comparison content after an earlier failure", ex);
                }
            }
            Close(b);
            Close(a);
            if (closeFailure is not null) ExceptionDispatchInfo.Capture(closeFailure).Throw();
        }
    }

    public Task<bool> EnumerateAsync(ResourceProvider provider, Location location, IEnumerationSink sink, CancellationToken ct)
        => Run(provider, location, ct, () => { provider.EnumerateAsync(location, sink, ct).GetAwaiter().GetResult(); return true; });

    public Task<FolderContents?> ReadFolderContentsAsync(Location inside, CancellationToken ct)
        => Run(providers.For(inside), inside, ct, () => FolderContents.Read(inside.Path, ct));
}
