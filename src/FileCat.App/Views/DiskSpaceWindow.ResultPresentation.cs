using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private static TextBlock ReadingLabel(string role = "Text", FontWeight weight = FontWeight.Normal) => new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontWeight = weight,
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("FcAnalysis" + role)
    };

    private static Border ReadingSurface(Control content, Thickness padding, Thickness border, string line = "Divider") => new()
    {
        Child = content,
        Padding = padding,
        BorderThickness = border,
        [!Border.BackgroundProperty] = new DynamicResourceExtension("FcAnalysisSurface"),
        [!Border.BorderBrushProperty] = new DynamicResourceExtension("FcAnalysis" + line)
    };

    private static bool HasCoverageWarning(NamespaceCoverage coverage, AnalysisFreshness freshness) =>
        coverage != NamespaceCoverage.CompleteUnderPolicy || freshness != AnalysisFreshness.Observed;

    private sealed class ReadingStatus : Border
    {
        internal ReadingStatus(TextBlock label)
        {
            Child = label;
            HorizontalAlignment = HorizontalAlignment.Left;
            SetWarning(false);
        }

        internal void SetWarning(bool warning)
        {
            if (_warning == warning) return;
            _warning = warning;
            var label = (TextBlock)Child!;
            string ink = warning ? "FcAnalysisWarningText" : "FcAnalysisSecondary";
            _foreground?.Dispose();
            _background?.Dispose();
            _border?.Dispose();
            _foreground = label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(ink));
            label.FontWeight = warning ? FontWeight.SemiBold : FontWeight.Normal;
            _background = Bind(BackgroundProperty, new DynamicResourceExtension(warning ? "FcAnalysisWarningSurface" : "FcAnalysisSurface"));
            _border = Bind(BorderBrushProperty, new DynamicResourceExtension(ink));
            BorderThickness = new Thickness(warning ? 2 : 0, 0, 0, 0);
            Padding = warning ? new Thickness(5, 0, 4, 0) : default;
        }

        private bool? _warning;
        private IDisposable? _foreground, _background, _border;
    }

    private static TextBlock MeasureLabel()
    {
        var label = ReadingLabel("Text", FontWeight.Bold);
        label.Bind(TextBlock.FontSizeProperty, new Binding(nameof(FontSize))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Window) },
            Converter = new FuncValueConverter<double, double>(size => size + 3)
        });
        return label;
    }

    private static string KeepMeasurementUnits(string text) =>
        Regex.Replace(text, @"(?<=\d) (?=(?:[KMGTPEZY]i)?B\b)", "\u00a0", RegexOptions.CultureInvariant);

    private static void ShowReadingLabel(TextBlock label, string text)
    {
        label.Text = text;
        label.IsVisible = text.Length != 0;
    }

    private sealed class SelectionReading : StackPanel
    {
        private readonly TextBlock _name = ReadingLabel("Accent", FontWeight.SemiBold);
        private readonly TextBlock _amount = MeasureLabel(), _coverage = ReadingLabel(), _details = ReadingLabel(), _notes = ReadingLabel("Secondary");
        private readonly ReadingStatus _warning;
        private AnalysisTreeItem? _item;
        private AnalysisMetric _metric;
        private string _plainNotes = "";
        private string _displayName = "";

        internal SelectionReading()
        {
            Spacing = 5;
            _warning = new(_coverage);
            _warning.SetWarning(true);
            Children.Add(_name);
            Children.Add(_amount);
            Children.Add(_warning);
            Children.Add(_details);
            Children.Add(_notes);
            Show(null, default, "Choose an entry in either chart or the table.");
        }

        internal string PlainText => _item is null ? _plainNotes :
            _displayName + (_item.IncludedByPolicy
                ? "\n\n" + DiskSpaceViewModel.InspectorMeasurements(_item, _metric) + "\n\n" : "\n") + _plainNotes;

        internal void Show(AnalysisTreeItem? item, AnalysisMetric metric, string notes, string? displayName = null)
        {
            _item = item;
            _metric = metric;
            _plainNotes = notes;
            _displayName = displayName ?? (item is null ? "" : AnalysisNames.Display(item.Name));
            var (coverage, amount, details) = item is { IncludedByPolicy: true }
                ? DiskSpaceViewModel.InspectorReading(item, metric) : ("", "", "");
            ShowReadingLabel(_name, _displayName);
            ShowReadingLabel(_amount, KeepMeasurementUnits(amount));
            ShowReadingLabel(_coverage, coverage);
            _warning.IsVisible = coverage.Length != 0;
            ShowReadingLabel(_details, KeepMeasurementUnits(details));
            ShowReadingLabel(_notes, notes);
        }

        internal SelectionReading Copy()
        {
            var reading = new SelectionReading();
            reading.Show(_item, _metric, _plainNotes, _displayName);
            return reading;
        }
    }

    private sealed class AnalysisActions : StackPanel
    {
        private readonly ToggleButton _trigger = new() { Content = "Analysis actions" };
        public WrapPanel Commands { get; } = new() { ItemSpacing = 7, LineSpacing = 5, IsVisible = false };

        public AnalysisActions()
        {
            Spacing = 5;
            var help = "Show or hide saved analyses, output, history and other analysis commands.";
            ToolTip.SetTip(_trigger, new RichTip("Analysis actions", detail: help));
            AutomationProperties.SetHelpText(_trigger, help);
            Children.Add(_trigger);
            Children.Add(Commands);
            _trigger.IsCheckedChanged += (_, _) => Commands.IsVisible = _trigger.IsChecked == true;
            AddHandler(KeyDownEvent, (_, args) =>
            {
                if (args.Key != Key.Escape || !Commands.IsVisible) return;
                Dismiss();
                args.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        public void Collapse() => Dismiss();

        private void Dismiss()
        {
            if (Commands.IsKeyboardFocusWithin) _trigger.Focus();
            _trigger.IsChecked = false;
        }
    }

    private Button CoverageDetails(Func<string?> details) => ActionButton("Coverage details", async () =>
    {
        if (details() is not { Length: > 0 } text || _dialogs.IsOpen) return;
        await _dialogs.ShowCustomAsync("Coverage details", new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            [new("Close coverage details", true, IsDefault: true, IsCancel: true)]);
    }, "Read measurement coverage, scan boundaries, issues and storage information.");

    private static string CoverageHeadline(NamespaceCoverage coverage, AnalysisFreshness freshness, bool compact = false)
    {
        string coverageLabel = coverage switch
        {
            NamespaceCoverage.CompleteUnderPolicy => compact ? "Complete under policy" : "Complete under the scan policy",
            NamespaceCoverage.Partial => compact ? "Partial results" : "Partial results · some contents were not observed",
            _ => "Coverage is unknown"
        };
        string? freshnessLabel = freshness switch
        {
            AnalysisFreshness.SourceUnavailable => compact ? "Source unavailable" : "Source unavailable · showing recorded observations",
            AnalysisFreshness.IdentityMismatch => compact ? "Source changed" : "Source changed · showing recorded observations",
            AnalysisFreshness.PossiblyStale => "Results may be stale",
            _ => null
        };
        return freshnessLabel is null ? coverageLabel : coverage == NamespaceCoverage.CompleteUnderPolicy ? freshnessLabel : freshnessLabel + " · " + coverageLabel;
    }

    private static string ResultScopeName(AnalysisTree tree, string scope) => scope == AnalysisTree.RootKey &&
        tree.Snapshot.Roots.Length == 1 && tree.GroupingDimension == AnalysisGroupingDimension.FolderHierarchy
            ? tree.Snapshot.Roots[0].Label : tree.NameOf(scope);

}
