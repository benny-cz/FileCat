using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

namespace FileCat.Core.Analysis;

/// <summary>
/// Ordinary-user, cross-session capacity tokens for a trusted private profile ID. This is runtime
/// ownership, never inventory data. A slot protects capacity only, so abandoned ownership is reusable.
/// </summary>
public sealed class AnalysisProfileQuota : IDisposable
{
    public const long QuantumBytes = 256L * 1024 * 1024;
    private const int Slots = (int)(AnalysisLimits.ProfileScratchBytes / QuantumBytes);
    private static int s_workers;
    private readonly object _gate = new();
    private readonly BlockingCollection<Action> _commands = new(1);
    private readonly Thread _thread;
    private readonly string _name;
    private readonly Mutex?[] _held = new Mutex?[Slots];
    private readonly IDisposable?[] _handles = new IDisposable?[Slots];
    private AnalysisUnixProfileCapacity? _unix;
    private long _bytes;
    private int _references = 1;
    private bool _disposed, _ownerClosed;
    private Exception? _failure;

    // Construct only from application-owned private profile state. Parsed SessionIdentity data must
    // not pick an IPC namespace; importers have no reason to call this method.
    public static AnalysisProfileQuota Open(Guid privateProfileId)
    {
        if (privateProfileId == Guid.Empty) throw new ArgumentException("A private profile ID is required.", nameof(privateProfileId));
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        return new(privateProfileId);
    }
    private AnalysisProfileQuota(Guid profile)
    {
        // No paths, host identities, queries or source names enter a public kernel-object name.
        _name = "FileCat.AnalysisQuota.v1." + Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(profile.ToString("N"))));
        int workers = Interlocked.Increment(ref s_workers);
        if (workers > AnalysisLimits.Sessions)
        { Interlocked.Decrement(ref s_workers); _commands.Dispose(); throw new IOException("Eight profile quota owners are still active. Wait for an analysis to close."); }
        try
        {
            _thread = new Thread(Work) { IsBackground = true, Name = "FileCat analysis quota" };
            _thread.Start();
        }
        catch { Interlocked.Decrement(ref s_workers); _commands.Dispose(); throw; }
    }
    internal long ReservedBytes { get { lock (_gate) return _bytes; } }
    internal long CapacityBytes { get { lock (_gate) return (_unix?.Units ?? _held.Count(item => item is not null)) * QuantumBytes; } }
    internal IDisposable Borrow()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_ownerClosed, this);
            if (_references >= AnalysisLimits.Sessions + 1) throw new IOException("The profile quota retention limit was reached.");
            var retained = new Retention(this); _references++; return retained;
        }
    }
    internal bool TryReserve(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (bytes > AnalysisLimits.ProfileScratchBytes - _bytes) return false;
            long next = _bytes + bytes;
            bool acquired;
            try { acquired = Invoke(() => Grow(Units(next))); }
            catch (Exception error) when (error is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException or PlatformNotSupportedException)
            {
                // Kernel object names contain a private profile digest. Surface a finite operational
                // reason without copying runtime messages/names into UI or default diagnostics.
                throw new IOException("Private profile capacity coordination is unavailable.", error.HResult);
            }
            if (acquired) _bytes = next;
            return acquired;
        }
    }
    internal void Release(long bytes)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (bytes < 0 || bytes > _bytes) throw new InvalidOperationException("The capacity reservation does not belong to this profile owner.");
            long next = _bytes - bytes;
            Invoke(() => { Shrink(Units(next)); return true; }); _bytes = next;
        }
    }
    private static int Units(long bytes) => checked((int)((bytes + QuantumBytes - 1) / QuantumBytes));
    private bool Grow(int required)
    {
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        if (_failure is not null) throw new IOException("Profile capacity ownership could not be verified.", _failure);
        if (!OperatingSystem.IsWindows())
        { _unix ??= new(_name); return _unix.SetUnits(required); }
        int current = _held.Count(item => item is not null);
        if (required <= current) return true;
        var acquired = new List<int>(required - current);
        try
        {
            for (int slot = 0; slot < Slots && current + acquired.Count < required; slot++)
            {
                if (_held[slot] is not null) continue;
                IDisposable? reservation = AnalysisNativeHandles.Acquire(); Mutex? mutex = null; bool owned = false;
                try
                {
                    // Both choices are explicit: never accept the cross-user Unix namespace or a
                    // shell/RDP-session-specific ceiling. The runtime verifies existing object ACLs.
                    mutex = new Mutex(false, _name + "." + slot, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false });
                    try { owned = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { owned = true; } // No protected mutable record to recover.
                    if (!owned) continue;
                    _held[slot] = mutex; _handles[slot] = reservation; acquired.Add(slot); mutex = null; reservation = null;
                }
                finally { mutex?.Dispose(); reservation?.Dispose(); }
            }
            if (current + acquired.Count == required) return true;
        }
        catch
        {
            foreach (int slot in acquired) { try { Retire(slot); } catch (Exception error) { _failure ??= error; } }
            throw;
        }
        foreach (int slot in acquired) Retire(slot);
        return false;
    }
    private void Shrink(int required)
    {
        if (!OperatingSystem.IsWindows())
        { if (_unix is not null && !_unix.SetUnits(required)) throw new IOException("Profile capacity release was refused."); return; }
        int current = _held.Count(item => item is not null);
        Exception? error = null;
        for (int slot = Slots - 1; slot >= 0 && current > required; slot--)
            if (_held[slot] is not null)
            { try { Retire(slot); } catch (Exception failure) { _failure ??= failure; error ??= failure; } finally { current--; } }
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private void Retire(int slot)
    {
        var mutex = _held[slot]!;
        try { mutex.ReleaseMutex(); }
        finally { try { mutex.Dispose(); } finally { _held[slot] = null; _handles[slot]?.Dispose(); _handles[slot] = null; } }
    }
    private T Invoke<T>(Func<T> work)
    {
        using var done = new ManualResetEventSlim(); T? result = default; Exception? error = null;
        _commands.Add(() => { try { result = work(); } catch (Exception failure) { error = failure; } finally { done.Set(); } });
        done.Wait();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        return result!;
    }
    private void Work()
    {
        try { foreach (var command in _commands.GetConsumingEnumerable()) command(); }
        finally { Interlocked.Decrement(ref s_workers); }
    }
    public void Dispose()
    {
        lock (_gate) { if (_ownerClosed) return; _ownerClosed = true; }
        ReleaseOwner();
    }
    private void ReleaseOwner()
    {
        Exception? error = null;
        lock (_gate)
        {
            if (--_references != 0) return;
            try { Invoke(() => { try { Shrink(0); } finally { _unix?.Dispose(); _unix = null; } return true; }); }
            catch (Exception failure) { error = failure; }
            finally { _disposed = true; _bytes = 0; _commands.CompleteAdding(); }
        }
        _thread.Join(); _commands.Dispose();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private sealed class Retention(AnalysisProfileQuota owner) : IDisposable
    {
        private AnalysisProfileQuota? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseOwner();
    }
}
