using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Threading;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Controls;

/// <summary>
/// A bounded semantic view over the custom renderer. Rows own a store identity, never a recycled visible index.
/// Avalonia's native selection bridge requires a complete array; large selections use the explicit paged review
/// instead of truncating the selected set or creating an unbounded number of native peer objects.
/// </summary>
internal sealed class FileListAutomationPeer : ControlAutomationPeer, ISelectionProvider, IScrollProvider, IValueProvider
{
    internal const int DirectSelectionLimit = 512;
    internal const int VisiblePeerLimit = 256;
    internal const int CachedPeerLimit = DirectSelectionLimit + VisiblePeerLimit + 1;
    private readonly FileListControl _list;
    private readonly Dictionary<int, FileListItemAutomationPeer> _items = [];
    private EntryStore? _store;
    private EntryStore? _selectionStore;
    private int _storeRevision;
    private bool _queued;
    private ListingChange _changes;
    private string _name, _help, _value, _status;
    private double _scrollPercent, _viewSize;
    private IReadOnlyList<AutomationPeer>? _selection;

    internal FileListAutomationPeer(FileListControl list) : base(list)
    {
        _list = list;
        _name = list.DescribeListName();
        _help = list.DescribeListHelp();
        _value = list.DescribeFocus();
        _status = list.DescribeListingState();
        _scrollPercent = VerticalScrollPercent;
        _viewSize = VerticalViewSize;
        _selection = SnapshotSelection();
        _selectionStore = _store;
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
    protected override string GetClassNameCore() => "FileList";
    protected override string GetNameCore() => _list.DescribeListName();
    protected override string GetHelpTextCore() => _list.DescribeListHelp();
    protected override string GetItemStatusCore() => _list.DescribeListingState();
    protected override bool IsKeyboardFocusableCore() => _list.AutomationListing is not null;
    protected override bool IsEnabledCore() => _list.AutomationListing is not null && base.IsEnabledCore();

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
    {
        EnsureStore();
        var children = new List<AutomationPeer>();
        if (_list.AutomationListing is { } listing)
        {
            int end = Math.Min(listing.VisibleCount, _list.TopRow + Math.Min(VisiblePeerLimit, _list.VisibleRowCapacity + 1));
            for (int row = _list.TopRow; row < end; row++) children.Add(Item(listing.GetStoreIndex(row)));
            if (listing.FocusedIndex >= 0 && (listing.FocusedIndex < _list.TopRow || listing.FocusedIndex >= end))
                children.Add(Item(listing.FocusedStoreIndex));
        }
        if (base.GetChildrenCore() is { } controls) children.AddRange(controls);
        return children;
    }

    private void EnsureStore()
    {
        if (ReferenceEquals(_store, _list.AutomationListing?.Store)) return;
        _store = _list.AutomationListing?.Store;
        _storeRevision++;
        _items.Clear();
        // Keep the last announced selection until Update can report the real old/new values.
        // Retired peers retain a safe name and reject actions against the replacement store.
    }

    internal int CachedItemCount => _items.Count;

    internal FileListItemAutomationPeer Item(int storeIndex)
    {
        EnsureStore();
        if (_list.AutomationListing is not { } listing || _store is null || (uint)storeIndex >= (uint)_store.Count)
            throw new InvalidOperationException("This listing item is no longer available.");
        if (_items.TryGetValue(storeIndex, out var item)) return item;
        if (_items.Count >= CachedPeerLimit)
        {
            // Only native clients retaining an old peer keep it alive; the list itself has a fixed cache budget.
            int end = _list.TopRow + Math.Min(VisiblePeerLimit, _list.VisibleRowCapacity + 1);
            int evict = _items.Keys.FirstOrDefault(i => i != listing.FocusedStoreIndex &&
                (listing.MarkedCount > DirectSelectionLimit || !listing.IsMarked(i)) &&
                (listing.GetVisibleIndex(i) < _list.TopRow || listing.GetVisibleIndex(i) >= end), -1);
            if (evict < 0) evict = _items.Keys.First();
            _items.Remove(evict);
        }
        item = new FileListItemAutomationPeer(this, _list, listing, _store, storeIndex, _storeRevision);
        _items.Add(storeIndex, item);
        return item;
    }

    public bool CanSelectMultiple => true;
    public bool IsSelectionRequired => false;

    public IReadOnlyList<AutomationPeer> GetSelection()
    {
        EnsureStore();
        if (_list.AutomationListing is not { } listing || listing.MarkedCount == 0) return [];
        if (listing.MarkedCount > DirectSelectionLimit)
            throw new NotSupportedException("This selection is too large for one native accessibility array. Use the Review marked items command to review the complete selected set in pages, including hidden marks.");
        Span<int> indices = stackalloc int[listing.MarkedCount];
        int count = listing.CopyMarkedStoreIndices(-1, indices);
        var selected = new AutomationPeer[count];
        for (int i = 0; i < count; i++) selected[i] = Item(indices[i]);
        return selected;
    }

    private IReadOnlyList<AutomationPeer>? SnapshotSelection() =>
        (_list.AutomationListing?.MarkedCount ?? 0) <= DirectSelectionLimit ? GetSelection() : null;

    public bool IsReadOnly => true;
    public string Value => _list.DescribeFocus();
    public void SetValue(string? value) => throw new InvalidOperationException("Use list navigation and marking actions to change the focused item or selection.");

    public bool HorizontallyScrollable => false;
    public double HorizontalScrollPercent => ScrollPatternIdentifiers.NoScroll;
    public double HorizontalViewSize => 100;
    private int MaximumTop => Math.Max(0, (_list.AutomationListing?.VisibleCount ?? 0) - _list.VisibleRowCapacity);
    public bool VerticallyScrollable => MaximumTop > 0;
    public double VerticalScrollPercent => MaximumTop > 0 ? _list.TopRow * 100.0 / MaximumTop : ScrollPatternIdentifiers.NoScroll;
    public double VerticalViewSize => _list.AutomationListing is { VisibleCount: > 0 } listing
        ? Math.Min(100, _list.VisibleRowCapacity * 100.0 / listing.VisibleCount) : 100;

    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        EnsureEnabled();
        if (horizontalAmount != ScrollAmount.NoAmount) throw new InvalidOperationException("The file list does not scroll horizontally.");
        int delta = verticalAmount switch
        {
            ScrollAmount.NoAmount => 0,
            ScrollAmount.SmallIncrement => 1,
            ScrollAmount.SmallDecrement => -1,
            ScrollAmount.LargeIncrement => Math.Max(1, _list.VisibleRowCapacity - 1),
            ScrollAmount.LargeDecrement => -Math.Max(1, _list.VisibleRowCapacity - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(verticalAmount)),
        };
        _list.ScrollToTopRow((int)Math.Clamp((long)_list.TopRow + delta, 0, MaximumTop));
    }

