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
    private const int ExportOffset = HashOffset + HashLength, ReplyLength = 4 * sizeof(int);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);

    internal readonly record struct Result(StorageEfsKeyState State, int NativeError, WindowsEfsKeys.BackupState? Backup, int BackupError);
    private static string Executable => Path.Join(AppContext.BaseDirectory, "FileCat.ShellHost.exe");
    internal static bool Available => File.Exists(Executable);

    internal static Result? Request(string sid, string hash, nint owner, bool export, CancellationToken ct)
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
            Span<byte> request = stackalloc byte[ExportOffset + 1];
            request.Clear();
            BinaryPrimitives.WriteInt64LittleEndian(request, owner.ToInt64());
            request[sizeof(long)] = checked((byte)Encoding.ASCII.GetBytes(sid.AsSpan(), request[(sizeof(long) + 1)..HashOffset]));
            Encoding.ASCII.GetBytes(hash.AsSpan(), request[HashOffset..ExportOffset]);
            request[ExportOffset] = export ? (byte)1 : (byte)0;
            ct.ThrowIfCancellationRequested();
            pipe.Write(request);
            byte[] reply = new byte[ReplyLength];
            // An admitted export can create a PFX even if the job is stopped. Retain ownership until Windows reports the result.
            var wait = export ? CancellationToken.None : ct;
            pipe.ReadExactlyAsync(reply, wait).AsTask().GetAwaiter().GetResult();
            if (!worker.WaitForExit((int)ConnectTimeout.TotalMilliseconds) || worker.ExitCode != 0) return null;
            wait.ThrowIfCancellationRequested();
            var state = (StorageEfsKeyState)BinaryPrimitives.ReadInt32LittleEndian(reply);
            int error = BinaryPrimitives.ReadInt32LittleEndian(reply.AsSpan(sizeof(int)));
            int backup = BinaryPrimitives.ReadInt32LittleEndian(reply.AsSpan(2 * sizeof(int)));
            int backupError = BinaryPrimitives.ReadInt32LittleEndian(reply.AsSpan(3 * sizeof(int)));
            if (!Enum.IsDefined(state) || (backup != -1 && (!export || !Enum.IsDefined((WindowsEfsKeys.BackupState)backup)))) return null;
            return new(state, error, backup == -1 ? null : (WindowsEfsKeys.BackupState)backup, backupError);
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
            Span<byte> request = stackalloc byte[ExportOffset + 1];
            pipe.ReadExactly(request);
            int sidLength = request[sizeof(long)];
            if (sidLength == 0 || request[ExportOffset] > 1) return 2;
            string sid = Encoding.ASCII.GetString(request.Slice(sizeof(long) + 1, sidLength));
            string hash = Encoding.ASCII.GetString(request[HashOffset..ExportOffset]);
            var owner = (nint)BinaryPrimitives.ReadInt64LittleEndian(request);
            using var native = StorageReviewResources.EnterNative(CancellationToken.None);
            var result = new WindowsEfsKeys(qualifyNativeArm64: true)
                .InspectCapiInWorker(sid, hash, owner, parentId, export: request[ExportOffset] == 1, CancellationToken.None);
            if (parent.HasExited) return 2;
            Span<byte> reply = stackalloc byte[ReplyLength];
            BinaryPrimitives.WriteInt32LittleEndian(reply, (int)result.State);
            BinaryPrimitives.WriteInt32LittleEndian(reply[sizeof(int)..], result.NativeError);
            BinaryPrimitives.WriteInt32LittleEndian(reply[(2 * sizeof(int))..], (int?)result.Backup ?? -1);
            BinaryPrimitives.WriteInt32LittleEndian(reply[(3 * sizeof(int))..], result.BackupError);
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
