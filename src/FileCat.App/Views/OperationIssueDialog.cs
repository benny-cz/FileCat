using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Jobs;

namespace FileCat.App.Views;

/// <summary>Full, selectable detail for one retained ordinary-job outcome.</summary>
internal static class OperationIssueDialog
{
    internal static Task ShowAsync(MainViewModel main, Job job, JobIssue issue)
    {
        var body = new StackPanel { Spacing = 8, MaxWidth = 780 };
        body.Children.Add(new TextBlock
        {
            Text = $"{issue.Severity} · {issue.Outcome}", FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        AddFact("Requested action", issue.RequestedAction ?? job.Kind.ToString());
        AddFact("Committed effect", issue.CommittedEffect ?? "Not established by this issue; inspect the outcome and reason.");
        AddFact("Source disposition", issue.SourceDisposition ?? "Not established by this issue.");
        AddFact("Target disposition", issue.TargetDisposition ?? "Not established by this issue.");
        AddFact("Certainty", issue.Certainty ?? "No further certainty is claimed by this issue.");

        if (issue.SourcePath is { } source)
        {
            AddFact("Source location", LocationKind(issue.SourceIsLocal));
            AddCopyField("Source path", source, issue.SourceIsLocal);
        }
        if (issue.TargetPath is { } target)
        {
            AddFact("Target location", LocationKind(issue.TargetIsLocal));
            AddCopyField("Target path", target, issue.TargetIsLocal);
        }
        if (issue.Path.Length > 0 && (issue.SourcePath is not null || issue.TargetPath is not null) &&
            issue.Path != issue.SourcePath && issue.Path != issue.TargetPath)
            AddCopyField("Issue path", issue.Path, false);
        if (issue.SourcePath is null && issue.TargetPath is null && issue.Path.Length > 0)
            AddCopyField("Issue path", issue.Path, false);
        AddCopyField("Reason", issue.Message, false, isPath: false);
        if (issue.Cause is { } cause) AddFact("Error class", cause);

        return main.Dialogs.ShowCustomAsync("Operation issue details", body,
            [new DialogButton("Close", "close", IsDefault: true, IsCancel: true)]);

        void AddFact(string label, string value)
        {
            body.Children.Add(new TextBlock { Text = label + ": " + Formatters.SafeName(value), TextWrapping = TextWrapping.Wrap });
        }

        void AddCopyField(string label, string raw, bool? local, bool isPath = true)
        {
            var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
            row.Children.Add(new TextBlock { Text = label + " (hidden characters escaped):", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var copy = new Button { Content = isPath ? "Copy exact " + label.ToLowerInvariant() : "Copy exact reason" };
            copy.Click += async (_, _) =>
            {
                try
                {
                    if (main.View.Clipboard is not { } clipboard)
                    {
                        main.Notify("The clipboard is not available.", true);
                        return;
                    }
                    await clipboard.SetTextAsync(raw);
                    main.Notify(label + " copied exactly.");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    main.Notify("The text could not be copied: " + Formatters.SafeName(ex.Message), true);
                }
            };
            row.Children.Add(copy);
            if (local is true && isPath)
            {
                var reveal = new Button { Content = "Reveal locally" };
                reveal.Click += async (_, _) =>
                {
                    try
                    {
                        bool exists = await Task.Run(() => Path.IsPathFullyQualified(raw) && (File.Exists(raw) || Directory.Exists(raw)));
                        if (!exists)
                        {
                            main.Notify("That local item is no longer available. Copy its exact path to inspect it elsewhere.", true);
                            return;
                        }
                        main.Services.Shell.Reveal(raw);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        main.Notify("The local item could not be revealed: " + Formatters.SafeName(ex.Message), true);
                    }
                };
                row.Children.Add(reveal);
            }
            body.Children.Add(row);
            var field = new TextBox
            {
                Text = Formatters.SafeName(raw), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true, MinWidth = 300, MaxHeight = isPath ? 100 : 160,
            };
            AutomationProperties.SetName(field, label + " with hidden characters escaped");
            body.Children.Add(field);
        }
    }

    private static string LocationKind(bool? local) => local switch
    {
        true => "Local filesystem",
        false => "Non-local provider",
        null => "Not established by this issue",
    };
}
