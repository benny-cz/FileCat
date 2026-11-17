using Avalonia.Controls;

namespace FileCat.App.Views;

public sealed partial class ReportWindow
{
    // These actions apply to the immutable snapshot the window opens. A new snapshot requires a new review.
    internal void ConfigureStorageReview(string exactReport, Func<Task> saveCalls, Func<Task>? acknowledge)
    {
        _refresh.IsVisible = false;
        void Add(string label, Func<Task> run)
        {
            var button = new Button { Content = label };
            button.Click += async (_, _) =>
            {
                if (_reading is not null || _text != exactReport)
                { _status.Text = "Wait for this exact storage snapshot to finish displaying before using its actions."; return; }
                button.IsEnabled = false;
                try { await run(); }
                finally { if (!_closing.IsCancellationRequested) button.IsEnabled = true; }
            };
            _actions.Children.Add(button);
        }
        Add("Save call details…", saveCalls);
        if (acknowledge is not null) Add("Acknowledge reviewed history…", acknowledge);
    }
}
