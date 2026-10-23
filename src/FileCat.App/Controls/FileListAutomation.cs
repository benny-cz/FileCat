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
internal sealed class FileListAutomationPeer : ControlAutomationPeer, ISelectionProvider, IScrollProvider, IValueProvider, IFocusedDescendantPeer
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
    private double _scrollPercent, _viewSize, _horizontalScrollPercent, _horizontalViewSize;
    private bool _horizontallyScrollable;
    private IReadOnlyList<AutomationPeer>? _selection;
    private AutomationPeer? _focusedDescendant;
    private int _exposedRevision = -1, _exposedExtra = -1;
    private int[] _exposedRows = [];

    internal FileListAutomationPeer(FileListControl list) : base(list)
    {
        _list = list;
        _name = list.DescribeListName();
        _help = list.DescribeListHelp();
        _value = list.DescribeFocus();
        _status = list.DescribeListingState();
        _scrollPercent = VerticalScrollPercent;
        _viewSize = VerticalViewSize;
        _horizontalScrollPercent = HorizontalScrollPercent;
        _horizontalViewSize = HorizontalViewSize;
        _horizontallyScrollable = HorizontallyScrollable;
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
        var (revision, rows, extra) = ExposedChildren();
        foreach (int row in rows) children.Add(Item(row));
        if (extra >= 0) children.Add(Item(extra));
        (_exposedRevision, _exposedRows, _exposedExtra) = (revision, rows, extra);
        if (base.GetChildrenCore() is { } controls)
            children.AddRange(controls.Where(control => !_list.IsAutomationOwnerHost(control)));
        return children;
    }

    /// <summary>The store items exposed as children: the visible window plus an offscreen focused row.</summary>
    private (int Revision, int[] Rows, int Extra) ExposedChildren()
    {
        if (_list.AutomationListing is not { } listing) return (_storeRevision, [], -1);
        int top = _list.TopRow;
        int end = Math.Min(listing.VisibleCount, top + Math.Min(VisiblePeerLimit, _list.VisibleRowCapacity + 1));
        var rows = new int[Math.Max(0, end - top)];
        for (int row = top; row < end; row++) rows[row - top] = listing.GetStoreIndex(row);
        int focused = listing.FocusedIndex;
        return (_storeRevision, rows, focused >= 0 && (focused < top || focused >= end) ? listing.FocusedStoreIndex : -1);
    }

    private bool ExposedChildrenChanged()
    {
        var (revision, rows, extra) = ExposedChildren();
        return revision != _exposedRevision || extra != _exposedExtra || !rows.AsSpan().SequenceEqual(_exposedRows);
    }

    /// <summary>The focused row while the list itself has keyboard focus, so assistive focus follows the row.</summary>
    public AutomationPeer? FocusedDescendant
    {
        get
        {
            EnsureStore();
            return _list.IsFocused && _list.AutomationListing is { FocusedIndex: >= 0 } listing ? Item(listing.FocusedStoreIndex) : null;
        }
    }

    public event EventHandler? FocusedDescendantChanged;

    private void EnsureStore()
    {
        if (ReferenceEquals(_store, _list.AutomationListing?.Store)) return;
        foreach (var item in _items.Values)
        {
            item.Retire();
            _list.RemoveAutomationOwner(item.Owner);
        }
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
            var retired = _items[evict];
            retired.Retire();
            _list.RemoveAutomationOwner(retired.Owner);
            _items.Remove(evict);
            if (evict == _exposedExtra || Array.IndexOf(_exposedRows, evict) >= 0)
            {
                // The published children include the retired peer; republish them with its replacement.
                _exposedRevision = -1;
                QueueUpdate(ListingChange.None);
            }
        }
        var owner = _list.AddAutomationOwner();
        try
        {
            item = new FileListItemAutomationPeer(this, _list, listing, _store, storeIndex, _storeRevision, owner);
            _items.Add(storeIndex, item);
            return item;
        }
        catch
        {
            _list.RemoveAutomationOwner(owner);
            throw;
        }
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

    private double MaximumHorizontal => Math.Max(0, _list.HorizontalExtent - _list.HorizontalViewport);
    public bool HorizontallyScrollable => MaximumHorizontal > 0;
    public double HorizontalScrollPercent => HorizontallyScrollable
        ? Math.Clamp(_list.HorizontalOffset * 100.0 / MaximumHorizontal, 0, 100) : ScrollPatternIdentifiers.NoScroll;
    public double HorizontalViewSize => _list.HorizontalExtent > 0
        ? Math.Min(100, _list.HorizontalViewport * 100.0 / _list.HorizontalExtent) : 100;
    private int MaximumTop => Math.Max(0, (_list.AutomationListing?.VisibleCount ?? 0) - _list.VisibleRowCapacity);
    public bool VerticallyScrollable => MaximumTop > 0;
    public double VerticalScrollPercent => MaximumTop > 0 ? _list.TopRow * 100.0 / MaximumTop : ScrollPatternIdentifiers.NoScroll;
    public double VerticalViewSize => _list.AutomationListing is { VisibleCount: > 0 } listing
        ? Math.Min(100, _list.VisibleRowCapacity * 100.0 / listing.VisibleCount) : 100;

    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        EnsureEnabled();
        if (horizontalAmount != ScrollAmount.NoAmount && !HorizontallyScrollable)
            throw new InvalidOperationException("The file list has no horizontal overflow to scroll.");
        double horizontalDelta = horizontalAmount switch
        {
            ScrollAmount.NoAmount => 0,
            ScrollAmount.SmallIncrement => 48,
            ScrollAmount.SmallDecrement => -48,
            ScrollAmount.LargeIncrement => Math.Max(48, _list.HorizontalViewport * 0.8),
            ScrollAmount.LargeDecrement => -Math.Max(48, _list.HorizontalViewport * 0.8),
            _ => throw new ArgumentOutOfRangeException(nameof(horizontalAmount)),
        };
        if (horizontalDelta != 0) _list.ScrollToHorizontalOffset(_list.HorizontalOffset + horizontalDelta);
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
        if (horizontalPercent != ScrollPatternIdentifiers.NoScroll)
        {
            if (!HorizontallyScrollable) throw new InvalidOperationException("The file list has no horizontal overflow to scroll.");
            if (!double.IsFinite(horizontalPercent) || horizontalPercent < 0 || horizontalPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(horizontalPercent));
            _list.ScrollToHorizontalOffset(MaximumHorizontal * horizontalPercent / 100);
        }
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
        if (_value != value)
        {
            // While a row carries native focus, its focus announcement already presents this text.
            if (FocusedDescendant is null) RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, _value, value);
            _value = value;
        }
        if (_status != status) { RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, _status, status); _status = status; }
        double scroll = VerticalScrollPercent, size = VerticalViewSize;
        if (_scrollPercent != scroll) { RaisePropertyChangedEvent(ScrollPatternIdentifiers.VerticalScrollPercentProperty, _scrollPercent, scroll); _scrollPercent = scroll; }
        if (_viewSize != size) { RaisePropertyChangedEvent(ScrollPatternIdentifiers.VerticalViewSizeProperty, _viewSize, size); _viewSize = size; }
        double horizontal = HorizontalScrollPercent, horizontalSize = HorizontalViewSize;
        bool horizontallyScrollable = HorizontallyScrollable;
        if (_horizontallyScrollable != horizontallyScrollable) { RaisePropertyChangedEvent(ScrollPatternIdentifiers.HorizontallyScrollableProperty, _horizontallyScrollable, horizontallyScrollable); _horizontallyScrollable = horizontallyScrollable; }
        if (_horizontalScrollPercent != horizontal) { RaisePropertyChangedEvent(ScrollPatternIdentifiers.HorizontalScrollPercentProperty, _horizontalScrollPercent, horizontal); _horizontalScrollPercent = horizontal; }
        if (_horizontalViewSize != horizontalSize) { RaisePropertyChangedEvent(ScrollPatternIdentifiers.HorizontalViewSizeProperty, _horizontalViewSize, horizontalSize); _horizontalViewSize = horizontalSize; }
        // A native event listener may synchronously inspect/create another peer while it handles a change.
        foreach (var item in _items.Values.ToArray()) item.UpdateState();
        // Moving focus inside the visible rows keeps the same children. Rebuilding them anyway made native
        // bridges re-register every row while a screen reader was presenting the newly focused one.
        if ((changes & ListingChange.Reset) != 0 || ExposedChildrenChanged()) InvalidateChildren();
        var focused = FocusedDescendant;
        if (!ReferenceEquals(focused, _focusedDescendant))
        {
            _focusedDescendant = focused;
            FocusedDescendantChanged?.Invoke(this, EventArgs.Empty);
        }
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

