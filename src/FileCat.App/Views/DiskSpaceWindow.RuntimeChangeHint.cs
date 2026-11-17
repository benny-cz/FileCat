using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private static TextBlock CreateRuntimeChangeHint()
    {
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        AutomationProperties.SetAutomationId(hint, "Captured source change hints");
        AutomationProperties.SetLiveSetting(hint, AutomationLiveSetting.Polite);
        return hint;
    }

    private static void PresentRuntimeChangeHint(TextBlock hint, string? text)
    {
        if (hint.Text != text) hint.Text = text;
        hint.IsVisible = text is not null;
    }

    private sealed partial class SessionView
    {
        private readonly TextBlock _runtimeChangeHint = CreateRuntimeChangeHint();

        private void RefreshRuntimeChangeHint()
        {
            if (_disposed) return;
            PresentRuntimeChangeHint(_runtimeChangeHint, Model.RuntimeChangeHints());
        }
    }

    private sealed partial class IndexedSessionView
    {
        private readonly TextBlock _runtimeChangeHint = CreateRuntimeChangeHint();
        private readonly DispatcherTimer _runtimeChangeTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        private void AddRuntimeChangeHint()
        {
            Children.Add(_runtimeChangeHint);
            AttachedToVisualTree += (_, _) => _runtimeChangeTimer.Start();
            DetachedFromVisualTree += (_, _) => _runtimeChangeTimer.Stop();
            _runtimeChangeTimer.Tick += (_, _) =>
            {
                if (!_disposed && IsEffectivelyVisible)
                    PresentRuntimeChangeHint(_runtimeChangeHint, _runtimeAcquisition?.ReadRuntimeChangeHints());
            };
        }
    }
}
