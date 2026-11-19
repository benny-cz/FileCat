using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
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

    private static string CoverageHeadline(NamespaceCoverage coverage, AnalysisFreshness freshness, bool compact = false) => freshness switch
    {
        AnalysisFreshness.SourceUnavailable => "Source unavailable · showing recorded observations",
        AnalysisFreshness.IdentityMismatch => "Source changed · showing recorded observations",
        AnalysisFreshness.PossiblyStale => "Results may be stale",
        _ => coverage switch
        {
            NamespaceCoverage.CompleteUnderPolicy => compact ? "Complete under policy" : "Complete under the scan policy",
            NamespaceCoverage.Partial => compact ? "Partial results" : "Partial results · some contents were not observed",
            _ => "Coverage is unknown"
        }
    };

    private static string ResultScopeName(AnalysisTree tree, string scope) => scope == AnalysisTree.RootKey &&
        tree.Snapshot.Roots.Length == 1 && tree.GroupingDimension == AnalysisGroupingDimension.FolderHierarchy
            ? tree.Snapshot.Roots[0].Label : tree.NameOf(scope);

}
