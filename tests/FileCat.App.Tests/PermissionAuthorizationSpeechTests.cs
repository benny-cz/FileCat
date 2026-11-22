using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.App.Tests;

/// <summary>
/// G-UX, NVDA on the Windows authorized-inspection flow and audit editor: a screen reader reads the focused control,
/// not the text beside it. The waiting state and an alert speak their sentence from the focused button, an asynchronous
/// action keeps focus in its dialog, and the audit mask says its format with the label it shows.
/// </summary>
public sealed class PermissionAuthorizationSpeechTests
{
    private static (Window Window, OverlayDialogService Dialogs) Open()
    {
        var fallback = new TextBox { Text = "fallback" };
        var host = new Grid();
        var window = new Window { Width = 900, Height = 700, Content = new Grid { Children = { fallback, host } } };
        window.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://FileCat/"))
            { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
        window.Show();
        fallback.Focus();
        return (window, new OverlayDialogService(host, () => fallback));
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static IInputElement? Focused(Window window) => window.FocusManager?.GetFocusedElement();

    [AvaloniaFact]
    public async Task The_authorization_wait_speaks_its_sentence_from_the_focused_Cancel()
    {
        var (window, dialogs) = Open();
        try
        {
            const string approval = "Windows asks for administrator approval to read this item's security metadata.";
            var closer = new DialogCloser();
            var waiting = MainViewModel.ShowAuthorizationWaitAsync(dialogs, @"C:\fixture\F-audit.txt", approval, closer);
            Settle(window);
            var cancel = Assert.IsType<Button>(Focused(window));
            Assert.Equal("Cancel", cancel.Content);
            Assert.Equal(approval, ControlAutomationPeer.CreatePeerForElement(cancel).GetHelpText());
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == approval);
            closer.Close("done");
            Assert.Equal("done", await waiting);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task An_alert_speaks_its_message_from_the_focused_OK()
    {
        var (window, dialogs) = Open();
        try
        {
            const string reason = "The reviewed object, its namespace or its metadata changed.";
            var alert = dialogs.AlertAsync("Authorized inspection unavailable", reason);
            Settle(window);
            var ok = Assert.IsType<Button>(Focused(window));
            Assert.Equal("OK", ok.Content);
            Assert.Equal(reason, ControlAutomationPeer.CreatePeerForElement(ok).GetHelpText());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await alert;
        }
        finally { window.Close(); }
        // Paragraphs become one spoken line, and a long message stays bounded.
        Assert.Equal("First line. Second paragraph.", OverlayDialogService.SpokenMessage("First line.\r\n\r\n  Second paragraph.  "));
        string spoken = OverlayDialogService.SpokenMessage(new string('a', 700));
        Assert.Equal(600, spoken.Length);
        Assert.EndsWith("…", spoken, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task An_asynchronous_action_keeps_keyboard_focus_in_its_dialog()
    {
        var (window, dialogs) = Open();
        try
        {
            var action = new TaskCompletionSource();
            bool available = true;
            var shown = dialogs.ShowCustomAsync("Edit audit rules", new TextBlock { Text = "Reviewed draft" },
                [new("Cancel", "close", IsCancel: true),
                 new("Apply with authorization", "apply", IsDefault: true) { IsAvailable = () => available, InvokeAsync = () => action.Task }],
                behavior: new DialogBehavior { DismissOnBackdrop = false, KeepOperationsUsableDuringAction = true });
            Settle(window);
            var apply = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Apply with authorization"));
            var card = window.GetVisualDescendants().OfType<DialogCard>().Single();
            Assert.Same(apply, Focused(window));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Settle(window);
            // The running action disables its button; the dialog, not the window, keeps focus (NVDA names the dialog).
            Assert.False(apply.IsEnabled);
            Assert.Same(card, Focused(window));
            Assert.Equal("Edit audit rules dialog", AutomationProperties.GetName(card));
            available = false;
            action.SetResult();
            Settle(window);
            Assert.Same(card, Focused(window));
            dialogs.CancelAll();
            Assert.Equal("close", await shown);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_audit_mask_speaks_its_visible_label_with_the_hexadecimal_format()
    {
        var everyone = new Principal(PrincipalKind.WindowsSid, "S-1-1-0", "local");
        var editor = new WindowsAuditEditor(new WindowsSacl(true, 2, new(0x10, 1, null),
            [WindowsPermissionAuditPatch.FileEntry(everyone, 0x80, WindowsPermissionAuditPatch.Success)], [], []), everyone.Authority);
        var boxes = editor.Children.OfType<TextBox>().ToArray();
        var labels = editor.Children.OfType<TextBlock>().Select(label => label.Text ?? "").ToArray();
        string mask = AutomationProperties.GetName(Assert.Single(boxes,
            box => AutomationProperties.GetName(box)?.StartsWith("Audit rights mask", StringComparison.Ordinal) == true))!;
        Assert.Contains("hexadecimal", mask, StringComparison.Ordinal);
        // Label in name: the text shown above each box is part of what a screen reader and speech input call it.
        Assert.Contains(mask, labels);
        Assert.Contains(labels, label => label.Length > 0 && "Audit account SID".Contains(label, StringComparison.OrdinalIgnoreCase));
        Assert.Single(boxes, box => AutomationProperties.GetName(box) == "Audit account SID");
    }
}
