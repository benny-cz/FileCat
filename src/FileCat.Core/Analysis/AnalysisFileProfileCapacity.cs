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
    private bool _disposed;
    internal int Units { get; private set; }
    internal AnalysisFileProfileCapacity(AnalysisPrivateDirectory directory, Guid profile)
    {
        _directory = directory; _profile = profile; _store = new(directory, leaveOpen: true);
        if (new AnalysisPrivateProfileState(_store).ReadExisting() != profile)
            throw new IOException("The retained private profile identity changed before capacity admission.");
    }
    internal bool SetUnits(int required)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (required is < 0 or > Slots) throw new ArgumentOutOfRangeException(nameof(required));
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
                AnalysisPrivateFile? file = OpenToken(slot);
                try
                {
                    if (!file.TryLockCapacity()) continue;
                    _held[slot] = file; file = null; Units++; acquired.Add(slot);
                }
                finally { file?.Dispose(); }
            }
            if (Units == required) return true;
        }
        catch
        {
            foreach (int slot in acquired) try { Retire(slot); } catch { /* Owner latches the operational failure; attempt every close. */ }
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
            catch (IOException) { /* A concurrent create-new winner must pass the exact read below. */ }
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
        catch { file.Dispose(); throw; }
    }
    private static IOException Invalid() => new("The private profile capacity token is unsupported or invalid. Existing bytes were preserved.");
    private void Retire(int slot)
    {
        try { _held[slot]!.Dispose(); }
        finally { _held[slot] = null; Units--; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        try { SetUnits(0); }
        finally { _disposed = true; _store.Dispose(); }
    }
}
