using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Views;

/// <summary>What a synchronization may do to each side, and how it starts its jobs.</summary>
/// <param name="Submit">Starts the jobs (source is left: true) and returns false when nothing was started.</param>
public sealed record SyncContext(
    string LeftName, string RightName,
    bool LeftWritable, bool RightWritable, bool LeftRecycles, bool RightRecycles, bool IgnoresCase,
    Func<IReadOnlyList<SyncItem>, bool, bool, bool> Submit)
{
    /// <summary>Why the side cannot receive a synchronization, or null.</summary>
    public string? WhyNotTarget(bool left) => (left ? LeftWritable : RightWritable)
        ? null
        : "Synchronizing into a server folder or an archive is not supported yet; open the differences as results and copy them.";
}

/// <summary>
/// One-way synchronization preview (plan §16.2; approved scope: Update and Mirror, no two-way sync, no stored state).
/// Every step is listed and can be excluded; Mirror's removals are explicit items; nothing changes until Synchronize,
/// and then only ordinary jobs run.
/// </summary>
public sealed class SyncWindow : Window
{
    // Screen readers follow a list's focused row and, on Linux, hear focus after window activation.
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new FileCat.App.Controls.DescendantFocusWindowAutomationPeer(this);

    private TreeCompareResult _comparison;
    private SyncContext _context;
    private bool _closed;
    private readonly ComboBox _direction = new() { MinWidth = 320 };
    private readonly ComboBox _mode = new() { MinWidth = 300 };
    private readonly CheckBox _showAll = new() { Content = "Also show items left alone" };
    private readonly CheckBox _permanent = new() { Content = "Delete permanently: the target has no Recycle Bin", IsVisible = false };
    private readonly TextBlock _warning = new() { Classes = { "warning" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _sourcePath = EndpointBox();
    private readonly TextBox _targetPath = EndpointBox();
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    private readonly Button _run = new() { Content = "Synchronize", Classes = { "primary" } };
    private List<SyncItem> _items = [];
    private readonly Dictionary<(TreeDiffEntry Entry, bool SourceIsLeft, SyncAction Action), bool> _reviewedChoices = [];
    private string? _acknowledgedRemovalScope;
    private string _planNotice = string.Empty;
    private bool _planBuilt, _updatingConsent;

    public SyncWindow(TreeCompareResult comparison, SyncContext context)
    {
        _comparison = comparison;
        _context = context;
        Title = "Synchronize folders";
        Width = 1000; Height = 680; MinWidth = 600; MinHeight = 320;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (Exception) { }
        Avalonia.Automation.AutomationProperties.SetName(_direction, "Direction");
        Avalonia.Automation.AutomationProperties.SetName(_mode, "Mode");
        Avalonia.Automation.AutomationProperties.SetName(_list, "Synchronization steps");
        Avalonia.Automation.AutomationProperties.SetLiveSetting(_warning, Avalonia.Automation.AutomationLiveSetting.Polite);
        _direction.ItemsSource = new[] { "Left → right", "Right → left" };
        _direction.SelectedIndex = context.RightWritable || !context.LeftWritable ? 0 : 1;
        _mode.ItemsSource = new[] { "Update: copy new and newer items", "Mirror: make the target like the source" };
        _mode.SelectedIndex = 0;
        _direction.SelectionChanged += (_, _) => Propose();
        _mode.SelectionChanged += (_, _) => Propose();
        _showAll.IsCheckedChanged += (_, _) => Refresh();
        _permanent.IsCheckedChanged += (_, _) =>
        {
            if (_updatingConsent) return;
            _acknowledgedRemovalScope = _permanent.IsChecked == true ? RemovalScope() : null;
            UpdateSummary();
        };
        _list.ItemTemplate = new FuncDataTemplate<SyncItem>((item, _) => item is null ? new TextBlock() : Row(item));
        // Space includes or excludes the selected steps (all of them the same way), before the list's items take it.
        Controls.ListKeys.OnKey(_list, Key.Space, _ =>
        {
            if (_list.SelectedItems is null) return false;
            var selected = _list.SelectedItems.OfType<SyncItem>().Where(i => i.CanInclude).ToList();
            bool include = selected.Any(i => !i.Include);
            foreach (var i in selected)
            {
                i.Include = include;
                _reviewedChoices[(i.Entry, SourceIsLeft, i.Action)] = include;
            }
            Refresh(keepSelection: true);
            Controls.ListKeys.Focus(_list); // the refreshed list has new rows: keep the keyboard in it
            return true;
        });
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        _run.Click += (_, _) => Run();
        var options = new WrapPanel
        {
            Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 4, Margin = new Thickness(8, 6),
            Children = { _direction, _mode, _showAll },
        };
        var endpoints = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(8, 0, 8, 6) };
        void Endpoint(int row, string role, TextBox path)
        {
            var label = new TextBlock { Text = role, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetRow(label, row); endpoints.Children.Add(label);
            Grid.SetRow(path, row); Grid.SetColumn(path, 1); endpoints.Children.Add(path);
        }
        Endpoint(0, "Source", _sourcePath);
        Endpoint(1, "Target", _targetPath);
        var info = new StackPanel { Margin = new Thickness(8, 0, 8, 6), Spacing = 4, Children = { _warning, _permanent, _summary } };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, _run },
        };
        var root = new DockPanel();
        DockPanel.SetDock(options, Dock.Top);
        DockPanel.SetDock(endpoints, Dock.Top);
        DockPanel.SetDock(info, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(options);
        root.Children.Add(endpoints);
        root.Children.Add(info);
        root.Children.Add(buttons);
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
        // Once shown and laid out, the steps have the keyboard (arrows move, Space includes or excludes).
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Controls.ListKeys.Focus(_list), Avalonia.Threading.DispatcherPriority.Input);
        Closed += (_, _) =>
        {
            if (_closed) return;
            _closed = true;
            ControlRetirement.ClearItems(_list);
            _items = [];
            // The input is borrowed: another open preview or its caller can still own the original comparison.
            _comparison = new TreeCompareResult([], false, 0);
            _context = _context with { Submit = static (_, _, _) => false };
        };
        Propose();
    }

