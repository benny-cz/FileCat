using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Services;

namespace FileCat.App.Views;

public sealed partial class ReportWindow
{
    internal void AddPermissionReviewTargets(IReadOnlyList<(int Index, string Name)> targets, Func<int, CancellationToken, Task<string?>> inspect)
    {
        if (targets.Count == 0) return;
        var choice = new ComboBox { ItemsSource = targets.Select(target => $"Object {target.Index + 1} · {Formatters.SafeName(target.Name)}").ToArray(), SelectedIndex = 0 };
        AutomationProperties.SetName(choice, "Retained permission subjects");
        var review = new Button { Content = "Review current state…" };
        AutomationProperties.SetHelpText(review, "Reinspect the retained object through the owning workspace.");
        _actions.Children.Add(choice);
        _actions.Children.Add(review);
        var notice = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false, MaxWidth = 480 };
        AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        _actions.Children.Add(notice);
        review.Click += async (_, _) =>
        {
            if (!review.IsEnabled || _closing.IsCancellationRequested || choice.SelectedIndex < 0) return;
            review.IsEnabled = false;
            notice.IsVisible = false;
            try
            {
                string? problem = await inspect(targets[choice.SelectedIndex].Index, _closing.Token);
                if (_closing.IsCancellationRequested) return;
                notice.Text = problem;
                notice.IsVisible = problem is not null;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (_closing.IsCancellationRequested) return;
                notice.Text = "Current permissions could not be inspected. " + Formatters.SafeName(error.Message);
                notice.IsVisible = true;
            }
            finally { review.IsEnabled = !_closing.IsCancellationRequested; }
        };
    }
}
