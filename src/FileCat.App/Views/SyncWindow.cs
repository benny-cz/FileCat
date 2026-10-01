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
    private readonly TreeCompareResult _comparison;
    private readonly SyncContext _context;
    private readonly ComboBox _direction = new() { MinWidth = 320 };
    private readonly ComboBox _mode = new() { MinWidth = 300 };
    private readonly CheckBox _showAll = new() { Content = "Also show items left alone" };
    private readonly CheckBox _permanent = new() { Content = "Delete permanently: the target has no Recycle Bin", IsVisible = false };
    private readonly TextBlock _warning = new() { Classes = { "warning" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    private readonly Button _run = new() { Content = "Synchronize", Classes = { "primary" } };
    private List<SyncItem> _items = [];

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
        _direction.ItemsSource = new[] { $"Left to right: {Short(context.LeftName)} → {Short(context.RightName)}", $"Right to left: {Short(context.RightName)} → {Short(context.LeftName)}" };
        _direction.SelectedIndex = context.RightWritable || !context.LeftWritable ? 0 : 1;
        _mode.ItemsSource = new[] { "Update: copy new and newer items", "Mirror: make the target like the source" };
        _mode.SelectedIndex = 0;
        _direction.SelectionChanged += (_, _) => Propose();
        _mode.SelectionChanged += (_, _) => Propose();
        _showAll.IsCheckedChanged += (_, _) => Refresh();
        _permanent.IsCheckedChanged += (_, _) => UpdateSummary();
        _list.ItemTemplate = new FuncDataTemplate<SyncItem>((item, _) => item is null ? new TextBlock() : Row(item));
        // Space includes or excludes the selected steps (all of them the same way), before the list's items take it.
        Controls.ListKeys.OnKey(_list, Key.Space, _ =>
        {
            if (_list.SelectedItems is null) return false;
            var selected = _list.SelectedItems.OfType<SyncItem>().Where(i => i.CanInclude).ToList();
            bool include = selected.Any(i => !i.Include);
            foreach (var i in selected) i.Include = include;
            Refresh(keepSelection: true);
            Controls.ListKeys.Focus(_list); // the refreshed list has new rows: keep the keyboard in it
            return true;
        });
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        _run.Click += (_, _) => Run();
        var options = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 6),
            Children = { _direction, _mode, _showAll },
        };
        var info = new StackPanel { Margin = new Thickness(8, 0, 8, 6), Spacing = 4, Children = { _warning, _permanent, _summary } };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, _run },
        };
        var root = new DockPanel();
        DockPanel.SetDock(options, Dock.Top);
        DockPanel.SetDock(info, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(options);
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
        _direction.SelectedIndex = sourceIsLeft ? 0 : 1;
        _mode.SelectedIndex = mode == SyncMode.Mirror ? 1 : 0;
    }

    private void Propose()
    {
        _items = SyncPlanner.Propose(_comparison, SourceIsLeft, Mode, _context.IgnoresCase);
        Refresh();
    }

    private void Refresh(bool keepSelection = false)
    {
        var selected = keepSelection ? _list.SelectedItems?.OfType<SyncItem>().ToList() : null;
        _list.ItemsSource = null;
        _list.ItemsSource = _items.Where(i => _showAll.IsChecked == true || i.CanInclude).ToList();
        if (selected is not null) foreach (var s in selected) _list.SelectedItems?.Add(s);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var whyNot = _context.WhyNotTarget(!SourceIsLeft);
        bool removals = _items.Any(i => i.Include && i.Action == SyncAction.Remove);
        _permanent.IsVisible = removals && !TargetRecycles;
        var warnings = new List<string>();
        if (Mode == SyncMode.Mirror)
            warnings.Add("Mirror is not two-way synchronization: items only in the target are removed" + (TargetRecycles ? " (to the Recycle Bin)" : "") +
                         ", and differing files are replaced by the source's.");
        if (removals && !TargetRecycles && _permanent.IsChecked != true)
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
    }

    /// <summary>Included steps that may run: removals only to a Recycle Bin or with permanent deletion chosen.</summary>
    private List<SyncItem> RunnableItems() =>
        _items.Where(i => i.Include && i.CanInclude && (i.Action != SyncAction.Remove || TargetRecycles || _permanent.IsChecked == true)).ToList();

    public void Run()
    {
        if (!_run.IsEnabled) return;
        if (_context.Submit(RunnableItems(), SourceIsLeft, !TargetRecycles && _permanent.IsChecked == true)) Close();
    }

    private static string Short(string path) => Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path;

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
            item.Include = box.IsChecked == true;
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
