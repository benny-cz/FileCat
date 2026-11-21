using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.App.Views;

internal sealed class WindowsAuditEditor : StackPanel
{
    private readonly WindowsSacl _baseline;
    private readonly string _authority;
    private readonly ListBox _entries = new()
    {
        MinHeight = 80, MaxHeight = 200,
        ItemTemplate = new FuncDataTemplate<string>((value, _) => new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap })
    };
    private readonly CheckBox _add = new() { Content = "Add an explicit audit entry" };
    private readonly CheckBox _remove = new() { Content = "Remove selected entry" };
    private readonly TextBox _sid = new() { PlaceholderText = "S-1-…" };
    private readonly TextBox _mask = new();
    private readonly CheckBox _success = new() { Content = "Audit successful access" };
    private readonly CheckBox _failure = new() { Content = "Audit failed access" };
    private readonly Button _reset = new() { Content = "Discard entry changes" };
    private bool _setting;
    private string? _initialSid;
    private string? _initialMask;
    private byte _initialFlags;

    internal WindowsAuditEditor(WindowsSacl baseline, string authority)
    {
        _baseline = baseline;
        _authority = authority;
        Spacing = 7;
        AutomationProperties.SetName(_entries, "Ordered Windows audit entries");
        AutomationProperties.SetName(_sid, "Audit account SID");
        AutomationProperties.SetName(_mask, "Audit rights mask");
        _entries.ItemsSource = baseline.Entries.Select((entry, index) => $"{index + 1}. {Describe(entry)}").ToArray();
        Children.Add(_entries);
        Children.Add(new WrapPanel { Children = { _add, _remove, _reset } });
        Children.Add(new TextBlock { Text = "Account SID" });
        Children.Add(_sid);
        Children.Add(new TextBlock { Text = "File rights mask (hexadecimal)" });
        Children.Add(_mask);
        Children.Add(new WrapPanel { Children = { _success, _failure } });
        _entries.SelectionChanged += (_, _) => { if (!_setting) LoadEntry(); };
        _add.IsCheckedChanged += (_, _) => { if (!_setting) LoadEntry(); };
        _remove.IsCheckedChanged += (_, _) => Refresh();
        _sid.TextChanged += (_, _) => Refresh();
        _mask.TextChanged += (_, _) => Refresh();
        _success.IsCheckedChanged += (_, _) => Refresh();
        _failure.IsCheckedChanged += (_, _) => Refresh();
        _reset.Click += (_, _) =>
        {
            _setting = true;
            _add.IsChecked = false;
            LoadEntry();
        };
        _entries.SelectedIndex = baseline.Entries.IsEmpty ? -1 : 0;
        LoadEntry();
    }

    internal event Action? Changed;
    private WindowsAuditAce? Selected =>
        (uint)_entries.SelectedIndex < (uint)_baseline.Entries.Length ? _baseline.Entries[_entries.SelectedIndex] : null;
    private bool Editable => Selected is { } selected && WindowsPermissionAuditPatch.CanEditFileEntry(selected);
    private bool Adding => _add.IsChecked == true;
    private byte Flags => (byte)((_success.IsChecked == true ? WindowsPermissionAuditPatch.Success : 0) |
        (_failure.IsChecked == true ? WindowsPermissionAuditPatch.Failure : 0));
    private bool Dirty => Adding || _remove.IsChecked == true || Editable &&
        (_sid.Text != _initialSid || _mask.Text != _initialMask || Flags != _initialFlags);

    internal (PermissionDelta? Change, string? Error) ReadChange()
    {
        if (!Dirty || !Adding && !Editable) return (null, null);
        int? index = Adding ? null : _entries.SelectedIndex;
        var expected = Adding ? null : Selected;
        if (_remove.IsChecked == true) return (new ChangeWindowsAuditAce(index, expected, null), null);
        if (Flags == 0) return (null, "Select successful access, failed access or both.");
        var text = _mask.Text.AsSpan().Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (text.Length is < 1 or > 8 || !uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint mask) ||
            mask == 0 || (mask & ~WindowsFileAccessRights.Known) != 0)
            return (null, "Enter a hexadecimal mask with at least one supported file right.");
        try
        {
            var principal = new Principal(PrincipalKind.WindowsSid, _sid.Text?.Trim() ?? "", _authority);
            var replacement = WindowsPermissionAuditPatch.FileEntry(principal, mask, Flags);
            if (expected is not null && expected.Bytes.AsSpan().SequenceEqual(replacement.Bytes.AsSpan())) return (null, null);
            return (new ChangeWindowsAuditAce(index, expected, replacement), null);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        {
            return (null, "Enter a valid Windows account SID.");
        }
    }

    private void LoadEntry()
    {
        _setting = true;
        var selected = Adding ? null : Selected;
        _remove.IsChecked = false;
        _sid.Text = _initialSid = selected?.Principal?.Id ?? "";
        _mask.Text = _initialMask = selected?.Mask is uint mask ? $"0x{WindowsFileAccessRights.MapGeneric(mask):X8}" : "";
        _success.IsChecked = selected?.AuditsSuccess == true || Adding;
        _failure.IsChecked = selected?.AuditsFailure == true;
        _initialFlags = Flags;
        _setting = false;
        Refresh();
    }

    private void Refresh()
    {
        if (_setting) return;
        bool dirty = Dirty;
        bool edit = (Adding || Editable) && _remove.IsChecked != true;
        _entries.IsEnabled = !dirty;
        _add.IsEnabled = !dirty || Adding;
        _remove.IsEnabled = !Adding && Editable;
        _reset.IsVisible = dirty;
        _sid.IsEnabled = _mask.IsEnabled = _success.IsEnabled = _failure.IsEnabled = edit;
        Changed?.Invoke();
    }

    internal static string Describe(WindowsAuditAce? entry)
    {
        if (entry is null) return "No entry";
        string inheritance = (entry.Flags & 0x10) != 0 ? "inherited" : "explicit";
        string flags = $"{inheritance} · flags 0x{entry.Flags:X2}";
        if (entry.Type == 2)
        {
            string events = (entry.AuditsSuccess, entry.AuditsFailure) switch
            {
                (true, true) => "Success and failure",
                (true, false) => "Success",
                (false, true) => "Failure",
                _ => "No audit events selected"
            };
            return $"{events} · {Formatters.SafeName(entry.Principal?.Id ?? "")} · mask 0x{entry.Mask:X8} · {flags}";
        }
        string type = entry.Type switch
        {
            0x11 => "Integrity label",
            0x12 => "Resource attributes",
            0x13 => "Central policy ID",
            _ => $"System ACE 0x{entry.Type:X2}"
        };
        return $"{type} · {flags}";
    }
}
