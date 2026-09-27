using System.Buffers.Binary;
using FileCat.Core.Resources;

namespace FileCat.Core.Listing;

/// <summary>
/// Append-only entry store. Ordinary listings use compact pages; filesystem listings can move their
/// records and names to private, delete-on-close scratch files after a managed-memory budget is reached.
/// The writer publishes Count only after a complete batch is written. Readers receive values, never
/// references into a page that may be retired during a spill.
/// </summary>
public sealed class EntryStore : IDisposable
{
    private const int PageShift = 12;
    private const int PageSize = 1 << PageShift;
    private const int PageMask = PageSize - 1;
    private const int RecordSize = 48;
    private const int CacheLimit = 4096;

    private readonly object _gate = new();
    private readonly string? _scratchDirectory;
    private readonly long _memoryBudget;
    private EntryData[][] _pages = new EntryData[4][];
    private readonly Dictionary<int, EntryData> _cache = [];
    private readonly Queue<int> _cacheOrder = new();
    private FileStream? _records;
    private FileStream? _names;
    private string? _recordPath;
    private string? _namePath;
    private long _nameLength;
    private long _estimatedBytes;
    private int _count;
    private int _directories;
    private int _files;
    private long _knownFileBytes;
    private bool _hasPayload;
    private bool _disposed;
    private int _leases;
    private bool _disposeRequested;

    public EntryStore(string? scratchDirectory = null, long memoryBudgetBytes = long.MaxValue)
    {
        if (memoryBudgetBytes < 1) throw new ArgumentOutOfRangeException(nameof(memoryBudgetBytes));
        _scratchDirectory = scratchDirectory;
        _memoryBudget = memoryBudgetBytes;
    }

    public int Count => Volatile.Read(ref _count);
    public (int Directories, int Files, long KnownFileBytes) Totals
    {
        get { lock (_gate) return (_directories, _files, _knownFileBytes); }
    }
    public bool IsSpilled { get { lock (_gate) return _records is not null; } }
    public long SpillBytes { get { lock (_gate) return _records is null ? 0 : (long)_count * RecordSize + _nameLength; } }

    public EntryData this[int index] => Get(index);

