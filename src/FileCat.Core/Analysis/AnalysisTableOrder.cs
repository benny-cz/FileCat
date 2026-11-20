using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace FileCat.Core.Analysis;

public enum AnalysisTableSort { ActiveMetric, Name, FileSize, FilePaths, MeasurementState }

/// <summary>Owns a bounded table-order reference array. It never changes accounting or selection membership.</summary>
public sealed class AnalysisOrderedRows : IDisposable
{
    private long _reservation;
    private ImmutableArray<AnalysisTreeItem> _rows;
    internal AnalysisOrderedRows(ImmutableArray<AnalysisTreeItem> rows, long reservation) { _rows = rows; _reservation = reservation; }
    public ImmutableArray<AnalysisTreeItem> Rows => Volatile.Read(ref _reservation) != 0 ? _rows : throw new ObjectDisposedException(nameof(AnalysisOrderedRows));
    public void Dispose()
    {
        long reservation = Interlocked.Exchange(ref _reservation, 0); _rows = [];
        if (reservation != 0) AnalysisTableOrder.Release(reservation);
    }
}

/// <summary>Exact stable ordering of observed table rows. Missing numeric values stay last in either direction.</summary>
public static class AnalysisTableOrder
{
    private const long Budget = 32L * 1024 * 1024;
    private static long s_reserved;
    internal static void Release(long bytes) => Interlocked.Add(ref s_reserved, -bytes);
    public static AnalysisOrderedRows Apply(ImmutableArray<AnalysisTreeItem> rows, AnalysisTableSort field,
        bool descending = true, CancellationToken cancellation = default)
    {
        if (!Enum.IsDefined(field)) throw new ArgumentOutOfRangeException(nameof(field));
        cancellation.ThrowIfCancellationRequested();
        // Two work arrays plus conservative retained-array/header overhead, reserved before allocation.
        long cost = checked(128L + rows.Length * 24L);
        while (true)
        {
            long current = Volatile.Read(ref s_reserved);
            if (cost > Budget - current) throw new InvalidOperationException("The shared table ordering memory budget is in use. The previous view remains displayed.");
            if (Interlocked.CompareExchange(ref s_reserved, current + cost, current) == current) break;
        }
        try
        {
            var source = rows.ToArray(); var target = new AnalysisTreeItem[source.Length]; int work = 0;
            void Check() { if ((work++ & 4095) == 0) cancellation.ThrowIfCancellationRequested(); }
            // Bottom-up merge avoids framework sort wrapping a cancellation exception from a comparer.
            for (long width = 1; width < source.Length; width *= 2)
            {
                for (long start = 0; start < source.Length; start += 2 * width)
                {
                    int left = (int)start, middle = (int)Math.Min(start + width, source.Length), right = middle,
                        end = (int)Math.Min(start + 2 * width, source.Length), output = left;
                    while (left < middle || right < end)
                    {
                        Check();
                        target[output++] = right >= end || left < middle && Compare(source[left], source[right], field, descending) <= 0
                            ? source[left++] : source[right++];
                    }
                }
                (source, target) = (target, source);
            }
            cancellation.ThrowIfCancellationRequested();
            return new(ImmutableCollectionsMarshal.AsImmutableArray(source), cost);
        }
        catch { Release(cost); throw; }
    }
    private static int Compare(AnalysisTreeItem a, AnalysisTreeItem b, AnalysisTableSort field, bool descending)
    {
        bool Missing(AnalysisTreeItem item) => field switch
        {
            AnalysisTableSort.ActiveMetric => !item.IncludedByPolicy || item.IsNonFileLeaf || (item.Unknown || item.SharedElsewhere) && item.Value == 0,
            AnalysisTableSort.FileSize => !item.IncludedByPolicy || item.IsNonFileLeaf || item.LogicalUnknown && (item.ReferenceLogical?.KnownBytes ?? item.LogicalSize) == 0,
            AnalysisTableSort.FilePaths => !item.IncludedByPolicy || item.IsNonFileLeaf,
            AnalysisTableSort.MeasurementState => item.Unknown,
            _ => false
        };
        int missing = Missing(a).CompareTo(Missing(b)); if (missing != 0) return missing;
        int result = field switch
        {
            AnalysisTableSort.Name => StringComparer.OrdinalIgnoreCase.Compare(a.CapturedOrderName ?? a.Name, b.CapturedOrderName ?? b.Name),
            AnalysisTableSort.FileSize => (a.ReferenceLogical?.KnownBytes ?? a.LogicalSize).CompareTo(b.ReferenceLogical?.KnownBytes ?? b.LogicalSize),
            AnalysisTableSort.FilePaths => (a.ReferenceLogical?.Files ?? a.Files).CompareTo(b.ReferenceLogical?.Files ?? b.Files),
            AnalysisTableSort.MeasurementState => a.MetadataState.CompareTo(b.MetadataState),
            _ => a.Value.CompareTo(b.Value)
        };
        if (result != 0) return descending ? -Math.Sign(result) : Math.Sign(result);
        if (field == AnalysisTableSort.Name && a.CapturedOrderName is not null && b.CapturedOrderName is not null && a.NodeId is { } left && b.NodeId is { } right)
            return descending ? left.CompareTo(right) : right.CompareTo(left);
        result = StringComparer.Ordinal.Compare(a.Name, b.Name);
        return result != 0 ? result : StringComparer.Ordinal.Compare(a.Key, b.Key);
    }
}
