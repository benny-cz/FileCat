using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>
/// Queries the Cloud Files membership of the exact held object. Some Windows builds retain a process handle on
/// each unsuccessful lookup. The application runs the query in a short-lived child so the OS reclaims those handles.
/// </summary>
internal static partial class WindowsStorageCloudQuery
{
    internal const string WorkerArgument = "--storage-cloud-query";
    private const int RequestLimit = 256;
    private const int Unavailable = unchecked((int)0x80004005);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);
    private static Worker? _active;

    internal static IDisposable UseWorker(string executable, string? managedEntry = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true,
        };
        if (managedEntry is not null) start.ArgumentList.Add(managedEntry);
        start.ArgumentList.Add(WorkerArgument);
        var worker = new Worker(start);
        if (Interlocked.CompareExchange(ref _active, worker, null) is not null)
            throw new InvalidOperationException("A Cloud Files query worker is already installed.");
        return worker;
    }

    internal static int Query(SafeFileHandle file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Standalone native probes can exercise the API directly. FileCat configures isolation before opening its UI.
        return Volatile.Read(ref _active) is { } worker ? worker.Query(file, ct) : QueryNative(file);
    }

    internal static int RunWorker()
    {
        if (!OperatingSystem.IsWindows()) return 1;
        using var input = new BinaryReader(Console.OpenStandardInput());
        using var output = new BinaryWriter(Console.OpenStandardOutput());
        for (int i = 0; i < RequestLimit; i++)
        {
            long raw;
            try { raw = input.ReadInt64(); }
            catch (EndOfStreamException) { return 0; }
            using var file = new SafeFileHandle((nint)raw, ownsHandle: true);
            if (file.IsInvalid) return 1;
            output.Write(QueryNative(file));
            output.Flush();
        }
        return 0;
    }

    private static int QueryNative(SafeFileHandle file)
    {
        try { return CfGetSyncRootInfoByHandle(file, 0 /* CF_SYNC_ROOT_INFO_BASIC */, out _, sizeof(long), out _); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return Unavailable; }
    }

    private sealed class Worker(ProcessStartInfo start) : IDisposable
    {
        private readonly SemaphoreSlim _serial = new(1);
        private Process? _child;
        private Stream? _input, _output;
        private int _requests;
        private bool _unavailable;

        internal int Query(SafeFileHandle file, CancellationToken ct)
        {
            _serial.Wait(ct);
            try
            {
                if (_unavailable) return Unavailable;
                if (_child is null)
                {
                    _child = Process.Start(start) ?? throw new IOException("Windows did not start the Cloud Files query worker.");
                    _input = _child.StandardInput.BaseStream;
                    _output = _child.StandardOutput.BaseStream;
                }
                // The child receives only READ_ATTRIBUTES access. The handle names the held file even if a name changes.
                if (!DuplicateHandle(-1, file, _child.SafeHandle, out nint duplicate, NtFile.ReadAttributes, false, 0))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                Span<byte> request = stackalloc byte[sizeof(long)];
                BinaryPrimitives.WriteInt64LittleEndian(request, duplicate.ToInt64());
                _input!.Write(request);
                _input.Flush();
                byte[] reply = new byte[sizeof(int)];
                _output!.ReadExactlyAsync(reply, ct).AsTask().WaitAsync(ReplyTimeout, ct).GetAwaiter().GetResult();
                if (++_requests == RequestLimit) Retire();
                ct.ThrowIfCancellationRequested();
                return BinaryPrimitives.ReadInt32LittleEndian(reply);
            }
            catch (OperationCanceledException)
            {
                TryRetire();
                throw;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
            {
                TryRetire();
                return Unavailable;
            }
            finally { _serial.Release(); }
        }

        private void Retire()
        {
            if (_child is not { } child) return;
            _input?.Dispose(); // EOF also retires an idle worker when FileCat exits.
            if (!child.WaitForExit(250))
            {
                child.Kill();
                if (!child.WaitForExit(1000)) throw new IOException("The Cloud Files query worker did not exit.");
            }
            _output?.Dispose();
            child.Dispose();
            _child = null;
            _input = _output = null;
            _requests = 0;
        }

        private void TryRetire()
        {
            try { Retire(); }
            catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
            {
                // Keep the failed child owned and refuse subsequent queries until disposal can retire it.
                _unavailable = true;
            }
        }

        public void Dispose()
        {
            _serial.Wait();
            try
            {
                _unavailable = true;
                TryRetire();
                Interlocked.CompareExchange(ref _active, null, this);
            }
            finally { _serial.Release(); }
        }
    }

    [LibraryImport("cldapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CfGetSyncRootInfoByHandle(SafeFileHandle file, int infoClass, out long rootId, uint length, out uint returned);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateHandle(nint process, SafeFileHandle file, SafeProcessHandle target, out nint duplicate, uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
}
