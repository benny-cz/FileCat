using FileCat.Core.Resources;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Archives;

/// <summary>A local archive path whose private file stays present until the last borrower returns it.</summary>
public sealed class ArchiveSourceLease : IDisposable
{
    private sealed class Source(string path, IDisposable? owner, OutputEfsObservation.Receipt? origin)
    {
        private int _references = 1;
        public string Path { get; } = path;
        public OutputEfsObservation.Receipt? Origin { get; } = origin;
        public void Retain()
        {
            int count;
            do
            {
                count = Volatile.Read(ref _references);
                if (count == 0) throw new ObjectDisposedException(nameof(ArchiveSourceLease));
            } while (Interlocked.CompareExchange(ref _references, count + 1, count) != count);
        }
        public void Release() { if (Interlocked.Decrement(ref _references) == 0) owner?.Dispose(); }
    }
    private Source? _source;
    public string Path { get; }
    internal OutputEfsObservation.Receipt? Origin => (Volatile.Read(ref _source) ?? throw new ObjectDisposedException(nameof(ArchiveSourceLease))).Origin;
    public ArchiveSourceLease(string path, IDisposable? owner = null) : this(path, owner, null) { }
    internal ArchiveSourceLease(string path, IDisposable? owner, OutputEfsObservation.Receipt? origin) { Path = path; _source = new Source(path, owner, origin); }
    private ArchiveSourceLease(Source source) { Path = source.Path; _source = source; }
    public ArchiveSourceLease Retain()
    {
        var source = Volatile.Read(ref _source) ?? throw new ObjectDisposedException(nameof(ArchiveSourceLease));
        source.Retain(); return new ArchiveSourceLease(source);
    }
    public void Dispose() => Interlocked.Exchange(ref _source, null)?.Release();
}

/// <summary>The bounded cache owns one reference; admitted readers can retain it across eviction.</summary>
public sealed class ArchiveSpool : IDisposable
{
    private readonly ArchiveSourceLease _source;
    public ArchiveSpool(string path, FileStream holder) : this(path, holder, null) { }
    internal ArchiveSpool(string path, FileStream holder, OutputEfsObservation.Receipt? origin)
    {
        _source = new(path, holder, origin);
        Holder = holder;
    }
    public string Path => _source.Path;
    public FileStream Holder { get; }
    public DateTime Used { get; set; } = DateTime.UtcNow;
    public ArchiveSourceLease Acquire() => _source.Retain();
    public void Dispose() => _source.Dispose();
}

/// <summary>Cross-provider nested archive admission requires an owned private path, not just its name.</summary>
public interface IArchiveSpoolProvider : IContainerDetector
{
    ArchiveSourceLease OpenSpool(Location member);
}
