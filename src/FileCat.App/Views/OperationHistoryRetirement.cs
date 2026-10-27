using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Jobs;

namespace FileCat.App.Views;

/// <summary>Snapshot of the finished rows a history action may retire after its review completes.</summary>
internal sealed class OperationHistoryRetirement
{
    internal IReadOnlyList<Job> Jobs { get; }
    internal int AvailableUndoCount { get; }
    internal int ActiveUndoCount { get; }
    internal int ReportCount { get; }
    internal bool NeedsConfirmation => AvailableUndoCount > 0 || ActiveUndoCount > 0 || ReportCount > 0;

    internal OperationHistoryRetirement(IEnumerable<Job> jobs)
    {
        Jobs = jobs.Where(j => j.State.IsFinished()).Distinct().ToArray();
        AvailableUndoCount = Jobs.Count(j => j.CanUndo);
        ActiveUndoCount = Jobs.Count(j => j.UndoState is UndoAttemptState.Reviewing or UndoAttemptState.Attempting);
        ReportCount = Jobs.Count(HasReviewableReport);
    }

    internal static bool HasReviewableReport(Job job) =>
        job.UndoReport is not null || job.VerificationReport is not null ||
        job.IssueTotals is { Warning: > 0 } or { Error: > 0 } or { Omitted: > 0 } ||
        job.ItemsFailed > 0 || job.State is JobState.Failed or JobState.Interrupted or JobState.Canceled;

    internal async Task<bool> ConfirmAsync(MainViewModel main, string action)
    {
        if (Jobs.Count == 0) return false;
        if (!NeedsConfirmation) return true;

        var body = new StackPanel { Spacing = 9 };
        body.Children.Add(new TextBlock
        {
            Text = $"Remove {Jobs.Count:N0} finished operation{(Jobs.Count == 1 ? "" : "s")} from this session's history?",
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
        });
        string spokenConsequence = $"Clearing history removes {AvailableUndoCount:N0} unused Undo " +
            (AvailableUndoCount == 1 ? "record" : "records") +
            " and the selected operation reports; it does not change files. " +
            (ActiveUndoCount > 0 ? "Already running Undo work continues. " : string.Empty) +
            "Keep history preserves the available Undo and reports.";
        var keyPoints = new StackPanel { Spacing = 5 };
        keyPoints.Children.Add(new TextBlock
        {
            Text = "Important effects", FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        keyPoints.Children.Add(OverlayDialogService.ReviewLine(
            $"Unused Undo: {AvailableUndoCount:N0} eligible record{(AvailableUndoCount == 1 ? "" : "s")} removed if approved"));
        keyPoints.Children.Add(OverlayDialogService.ReviewLine(
            "Reports: selected operation reports leave this session's history if approved"));
        keyPoints.Children.Add(OverlayDialogService.ReviewLine("File changes: none"));
        if (ActiveUndoCount > 0)
            keyPoints.Children.Add(OverlayDialogService.ReviewLine(
                "Active Undo: already admitted inverse work continues independently"));
        body.Children.Add(new Border { Classes = { "reviewWarnings" }, Child = keyPoints });
        body.Children.Add(new TextBlock
        {
            Text = $"{AvailableUndoCount:N0} unused Undo record{(AvailableUndoCount == 1 ? "" : "s")}; " +
                   $"{ReportCount:N0} operation{(ReportCount == 1 ? "" : "s")} with retained results or problems. " +
                   "Removing these rows also removes their reports and unused Undo steps. It does not change files.",
            TextWrapping = TextWrapping.Wrap,
        });
        if (ActiveUndoCount > 0)
            body.Children.Add(new TextBlock
            {
                Text = $"{ActiveUndoCount:N0} Undo attempt{(ActiveUndoCount == 1 ? " is" : "s are")} being reviewed or run. " +
                       "Removing the original row does not cancel active Undo work; its result remains in Operations.",
                TextWrapping = TextWrapping.Wrap,
            });
        body.Children.Add(new TextBlock
        {
            Text = $"History is kept only for this session. FileCat is configured to keep {Math.Max(0, main.Services.Jobs.KeepFinished):N0} finished operations; older records retire as new work is submitted. " +
                   "Copy or save a report you need before clearing it or closing FileCat.",
            TextWrapping = TextWrapping.Wrap,
        });
        var rows = new ListBox
        {
            ItemsSource = Jobs.Select(Describe).ToArray(), MinHeight = 80, MaxHeight = 230,
            ItemTemplate = DialogLayout.WrappingListText,
        };
        AutomationProperties.SetName(rows, "Finished operations and records to remove");
        AutomationProperties.SetHelpText(rows, spokenConsequence);
        ScrollViewer.SetHorizontalScrollBarVisibility(rows, ScrollBarVisibility.Disabled);
        body.Children.Add(rows);
        return await main.Dialogs.ShowCustomAsync("Remove operation history", body,
            [new("Keep history", false, IsDefault: true, IsCancel: true) { HelpText = spokenConsequence },
             new(action, true, IsDanger: true)], rows,
            resizable: true) is true;
    }

    private static string Describe(Job job)
    {
        var tags = new List<string>();
        if (job.CanUndo) tags.Add("unused Undo available");
        if (job.UndoState is UndoAttemptState.Reviewing or UndoAttemptState.Attempting) tags.Add("Undo in progress");
        if (HasReviewableReport(job)) tags.Add("report or problem details");
        return Formatters.SafeName(job.Title) + " — " + (tags.Count == 0 ? "finished" : string.Join("; ", tags));
    }
}
