using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ShowRecentNotificationsAsync()
    {
        var report = new StringBuilder();
        foreach (var item in RecentNotifications.Reverse())
        {
            if (report.Length > 0) report.AppendLine().AppendLine();
            report.Append('[').Append(item.LastAt.ToLocalTime().ToString("g")).Append("] ")
                .Append(item.IsError ? "Error" : "Notice");
            if (item.Occurrences > 1) report.Append(" · ").Append(item.OccurrenceLabel);
            report.AppendLine().Append(item.Message);
            if (item.ActionCommandId is { } action)
                report.AppendLine().Append("Action: ").Append(Services.Commands.Get(action)?.Title ?? action);
        }
        var text = new TextBox
        {
            Text = report.Length == 0 ? "No notifications yet." : report.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinWidth = 240,
            MaxWidth = 620,
            MinHeight = 200,
            MaxHeight = 400,
        };
        AutomationProperties.SetName(text, "Recent notifications, newest first; Ctrl+A selects all and Ctrl+C copies");
        await Dialogs.ShowCustomAsync("Recent notifications", text,
            [new DialogButton("Close", true, IsDefault: true, IsCancel: true)], text);
    }
}
