using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal static partial class WindowsEfsCapiWorker
{
    internal const string Argument = "--efs-capi-key-access";
    private const string PipePrefix = "FileCat.EfsCapi.";
    private const int HashLength = 40, SidLimit = byte.MaxValue, HashOffset = sizeof(long) + 1 + SidLimit;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);

    internal readonly record struct Result(StorageEfsKeyState State, int NativeError);
    private static string Executable => Path.Join(AppContext.BaseDirectory, "FileCat.ShellHost.exe");
    internal static bool Available => File.Exists(Executable);

    internal static Result? Compare(string sid, string hash, nint owner, CancellationToken ct)
    {
        using var pipeSlot = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        using var processSlot = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        string name = PipePrefix + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add(Argument);
        start.ArgumentList.Add(name);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        // Use the exact executable in the application directory, including in native fixture runs.
        Process? worker = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            worker = Process.Start(start) ?? throw new IOException("Windows did not start the CryptoAPI child.");
            pipe.WaitForConnectionAsync(ct).WaitAsync(ConnectTimeout, ct).GetAwaiter().GetResult();
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint peer) || peer != (uint)worker.Id || worker.HasExited)
                return null;
            Span<byte> request = stackalloc byte[HashOffset + HashLength];
            request.Clear();
            BinaryPrimitives.WriteInt64LittleEndian(request, owner.ToInt64());
            request[sizeof(long)] = checked((byte)Encoding.ASCII.GetBytes(sid.AsSpan(), request[(sizeof(long) + 1)..HashOffset]));
            Encoding.ASCII.GetBytes(hash.AsSpan(), request[HashOffset..]);
            ct.ThrowIfCancellationRequested();
            pipe.Write(request);
            byte[] reply = new byte[2 * sizeof(int)];
            pipe.ReadExactlyAsync(reply, ct).AsTask().GetAwaiter().GetResult();
            if (!worker.WaitForExit((int)ConnectTimeout.TotalMilliseconds) || worker.ExitCode != 0) return null;
            ct.ThrowIfCancellationRequested();
            return new((StorageEfsKeyState)BinaryPrimitives.ReadInt32LittleEndian(reply), BinaryPrimitives.ReadInt32LittleEndian(reply.AsSpan(sizeof(int))));
        }
        catch (Exception error) when (error is IOException or TimeoutException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { return null; }
        finally
        {
            if (worker is not null)
            {
                try
                {
                    if (!worker.HasExited)
                    {
                        worker.Kill();
                        if (!worker.WaitForExit((int)ConnectTimeout.TotalMilliseconds))
                            throw new IOException("Windows did not retire the CryptoAPI child.");
                    }
                }
                finally { worker.Dispose(); }
            }
        }
    }

    internal static int Run(string name, string parentArgument)
    {
        if (!WindowsStorageTransforms.SupportsHost(qualifyNativeArm64: true) ||
            !name.StartsWith(PipePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(name.AsSpan(PipePrefix.Length), "N", out _) ||
            !int.TryParse(parentArgument, NumberStyles.None, CultureInfo.InvariantCulture, out int parentId) ||
            parentId <= 0 || parentId == Environment.ProcessId) return 2;
        try
        {
            using var parent = Process.GetProcessById(parentId);
            using var self = Process.GetCurrentProcess();
            _ = parent.SafeHandle; // Keep the verified process object open for this one request.
            if (parent.HasExited || parent.SessionId != self.SessionId) return 2;
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint peer) || peer != (uint)parentId || parent.HasExited)
                return 2;
            parent.Exited += static (_, _) => Environment.Exit(2);
            parent.EnableRaisingEvents = true;
            Span<byte> request = stackalloc byte[HashOffset + HashLength];
            pipe.ReadExactly(request);
            int sidLength = request[sizeof(long)];
            if (sidLength == 0) return 2;
            string sid = Encoding.ASCII.GetString(request.Slice(sizeof(long) + 1, sidLength));
            string hash = Encoding.ASCII.GetString(request[HashOffset..]);
            var owner = (nint)BinaryPrimitives.ReadInt64LittleEndian(request);
            using var native = StorageReviewResources.EnterNative(CancellationToken.None);
            var evidence = new WindowsEfsKeys(qualifyNativeArm64: true).InspectCapiInWorker(sid, hash, owner, parentId, CancellationToken.None);
            if (parent.HasExited) return 2;
            Span<byte> reply = stackalloc byte[2 * sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(reply, (int)evidence.State);
            BinaryPrimitives.WriteInt32LittleEndian(reply[sizeof(int)..], evidence.NativeError ?? 0);
            pipe.Write(reply);
            return 0;
        }
        catch (Exception error) when (error is IOException or TimeoutException or Win32Exception or InvalidOperationException or
            UnauthorizedAccessException or ArgumentException) { return 2; }
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