    private bool SourceIsLeft => _direction.SelectedIndex == 0;

    private SyncMode Mode => _mode.SelectedIndex == 1 ? SyncMode.Mirror : SyncMode.Update;

    /// <summary>Whether removals go to the target's Recycle Bin (otherwise they need permanent deletion chosen).</summary>
    public bool TargetRecycles => SourceIsLeft ? _context.RightRecycles : _context.LeftRecycles;

    public IReadOnlyList<SyncItem> Items => _items;

    public string Summary => _summary.Text ?? "";

    public void Choose(bool sourceIsLeft, SyncMode mode)
    {
        if (_closed) return;
        _direction.SelectedIndex = sourceIsLeft ? 0 : 1;
        _mode.SelectedIndex = mode == SyncMode.Mirror ? 1 : 0;
    }

    private void Propose()
    {
        if (_closed) return;
        if (_planBuilt)
        {
            ClearPermanentConsent();
            _planNotice = "Plan rebuilt for the current direction and mode. Choices for matching actions were kept; changed actions returned to their defaults. Review the included steps again.";
        }
        _items = SyncPlanner.Propose(_comparison, SourceIsLeft, Mode, _context.IgnoresCase);
        foreach (var item in _items)
            if (_reviewedChoices.TryGetValue((item.Entry, SourceIsLeft, item.Action), out bool include)) item.Include = include;
        _planBuilt = true;
        Refresh();
    }

    private void ClearPermanentConsent()
    {
        _acknowledgedRemovalScope = null;
        _updatingConsent = true;
        try { _permanent.IsChecked = false; }
        finally { _updatingConsent = false; }
    }

    private string? RemovalScope()
    {
        if (TargetRecycles) return null;
        var paths = _items.Where(i => i.Include && i.Action == SyncAction.Remove)
            .Select(i => i.Entry.RelativePath).Order(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0) return null;
        string target = SourceIsLeft ? _context.RightName : _context.LeftName;
        return (SourceIsLeft ? "L" : "R") + "\0" + target + "\0" + string.Join("\0", paths);
    }

