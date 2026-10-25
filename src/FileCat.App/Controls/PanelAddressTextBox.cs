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
    // Reuse the TextBox theme template and the panel's TextBox.path/filter styles.
    // Without a style key, this subclass still has a UIA value and layout bounds,
    // but no TextPresenter, so the mask and path editor paint no text.
    protected override Type StyleKeyOverride => typeof(TextBox);

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
