using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace FileCat.App.Services;

/// <summary>
/// A decoder launched by FileCat ends when its dedicated parent-owned pipe closes. This covers the decoder
/// process itself, including while encoded stdin remains open; it is not descendant or permission containment.
/// </summary>
internal sealed partial class PictureWorkerLifetime : IDisposable
{
    internal const string EnvironmentName = "FILECAT_PICTURE_PARENT_PIPE";
    private readonly Stream _input;
    private readonly Action _endProcess;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state;
    internal Task Completion => _stopped.Task;

    internal PictureWorkerLifetime(Stream input, Action endProcess)
    {
        _input = input;
        _endProcess = endProcess;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ready.SetResult();
            Observe();
        }) { IsBackground = true, Name = "FileCat picture parent lifetime" };
        try
        {
            thread.Start();
            // The Linux all-thread process policy must include this thread before any encoded input is read.
            ready.Task.GetAwaiter().GetResult();
        }
        catch
        {
            _input.Dispose();
            throw;
        }
    }

    internal static PictureWorkerLifetime? Start()
    {
        if (OperatingSystem.IsWindows()) return null;
        string? handle = Environment.GetEnvironmentVariable(EnvironmentName);
        Environment.SetEnvironmentVariable(EnvironmentName, null);
        // Direct worker invocations keep their existing standalone protocol; FileCat's launcher always supplies it.
        if (handle is null) return null;
        return new PictureWorkerLifetime(Open(handle), () => ExitImmediately(3));
    }

    internal static Stream Open(string handle)
    {
        if (!int.TryParse(handle, NumberStyles.None, CultureInfo.InvariantCulture, out int descriptor) || descriptor < 0)
            throw new IOException("The picture decoder parent lifetime pipe is invalid.");
        try { return new AnonymousPipeClientStream(PipeDirection.In, handle); }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            throw new IOException("The picture decoder parent lifetime pipe could not be opened.", ex);
        }
    }

    private void Observe()
    {
        try
        {
            try { _input.ReadByte(); } // The parent writes no data. EOF, unexpected data or a read error ends this worker.
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) _endProcess();
        }
        finally { _input.Dispose(); _stopped.TrySetResult(); }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _state, 1);
        // Closing a synchronous Unix pipe while its reader is blocked can wait for that read. Let the reader
        // release its own handle after EOF; normal worker process exit closes it without waiting for the parent.
    }

    [LibraryImport("libc", EntryPoint = "_exit")]
    private static partial void ExitImmediately(int status);
}
