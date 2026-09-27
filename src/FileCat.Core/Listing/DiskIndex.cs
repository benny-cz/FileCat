using System.IO.MemoryMappedFiles;

namespace FileCat.Core.Listing;

/// <summary>Fixed-capacity, file-backed integer index with on-demand OS paging.</summary>
internal sealed class DiskIntIndex : IDisposable
{
    private readonly FileStream _file;
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private readonly string _path;
    private bool _disposed;

    public DiskIntIndex(string scratchDirectory, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Directory.CreateDirectory(scratchDirectory);
        Capacity = capacity;
        _path = Path.Combine(scratchDirectory, "listing-index-" + Guid.NewGuid().ToString("N") + ".tmp");
        FileStream? file = null;
        MemoryMappedFile? map = null;
        MemoryMappedViewAccessor? view = null;
        try
        {
            file = new FileStream(_path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.DeleteOnClose | FileOptions.RandomAccess);
            long length = Math.Max(4L, (long)capacity * sizeof(int));
            file.SetLength(length);
            map = MemoryMappedFile.CreateFromFile(file, null, length, MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None, leaveOpen: true);
            view = map.CreateViewAccessor(0, length, MemoryMappedFileAccess.ReadWrite);
            _file = file;
            _map = map;
            _view = view;
        }
        catch
        {
            view?.Dispose();
            map?.Dispose();
            file?.Dispose();
            try { File.Delete(_path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public int Capacity { get; }
    public long Bytes => (long)Capacity * sizeof(int);

    public int Read(int index)
    {
        if ((uint)index >= (uint)Capacity) throw new ArgumentOutOfRangeException(nameof(index));
        return _view.ReadInt32((long)index * sizeof(int));
    }

    public void Write(int index, int value)
    {
        if ((uint)index >= (uint)Capacity) throw new ArgumentOutOfRangeException(nameof(index));
        _view.Write((long)index * sizeof(int), value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _view.Dispose();
        _map.Dispose();
        _file.Dispose();
        try { File.Delete(_path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Immutable visible-order and store-to-visible indexes. Position zero means hidden.</summary>
internal sealed class DiskView(DiskIntIndex visible, DiskIntIndex positions, int count) : IDisposable
{
    public int Count { get; } = count;
    public long Bytes => visible.Bytes + positions.Bytes;
    public int GetStoreIndex(int row) => row >= 0 && row < Count ? visible.Read(row) : throw new ArgumentOutOfRangeException(nameof(row));
    public int GetVisibleIndex(int storeIndex) => storeIndex >= 0 && storeIndex < positions.Capacity ? positions.Read(storeIndex) - 1 : -1;
    public IEnumerable<int> Enumerate()
    {
        for (int i = 0; i < Count; i++) yield return visible.Read(i);
    }
    public void Dispose()
    {
        visible.Dispose();
        positions.Dispose();
    }
}
