using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Jobs;

namespace FileCat.App.Views;

/// <summary>Full paths and actual inverse verbs, with a bounded, virtualized list for large operations.</summary>
internal static class UndoReviewDialog
{
    private static readonly IDataTemplate ActionText = new FuncDataTemplate<string>((text, _) =>
        OverlayDialogService.ReviewLine(text));

    internal static string Explanation(UndoPlan plan) =>
        $"{plan.Steps.Count:N0} recorded inverse actions. Changed files and occupied original names are kept and reported; nothing is overwritten to make Undo succeed. " +
        "Moving a folder back moves its current contents; no recursive unchanged-content snapshot is promised. " +
        "Created empty items are removed only while their individual safety checks still pass. " +
        "This is one attempt. Completed, kept, failed, and unattempted actions remain in its report; they are not automatically retried.";

    public static async Task<bool> ShowAsync(MainViewModel vm, UndoPlan plan, bool mostRecentUndoable, bool newerOperationsExist)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = Formatters.SafeName(plan.OriginalTitle), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (newerOperationsExist)
            content.Children.Add(new TextBlock
            {
                Text = "This is the most recent undoable operation. More recent finished operations are not covered by this Undo.",
                TextWrapping = TextWrapping.Wrap,
            });
        content.Children.Add(new TextBlock { Text = Explanation(plan), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock
        {
            Text = $"The complete report is kept in Operations for this session, within its history limit of {Math.Max(0, vm.Services.Jobs.KeepFinished):N0} finished operations. " +
                "Use Copy all in the report before clearing its operation or closing FileCat.",
            TextWrapping = TextWrapping.Wrap,
        });
        var actions = new ListBox
        {
            ItemsSource = plan.Steps.Reverse().Select(s => Formatters.SafeName(UndoActions.Describe(s))).ToArray(),
            ItemTemplate = ActionText,
            MinHeight = 100, MaxHeight = 260, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(actions, "Recorded Undo actions, with full affected paths");
        ScrollViewer.SetHorizontalScrollBarVisibility(actions, ScrollBarVisibility.Disabled);
        content.Children.Add(actions);
        if (plan.RemainderCount > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Undo reverses only the actions above. {plan.RemainderCount:N0} other recorded effects have no inverse; overwritten destination contents cannot be restored.",
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold,
            });
            var remainder = new ListBox
            {
                ItemsSource = plan.Remainder.Select(r => Formatters.SafeName(r.Path + ": " + r.Reason)).ToArray(), MaxHeight = 140,
                ItemTemplate = DialogLayout.WrappingListText,
            };
            AutomationProperties.SetName(remainder, "Effects that Undo cannot reverse");
            ScrollViewer.SetHorizontalScrollBarVisibility(remainder, ScrollBarVisibility.Disabled);
            content.Children.Add(remainder);
            if (plan.RemainderCount > plan.Remainder.Count)
                content.Children.Add(new TextBlock { Text = $"The first {plan.Remainder.Count:N0} irreversible effects are listed; {plan.RemainderCount - plan.Remainder.Count:N0} additional effects are counted.", TextWrapping = TextWrapping.Wrap });
        }
        return await vm.Dialogs.ShowCustomAsync(mostRecentUndoable ? "Review most recent undoable operation" : "Review Undo", content,
            [new("Cancel", false, IsDefault: true, IsCancel: true), new("Run these inverse actions", true, IsDanger: true)], actions,
            resizable: true) is true;
    }
}
