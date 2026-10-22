using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.Operations;

namespace FileCat.App.Views;

/// <summary>A complete, virtualized review of the exact rows submitted to bulk rename.</summary>
internal sealed class BulkRenameReview : StackPanel
{
    internal sealed class Row
    {
        public Row(int index, RenamePreview plan)
        {
            Index = index;
            Plan = plan;
            OldPath = plan.Item.FileSystemPath ?? plan.OldName;
            string? folder = Path.GetDirectoryName(OldPath);
            NewPath = folder is null ? plan.NewName : folder + Path.DirectorySeparatorChar + plan.NewName;
        }

        public int Index { get; }
        public RenamePreview Plan { get; }
        public bool ProposedChange => !string.Equals(Plan.OldName, Plan.NewName, StringComparison.Ordinal);
        public string OldPath { get; }
        public string NewPath { get; }
        public string Label => $"{Index + 1:N0}. {Formatters.SafeName(Plan.OldName)}  →  {Formatters.SafeName(Plan.NewName)}";
        public string Detail => $"From: {Formatters.SafeName(OldPath)}\nTo: {Formatters.SafeName(NewPath)}" +
            (Plan.Problem is { } problem ? $"\nProblem: {Formatters.SafeName(problem)}" : "");
    }

    private readonly ComboBox _filter;
    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly TextBox _details;
    private readonly Button _problems;
    private readonly Button _nextProblem;
    private readonly Button _previousMapping;
    private readonly Button _nextMapping;
    private readonly TextBlock _position;
    private IReadOnlyList<Row> _all = [];

