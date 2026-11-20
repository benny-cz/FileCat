using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>Held-stream ABI only. Admission, eligibility, ownership and durable intent belong to the storage worker.</summary>
internal static unsafe partial class WindowsCompression
{
    // Windows SDK 10.0.22000.0 ntifs.h: FILE_DEVICE_FILE_SYSTEM, functions 15/16, METHOD_BUFFERED;
    // GET is FILE_ANY_ACCESS, SET requires FILE_READ_DATA | FILE_WRITE_DATA (the directory aliases are 1 | 2).
    private const uint GetCompression = 0x9003C, SetCompression = 0x9C040;
    internal const ushort None = 0, Default = 1, Lznt1 = 2;
    internal readonly record struct Format(ushort? Native, int Error)
    {
        internal StorageObservationState State => Native switch
        {
            None => StorageObservationState.Disabled,
            Lznt1 => StorageObservationState.Enabled,
            _ => StorageObservationState.Unknown,
        };
    }
    internal sealed record CallResult(bool NativeSucceeded, int NativeError, Format After);

    internal static Format Query(SafeFileHandle file)
    {
        ushort format = ushort.MaxValue;
        bool ok = Control(file, GetCompression, null, 0, &format, sizeof(ushort), out uint returned, 0);
        if (!ok) return new(null, Marshal.GetLastPInvokeError());
        return returned == sizeof(ushort) ? new(format, 0) : new(null, 13 /* ERROR_INVALID_DATA */);
    }

    /// <summary>No cancellation boundary may occur after durable intent/call admission and before this SET.
    /// Always observes the same held stream after return, including native failure. Success alone is not the outcome.</summary>
    internal static CallResult InvokeAdmitted(SafeFileHandle file, StorageAction action, Action<bool>? setReturnedForQualification = null,
        Action? setPendingForQualification = null)
    {
        ushort desired = action switch
        {
            StorageAction.Compress => Default,
            StorageAction.RemoveCompression => None,
            _ => throw new ArgumentOutOfRangeException(nameof(action), "This primitive only changes classic NTFS compression."),
        };
        // The opt-in device-loss fixture signals from another thread only if this synchronous SET has not returned
        // after a short delay. The controller must still observe an interrupted native result before claiming loss
        // during the call; this marker alone is not evidence that the thread entered the kernel.
        int returned = 0;
        Task? pending = setPendingForQualification is null ? null : Task.Run(() =>
        {
            Thread.Sleep(250);
            if (Volatile.Read(ref returned) == 0) setPendingForQualification();
        });
        bool ok;
        int error;
        try
        {
            ok = Control(file, SetCompression, &desired, sizeof(ushort), null, 0, out _, 0);
            error = ok ? 0 : Marshal.GetLastPInvokeError();
        }
        finally { Volatile.Write(ref returned, 1); }
        pending?.GetAwaiter().GetResult();
        setReturnedForQualification?.Invoke(ok);
        return new(ok, error, Query(file));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Control(SafeFileHandle file, uint code, void* input, uint inputBytes, void* output,
        uint outputBytes, out uint returned, nint overlapped);
}
