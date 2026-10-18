using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FileCat.App.Controls;

/// <summary>Copy evidence remains selectable until dismissed or replaced by another explicit copy.</summary>
internal sealed class ContentCopyNotice : Border
{
    private readonly TextBox _result = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 112, MinHeight = 44 };
    private readonly Button _stop = new() { Content = "Stop copy", IsVisible = false };
    internal string ResultText => _result.Text ?? "";
    internal event Action? StopRequested;

    internal ContentCopyNotice()
    {
        IsVisible = false;
        Padding = new Thickness(8, 4);
        Classes.Add("banner");
        AutomationProperties.SetName(_result, "Copy result and completeness");
        var dismiss = new Button { Content = "Dismiss copy result", VerticalAlignment = VerticalAlignment.Top };
        dismiss.Click += (_, _) => { if (_stop.IsVisible) StopRequested?.Invoke(); IsVisible = false; };
        _stop.Click += (_, _) => StopRequested?.Invoke();
        var actions = new StackPanel { Spacing = 4, Margin = new Thickness(8, 0, 0, 0), Children = { _stop, dismiss } };
        DockPanel.SetDock(actions, Dock.Right);
        Child = new DockPanel { Children = { actions, _result } };
    }

    internal void Show(string message, bool running = false)
    {
        _result.Text = message;
        _stop.IsVisible = running;
        IsVisible = true;
    }
}