    public EntryData Get(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_records is null) return _pages[index >> PageShift][index & PageMask];
            if (_cache.TryGetValue(index, out var cached)) return cached;
            var entry = ReadRecord(index);
            Cache(index, entry);
            return entry;
        }
    }

    /// <summary>Updates presentation metadata while preserving the entry's exact identity.</summary>
    public void Update(int index, in EntryData entry)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        lock (_gate)
        {
            ThrowIfDisposed();
            var old = _records is null ? _pages[index >> PageShift][index & PageMask] : ReadRecord(index);
            if (!string.Equals(old.Name, entry.Name, StringComparison.Ordinal) || old.Kind != entry.Kind)
                throw new InvalidOperationException("Entry identity cannot change in place.");
            if (!old.IsContainer) _knownFileBytes += Math.Max(0, entry.Size) - Math.Max(0, old.Size);
            if (_records is null)
            {
                _pages[index >> PageShift][index & PageMask] = entry;
                return;
            }
            if (entry.Tag is not null) throw new InvalidOperationException("Spilled entries cannot hold provider payloads.");
            Span<byte> record = stackalloc byte[RecordSize];
            ReadExactly(_records.SafeFileHandle, record, (long)index * RecordSize);
            WriteRecord(record, entry, BinaryPrimitives.ReadInt64LittleEndian(record), BinaryPrimitives.ReadInt32LittleEndian(record[8..]));
            RandomAccess.Write(_records.SafeFileHandle, record, (long)index * RecordSize);
            Cache(index, entry);
        }
    }

    public void Append(in EntryData entry) => Append(new ReadOnlySpan<EntryData>(in entry));

    public void Append(ReadOnlySpan<EntryData> entries)
    {
        if (entries.IsEmpty) return;
        lock (_gate)
        {
            ThrowIfDisposed();
            long added = 0;
            bool batchHasPayload = !AllWithoutPayload(entries);
            foreach (ref readonly var e in entries)
            {
                if (e.Name is null) throw new ArgumentException("Entry names cannot be null.", nameof(entries));
                added += 78L + 2L * e.Name.Length;
            }
            if (_records is null && _scratchDirectory is not null &&
                _estimatedBytes + added > _memoryBudget && !_hasPayload && !batchHasPayload)
                Spill();
            if (_records is not null)
            {
                if (batchHasPayload) throw new InvalidOperationException("Spilled entries cannot hold provider payloads.");
                WriteBatch(entries, _count);
            }
            else
            {
                int n = _count;
                foreach (ref readonly var e in entries)
                {
                    EnsurePage(n >> PageShift);
                    _pages[n >> PageShift][n & PageMask] = e;
                    n++;
                }
            }
            foreach (ref readonly var e in entries)
            {
                if (e.Kind == EntryKind.Parent) continue;
                if (e.IsContainer) _directories++;
                else { _files++; _knownFileBytes += Math.Max(0, e.Size); }
            }
            _hasPayload |= batchHasPayload;
            _estimatedBytes += added;
            Volatile.Write(ref _count, checked(_count + entries.Length));
        }
    }

    private static bool AllWithoutPayload(ReadOnlySpan<EntryData> entries)
    {
        foreach (ref readonly var e in entries) if (e.Tag is not null) return false;
        return true;
    }

    private void Spill()
    {
        Directory.CreateDirectory(_scratchDirectory!);
        _recordPath = Path.Combine(_scratchDirectory!, "listing-" + Guid.NewGuid().ToString("N") + ".idx");
        _namePath = Path.Combine(_scratchDirectory!, "listing-" + Guid.NewGuid().ToString("N") + ".names");
        try
        {
            const FileOptions options = FileOptions.DeleteOnClose | FileOptions.RandomAccess;
            _records = new FileStream(_recordPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, 4096, options);
            _names = new FileStream(_namePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, 4096, options);
            for (int start = 0; start < _count; start += 512)
            {
                int length = Math.Min(512, _count - start);
                var batch = new EntryData[length];
                for (int i = 0; i < length; i++) batch[i] = _pages[(start + i) >> PageShift][(start + i) & PageMask];
                WriteBatch(batch, start);
            }
            _pages = [];
        }
        catch
        {
            _records?.Dispose();
            _names?.Dispose();
            _records = null;
            _names = null;
            DeleteScratch();
            throw;
        }
    }

    private void WriteBatch(ReadOnlySpan<EntryData> entries, int firstIndex)
    {
        var records = new byte[checked(entries.Length * RecordSize)];
        using var names = new MemoryStream();
        for (int i = 0; i < entries.Length; i++)
        {
            var nameBytes = new byte[checked(entries[i].Name.Length * 2)];
            for (int c = 0; c < entries[i].Name.Length; c++)
                BinaryPrimitives.WriteUInt16LittleEndian(nameBytes.AsSpan(c * 2), entries[i].Name[c]);
            long offset = _nameLength + names.Position;
            names.Write(nameBytes);
            WriteRecord(records.AsSpan(i * RecordSize, RecordSize), entries[i], offset, nameBytes.Length);
        }
        RandomAccess.Write(_names!.SafeFileHandle, names.GetBuffer().AsSpan(0, (int)names.Length), _nameLength);
        RandomAccess.Write(_records!.SafeFileHandle, records, (long)firstIndex * RecordSize);
        _nameLength += names.Length;
    }

    private EntryData ReadRecord(int index)
    {
        Span<byte> record = stackalloc byte[RecordSize];
        ReadExactly(_records!.SafeFileHandle, record, (long)index * RecordSize);
        long offset = BinaryPrimitives.ReadInt64LittleEndian(record);
        int length = BinaryPrimitives.ReadInt32LittleEndian(record[8..]);
        if (length < 0 || length > 1024 * 1024 || (length & 1) != 0 || offset < 0 || offset > _nameLength - length)
            throw new IOException("Invalid listing spill record.");
        var nameBytes = new byte[length];
        ReadExactly(_names!.SafeFileHandle, nameBytes, offset);
        var chars = new char[length / 2];
        for (int c = 0; c < chars.Length; c++) chars[c] = (char)BinaryPrimitives.ReadUInt16LittleEndian(nameBytes.AsSpan(c * 2));
        return new EntryData
        {
            Name = new string(chars),
            Kind = (EntryKind)record[12],
            Flags = (EntryFlags)BinaryPrimitives.ReadUInt16LittleEndian(record[13..]),
            Size = BinaryPrimitives.ReadInt64LittleEndian(record[16..]),
            Modified = BinaryPrimitives.ReadInt64LittleEndian(record[24..]),
            Created = BinaryPrimitives.ReadInt64LittleEndian(record[32..]),
            Attributes = BinaryPrimitives.ReadUInt32LittleEndian(record[40..]),
        };
    }

    private static void WriteRecord(Span<byte> record, in EntryData entry, long nameOffset, int nameLength)
    {
        record.Clear();
        BinaryPrimitives.WriteInt64LittleEndian(record, nameOffset);
        BinaryPrimitives.WriteInt32LittleEndian(record[8..], nameLength);
        record[12] = (byte)entry.Kind;
        BinaryPrimitives.WriteUInt16LittleEndian(record[13..], (ushort)entry.Flags);
        BinaryPrimitives.WriteInt64LittleEndian(record[16..], entry.Size);
        BinaryPrimitives.WriteInt64LittleEndian(record[24..], entry.Modified);
        BinaryPrimitives.WriteInt64LittleEndian(record[32..], entry.Created);
        BinaryPrimitives.WriteUInt32LittleEndian(record[40..], entry.Attributes);
    }

    private static void ReadExactly(Microsoft.Win32.SafeHandles.SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        while (!buffer.IsEmpty)
        {
            int n = RandomAccess.Read(handle, buffer, offset);
            if (n == 0) throw new EndOfStreamException("Listing spill file ended unexpectedly.");
            buffer = buffer[n..];
            offset += n;
        }
    }

    private void Cache(int index, EntryData entry)
    {
        if (_cache.ContainsKey(index)) { _cache[index] = entry; return; }
        if (_cache.Count >= CacheLimit)
        {
            while (_cacheOrder.Count > 0 && !_cache.Remove(_cacheOrder.Dequeue())) { }
        }
        _cache[index] = entry;
        _cacheOrder.Enqueue(index);
    }

    private void EnsurePage(int page)
    {
        if (page >= _pages.Length) Array.Resize(ref _pages, Math.Max(_pages.Length * 2, page + 1));
        _pages[page] ??= new EntryData[PageSize];
    }

    /// <summary>Approximate managed storage, including the bounded spill cache.</summary>
    public long EstimateBytes()
    {
        lock (_gate)
        {
            if (_records is null) return _estimatedBytes + (long)_pages.Length * 8;
            return (long)CacheLimit * 256 + 2L * _cache.Count * 64;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void DeleteScratch()
    {
        foreach (var path in new[] { _recordPath, _namePath })
        {
            if (path is null) continue;
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Keeps the store readable after its listing has moved on (a job reading captured sources). Disposal
    /// requested meanwhile happens when the last lease is released.
    /// </summary>
    public IDisposable Lease()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _leases++;
        }
        return new StoreLease(this);
    }

    private void ReleaseLease()
    {
        lock (_gate)
        {
            if (--_leases == 0 && _disposeRequested) DisposeLocked();
        }
    }

    private sealed class StoreLease(EntryStore store) : IDisposable
    {
        private EntryStore? _store = store;

        public void Dispose() => Interlocked.Exchange(ref _store, null)?.ReleaseLease();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_leases > 0)
            {
                _disposeRequested = true;
                return;
            }
            DisposeLocked();
        }
    }

    private void DisposeLocked()
    {
        _disposed = true;
        _records?.Dispose();
        _names?.Dispose();
        _records = null;
        _names = null;
        _pages = [];
        _cache.Clear();
        _cacheOrder.Clear();
        DeleteScratch();
    }
}