/// <summary>A bounded visual-owner peer with no metadata production or ownership of file I/O.</summary>
internal sealed class FileListItemAutomationPeer : ControlAutomationPeer, ISelectionItemProvider, IInvokeProvider
{
    private readonly FileListAutomationPeer _parent;
    private readonly FileListControl _list;
    private readonly ListingModel _listing;
    private readonly EntryStore _store;
    private readonly int _index;
    private readonly string _id;
    private readonly string _name;
    private readonly string _kind;
    private bool _retired;
    private bool _selected;
    private string _status;
    private string _help;
    private Rect _bounds;

    internal FileListItemAutomationPeer(FileListAutomationPeer parent, FileListControl list, ListingModel listing, EntryStore store, int index, int revision, Control owner)
        : base(owner)
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

    internal void Retire() => _retired = true;
    private bool Current => !_retired && !_listing.IsDisposed && ReferenceEquals(_list.AutomationListing, _listing) && ReferenceEquals(_listing.Store, _store);
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

    public void AddToSelection() { CheckSelectable(); _listing.SetStoreMark(_index, true); }
    public void RemoveFromSelection() { CheckSelectable(); _listing.SetStoreMark(_index, false); }
    public void Select()
    {
        CheckSelectable();
        _listing.SelectStoreMark(_index);
    }

    private void CheckSelectable()
    {
        CheckCurrent();
        if (_store[_index].Kind == EntryKind.Parent) throw new InvalidOperationException("Parent navigation is not a selectable item.");
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
    // VoiceOver invokes AXSelect on a list item while moving its cursor. Exposing SelectionItem there
    // would turn ordinary arrow navigation into a mark change, then speak the list's mark summary
    // instead of the focused filename. The row still exposes its mark state in ItemStatus and HelpText;
    // explicit keyboard/command marking remains available. AT-SPI lists only selection items as the
    // children of a selection container, so Linux keeps the never-selected parent provider too.
    protected override object? GetProviderCore(Type providerType) =>
        providerType == typeof(ISelectionItemProvider) &&
            (OperatingSystem.IsMacOS() || (_kind == "parent navigation" && !OperatingSystem.IsLinux()))
            ? null : base.GetProviderCore(providerType);
    protected override string GetHelpTextCore() => Current ? _list.DescribeItem(_index, includeName: false) : "This item belongs to a previous listing and cannot be acted on.";
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
