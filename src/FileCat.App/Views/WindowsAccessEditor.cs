using System.Globalization;
using System.Security.AccessControl;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.App.Views;

internal sealed class WindowsAccessEditor : StackPanel
{
    private static readonly (FileSystemRights Right, string Name)[] Rights =
    [
        (FileSystemRights.ReadData, "Read data"), (FileSystemRights.WriteData, "Write data"),
        (FileSystemRights.AppendData, "Append data"), (FileSystemRights.ExecuteFile, "Execute file"),
        (FileSystemRights.ReadAttributes, "Read attributes"), (FileSystemRights.WriteAttributes, "Write attributes"),
        (FileSystemRights.ReadExtendedAttributes, "Read extended attributes"), (FileSystemRights.WriteExtendedAttributes, "Write extended attributes"),
        (FileSystemRights.Delete, "Delete this file"), (FileSystemRights.ReadPermissions, "Read security"),
        (FileSystemRights.ChangePermissions, "Write DACL"), (FileSystemRights.TakeOwnership, "Write owner"),
        (FileSystemRights.Synchronize, "Synchronize")
    ];
    private readonly WindowsDacl _baseline;
    private readonly string _authority;
    private readonly ListBox _entries = new() { MinHeight = 80, MaxHeight = 210, ItemTemplate =
        new FuncDataTemplate<string>((value, _) => new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap }) };
    private readonly CheckBox _add = new() { Content = "Add an explicit entry" };
    private readonly CheckBox _remove = new() { Content = "Remove selected entry" };
    private readonly CheckBox _inheritFiles = new() { Content = "Inherit into files" };
    private readonly CheckBox _inheritFolders = new() { Content = "Inherit into subfolders" };
    private readonly TextBox _sid = new() { PlaceholderText = "S-1-…", MinWidth = 175 };
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "Allow", "Deny" }, SelectedIndex = 0 };
    private readonly ComboBox _preset = new() { ItemsSource = new[] { "Read", "Read and execute", "Modify", "Full control" }, SelectedIndex = 0 };
    private readonly CheckBox _replace = new() { Content = "Replace other rights with this preset" };
    private readonly Button _usePreset = new() { Content = "Use preset" };
    private readonly TextBox _mask = new() { MinWidth = 110 };
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _special = new() { Orientation = Orientation.Horizontal };
    private readonly Button _reset = new() { Content = "Discard entry changes", IsVisible = false };
    private readonly List<(uint Bit, CheckBox Box)> _advanced = [];
    private bool _setting;
    private uint _initialMask;

    internal WindowsAccessEditor(WindowsDacl baseline, string authority, Func<string, Task>? copy, bool directory = false)
    {
        _baseline = baseline;
        _authority = authority;
        Spacing = 8;
        AutomationProperties.SetName(_entries, "Ordered Windows access entries");
        _entries.ItemsSource = baseline.Entries.Select((entry, index) => $"{index + 1}. {Describe(entry)}").ToArray();
        AutomationProperties.SetName(_sid, "New Windows entry SID");
        AutomationProperties.SetName(_kind, "New Windows entry type");
        AutomationProperties.SetName(_preset, directory ? "Folder rights preset" : "File rights preset");
        AutomationProperties.SetName(_mask, "Windows access mask");
        AutomationProperties.SetLiveSetting(_notice, AutomationLiveSetting.Polite);
        Children.Add(_entries);
        if (copy is not null)
        {
            var copySid = new Button { Content = "Copy selected SID" };
            copySid.Click += async (_, _) => { if (Selected?.Principal is { } principal) await copy(principal.Id); };
            _entries.SelectionChanged += (_, _) => copySid.IsEnabled = Selected?.Principal.HasValue == true;
            copySid.IsEnabled = false;
            Children.Add(copySid);
        }
        Children.Add(Row(_add, _remove, _reset));
        Children.Add(_special);
        _special.Children.Add(_kind);
        _special.Children.Add(_sid);
        if (directory)
        {
            _special.Children.Add(_inheritFiles);
            _special.Children.Add(_inheritFolders);
            _inheritFiles.IsCheckedChanged += (_, _) => Refresh();
            _inheritFolders.IsCheckedChanged += (_, _) => Refresh();
        }
        Children.Add(Row(_preset, _replace, _usePreset));
        Children.Add(Row(new TextBlock { Text = "Advanced mask", VerticalAlignment = VerticalAlignment.Center }, _mask));
        var advanced = new WrapPanel { Orientation = Orientation.Horizontal };
        var rights = directory ? Rights.Append((Right: FileSystemRights.DeleteSubdirectoriesAndFiles, Name: "Delete children")) : Rights;
        foreach (var right in rights)
        {
            string name = directory ? right.Right switch
            {
                FileSystemRights.ReadData => "List folder",
                FileSystemRights.WriteData => "Add files",
                FileSystemRights.AppendData => "Add subfolders",
                FileSystemRights.ExecuteFile => "Traverse folder",
                FileSystemRights.Delete => "Delete this folder",
                _ => right.Name
            } : right.Name;
            var box = new CheckBox { Content = name };
            uint bit = (uint)right.Right;
            _advanced.Add((bit, box));
            advanced.Children.Add(box);
            box.IsCheckedChanged += (_, _) =>
            {
                if (_setting || !TryMask(out uint current)) return;
                _mask.Text = FormatMask(box.IsChecked == true ? current | bit : current & ~bit);
            };
        }
        Children.Add(advanced);
        Children.Add(_notice);

        _entries.SelectionChanged += (_, _) => { if (!_setting) StartEntry(); };
        _add.IsCheckedChanged += (_, _) => { if (!_setting) StartEntry(); };
        _remove.IsCheckedChanged += (_, _) => Refresh();
        _kind.SelectionChanged += (_, _) => Refresh();
        _mask.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) Refresh(); };
        _sid.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) Refresh(); };
        _usePreset.Click += (_, _) =>
        {
            var mask = WindowsFileAccessRights.PresetMask((WindowsFileAccessPreset)_preset.SelectedIndex);
            if (_replace.IsChecked != true && TryMask(out uint existing)) mask |= existing;
            _mask.Text = FormatMask(mask | (_initialMask & ~WindowsFileAccessRights.Known));
        };
        _reset.Click += (_, _) =>
        {
            _setting = true;
            _add.IsChecked = false;
            _setting = false;
            StartEntry();
        };
        _entries.SelectedIndex = baseline.Entries.Length > 0 ? 0 : -1;
        StartEntry();
    }

    internal event Action? Changed;
    private bool Adding => _add.IsChecked == true;
    private WindowsAce? Selected => (uint)_entries.SelectedIndex < (uint)_baseline.Entries.Length ? _baseline.Entries[_entries.SelectedIndex] : null;
    private bool CanEditSelected => Selected is { Kind: not WindowsAceKind.Opaque, Flags: var flags, Principal: not null } && (flags & 0x10) == 0;

    internal (PermissionDelta? Change, string? Error) ReadChange()
    {
        if (!Adding && !CanEditSelected) return (null, null);
        if (!Adding && _remove.IsChecked == true) return (new ChangeWindowsAce(_entries.SelectedIndex, Selected!, null), null);
        if (!TryMask(out uint mask)) return (null, "Enter up to eight hexadecimal digits for the access mask.");
        if (!Adding && mask == _initialMask) return (null, null);
        if (mask == 0) return (null, "Choose at least one right, or remove the entry explicitly.");
        if ((mask & ~WindowsFileAccessRights.Known) != (_initialMask & ~WindowsFileAccessRights.Known))
            return (null, "Unsupported bits in an existing mask must be preserved.");
        try
        {
            if (Adding)
            {
                var principal = new Principal(PrincipalKind.WindowsSid, _sid.Text ?? string.Empty, _authority);
                byte flags = (byte)((_inheritFiles.IsChecked == true ? 1 : 0) | (_inheritFolders.IsChecked == true ? 2 : 0));
                var entry = WindowsPermissionAclPatch.SimpleEntry(_kind.SelectedIndex == 1 ? WindowsAceKind.Deny : WindowsAceKind.Allow,
                    principal, mask, flags);
                return (new AddWindowsAce(entry), null);
            }
            var selected = Selected!;
            return (new ChangeWindowsAce(_entries.SelectedIndex, selected,
                WindowsPermissionAclPatch.SimpleEntry(selected.Kind, selected.Principal!.Value, mask, selected.Flags)), null);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        { return (null, "Enter a valid SID for the named user or group."); }
    }

    private void StartEntry()
    {
        _setting = true;
        _remove.IsChecked = false;
        _sid.Text = string.Empty;
        _inheritFiles.IsChecked = false;
        _inheritFolders.IsChecked = false;
        _initialMask = Adding ? 0 : WindowsFileAccessRights.MapGeneric(Selected?.Mask ?? 0);
        _mask.Text = FormatMask(_initialMask);
        _setting = false;
        Refresh();
    }

    private void Refresh()
    {
        if (_setting) return;
        _setting = true;
        bool editable = (Adding || CanEditSelected) && _remove.IsChecked != true;
        bool valid = TryMask(out uint mask);
        bool dirty = Adding || _remove.IsChecked == true || CanEditSelected && (!valid || mask != _initialMask);
        _entries.IsEnabled = !dirty;
        _add.IsEnabled = !dirty || Adding;
        _remove.IsEnabled = !Adding && CanEditSelected;
        _special.IsVisible = Adding;
        _reset.IsVisible = dirty;
        _mask.IsEnabled = _preset.IsEnabled = _replace.IsEnabled = _usePreset.IsEnabled = editable;
        foreach (var (bit, box) in _advanced)
        {
            box.IsEnabled = editable && valid;
            box.IsChecked = valid && (mask & bit) != 0;
        }
        bool denying = Adding ? _kind.SelectedIndex == 1 : Selected?.Kind == WindowsAceKind.Deny;
        string notice = denying && (mask & (uint)FileSystemRights.Synchronize) != 0
            ? "Denying Synchronize can also block opens that request other rights."
            : !Adding && !CanEditSelected ? "Select a known explicit entry to change it." : string.Empty;
        _notice.Text = notice;
        _notice.IsVisible = notice.Length > 0;
        _setting = false;
        Changed?.Invoke();
    }

    private bool TryMask(out uint mask)
    {
        ReadOnlySpan<char> text = _mask.Text.AsSpan().Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        mask = 0;
        if (text.Length is < 1 or > 8 || !uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint raw)) return false;
        mask = WindowsFileAccessRights.MapGeneric(raw);
        return true;
    }

    internal static string Describe(WindowsAce entry)
    {
        if (entry.Kind == WindowsAceKind.Opaque) return $"Opaque ACE 0x{entry.Type:X2} · flags 0x{entry.Flags:X2}";
        string flags = (entry.Flags & 0x10) != 0 ? "inherited" : "explicit";
        uint mask = WindowsFileAccessRights.MapGeneric(entry.Mask ?? 0);
        return $"{entry.Kind} · {Formatters.SafeName(entry.Principal?.Id ?? string.Empty)} · {FormatMask(mask)} · {flags} · flags 0x{entry.Flags:X2}";
    }

    private static string FormatMask(uint mask) => $"0x{mask:X8}";

    private static WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls) row.Children.Add(control);
        return row;
    }
}