    public void SetScrollPercent(double horizontalPercent, double verticalPercent)
    {
        EnsureEnabled();
        if (horizontalPercent != ScrollPatternIdentifiers.NoScroll) throw new InvalidOperationException("The file list does not scroll horizontally.");
        if (verticalPercent == ScrollPatternIdentifiers.NoScroll) return;
        if (!double.IsFinite(verticalPercent) || verticalPercent < 0 || verticalPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(verticalPercent));
        _list.ScrollToTopRow((int)Math.Round(MaximumTop * verticalPercent / 100));
    }

    internal void QueueUpdate(ListingChange change)
    {
        _changes |= change;
        if (_queued) return;
        _queued = true;
        Dispatcher.UIThread.Post(Update, DispatcherPriority.Background);
    }

    private void Update()
    {
        _queued = false;
        var changes = _changes;
        _changes = ListingChange.None;
        EnsureStore();
        string name = _list.DescribeListName(), help = _list.DescribeListHelp(), value = _list.DescribeFocus(), status = _list.DescribeListingState();
        if (_name != name) { RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, _name, name); _name = name; }
        if (_help != help) { RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, _help, help); _help = help; }
        if (_value != value) { RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, _value, value); _value = value; }
        if (_status != status) { RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, _status, status); _status = status; }
        double scroll = VerticalScrollPercent, size = VerticalViewSize;
        if (_scrollPercent != scroll) { RaisePropertyChangedEvent(ScrollPatternIdentifiers.VerticalScrollPercentProperty, _scrollPercent, scroll); _scrollPercent = scroll; }
        if (_viewSize != size) { RaisePropertyChangedEvent(ScrollPatternIdentifiers.VerticalViewSizeProperty, _viewSize, size); _viewSize = size; }
        // A native event listener may synchronously inspect/create another peer while it handles a change.
        foreach (var item in _items.Values.ToArray()) item.UpdateState();
        if ((changes & (ListingChange.Rows | ListingChange.Reset | ListingChange.Focus)) != 0) InvalidateChildren();
        if ((changes & (ListingChange.Marks | ListingChange.Reset)) != 0 || !ReferenceEquals(_selectionStore, _store))
        {
            var selection = SnapshotSelection();
            if (_selection is not null && selection is not null)
            {
                if (!_selection.Select(p => p.GetAutomationId()).SequenceEqual(selection.Select(p => p.GetAutomationId())))
                    RaisePropertyChangedEvent(SelectionPatternIdentifiers.SelectionProperty, _selection, selection);
            }
            else
            {
                // An unsupported native array has no truthful old/new array value. Invalidate discovery
                // instead of announcing a fabricated null, empty, or truncated selected set.
                InvalidateChildren();
            }
            _selection = selection;
            _selectionStore = _store;
        }
    }
}