    private void Refresh(bool keepSelection = false)
    {
        if (_closed) return;
        var selected = keepSelection ? _list.SelectedItems?.OfType<SyncItem>().ToList() : null;
        ControlRetirement.ClearItems(_list);
        _list.ItemsSource = _items.Where(i => _showAll.IsChecked == true || i.CanInclude).ToList();
        if (selected is not null) foreach (var s in selected) _list.SelectedItems?.Add(s);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        string source = SourceIsLeft ? _context.LeftName : _context.RightName;
        string target = SourceIsLeft ? _context.RightName : _context.LeftName;
        _sourcePath.Text = source;
        _targetPath.Text = target;
        Avalonia.Automation.AutomationProperties.SetName(_sourcePath, "Source folder: " + source);
        Avalonia.Automation.AutomationProperties.SetName(_targetPath, "Target folder: " + target);
        Avalonia.Automation.AutomationProperties.SetName(_direction, $"Synchronization direction: from {source} to {target}");
        ToolTip.SetTip(_sourcePath, source);
        ToolTip.SetTip(_targetPath, target);
        var whyNot = _context.WhyNotTarget(!SourceIsLeft);
        bool removals = _items.Any(i => i.Include && i.Action == SyncAction.Remove);
        string? removalScope = RemovalScope();
        if (_permanent.IsChecked == true && (removalScope is null || _acknowledgedRemovalScope != removalScope))
            ClearPermanentConsent();
        _permanent.Content = new TextBlock
        {
            Text = $"Delete permanently from target \"{Formatters.SafeName(target)}\" (no Recycle Bin)",
            TextWrapping = TextWrapping.Wrap,
        };
        Avalonia.Automation.AutomationProperties.SetName(_permanent, "Allow permanent deletion from target " + target + " for the currently included removals");
        _permanent.IsVisible = removals && !TargetRecycles;
        var warnings = new List<string>();
        if (_planNotice.Length > 0) warnings.Add(_planNotice);
        if (Mode == SyncMode.Mirror)
            warnings.Add($"Mirror changes the target \"{target}\": items only in this target are removed" + (TargetRecycles ? " (to the Recycle Bin)" : "") +
                         ", and differing files are replaced by the source's.");
        if (removals && !TargetRecycles && (_permanent.IsChecked != true || _acknowledgedRemovalScope != removalScope))
            warnings.Add("The target has no Recycle Bin: removals run only if you choose to delete permanently.");
        if (!_comparison.Complete) warnings.Add("The comparison did not finish: only what was compared is listed.");
        if (whyNot is not null) warnings.Add(whyNot);
        _warning.Text = string.Join(" ", warnings);
        _warning.IsVisible = warnings.Count > 0;

        string plan = SyncPlanner.Describe(RunnableItems());
        int leftAlone = _items.Count(i => !i.Include || !i.CanInclude);
        _summary.Text = (plan == "nothing to do" ? "Nothing to do." : $"Will {plan}.") +
                        (leftAlone > 0 ? $" {leftAlone:N0} differences are left alone." : "") +
                        " Nothing changes until you press Synchronize; Space includes or excludes the selected steps.";
        _run.IsEnabled = whyNot is null && plan != "nothing to do";
        _run.Content = plan == "nothing to do" ? "Synchronize" : "Synchronize: " + plan;
        Avalonia.Automation.AutomationProperties.SetName(_run, $"Synchronize from {source} to {target}: {plan}");
    }

    /// <summary>Included steps that may run: removals only to a Recycle Bin or with permanent deletion chosen.</summary>
    private List<SyncItem> RunnableItems()
    {
        bool mayRemove = TargetRecycles ||
            _permanent.IsChecked == true && _acknowledgedRemovalScope == RemovalScope();
        return _items.Where(i => i.Include && i.CanInclude && (i.Action != SyncAction.Remove || mayRemove)).ToList();
    }

    public void Run()
    {
        if (_closed || !_run.IsEnabled) return;
        if (_context.Submit(RunnableItems(), SourceIsLeft,
                !TargetRecycles && _permanent.IsChecked == true && _acknowledgedRemovalScope == RemovalScope())) Close();
    }

    private static TextBox EndpointBox()
    {
        var box = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2), MinWidth = 0, MaxHeight = 96,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        return box;
    }

    private static string Verb(SyncAction action) => action switch
    {
        SyncAction.Copy => "copy",
        SyncAction.ReplaceOlder => "replace older",
        SyncAction.Replace => "replace",
        SyncAction.Remove => "remove",
        _ => "leave",
    };

    private Control Row(SyncItem item)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,110,3*,3*"), Margin = new Thickness(0, 1) };
        var box = new CheckBox { IsChecked = item.Include, IsEnabled = item.CanInclude, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(box, $"{Verb(item.Action)} {item.Entry.RelativePath}");
        box.IsCheckedChanged += (_, _) =>
        {
            if (_closed || box.DataContext is not SyncItem current) return;
            current.Include = box.IsChecked == true;
            _reviewedChoices[(current.Entry, SourceIsLeft, current.Action)] = current.Include;
            UpdateSummary();
        };
        grid.Children.Add(box);
        void Cell(int column, string text, string? cls = null)
        {
            var t = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };
            if (cls is not null) t.Classes.Add(cls);
            Grid.SetColumn(t, column);
            grid.Children.Add(t);
        }
        Cell(1, Verb(item.Action), item.Action == SyncAction.Remove ? "warning" : item.CanInclude ? null : "muted");
        Cell(2, item.Entry.RelativePath + (item.Entry.IsFolder ? "/" : ""));
        Cell(3, item.Reason, "muted");
        return grid;
    }
}
