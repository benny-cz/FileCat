using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Views;

/// <summary>
/// Recursive directory-difference preview (plan §16.2, P7): what is missing, newer, different, or undecided below two
/// folders. It changes nothing; each side's differences can be opened as a result set, where the usual commands act on
/// them.
/// </summary>
public sealed class DirectoryDiffWindow : Window
{
    private static readonly List<DirectoryDiffWindow> s_open = [];
    private static readonly (string Title, Func<TreeDiffEntry, bool> Show)[] Filters =
    [
        ("Differences", e => e.IsDifference),
        ("Only on the left", e => e.Kind == TreeDiffKind.LeftOnly),
        ("Only on the right", e => e.Kind == TreeDiffKind.RightOnly),
        ("Newer on the left", e => e.Kind == TreeDiffKind.LeftNewer),
        ("Newer on the right", e => e.Kind == TreeDiffKind.RightNewer),
        ("Different", e => e.Kind is TreeDiffKind.Different or TreeDiffKind.TypeMismatch),
        ("Undecided", e => e.Kind == TreeDiffKind.Unknown),
        ("Everything", _ => true),
    ];

    private TreeCompareResult? _result;
    private bool _closed, _finished;
    private Action<IReadOnlyList<TreeDiffEntry>, bool> _openSide;
    private readonly CancellationTokenSource _stop = new();
    private readonly Button _stopButton = new() { Content = "Stop", Classes = { "danger" } };
    private readonly Button _openLeft = new() { Content = "Open left items as results", IsEnabled = false };
    private readonly Button _openRight = new() { Content = "Open right items as results", IsEnabled = false };
    private readonly Button _sync = new() { Content = "Synchronize…", IsEnabled = false };
    private SyncContext? _syncContext;
    private Action<TreeDiffEntry>? _compareFiles;
    private readonly TextBlock _compareHint = new()
    {
        Text = "Enter or a double-click on a file present on both sides compares their contents.",
        Classes = { "muted", "small" },
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
    };
    private readonly ListBox _list = new();
    private readonly ComboBox _filter = new() { MinWidth = 180 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };

    public static IReadOnlyList<DirectoryDiffWindow> OpenWindows => s_open;

    /// <summary>Shows the window at once and runs the comparison in the background; Stop keeps what was compared.</summary>
    /// <param name="compare">The comparison (progress: the folder being compared).</param>
    /// <param name="openSide">Opens the given entries of one side (true: left) as a result set in that side's panel.</param>
    /// <param name="sync">How a one-way synchronization of these folders starts; null offers none.</param>
    /// <param name="compareFiles">Compares the two files of an entry (Enter or a double-click on it); null offers nothing.</param>
    public static DirectoryDiffWindow Start(string leftName, string rightName, string criteria,
        Func<Action<string>, CancellationToken, TreeCompareResult> compare, Action<IReadOnlyList<TreeDiffEntry>, bool> openSide, SyncContext? sync = null,
        Action<TreeDiffEntry>? compareFiles = null)
        => StartAsync(leftName, rightName, criteria, (progress, ct) => Task.Run(() => compare(progress, ct)), openSide, sync, compareFiles);

    public static DirectoryDiffWindow StartAsync(string leftName, string rightName, string criteria,
        Func<Action<string>, CancellationToken, Task<TreeCompareResult>> compare, Action<IReadOnlyList<TreeDiffEntry>, bool> openSide, SyncContext? sync = null,
        Action<TreeDiffEntry>? compareFiles = null)
    {
        var window = new DirectoryDiffWindow(leftName, rightName, criteria, openSide) { _syncContext = sync, _compareFiles = compareFiles };
        window._compareHint.IsVisible = compareFiles is not null;
        window._sync.IsVisible = sync is not null;
        s_open.Add(window);
        window.Closed += (_, _) =>
        {
            if (window._closed) return;
            window._closed = true;
            window._stop.Cancel();
            if (window._finished) window._stop.Dispose();
            s_open.Remove(window);
            ControlRetirement.ClearItems(window._list);
            window._result = null;
            window._syncContext = null;
            window._compareFiles = null;
            window._openSide = static (_, _) => { };
        };
        window.Show();
        _ = window.RunAsync(compare);
        return window;
    }

