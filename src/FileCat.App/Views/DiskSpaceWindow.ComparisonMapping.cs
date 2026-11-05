using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private sealed record RootMappingChoice(RootBinding? Root)
        {
            public override string ToString() => Root is null ? "Leave unmapped" : "[" + AnalysisNames.Display(Root.Id, 256) + "] " +
                AnalysisNames.Display(Root.Label, 256) + " · " + AnalysisNames.Display(Root.CanonicalLocator, 1024);
        }

        private async Task<ImmutableDictionary<string, string>?> ReviewRootMappingAsync(DiskSpaceViewModel.ComparisonInputs inputs)
        {
            var previous = inputs.PreviousRoots.Where(r => r.Disposition == AnalysisRootDisposition.Active).ToArray();
            var current = inputs.CurrentRoots.Where(r => r.Disposition == AnalysisRootDisposition.Active).ToArray();
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(new TextBlock { Text = "Map recorded roots", FontWeight = FontWeight.SemiBold });
            body.Children.Add(new TextBlock { Text = "Choose the Previous root corresponding to each Current root. This is an explicit structural comparison of recorded paths. Identity continuity is not inferred, and neither source is opened. Compatible policies, measurement bases and coverage still qualify every difference.", TextWrapping = TextWrapping.Wrap });
            var choices = new List<(RootBinding Current, ComboBox Previous)>();
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            foreach (var root in current)
            {
                body.Children.Add(new TextBlock { Text = "Current [" + AnalysisNames.Display(root.Id, 256) + "]: " + AnalysisNames.Display(root.CanonicalLocator, 4096), TextWrapping = TextWrapping.Wrap });
                var available = previous.Where(p => p.Kind == root.Kind && p.NameRules == root.NameRules).Select(p => new RootMappingChoice(p));
                var select = new ComboBox { ItemsSource = new[] { new RootMappingChoice(null) }.Concat(available).ToArray(), SelectedIndex = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 620 };
                AutomationProperties.SetName(select, "Previous root for current " + AnalysisNames.Display(root.Id, 256));
                choices.Add((root, select)); body.Children.Add(select);
            }
            ImmutableDictionary<string, string>? Selected()
            {
                var result = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
                foreach (var choice in choices)
                {
                    if (choice.Previous.SelectedItem is not RootMappingChoice { Root: { } root }) continue;
                    if (!result.TryAdd(root.Id, choice.Current.Id)) return null;
                }
                return result.ToImmutable();
            }
            void Validate()
            {
                var selected = Selected();
                status.Text = selected is null ? "Each Previous root can map to only one Current root." : selected.Count == 0 ? "Choose at least one compatible root pair. Roots with different entry or name rules cannot be paired here." :
                    selected.Count.ToString("N0") + " pairs selected · " + previous.Length.ToString("N0") + " Previous roots · " + current.Length.ToString("N0") + " Current roots. " +
                    (selected.Count == previous.Length && selected.Count == current.Length ? "All active roots are paired; other compatibility checks still apply." : "Partial mappings retain unknown relationships and have no qualified combined difference.");
            }
            foreach (var choice in choices) choice.Previous.SelectionChanged += (_, _) => Validate();
            body.Children.Add(status); Validate();
            var answer = await _window._dialogs.ShowCustomAsync("Review structural comparison", body,
                [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Compare mapped observations", true) { IsAvailable = () => Selected() is { Count: > 0 } }],
                choices.FirstOrDefault().Previous);
            return answer is true ? Selected() : null;
        }
    }
}
