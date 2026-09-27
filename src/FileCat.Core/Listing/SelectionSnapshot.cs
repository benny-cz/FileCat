using System.Collections;
using FileCat.Core.Resources;

namespace FileCat.Core.Listing;

/// <summary>
/// A captured selection (AI-07) that materializes item references on demand. It records one store index per
/// item and leases the listing's append-only store, so capturing a million marked items costs one integer per
/// item and the UI thread never builds a million objects. Entry identity in a store never changes, so later
/// refreshes or navigation cannot retarget the capture. The creator releases it when the consumer is done;
/// an unreleased snapshot only delays deletion of spill files until it is collected.
/// </summary>
public sealed class SelectionSnapshot : IReadOnlyList<ItemRef>
{
    private readonly ResourceProvider _provider;
    private readonly Location _location;
    private readonly int[] _indices;
    private IDisposable? _lease;

    internal SelectionSnapshot(ResourceProvider provider, Location location, EntryStore store, int[] indices)
    {
        _provider = provider;
        _location = location;
        Store = store;
        _indices = indices;
        _lease = store.Lease();
    }

    /// <summary>The store the indices refer to (compared by reference to map outcomes back to marks).</summary>
    public EntryStore Store { get; }

    public int Count => _indices.Length;

    /// <summary>The one parent every item has, when known without enumerating (a plain folder listing).</summary>
    public Location? CommonParent => _provider.ItemsShareListingParent ? _location : null;

    public ItemRef this[int index]
    {
        get
        {
            if (Volatile.Read(ref _lease) is null) throw new ObjectDisposedException(nameof(SelectionSnapshot), "The captured selection was released.");
            return _provider.GetItemRef(_location, Store[_indices[index]]);
        }
    }

    public int GetStoreIndex(int index) => _indices[index];

    /// <summary>Allows the store to be discarded. Indices stay readable for mapping outcomes.</summary>
    public void Release() => Interlocked.Exchange(ref _lease, null)?.Dispose();

    public IEnumerator<ItemRef> GetEnumerator()
    {
        for (int i = 0; i < _indices.Length; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Helpers that avoid enumerating huge captured selections when a cheaper answer exists.</summary>
public static class ItemSources
{
    /// <summary>
    /// Distinct parents of <paramref name="items"/>, or null when there are more than <paramref name="limit"/>.
    /// A snapshot of a plain listing answers without enumerating.
    /// </summary>
    public static IReadOnlyCollection<Location>? Parents(IReadOnlyList<ItemRef> items, int limit = int.MaxValue)
    {
        if (items is SelectionSnapshot { CommonParent: { } common }) return items.Count == 0 ? [] : [common];
        var parents = new HashSet<Location>();
        foreach (var item in items)
        {
            if (parents.Add(item.Parent) && parents.Count > limit) return null;
        }
        return parents;
    }

    /// <summary>Releases a captured selection (no-op for ordinary lists).</summary>
    public static void Release(IReadOnlyList<ItemRef>? items) => (items as SelectionSnapshot)?.Release();
}
