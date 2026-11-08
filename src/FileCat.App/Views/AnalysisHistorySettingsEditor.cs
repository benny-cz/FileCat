using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

/// <summary>Finite Settings draft only; the separate preserving review commits policy changes.</summary>
internal sealed class AnalysisHistorySettingsEditor
{
    private readonly CheckBox _enabled = new() { Content = "Keep rolling disk-space analysis history" };
    private readonly NumericUpDown _count = Number(1, AnalysisRollingHistoryPolicy.EntryHardLimit, 1);
    private readonly NumericUpDown _age = Number(1, AnalysisRollingHistoryPolicy.AgeHardLimitDays, 1);
    private readonly NumericUpDown _bytes = Number(1, AnalysisRollingHistoryPolicy.ByteHardLimit, 1L << 20);
    internal Control View { get; }
    internal AnalysisHistorySettingsEditor(AnalysisRollingHistoryPolicy current, string directory, Func<Task>? browseHistory = null)
    {
        current.Validate(); _enabled.IsChecked = current.Enabled;
        _count.Value = current.MaximumEntries; _age.Value = current.MaximumAgeDays; _bytes.Value = current.MaximumBytes;
        View = new DialogForm([
            ("", _enabled), ("Maximum successful analyses", _count), ("Maximum age in days", _age), ("Maximum total bytes", _bytes),
            ("", Note("The first limit reached applies. Defaults: 10 analyses, 30 days, 1 GiB (1,073,741,824 bytes). Limits stay finite even while history is off.")),
            ("Private history folder", new SelectableTextBlock { Text = AnalysisNames.Display(directory, 4096), TextWrapping = TextWrapping.Wrap }),
            ("", Note("History is off by default. When enabled, successful live metadata scans are captured once after source cleanup. Imported, stopped and failed sessions are excluded. Partial coverage stays labelled. File contents are excluded; names, paths and metadata can be sensitive.")),
            ("", Note("OK reviews changes to history separately before applying them. Automatic pruning affects only rolling copies in this folder. Turning history off keeps existing files. Named analyses, pinned named copies and source files are outside pruning. Deletion is logical, not secure erasure."))
        ]);
        if (browseHistory is not null && View is DialogForm form)
        {
            var browse = new Button { Content = "Browse and manage history" };
            browse.Click += async (_, _) => await browseHistory();
            View = new StackPanel { Spacing = 8, Children = { form, browse, Note("History uses the last applied policy until you confirm Settings.") } };
        }
    }
    internal bool TryGet(out AnalysisRollingHistoryPolicy policy, out string? error, out Control invalid)
    {
        policy = AnalysisRollingHistoryPolicy.Default; error = null; invalid = _count;
        foreach (var field in new[] { _count, _age, _bytes })
            if (field.Value is not { } value || value != decimal.Truncate(value) || value < field.Minimum || value > field.Maximum)
            { error = "History limits must be whole numbers within the displayed bounds."; invalid = field; return false; }
        policy = new(_enabled.IsChecked == true, (int)_count.Value!.Value, (int)_age.Value!.Value, (long)_bytes.Value!.Value);
        policy.Validate(); return true;
    }
    private static NumericUpDown Number(long minimum, long maximum, long increment) => new()
    { Minimum = minimum, Maximum = maximum, Increment = increment, FormatString = "N0", Width = 210, HorizontalAlignment = HorizontalAlignment.Left };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
}
