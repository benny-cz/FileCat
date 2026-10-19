namespace FileCat.App.Controls;

internal sealed partial class LinuxPageEngine
{
    private async Task<PageFindResult?> FindCoreAsync(string text, bool matchCase, bool forward, CancellationToken ct)
    {
        await _findSerial.WaitAsync(ct);
        var done = new TaskCompletionSource<PageFindResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await OnGtk(() => BeginFindOnGtk(text, matchCase, forward, done));
            using var cancel = ct.Register(() => done.TrySetCanceled(ct));
            return await done.Task;
        }
        finally
        {
            if (done.Task.IsCanceled) await OnGtk(() => CancelFindOnGtk(done));
            _findSerial.Release();
        }
    }
}