/// <summary>A lightweight peer with no Avalonia row control, no metadata production, and no ownership of file I/O.</summary>
internal sealed class FileListItemAutomationPeer : AutomationPeer, ISelectionItemProvider, IInvokeProvider
{
    private readonly FileListAutomationPeer _parent;
    private readonly FileListControl _list;
    private readonly ListingModel _listing;
    private readonly EntryStore _store;
    private readonly int _index;
    private readonly string _id;
    private readonly string _name;
    private readonly string _kind;
    private bool _selected;
    private string _status;
    private string _help;
    private Rect _bounds;

    internal FileListItemAutomationPeer(FileListAutomationPeer parent, FileListControl list, ListingModel listing, EntryStore store, int index, int revision)
    {
        _parent = parent;
        _list = list;
        _listing = listing;
        _store = store;
        _index = index;
        _id = $"row-{revision}-{index}";
        var entry = store[index];
        _name = FileListControl.ItemName(entry);
        _kind = FileListControl.ItemKind(entry);
        _selected = IsSelected;
        _status = Status;
        _help = GetHelpTextCore();
        _bounds = Bounds;
    }

    private bool Current => !_listing.IsDisposed && ReferenceEquals(_list.AutomationListing, _listing) && ReferenceEquals(_listing.Store, _store);
    private int Row => Current ? _listing.GetVisibleIndex(_index) : -1;
    public bool IsSelected => Current && _listing.IsMarked(_index);
    public ISelectionProvider SelectionContainer => _parent;
    private string Status => !Current ? "Item no longer available" :
        (IsSelected ? "Marked. " : "Not marked. ") + (HasKeyboardFocusCore() ? "Focused. " : "") +
        (Row < 0 ? "Hidden by filtering or Show hidden items." : $"{Row + 1} of {_listing.VisibleCount}.");

    private Rect Bounds
    {
        get
        {
            int row = Row;
            if (row < 0 || TopLevel.GetTopLevel(_list) is not { } root)
                return default;
            var bounds = _list.VisibleRowBounds(row);
            if (bounds.Width <= 0 || bounds.Height <= 0) return default;
            var point = _list.TranslatePoint(bounds.TopLeft, root);
            return point is { } p ? new Rect(p, bounds.Size) : default;
        }
    }

    private void CheckCurrent()
    {
        if (!Current) throw new InvalidOperationException("This item belongs to a listing that has changed. Review the current items again.");
        EnsureEnabled();
    }

    public void AddToSelection() { CheckCurrent(); _listing.SetStoreMark(_index, true); }
    public void RemoveFromSelection() { CheckCurrent(); _listing.SetStoreMark(_index, false); }
    public void Select()
    {
        CheckCurrent();
        if (_store[_index].Kind == EntryKind.Parent) throw new InvalidOperationException("Parent navigation is not a selectable item.");
        _listing.SelectStoreMark(_index);
    }

    public void Invoke() { CheckCurrent(); _list.FocusAutomationItem(Row, invoke: true); }
    protected override void SetFocusCore() { CheckCurrent(); _list.FocusAutomationItem(Row); }
    protected override void BringIntoViewCore() { CheckCurrent(); _list.ScrollToRow(Row); }
    protected override bool HasKeyboardFocusCore() => Current && _list.IsFocused && _listing.FocusedStoreIndex == _index;
    protected override bool IsKeyboardFocusableCore() => Current && Row >= 0;
    protected override bool IsEnabledCore() => Current && _list.IsEffectivelyEnabled;
    protected override bool IsOffscreenCore() => Bounds == default || !_list.IsEffectivelyVisible;
    protected override Rect GetBoundingRectangleCore() => Bounds;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
    protected override string GetClassNameCore() => "FileListItem";
    protected override string GetAutomationIdCore() => _id;
    protected override string GetNameCore() => _name;
    protected override string GetItemTypeCore() => _kind;
    protected override string GetItemStatusCore() => Status;
    protected override object? GetProviderCore(Type providerType) =>
        providerType == typeof(ISelectionItemProvider) && _kind == "parent navigation" ? null : base.GetProviderCore(providerType);
    protected override string GetHelpTextCore() => Current ? _list.DescribeItem(_index) : "This item belongs to a previous listing and cannot be acted on.";
    protected override AutomationPeer GetParentCore() => _parent;
    protected override bool TrySetParent(AutomationPeer? parent) => ReferenceEquals(parent, _parent);
    protected override AutomationPeer? GetLabeledByCore() => null;
    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => [];
    protected override string? GetAcceleratorKeyCore() => null;
    protected override string? GetAccessKeyCore() => null;
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
    protected override bool ShowContextMenuCore() => false;

    internal void UpdateState()
    {
        bool selected = IsSelected;
        string status = Status;
        string help = GetHelpTextCore();
        var bounds = Bounds;
        if (_selected != selected) { RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, _selected, selected); _selected = selected; }
        if (_status != status) { RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, _status, status); _status = status; }
        if (_help != help) { RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, _help, help); _help = help; }
        if (_bounds != bounds) { RaisePropertyChangedEvent(AutomationElementIdentifiers.BoundingRectangleProperty, _bounds, bounds); _bounds = bounds; }
    }
}