    internal BulkRenameReview()
    {
        Spacing = 5;
        _filter = new ComboBox { MinWidth = 145, ItemsSource = new[] { "All (0)", "Problems (0)", "Changed (0)" }, SelectedIndex = 0 };
        AutomationProperties.SetName(_filter, "Preview filter");
        _search = new TextBox { Width = 190, PlaceholderText = "Search names or paths" };
        AutomationProperties.SetName(_search, "Search rename preview");
        _problems = new Button { Content = "Problems (0)", IsEnabled = false };
        AutomationProperties.SetName(_problems, "Show blocking rename problems");
        _nextProblem = new Button { Content = "Next problem", IsEnabled = false };
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = double.NaN };
        tools.Children.Add(_filter);
        tools.Children.Add(_search);
        tools.Children.Add(_problems);
        tools.Children.Add(_nextProblem);
        _list = new ListBox { Height = 280 };
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _list.ItemTemplate = new FuncDataTemplate<Row>((row, _) => row is null ? new TextBlock() : Render(row));
        AutomationProperties.SetName(_list, "Preview of new names");
        AutomationProperties.SetHelpText(_list, "Every selected item is available here. Filter to problems or changed names, search paths, or use Previous and Next mapping to inspect every full path below.");
        _previousMapping = new Button { Content = "Previous mapping", IsEnabled = false,
            Margin = new Thickness(0, 0, 6, 0) };
        _nextMapping = new Button { Content = "Next mapping", IsEnabled = false,
            Margin = new Thickness(0, 0, 6, 0) };
        _position = new TextBlock { Text = "No mappings shown", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0) };
        AutomationProperties.SetName(_previousMapping, "Previous rename mapping");
        AutomationProperties.SetName(_nextMapping, "Next rename mapping");
        AutomationProperties.SetName(_position, "No rename mappings match the current preview filter.");
        AutomationProperties.SetLiveSetting(_position, AutomationLiveSetting.Polite);
        tools.Children.Add(_previousMapping);
        tools.Children.Add(_nextMapping);
        tools.Children.Add(_position);
        _details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 75 };
        AutomationProperties.SetName(_details, "Full old and new paths of selected rename");
        AutomationProperties.SetLiveSetting(_details, AutomationLiveSetting.Polite);
        Children.Add(tools);
        Children.Add(_list);
        Children.Add(_details);
        _filter.SelectionChanged += (_, _) => UpdateView();
        _search.TextChanged += (_, _) => UpdateView();
        _list.SelectionChanged += (_, _) => UpdateSelectionPresentation();
        _problems.Click += (_, _) => ShowProblems();
        _nextProblem.Click += (_, _) => NextProblem();
        _previousMapping.Click += (_, _) => StepMapping(-1);
        _nextMapping.Click += (_, _) => StepMapping(1);
    }

    internal IReadOnlyList<Row> AllRows => _all;
    internal IReadOnlyList<Row> ShownRows { get; private set; } = [];
    internal int ProblemCount { get; private set; }
    internal int ChangedCount { get; private set; }

    internal void SetRows(IReadOnlyList<RenamePreview> rows)
    {
        int selected = (_list.SelectedItem as Row)?.Index ?? -1;
        int filter = _filter.SelectedIndex;
        _all = rows.Select((row, index) => new Row(index, row)).ToArray();
        ProblemCount = _all.Count(r => r.Plan.Problem is not null);
        ChangedCount = _all.Count(r => r.ProposedChange);
        _filter.ItemsSource = new[] { $"All ({_all.Count:N0})", $"Problems ({ProblemCount:N0})", $"Changed ({ChangedCount:N0})" };
        _filter.SelectedIndex = filter;
        _problems.Content = $"Problems ({ProblemCount:N0})";
        _problems.IsEnabled = _nextProblem.IsEnabled = ProblemCount > 0;
        UpdateView(selected);
    }

    private void ShowProblems()
    {
        _search.Text = string.Empty;
        _filter.SelectedIndex = 1;
        Select(_all.FirstOrDefault(r => r.Plan.Problem is not null));
    }

    private void NextProblem()
    {
        if (ProblemCount == 0) return;
        int selected = (_list.SelectedItem as Row)?.Index ?? -1;
        var next = _all.FirstOrDefault(r => r.Index > selected && r.Plan.Problem is not null)
            ?? _all.First(r => r.Plan.Problem is not null);
        _search.Text = string.Empty;
        _filter.SelectedIndex = 1;
        Select(next);
    }

    private void Select(Row? row)
    {
        if (row is null) return;
        _list.SelectedItem = row;
        _list.ScrollIntoView(row);
        _list.Focus();
    }

    private void StepMapping(int direction)
    {
        int current = SelectedShownIndex();
        int next = current + direction;
        if (next < 0 || next >= ShownRows.Count) return;
        _list.SelectedItem = ShownRows[next];
        _list.ScrollIntoView(ShownRows[next]);
    }

    private int SelectedShownIndex()
    {
        for (int i = 0; i < ShownRows.Count; i++)
            if (ReferenceEquals(ShownRows[i], _list.SelectedItem)) return i;
        return -1;
    }

    private void UpdateSelectionPresentation()
    {
        int index = SelectedShownIndex();
        var selected = index >= 0 ? ShownRows[index] : null;
        _details.Text = selected?.Detail ?? string.Empty;
        _position.Text = selected is null ? "No mappings shown" : $"{index + 1:N0} of {ShownRows.Count:N0} shown";
        AutomationProperties.SetName(_position, selected is null
            ? "No rename mappings match the current preview filter."
            : $"Rename mapping {index + 1:N0} of {ShownRows.Count:N0} shown. {selected.Detail}");
        _previousMapping.IsEnabled = index > 0;
        _nextMapping.IsEnabled = index >= 0 && index < ShownRows.Count - 1;
        AutomationProperties.SetHelpText(_previousMapping, index > 0
            ? "Previous mapping: " + ShownRows[index - 1].Detail : "No previous mapping.");
        AutomationProperties.SetHelpText(_nextMapping, index >= 0 && index < ShownRows.Count - 1
            ? "Next mapping: " + ShownRows[index + 1].Detail : "No next mapping.");
    }

    private void UpdateView(int selected = -1)
    {
        string query = (_search.Text ?? string.Empty).Trim();
        if (query.Length > 256) query = query[..256];
        int filter = _filter.SelectedIndex;
        ShownRows = _all.Where(row => (filter switch
            {
                1 => row.Plan.Problem is not null,
                2 => row.ProposedChange,
                _ => true,
            }) && (query.Length == 0 || row.OldPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.NewPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Plan.Problem?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)).ToArray();
        _list.ItemsSource = ShownRows;
        _list.SelectedItem = ShownRows.FirstOrDefault(row => row.Index == selected) ?? ShownRows.FirstOrDefault();
        UpdateSelectionPresentation();
    }

    private static Control Render(Row row)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = row.Label, TextTrimming = TextTrimming.CharacterEllipsis });
        if (row.Plan.Problem is { } problem)
            panel.Children.Add(new TextBlock { Text = "⚠ " + Formatters.SafeName(problem), Classes = { "error" }, TextTrimming = TextTrimming.CharacterEllipsis });
        AutomationProperties.SetName(panel, row.Detail);
        ToolTip.SetTip(panel, row.Detail);
        return panel;
    }
}
