using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Search;

namespace FileCat.App.Views;

/// <summary>
/// An accessible complete selection review with a fixed page of real items. Paging reads only already listed
/// records; it never resolves a provider item, reads file content, or substitutes the focused fallback item.
/// </summary>
internal sealed class MarkedItemsReview : Grid, IDisposable
{
    internal const int PageSize = 64;
    private readonly TabViewModel _tab;
    private readonly ListingModel _listing;
    private readonly EntryStore _store;
    private readonly TextBlock _summary;
    private readonly ListBox _items;
    private readonly TextBox _details;
    private readonly Button _previous, _next;
    private readonly List<int> _cursors = [-1];
    private int _page;
    private int _lastIndex = -1;
    private bool _retired;
    private readonly int[] _indices = new int[PageSize + 1];

    internal MarkedItemsReview(TabViewModel tab)
    {
        _tab = tab;
        _listing = tab.Listing;
        _store = _listing.Store;
        RowDefinitions = RowDefinitions.Parse("Auto,*,Auto,Auto");
        Height = 280;
        RowSpacing = 6;
        _summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(_summary, AutomationLiveSetting.Polite);
        _items = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<ReviewItem>((item, _) => new TextBlock
            {
                Text = item?.Label,
                TextWrapping = TextWrapping.Wrap,
            }),
        };
        AutomationProperties.SetName(_items, "Marked items on this page");
        AutomationProperties.SetHelpText(_items, "Every item in this review is marked. Next page includes further marks, including those hidden in the file list.");
        _details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 64 };
        AutomationProperties.SetName(_details, "Full details of the reviewed item");
        _items.SelectionChanged += (_, _) => _details.Text = (_items.SelectedItem as ReviewItem)?.Details ?? string.Empty;
        _previous = new Button { Content = "Previous page" };
        _next = new Button { Content = "Next page" };
        AutomationProperties.SetName(_previous, "Previous page of marked items");
        AutomationProperties.SetName(_next, "Next page of marked items");
        _previous.Click += (_, _) => { if (_page > 0) { _page--; LoadPage(); } };
        _next.Click += (_, _) =>
        {
            if (_retired || !_next.IsEnabled) return;
            if (_cursors.Count == _page + 1) _cursors.Add(_lastIndex);
            _page++;
            LoadPage();
        };
        var paging = new WrapPanel { Orientation = Orientation.Horizontal };
        _previous.Margin = new Thickness(0, 0, 8, 0);
        paging.Children.Add(_previous);
        paging.Children.Add(_next);
        SetRow(_items, 1);
        SetRow(_details, 2);
        SetRow(paging, 3);
        Children.Add(_summary);
        Children.Add(_items);
        Children.Add(_details);
        Children.Add(paging);
        _listing.Changed += OnListingChanged;
        LoadPage();
    }

    internal Control InitialFocus => _items;
    internal IReadOnlyList<ReviewItem> CurrentItems { get; private set; } = [];
    internal string Summary => _summary.Text ?? string.Empty;

    internal static async Task ShowAsync(TabViewModel tab, IDialogService dialogs)
    {
        if (tab.Listing.IsDisposed) return;
        using var review = new MarkedItemsReview(tab);
        await dialogs.ShowCustomAsync("Review marked items", review,
            [new DialogButton("Close", true, IsCancel: true)], review.InitialFocus);
    }

    private void OnListingChanged(object? sender, ListingChange change)
    {
        if (_retired) return;
        if (_listing.IsDisposed || !ReferenceEquals(_listing.Store, _store))
        {
            _retired = true;
            CurrentItems = [];
            _items.ItemsSource = null;
            _details.Text = string.Empty;
            _previous.IsEnabled = _next.IsEnabled = false;
            _summary.Text = "The listing changed. Close and reopen Review marked items to review its current selection.";
            return;
        }
        if ((change & ListingChange.Marks) != 0)
        {
            _page = 0;
            _cursors.Clear();
            _cursors.Add(-1);
            LoadPage("Selection changed; review restarted at the first page. ");
        }
        else if ((change & ListingChange.Rows) != 0) LoadPage();
    }

    private void LoadPage(string prefix = "")
    {
        if (_retired || _listing.IsDisposed) return;
        int count = _listing.CopyMarkedStoreIndices(_cursors[_page], _indices);
        int shown = Math.Min(PageSize, count);
        var items = new ReviewItem[shown];
        for (int i = 0; i < shown; i++)
        {
            int index = _indices[i];
            var entry = _store[index];
            bool hidden = _listing.GetVisibleIndex(index) < 0;
            string name = FileListControl.ItemName(entry);
            string kind = FileListControl.ItemKind(entry);
            string label = $"{name}, {kind}" + (hidden ? ", hidden in file list" : string.Empty);
            string location = FileListControl.ItemLocation(_tab, entry);
            // Find and working sets can contain identical names from distinct originals. Their
            // standard list-item labels must identify the original without provider resolution.
            if (entry.Tag is ResultTag) label += ", location: " + location;
            string details = label + FileListControl.ItemWarnings(entry) + Environment.NewLine + "Location: " + location;
            if (entry.Size >= 0 && !entry.IsContainer) details += Environment.NewLine + "Size: " + Formatters.SizeWithUnit(entry.Size);
            if (entry.Modified > 0) details += Environment.NewLine + "Modified: " + Formatters.Date(entry);
            items[i] = new ReviewItem(index, name, label, details, hidden);
        }
        int selected = Math.Clamp(_items.SelectedIndex, 0, Math.Max(0, shown - 1));
        CurrentItems = items;
        _items.ItemsSource = items;
        _items.SelectedIndex = shown > 0 ? selected : -1;
        _lastIndex = shown > 0 ? _indices[shown - 1] : _cursors[_page];
        _previous.IsEnabled = _page > 0;
        _next.IsEnabled = count > PageSize;
        var stats = _listing.GetMarkStats();
        _summary.Text = prefix + (shown == 0
            ? "No marked items. Commands use the focused item; it is not marked."
            : $"Marked items {_page * PageSize + 1:N0}–{_page * PageSize + shown:N0} of {stats.Count:N0}. {stats.HiddenByFilter:N0} marks are hidden in the file list.");
    }

    public void Dispose() => _listing.Changed -= OnListingChanged;

    internal sealed record ReviewItem(int StoreIndex, string Name, string Label, string Details, bool Hidden);
}
