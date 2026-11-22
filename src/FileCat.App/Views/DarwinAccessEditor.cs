using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;

namespace FileCat.App.Views;

internal sealed class DarwinAccessEditor : StackPanel
{
    private readonly ImmutableArray<DarwinAce> _original;
    private readonly string _authority;
    private readonly ListBox _entries = new() { MaxHeight = 175, MinHeight = 60, ItemsPanel = PermissionInspectorView.AllRows(),
        ItemTemplate = new FuncDataTemplate<string>((value, _) => new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap }) };
    private readonly ComboBox _operation = new();
    private readonly ComboBox _position = new();
    private readonly TextBox _uuid = new() { MaxLength = 64 };
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "Allow", "Deny" } };
    private readonly StackPanel _fields = new() { Spacing = 6 };
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _acceptRemoval = new() { Content = "Review removal of all extended access entries" };
    private readonly Button _reset = new() { Content = "Discard entry changes", IsVisible = false };
    private readonly List<(uint Bit, CheckBox Box)> _rights = [], _inheritance = [];
    private readonly bool _directory;
    private bool _setting;
    private enum Operation { Edit, Insert, RemoveEntry, RemoveAcl }

    internal DarwinAccessEditor(PermissionSnapshot snapshot, Func<string, Task>? copy)
    {
        var baseline = snapshot.Access.Value?.Acl as DarwinAcl;
        _original = baseline?.Entries ?? [];
        _directory = snapshot.Binding?.Identity.Kind is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot;
        _authority = snapshot.Subject.Authority.Id;
        Spacing = 7;
        AutomationProperties.SetName(_entries, "Ordered Darwin access entries");
        AutomationProperties.SetName(_operation, "Darwin ACL operation");
        AutomationProperties.SetName(_position, "Darwin ACL insertion position");
        AutomationProperties.SetName(_uuid, "Darwin entry UUID");
        AutomationProperties.SetName(_kind, "Darwin entry type");
        AutomationProperties.SetLiveSetting(_notice, AutomationLiveSetting.Polite);
        _entries.ItemsSource = _original.Select((entry, index) => $"Entry {index + 1} · {PermissionInspectorView.DarwinRule(entry, _directory)}").ToArray();
        _operation.ItemsSource = new[] { "Edit selected entry", "Insert entry", "Remove selected entry", "Remove extended ACL" };
        _position.ItemsSource = Enumerable.Range(1, _original.Length).Select(index => $"Before entry {index}").Append("At end").ToArray();
        _operation.SelectedIndex = baseline is null ? (int)Operation.Insert : (int)Operation.Edit;
        _entries.SelectedIndex = _original.IsEmpty ? -1 : 0;
        Children.Add(_entries);
        if (copy is not null && !_original.IsEmpty)
        {
            var button = new Button { Content = "Copy selected UUID" };
            button.Click += async (_, _) => { if (Selected is { } entry) await copy(entry.Principal.Id); };
            Children.Add(button);
        }
        Children.Add(_operation);
        Children.Add(_position);
        _fields.Children.Add(new Label { Content = "UUID", Target = _uuid });
        _fields.Children.Add(_uuid);
        _fields.Children.Add(_kind);
        var rights = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (bit, label) in PermissionInspectorView.DarwinRights(_directory)) AddCheck(rights, _rights, bit, label);
        _fields.Children.Add(rights);
        if (_directory)
        {
            var inheritance = new WrapPanel { Orientation = Orientation.Horizontal };
            AddCheck(inheritance, _inheritance, DarwinPermissionAcl.FileInherit, "Inherit to files");
            AddCheck(inheritance, _inheritance, DarwinPermissionAcl.DirectoryInherit, "Inherit to folders");
            AddCheck(inheritance, _inheritance, DarwinPermissionAcl.LimitInherit, "Limit inheritance");
            AddCheck(inheritance, _inheritance, DarwinPermissionAcl.InheritOnly, "Inherit only");
            _fields.Children.Add(inheritance);
        }
        Children.Add(_fields);
        Children.Add(_acceptRemoval);
        Children.Add(_notice);
        Children.Add(_reset);
        _entries.SelectionChanged += (_, _) => ResetFields();
        _operation.SelectionChanged += (_, _) => ResetFields();
        _position.SelectionChanged += (_, _) => OnEdit();
        _kind.SelectionChanged += (_, _) => OnEdit();
        _uuid.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) OnEdit(); };
        _acceptRemoval.IsCheckedChanged += (_, _) => OnEdit();
        _reset.Click += (_, _) => ResetFields();
        ResetFields();

        void AddCheck(WrapPanel panel, List<(uint Bit, CheckBox Box)> group, uint bit, string label)
        {
            var box = new CheckBox { Content = label };
            group.Add((bit, box));
            panel.Children.Add(box);
            box.IsCheckedChanged += (_, _) => OnEdit();
        }
    }

    internal event Action? Changed;
    private Operation CurrentOperation => (Operation)_operation.SelectedIndex;
    private DarwinAce? Selected => (uint)_entries.SelectedIndex < (uint)_original.Length ? _original[_entries.SelectedIndex] : null;

    internal (DarwinAclDelta? Change, string? Error) ReadChange()
    {
        if (CurrentOperation == Operation.RemoveAcl)
            return _acceptRemoval.IsChecked == true ? (new RemoveDarwinAcl(), null) : (null, "Confirm removal of the extended ACL before applying.");
        var selected = Selected;
        if (CurrentOperation == Operation.RemoveEntry)
            return selected is { Kind: DarwinAceKind.Allow or DarwinAceKind.Deny } original
                ? (new ChangeDarwinAce(_entries.SelectedIndex, original, null), null) : (null, null);
        bool insert = CurrentOperation == Operation.Insert;
        if (insert && !_reset.IsVisible) return (null, null);
        if (!insert && selected is not { Kind: DarwinAceKind.Allow or DarwinAceKind.Deny }) return (null, null);
        if (insert && _position.SelectedIndex < 0) return (null, "Choose an insertion position in the ordered ACL.");
        if (!Guid.TryParseExact(_uuid.Text, "D", out var uuid) || uuid == Guid.Empty)
            return (null, "Enter a Darwin UUID in its full hyphenated form.");
        uint priorFlags = insert ? 0 : selected!.Value.Flags;
        uint rights = ReadBits(_rights) | (insert ? 0 : selected!.Value.Rights & ~DarwinPermissionAcl.EditableRights);
        uint flags = _directory ? (priorFlags & ~DarwinPermissionAcl.InheritanceFlags) | ReadBits(_inheritance) : priorFlags;
        var rule = new DarwinAce(new Principal(PrincipalKind.DarwinUuid, uuid.ToString("D"), _authority),
            _kind.SelectedIndex == 1 ? DarwinAceKind.Deny : DarwinAceKind.Allow, flags, rights);
        if (insert) return (new AddDarwinAce(_position.SelectedIndex, rule), null);
        return rule == selected!.Value ? (null, null) : (new ChangeDarwinAce(_entries.SelectedIndex, selected.Value, rule), null);
    }

    private static uint ReadBits(List<(uint Bit, CheckBox Box)> checkboxes) =>
        checkboxes.Aggregate(0u, (mask, option) => option.Box.IsChecked == true ? mask | option.Bit : mask);

    private void ResetFields()
    {
        if (_setting) return;
        _setting = true;
        bool insert = CurrentOperation == Operation.Insert;
        var selected = insert ? null : Selected;
        _position.IsVisible = insert;
        _position.SelectedIndex = insert && _original.IsEmpty ? 0 : -1;
        _fields.IsVisible = insert || CurrentOperation == Operation.Edit && selected is { Kind: DarwinAceKind.Allow or DarwinAceKind.Deny };
        _uuid.Text = selected?.Principal.Id ?? string.Empty;
        _kind.SelectedIndex = selected?.Kind == DarwinAceKind.Deny ? 1 : 0;
        foreach (var (bit, box) in _rights) box.IsChecked = (selected.GetValueOrDefault().Rights & bit) != 0;
        foreach (var (bit, box) in _inheritance) box.IsChecked = (selected.GetValueOrDefault().Flags & bit) != 0;
        _acceptRemoval.IsVisible = CurrentOperation == Operation.RemoveAcl;
        _acceptRemoval.IsChecked = false;
        _notice.Text = CurrentOperation == Operation.RemoveAcl ? "Removal may change access that the extended ACL currently grants or denies."
            : !insert && selected is { Kind: DarwinAceKind.Opaque } ? "This entry has an unrecognized native type. Individual edits require a known allow or deny entry."
            : _directory ? "Rules are evaluated in order. Inheritance applies when new children are created." : "Rules are evaluated in order.";
        _entries.IsEnabled = true;
        _operation.IsEnabled = true;
        _reset.IsVisible = false;
        _setting = false;
        Changed?.Invoke();
    }

    private void OnEdit()
    {
        if (_setting) return;
        _entries.IsEnabled = false;
        _operation.IsEnabled = false;
        _reset.IsVisible = true;
        Changed?.Invoke();
    }
}
