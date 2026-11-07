using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FileCat.Core.Analysis;

/// <summary>
/// Kernel-only capacity. SEM_UNDO owns both capacity and registrations through actual process exit;
/// no runtime-directory, source name, PID cache or filesystem lock file chooses the namespace.
/// </summary>
internal sealed unsafe partial class AnalysisUnixProfileCapacity : IDisposable
{
    private const int Count = 4, Available = 1, Initialized = 2, Users = 3, Marker = 0x4643, Capacity = 32;
    private const short Nowait = 0x800, Undo = 0x1000;
    private readonly bool _mac = OperatingSystem.IsMacOS();
    private readonly uint _owner;
    private readonly IDisposable _native;
    private int _id = -1, _units;
    private bool _registered, _disposed;
    private Exception? _failure;
    internal int Units => _units;
    internal AnalysisUnixProfileCapacity(string domain)
    {
        if ((!_mac && !OperatingSystem.IsLinux()) || IntPtr.Size != 8) throw new PlatformNotSupportedException("No qualified profile capacity ABI is available.");
        _owner = GetEuid(); _native = AnalysisNativeHandles.Acquire();
        byte[] digest = SHA256.HashData(Encoding.ASCII.GetBytes(domain + ".uid=" + _owner.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        int key = BinaryPrimitives.ReadInt32LittleEndian(digest) & int.MaxValue; if (key == 0) key = 1;
        bool locked = false, created = false, verified = false;
        try
        {
            _id = SemGet(key, Count, 0x200 | 0x400 | 0x180); // create-exclusive, owner read/write only
            created = _id >= 0;
            if (_id < 0 && Marshal.GetLastPInvokeError() == 17) _id = SemGet(key, Count, 0x180);
            if (_id < 0) throw Error("The private profile capacity set could not be opened.");
            Verify(); verified = true;
            // A key collision or an abandoned, unpublished initializer is unavailable, not
            // permission to initialize/delete another program's blank semaphore record.
            var initial = State();
            if (!created && (initial.Marker != Marker || initial.Available > Capacity || initial.Users == 0 && initial.Available != Capacity))
                throw new IOException("The profile capacity namespace is uninitialized, inconsistent or belongs to another protocol.");
            Lock(); locked = true;
            int marker = Value(Initialized);
            if (marker == 0)
            {
                if (Value(Available) != 0 || Value(Users) != 0) throw Invalid();
                Operate([new(Available, Capacity, Nowait), new(Initialized, Marker, Nowait)]);
            }
            else if (marker != Marker) throw Invalid();
            if (Value(Users) >= 1024) throw new IOException("The profile capacity registration limit was reached.");
            Operate([new(Users, 1, Nowait | Undo)]); _registered = true;
            Unlock(); locked = false;
        }
        catch (Exception operation)
        {
            if (AnalysisCloseUnconfirmedException.IsPresent(operation) || created && !verified)
            { RetainFailure(operation); throw AnalysisCloseUnconfirmedException.From(operation); }
            try
            {
                if (!locked && created) { Lock(); locked = true; }
                if (locked)
                {
                    if (_registered) { Operate([new(Users, -1, Nowait | Undo)]); _registered = false; }
                    if ((created || Value(Initialized) == Marker) && Value(Users) == 0)
                    { if (Control(0, 0, 0) < 0) throw Error("Private profile capacity cleanup failed."); locked = false; }
                    if (locked) Unlock();
                }
            }
            catch (Exception cleanup)
            {
                RetainFailure(cleanup);
                throw AnalysisCloseUnconfirmedException.From(new AggregateException(operation, cleanup));
            }
            _native.Dispose();
            throw;
        }
    }
    internal bool SetUnits(int required)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (required is < 0 or > Capacity) throw new ArgumentOutOfRangeException(nameof(required));
        if (_failure is { } prior) throw AnalysisCloseUnconfirmedException.From(prior);
        try
        {
            Verify();
            var state = State();
            if (state.Marker != Marker || state.Users < 1 || state.Available > Capacity) throw Invalid();
            short delta = checked((short)(_units - required));
            if (delta != 0 && !TryOperate([new(Available, delta, Nowait | Undo)])) return false;
            _units = required; return true;
        }
        catch (Exception error) { RetainFailure(error); throw AnalysisCloseUnconfirmedException.From(error); }
    }

    private void Verify()
    {
        Span<byte> metadata = stackalloc byte[512]; metadata.Clear();
        fixed (byte* address = metadata)
            if (Control(0, 2, (nint)address) < 0) throw Error("Profile capacity ownership could not be verified.");
        int offset = _mac ? 0 : 4;
        uint uid = BinaryPrimitives.ReadUInt32LittleEndian(metadata[offset..]);
        uint creator = BinaryPrimitives.ReadUInt32LittleEndian(metadata[(offset + 8)..]);
        uint mode = _mac ? BinaryPrimitives.ReadUInt16LittleEndian(metadata[16..]) : BinaryPrimitives.ReadUInt32LittleEndian(metadata[20..]);
        if (uid != _owner || creator != _owner || (mode & 0x1ff) != 0x180) throw new IOException("Profile capacity is not private to the ordinary owner.");
        // Reject a planted/colliding semaphore set with a different record shape.
        if (Control(Count, _mac ? 5 : 12, 0) != -1 || Marshal.GetLastPInvokeError() != 22) throw Invalid();
    }
    private int Value(int number)
    { int value = Control(number, _mac ? 5 : 12, 0); return value >= 0 ? value : throw Error("Profile capacity state is unavailable."); }
    private (int Available, int Marker, int Users) State()
    {
        ushort* values = stackalloc ushort[Count];
        if (Control(0, _mac ? 6 : 13, (nint)values) < 0) throw Error("Profile capacity state could not be captured atomically.");
        return (values[Available], values[Initialized], values[Users]);
    }
    private void Lock()
    {
        for (int attempt = 0; attempt < 128; attempt++)
        {
            if (TryOperate([new(0, 0, Nowait), new(0, 1, Nowait | Undo)])) return;
            Thread.Yield();
        }
        throw new IOException("Another process is updating profile capacity. Wait before starting new scratch work.");
    }
    private void Unlock()
    {
        try { Operate([new(0, -1, Nowait | Undo)]); }
        catch (Exception error) { throw AnalysisCloseUnconfirmedException.From(error); }
    }
    private void Operate(ReadOnlySpan<Operation> operations)
    { if (!TryOperate(operations)) throw new IOException("The private profile capacity update could not complete immediately."); }
    private bool TryOperate(ReadOnlySpan<Operation> operations)
    {
        for (int retry = 0; retry < 8; retry++)
        {
            fixed (Operation* address = operations)
                if (SemOp(_id, address, (nuint)operations.Length) == 0) return true;
            int error = Marshal.GetLastPInvokeError();
            if (error == (_mac ? 35 : 11)) return false;
            if (error != 4) throw Error("The private profile capacity update failed.");
        }
        throw new IOException("Profile capacity updates were repeatedly interrupted.");
    }
    private void RetainFailure(Exception error) { _failure ??= error; AnalysisResourceQuarantine.Shared.Hold(this); }
    public void Dispose()
    {
        if (_disposed) { if (_failure is { } prior) throw AnalysisCloseUnconfirmedException.From(prior); return; }
        _disposed = true;
        if (_failure is { } known) throw AnalysisCloseUnconfirmedException.From(known);
        try
        {
            Verify(); Lock();
            if (_units == 0) Operate([new(Users, -1, Nowait | Undo)]);
            else Operate([new(Available, checked((short)_units), Nowait | Undo), new(Users, -1, Nowait | Undo)]);
            if (Value(Users) == 0)
            {
                if (Value(Available) != Capacity || Control(0, 0, 0) < 0) throw Error("The idle private profile capacity set could not be retired.");
            }
            else Unlock();
            // Commit local accounting only after the entire retirement is confirmed.
            _units = 0; _registered = false; _native.Dispose();
        }
        catch (Exception error)
        {
            // SEM_UNDO, including any held protocol lock, belongs to actual process exit.
            // Never return the native-owner slot or retry an uncertain protocol update.
            RetainFailure(error); throw AnalysisCloseUnconfirmedException.From(error);
        }
    }
    private int Control(int number, int command, nint argument) => _mac && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? ControlMacArm64(_id, number, command, 0, 0, 0, 0, 0, argument)
        : _mac ? ControlMac(_id, number, command, argument) : ControlUnix(_id, number, command, argument);
    private static IOException Error(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));
    private static IOException Invalid() => new("The private profile capacity state is inconsistent.");
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private readonly struct Operation(ushort number, short delta, short flags)
    { internal readonly ushort Number = number; internal readonly short Delta = delta, Flags = flags; }
    [LibraryImport("libc", EntryPoint = "geteuid")] private static partial uint GetEuid();
    [LibraryImport("libc", EntryPoint = "semget", SetLastError = true)] private static partial int SemGet(int key, int count, int flags);
    [LibraryImport("libc", EntryPoint = "semop", SetLastError = true)] private static partial int SemOp(int id, Operation* operations, nuint count);
    [LibraryImport("libc", EntryPoint = "semctl", SetLastError = true)] private static partial int ControlUnix(int id, int number, int command, nint argument);
    [LibraryImport("libc", EntryPoint = "semctl$UNIX2003", SetLastError = true)] private static partial int ControlMac(int id, int number, int command, nint argument);
    // Apple arm64 variadic arguments start on the stack, matching the anchored openat adapter.
    [LibraryImport("libc", EntryPoint = "semctl", SetLastError = true)]
    private static partial int ControlMacArm64(int id, int number, int command, int unused3, int unused4, int unused5, int unused6, int unused7, nint argument);
}