    public bool IsComparing => !_closed && _result is null;

    private async Task RunAsync(Func<Action<string>, CancellationToken, Task<TreeCompareResult>> compare)
    {
        int folders = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Progress(string folder)
        {
            folders++;
            if (clock.ElapsedMilliseconds < 200) return;
            clock.Restart();
            int n = folders;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!_closed && _result is null) _summary.Text = $"Comparing… {n:N0} folders so far: {folder}"; });
        }
        TreeCompareResult result;
        try
        {
            result = await compare(Progress, _stop.Token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            result = new TreeCompareResult([new TreeDiffEntry(".", TreeDiffKind.Unknown, null, null, ex.Message)], false, folders);
        }
        catch (OperationCanceledException) { result = new TreeCompareResult([], false, folders, Canceled: true); }
        finally
        {
            _finished = true;
            if (_closed) _stop.Dispose();
        }
        if (_closed) return;
        _result = result;
        _stopButton.IsVisible = false;
        _openLeft.IsEnabled = _openRight.IsEnabled = true;
        _sync.IsEnabled = _syncContext is not null && _result.Entries.Any(e => e.IsDifference);
        Refresh();
    }

    private DirectoryDiffWindow(string leftName, string rightName, string criteria, Action<IReadOnlyList<TreeDiffEntry>, bool> openSide)
    {
        CriteriaText = criteria;
        _openSide = openSide;
        Title = $"Compare folders: {Path.GetFileName(leftName.TrimEnd('\\', '/'))} ↔ {Path.GetFileName(rightName.TrimEnd('\\', '/'))}";
        Width = 1100; Height = 700; MinWidth = 600; MinHeight = 300;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (Exception) { }
        Avalonia.Automation.AutomationProperties.SetName(_list, "Folder differences");
        Avalonia.Automation.AutomationProperties.SetName(_filter, "Show");
        _filter.ItemsSource = Filters.Select(f => f.Title).ToList();
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => Refresh();
        _list.ItemTemplate = new FuncDataTemplate<TreeDiffEntry>((e, _) => e is null ? new TextBlock() : Row(e));
        Controls.ListKeys.OnKey(_list, Key.Enter, _ => CompareSelected());
        _list.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null) CompareSelected();
        };
        var openLeft = _openLeft;
        var openRight = _openRight;
        _stopButton.Click += (_, _) => _stop.Cancel();
        ToolTip.SetTip(openLeft, "The shown items that exist on the left, as a result set in the left panel, ready for F5, F6, or F8");
        ToolTip.SetTip(openRight, "The shown items that exist on the right, as a result set in the right panel");
        openLeft.Click += (_, _) => _openSide(Shown().Where(e => e.Left is not null).ToList(), true);
        openRight.Click += (_, _) => _openSide(Shown().Where(e => e.Right is not null).ToList(), false);
        ToolTip.SetTip(_sync, "One-way: Update copies new and newer items; Mirror also removes what only the target has. Every step is previewed.");
        _sync.Click += (_, _) => OpenSync();
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 6),
            Children = { new TextBlock { Text = "Show:", VerticalAlignment = VerticalAlignment.Center }, _filter, openLeft, openRight, _sync, _stopButton },
        };
        var info = new StackPanel
        {
            Margin = new Thickness(8, 0, 8, 6),
            Children =
            {
                new TextBlock { Text = $"{leftName}  ↔  {rightName}", TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = criteria, Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap },
                _compareHint,
                _summary,
            },
        };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(info, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(info);
        root.Children.Add(_list);
        Content = Controls.ThemeLayers.Over(root, _list);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        Refresh();
    }

    public string Summary => _summary.Text ?? "";

    /// <summary>Whether Synchronize is offered for these folders (tests).</summary>
    internal bool OffersSync => _sync.IsVisible;

    /// <summary>What the comparison went by, and anything not offered, as the window says it (tests).</summary>
    internal string CriteriaText { get; private set; } = "";

    public IReadOnlyList<TreeDiffEntry> ShownEntries => Shown();

    public void ShowFilter(int index) => _filter.SelectedIndex = index;

    /// <summary>The synchronization preview for this comparison; null before it finishes or without a context.</summary>
    public SyncWindow? OpenSync()
    {
        if (_closed || _result is null || _syncContext is null) return null;
        var window = new SyncWindow(_result, _syncContext);
        window.Show(this);
        return window;
    }

    public void OpenSide(bool left) { if (!_closed) _openSide(Shown().Where(e => left ? e.Left is not null : e.Right is not null).ToList(), left); }

    /// <summary>Compares the contents of the selected entry's two files; false when it is not a file on both sides.</summary>
    public bool CompareSelected()
    {
        if (_closed || _compareFiles is null || _list.SelectedItem is not TreeDiffEntry { Left: not null, Right: not null, IsFolder: false } entry) return false;
        _compareFiles(entry);
        return true;
    }

    /// <summary>Selects the entry with <paramref name="relativePath"/> (tests).</summary>
    internal bool Select(string relativePath)
    {
        _list.SelectedItem = Shown().FirstOrDefault(e => e.RelativePath == relativePath);
        return _list.SelectedItem is not null;
    }

    private List<TreeDiffEntry> Shown()
    {
        if (_result is null) return [];
        var show = Filters[Math.Max(0, _filter.SelectedIndex)].Show;
        return _result.Entries.Where(show).ToList();
    }

    private void Refresh()
    {
        if (_result is null)
        {
            _summary.Text = "Comparing…";
            return;
        }
        var shown = Shown();
        ControlRetirement.ClearItems(_list);
        _list.ItemsSource = shown;
        int differences = _result.Entries.Count(e => e.IsDifference);
        var parts = new List<string>();
        void Part(TreeDiffKind kind, string text)
        {
            int n = _result.Count(kind);
            if (n > 0) parts.Add($"{n:N0} {text}");
        }
        Part(TreeDiffKind.LeftOnly, "only left");
        Part(TreeDiffKind.RightOnly, "only right");
        Part(TreeDiffKind.LeftNewer, "newer left");
        Part(TreeDiffKind.RightNewer, "newer right");
        Part(TreeDiffKind.Different, "different");
        Part(TreeDiffKind.TypeMismatch, "file against folder");
        Part(TreeDiffKind.Unknown, "undecided");
        _summary.Text = (differences == 0 ? $"No differences in {_result.FoldersVisited:N0} folders ({_result.Count(TreeDiffKind.Same):N0} items the same)."
                            : $"{differences:N0} differences in {_result.FoldersVisited:N0} folders: {string.Join(", ", parts)}; {_result.Count(TreeDiffKind.Same):N0} the same.")
                        + (_result.Canceled ? " Stopped: the rest was not compared." : _result.Complete ? "" : $" Stopped after {TreeCompare.MaxEntries:N0} items; the rest was not compared.")
                        + (shown.Count == 0 ? " Nothing matches the filter." : "");
    }

    private static string Describe(TreeDiffKind kind) => kind switch
    {
        TreeDiffKind.Same => "same",
        TreeDiffKind.LeftOnly => "only left",
        TreeDiffKind.RightOnly => "only right",
        TreeDiffKind.LeftNewer => "newer left",
        TreeDiffKind.RightNewer => "newer right",
        TreeDiffKind.Different => "different",
        TreeDiffKind.TypeMismatch => "file / folder",
        _ => "undecided",
    };

    private static string Side(EntryData? e) =>
        e is not { } d ? "" : d.IsContainer ? "folder" : $"{Formatters.Size(d.Size)}  {Formatters.Date(d)}";

    private static Control Row(TreeDiffEntry e)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,110,190,190,2*"), Margin = new Thickness(0, 1) };
        void Cell(int column, string text, string? cls = null)
        {
            var t = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0) };
            if (cls is not null) t.Classes.Add(cls);
            Grid.SetColumn(t, column);
            grid.Children.Add(t);
        }
        Cell(0, e.RelativePath + (e.IsFolder ? "/" : ""));
        Cell(1, Describe(e.Kind), e.Kind == TreeDiffKind.Unknown ? "error" : null);
        Cell(2, Side(e.Left), "muted");
        Cell(3, Side(e.Right), "muted");
        Cell(4, e.Detail ?? "", "small");
        return grid;
    }
}
