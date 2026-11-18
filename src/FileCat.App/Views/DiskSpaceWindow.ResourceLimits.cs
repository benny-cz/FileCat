using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private Button ResourceLimitsButton()
        {
            var button = ActionButton("Resource limits…", () => _ = ShowResourceLimitsAsync(),
                "See what limits a scan and how FileCat preserves partial results.");
            AutomationProperties.SetAutomationId(button, "OpenAnalysisResourceLimits");
            return button;
        }

        private async Task ShowResourceLimitsAsync()
        {
            var body = new StackPanel { Spacing = 10 };
            void Add(string heading, string description)
            {
                body.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold });
                body.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
            }

            Add("Standard scans", $"Choose up to {AnalysisLimits.Roots:N0} locations. Standard scans use bounded memory and can finish early with partial results. " +
                "For more complete coverage after a limit, scan a smaller folder or exclude folders you do not need.");
            Add("Shared capacity", $"FileCat allows up to {AnalysisLimits.Sessions:N0} open analysis tabs and {AnalysisLimits.ActiveScans:N0} active scans. " +
                "Scans on the same device take turns. Close an analysis to free its capacity.");
            Add("Traversal and display", $"The traversal cap is {AnalysisLimits.Depth:N0} levels. The chart aggregates beyond {AnalysisLimits.ChartLeaves:N0} leaves. " +
                $"The issue counts include all recorded issues while the detailed list is capped at {AnalysisLimits.Issues:N0} rows.");
            Add("Private indexes", $"Analyses that use a private index can reserve up to {DiskSpaceViewModel.Bytes((UInt128)AnalysisLimits.ScratchBytes)} of scratch storage per session " +
                $"and {DiskSpaceViewModel.Bytes((UInt128)AnalysisLimits.ProfileScratchBytes)} across the active profile. " +
                "FileCat checks available space before writing and preserves a free-space reserve.");

            await _window._dialogs.ShowCustomAsync("Disk space resource limits", body,
                [new DialogButton("Close limits", true, IsDefault: true, IsCancel: true)]);
        }
    }
}
