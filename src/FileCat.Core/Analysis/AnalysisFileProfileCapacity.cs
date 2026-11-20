using System.Runtime.ExceptionServices;
using System.Text;

namespace FileCat.Core.Analysis;

/// <summary>
/// A ceiling shared by cooperating processes that open the same private profile directory,
/// independently of native IPC namespaces. The immutable files are never replaced or deleted.
/// Their native nonblocking locks are runtime ownership, not persisted reservations.
/// </summary>
internal sealed class AnalysisFileProfileCapacity : IDisposable
{
    private const int Slots = 32;
    private readonly AnalysisPrivateDirectory _directory;
    private readonly AnalysisPrivateInventoryStore _store;
    private readonly Guid _profile;
    private readonly AnalysisPrivateFile?[] _held = new AnalysisPrivateFile?[Slots];
    private readonly Exception?[] _closeFailures = new Exception?[Slots];
    private readonly AnalysisResourceQuarantine _quarantine;
    private AnalysisPrivateFile? _rejectedOwner;
    private Exception? _failure;
    private bool _disposed;
    internal int Units { get; private set; }
    internal AnalysisFileProfileCapacity(AnalysisPrivateDirectory directory, Guid profile, AnalysisResourceQuarantine? quarantine = null)
    {
        _directory = directory; _profile = profile; _store = new(directory, leaveOpen: true); _quarantine = quarantine ?? AnalysisResourceQuarantine.Shared;
        if (new AnalysisPrivateProfileState(_store).ReadExisting() != profile)
            throw new IOException("The retained private profile identity changed before capacity admission.");
    }
    internal bool SetUnits(int required)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (required is < 0 or > Slots) throw new ArgumentOutOfRangeException(nameof(required));
        if (_failure is { } prior) throw AnalysisCloseUnconfirmedException.From(prior);
        if (required <= Units)
        {
            Exception? error = null;
            for (int slot = Slots - 1; slot >= 0 && Units > required; slot--)
                if (_held[slot] is not null) try { Retire(slot); } catch (Exception failure) { error ??= failure; }
            if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
            return true;
        }
        var acquired = new List<int>(required - Units);
        try
        {
            for (int slot = 0; slot < Slots && Units < required; slot++)
            {
                if (_held[slot] is not null) continue;
                var file = OpenToken(slot);
                _held[slot] = file; Units++; // Own the probe before attempting the native lock.
                try
                {
                    if (file.TryLockCapacity()) { acquired.Add(slot); continue; }
                }
                catch (Exception operation)
                {
                    try { Retire(slot); }
                    catch (Exception close) { throw new AggregateException(operation, close); }
                    throw;
                }
                Retire(slot);
            }
            if (Units == required) return true;
        }
        catch (Exception operation)
        {
            if (AnalysisCloseUnconfirmedException.IsPresent(operation)) RetainFailure(operation);
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
    private AnalysisPrivateFile OpenToken(int slot)
    {
        string name = AnalysisPrivateNames.CapacityToken(slot);
        byte[] expected = Encoding.ASCII.GetBytes("FileCat profile capacity v1\n" + _profile.ToString("N") + "\n" + slot.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "\n");
        AnalysisPrivateFile file;
        try { file = _directory.OpenRead(name); }
        catch (FileNotFoundException)
        {
            try { _store.WriteMetadataNew(name, expected, CancellationToken.None); }
            catch (AnalysisPublicationUncertainException) { throw; }
            catch (IOException error) when (!AnalysisCloseUnconfirmedException.IsPresent(error)) { /* A concurrent create-new winner must pass the exact read below. */ }
            file = _directory.OpenRead(name);
        }
        try
        {
            _directory.Verify(); file.Verify();
            if (file.Stream.Length != expected.Length) throw Invalid();
            byte[] bytes = new byte[expected.Length]; file.Stream.ReadExactly(bytes);
            _directory.Verify(); file.Verify();
            if (!bytes.AsSpan().SequenceEqual(expected)) throw Invalid();
            return file;
        }
        catch (Exception operation)
        {
            try { AnalysisIo.CloseOnWorker(file); }
            catch (Exception close)
            {
                _rejectedOwner = file; RetainFailure(close);
                throw new AggregateException(operation, close);
            }
            throw;
        }
    }
    private static IOException Invalid() => new("The private profile capacity token is unsupported or invalid. Existing bytes were preserved.");
    private void Retire(int slot)
    {
        if (_closeFailures[slot] is { } prior) throw AnalysisCloseUnconfirmedException.From(prior);
        try { AnalysisIo.CloseOnWorker(_held[slot]!); }
        catch (Exception error)
        { _closeFailures[slot] = error; RetainFailure(error); throw; }
        _held[slot] = null; Units--;
    }
    private void RetainFailure(Exception error) { _failure ??= error; _quarantine.Hold(this); }
    public void Dispose()
    {
        if (_disposed) { if (_failure is { } prior) throw AnalysisCloseUnconfirmedException.From(prior); return; }
        _disposed = true;
        List<Exception>? errors = null;
        for (int slot = Slots - 1; slot >= 0; slot--)
            if (_held[slot] is not null && _closeFailures[slot] is null)
                try { Retire(slot); } catch (Exception error) { (errors ??= []).Add(error); }
        try { _store.Dispose(); } catch (Exception error) { RetainFailure(error); (errors ??= []).Add(error); }
        GC.KeepAlive(_rejectedOwner);
        if (_failure is { } failure && (errors is null || !errors.Contains(failure))) (errors ??= []).Add(failure);
        if (errors is not null) throw AnalysisCloseUnconfirmedException.From(errors.Count == 1 ? errors[0] : new AggregateException(errors));
    }
}
