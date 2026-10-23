using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FileCat.App.Views;

namespace FileCat.App.Controls;

/// <summary>
/// A stale macOS accessibility request can refocus the previous panel's address editor just after a
/// physical click in another panel. Reject that request at the peer before it changes keyboard focus.
/// Ordinary keyboard and pointer navigation remains available immediately on the next user input.
/// </summary>
public sealed class PanelAddressTextBox : TextBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new PanelAddressTextBoxAutomationPeer(this);

    private sealed class PanelAddressTextBoxAutomationPeer(PanelAddressTextBox owner) : TextBoxAutomationPeer(owner)
    {
        protected override void SetFocusCore()
        {
            if (TopLevel.GetTopLevel(owner) is MainWindow window &&
                window.ShouldRejectStalePanelAccessibilityFocus(owner)) return;
            base.SetFocusCore();
        }
    }
}
