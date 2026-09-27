using System.Text;
using FileCat.Core.Resources;
using FileCat.Core.Selection;

namespace FileCat.Core.Listing;

/// <summary>Bounded-memory stable external sort and view construction for very large listings.</summary>
internal static class ExternalViewBuilder
{
    private const int ChunkSize = 262_144;

    public static DiskIntIndex Sort(EntryStore store, int count, SortSpec sort,
        MetadataKeyProvider? metadataKeys, string scratchDirectory, CancellationToken ct)
    {
        var runs = new List<Run>();
        DiskIntIndex? order = null;
        try
        {
            int first = HasParent(store, count) ? 1 : 0;
            using var comparer = EntrySorter.CreateComparer(store, sort, metadataKeys, count);
            var cmp = comparer.Comparison;
            for (int from = first; from < count; from += ChunkSize)
            {
                ct.ThrowIfCancellationRequested();
                int length = Math.Min(ChunkSize, count - from);
                var indices = new int[length];
                for (int i = 0; i < length; i++) indices[i] = from + i;
                if (sort.Field != SortField.None) StableSort.Sort(indices, cmp);
                runs.Add(new Run(scratchDirectory, indices));
            }
            order = new DiskIntIndex(scratchDirectory, count - first);
            var queue = new PriorityQueue<Run, int>(Comparer<int>.Create(cmp));
            foreach (var run in runs)
                if (run.TryRead(out int index)) queue.Enqueue(run, index);
            int output = 0;
            while (queue.TryDequeue(out var run, out int index))
            {
                ct.ThrowIfCancellationRequested();
                order.Write(output++, index);
                if (run.TryRead(out int next)) queue.Enqueue(run, next);
            }
            var result = order;
            order = null;
            return result;
        }
        finally
        {
            order?.Dispose();
            foreach (var run in runs) run.Dispose();
        }
    }

    public static DiskView BuildView(EntryStore store, int count, DiskIntIndex order, Mask? filter,
        bool showHidden, string scratchDirectory, CancellationToken ct)
    {
        DiskIntIndex? visible = null, positions = null;
        try
        {
            int first = HasParent(store, count) ? 1 : 0;
            if (order.Capacity != count - first) throw new InvalidOperationException("Sorted index does not match the listing generation.");
            visible = new DiskIntIndex(scratchDirectory, count);
            positions = new DiskIntIndex(scratchDirectory, count);
            int visibleCount = 0;
            if (first == 1)
            {
                visible.Write(visibleCount, 0);
                positions.Write(0, ++visibleCount);
            }
            // Without hiding or a filter every entry is visible: nothing needs to be read.
            bool filtering = !showHidden || filter is not null;
            using var reader = filtering ? store.OpenSpillReader(count) : null;
            for (int i = 0; i < order.Capacity; i++)
            {
                if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                int index = order.Read(i);
                if (filtering && !(reader is not null
                        ? Passes(reader.Get(index), filter, showHidden)
                        : Passes(new EntryView(store[index]), filter, showHidden))) continue;
                visible.Write(visibleCount, index);
                positions.Write(index, ++visibleCount);
            }
            var view = new DiskView(visible, positions, visibleCount);
            visible = null;
            positions = null;
            return view;
        }
        finally
        {
            visible?.Dispose();
            positions?.Dispose();
        }
    }

    /// <summary>Hidden-item and filter rules of a view (the name is materialized only when a filter is set).</summary>
    internal static bool Passes(scoped in EntryView e, Mask? filter, bool showHidden) =>
        (showHidden || (e.Flags & EntryFlags.Hidden) == 0) && (filter is null || filter.IsMatch(e.Name.ToString(), e.IsContainer));

    private static bool HasParent(EntryStore store, int count) => count > 0 && store[0].Kind == EntryKind.Parent;

    private sealed class Run : IDisposable
    {
        private readonly FileStream _stream;
        private readonly BinaryReader _reader;
        private readonly string _path;
        private int _remaining;

        public Run(string directory, int[] sorted)
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "listing-run-" + Guid.NewGuid().ToString("N") + ".tmp");
            _stream = new FileStream(_path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
            try
            {
                using (var writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true))
                    foreach (int index in sorted) writer.Write(index);
                _stream.Position = 0;
                _reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
                _remaining = sorted.Length;
            }
            catch
            {
                _stream.Dispose();
                try { File.Delete(_path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        public bool TryRead(out int index)
        {
            if (_remaining == 0) { index = 0; return false; }
            index = _reader.ReadInt32();
            _remaining--;
            return true;
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
            try { File.Delete(_path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
