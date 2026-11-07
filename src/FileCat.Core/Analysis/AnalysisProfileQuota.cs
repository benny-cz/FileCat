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
    private readonly AnalysisNativeMutex?[] _held = new AnalysisNativeMutex?[Slots];
    private readonly Exception?[] _retireFailures = new Exception?[Slots];
    private readonly Func<string, AnalysisNativeMutex> _mutexFactory;
    private AnalysisUnixProfileCapacity? _unix;
    private readonly AnalysisPrivateDirectory? _directory;
    private readonly Guid _profile;
    private AnalysisFileProfileCapacity? _files;
    private long _bytes;
    private int _references = 1;
    private bool _disposed, _ownerClosed;
    private Exception? _failure;

    // Construct only from application-owned private profile state. Parsed SessionIdentity data must
    // not pick an IPC namespace. Import storage must use the destination application's
    // prepared private profile, never the imported inventory's historical profile.
    internal static AnalysisProfileQuota Open(Guid privateProfileId)
    {
        if (privateProfileId == Guid.Empty) throw new ArgumentException("A private profile ID is required.", nameof(privateProfileId));
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        return new(privateProfileId, null);
    }
    /// <summary>Owns the trusted native profile directory on success. Its immutable capacity files coordinate across IPC namespaces.</summary>
    public static AnalysisProfileQuota Open(Guid privateProfileId, AnalysisPrivateDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (privateProfileId == Guid.Empty) throw new ArgumentException("A private profile ID is required.", nameof(privateProfileId));
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        return new(privateProfileId, directory);
    }
    internal static AnalysisProfileQuota Open(Guid profile, Func<string, AnalysisNativeMutex> mutexFactory)
    {
        if (profile == Guid.Empty) throw new ArgumentException("A private profile ID is required.", nameof(profile));
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        return new(profile, null, mutexFactory);
    }
    private AnalysisProfileQuota(Guid profile, AnalysisPrivateDirectory? directory, Func<string, AnalysisNativeMutex>? mutexFactory = null)
    {
        _profile = profile; _directory = directory; _mutexFactory = mutexFactory ?? (name => new(name));
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
    internal bool WorkerAlive => _thread.IsAlive;
    internal static int ActiveWorkers => Volatile.Read(ref s_workers);
    internal long CapacityBytes { get { lock (_gate) return (_files?.Units ?? _unix?.Units ?? _held.Count(item => item is not null)) * QuantumBytes; } }
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
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error))
            {
                _failure ??= error;
                throw AnalysisCloseUnconfirmedException.From(error);
            }
            catch (Exception error) when (error is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException or PlatformNotSupportedException)
            {
                if (_directory is not null) _failure ??= error;
                // Kernel object names contain a private profile digest. Surface a finite operational
                // reason without copying runtime messages/names into UI or default diagnostics.
                throw new IOException("Private profile capacity coordination is unavailable.", error.HResult);
            }
            catch (Exception error) when (_directory is not null)
            {
                _failure ??= error;
                throw new IOException("Private profile capacity coordination is unavailable. Existing data was preserved.", error.HResult);
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
            try { Invoke(() => { Shrink(Units(next)); return true; }); }
            catch (Exception error) { _failure ??= error; throw; }
            _bytes = next;
        }
    }
    private static int Units(long bytes) => checked((int)((bytes + QuantumBytes - 1) / QuantumBytes));
    private bool Grow(int required)
    {
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        if (_failure is not null) throw new IOException("Profile capacity ownership could not be verified.", _failure);
        if (_directory is not null)
        { _files ??= new(_directory, _profile); return _files.SetUnits(required); }
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
                AnalysisNativeMutex? mutex = _mutexFactory(_name + "." + slot);
                try
                {
                    if (!mutex.TryAcquire()) continue;
                    _held[slot] = mutex; acquired.Add(slot); mutex = null;
                }
                finally { mutex?.Dispose(); }
            }
            if (current + acquired.Count == required) return true;
        }
        catch (Exception operation)
        {
            List<Exception>? errors = null;
            foreach (int slot in acquired)
                try { Retire(slot); } catch (Exception close) { (errors ??= [operation]).Add(close); }
            if (errors is not null) throw new AggregateException(errors);
            throw;
        }
        Exception? rollback = null;
        foreach (int slot in acquired) try { Retire(slot); } catch (Exception error) { rollback ??= error; }
        if (rollback is not null) ExceptionDispatchInfo.Capture(rollback).Throw();
        return false;
    }
    private void Shrink(int required)
    {
        if (_directory is not null)
        {
            try { if (_files is not null && !_files.SetUnits(required)) throw new IOException("Private profile capacity release was refused."); }
            catch (Exception failure) { _failure ??= failure; throw; }
            return;
        }
        if (!OperatingSystem.IsWindows())
        { if (_unix is not null && !_unix.SetUnits(required)) throw new IOException("Profile capacity release was refused."); return; }
        int current = _held.Count(item => item is not null);
        Exception? error = null;
        for (int slot = Slots - 1; slot >= 0 && current > required; slot--)
            if (_held[slot] is not null)
            { try { Retire(slot); current--; } catch (Exception failure) { _failure ??= failure; error ??= failure; } }
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private void Retire(int slot)
    {
        if (_retireFailures[slot] is { } prior) throw AnalysisCloseUnconfirmedException.From(prior);
        try { _held[slot]!.Dispose(); }
        catch (Exception error) { _retireFailures[slot] = error; _failure ??= error; throw; }
        _held[slot] = null;
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
        try { ReleaseOwner(); }
        catch (Exception error) { AnalysisResourceQuarantine.Shared.Hold(this); throw AnalysisCloseUnconfirmedException.From(error); }
    }
    private void ReleaseOwner()
    {
        Exception? error = null;
        lock (_gate)
        {
            if (--_references != 0) return;
            try { Invoke(() =>
            {
                Exception? failure = null;
                // Each component owns its final retirement. Do not retry Shrink(0) first,
                // or erase a component whose native ownership remains unconfirmed.
                if (_unix is not null)
                    try { _unix.Dispose(); _unix = null; } catch (Exception ex) { failure ??= ex; }
                if (_files is not null)
                    try { _files.Dispose(); _files = null; } catch (Exception ex) { failure ??= ex; }
                for (int slot = Slots - 1; slot >= 0; slot--)
                    if (_held[slot] is not null)
                        try { Retire(slot); } catch (Exception ex) { failure ??= ex; }
                if (failure is null && _directory is not null)
                    try { AnalysisIo.CloseOnWorker(_directory); } catch (Exception ex) { failure = ex; }
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                return true;
            }); }
            catch (Exception failure) { error = failure; }
            finally
            {
                _disposed = true;
                if (error is null) { _bytes = 0; _commands.CompleteAdding(); }
                else
                {
                    _failure ??= error; AnalysisResourceQuarantine.Shared.Hold(this);
                    // A live background worker preserves thread-owned mutex capacity. Its
                    // global worker admission also remains charged until process exit.
                }
            }
        }
        if (error is null) { _thread.Join(); _commands.Dispose(); }
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private sealed class Retention(AnalysisProfileQuota owner) : IDisposable
    {
        private AnalysisProfileQuota? _owner = owner;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is not { } captured) return;
            try { captured.ReleaseOwner(); }
            catch (Exception error) { AnalysisResourceQuarantine.Shared.Hold(captured); throw AnalysisCloseUnconfirmedException.From(error); }
        }
    }
}
