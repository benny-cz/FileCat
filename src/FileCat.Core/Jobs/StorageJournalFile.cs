namespace FileCat.Core.Jobs;

/// <summary>The writer and its native-purpose charge retire together, after the final flush/close.</summary>
internal sealed class StorageJournalFile(FileStream stream, IDisposable charge) : IDisposable
{
    private IDisposable? _charge = charge;
    internal FileStream Stream => stream;
    public void Dispose() { try { stream.Dispose(); } finally { Interlocked.Exchange(ref _charge, null)?.Dispose(); } }
}
