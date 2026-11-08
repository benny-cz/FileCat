using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    public async Task RollingHistoryPolicyReviewPreservesTheCurrentChoiceAndNamesOnlyItsPrivateScope(bool enabled, bool nextEnabled, bool accept)
    {
        using var styles = new ProductStyles();
        var overlay = new Grid { IsVisible = false }; var focus = new Button { Content = "Settings" };
        var window = new Window { Width = 480, Height = 620, FontSize = 20, Content = new Grid { Children = { focus, overlay } } };
        var dialogs = new OverlayDialogService(overlay, () => focus);
        var before = new AnalysisRollingHistoryPolicy(enabled);
        var next = new AnalysisRollingHistoryPolicy(nextEnabled, 20, 60, 2L << 30);
        try
        {
            window.Show(); Frame(window);
            var choice = AnalysisRollingHistoryPolicyDialog.ConfirmAsync(dialogs, before, next, "private/history/\u202Euntrusted-name");
            await Until(() => dialogs.IsOpen); Frame(window);
            string preserve = enabled ? "Keep current policy" : "Leave off";
            string apply = nextEnabled ? (enabled ? "Apply retention and pruning" : "Enable history and pruning")
                : enabled ? "Turn off history" : "Update limits and leave off";
            var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToArray();
            var keep = Assert.Single(buttons, b => b.Content as string == preserve);
            var change = Assert.Single(buttons, b => b.Content as string == apply);
            Assert.True(keep.IsDefault); Assert.True(keep.IsCancel); Assert.False(change.IsDefault);
            Assert.Equal(nextEnabled, change.Classes.Contains("danger")); Assert.Same(keep, window.FocusManager?.GetFocusedElement());
            foreach (var button in new[] { keep, change })
            {
                var at = button.TranslatePoint(default, window)!.Value;
                Assert.InRange(at.X, 0, window.ClientSize.Width - button.Bounds.Width + 1);
                Assert.InRange(at.Y, 0, window.ClientSize.Height - button.Bounds.Height + 1);
            }
            string text = string.Join('\n', window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
            Assert.Contains("Named saved analyses, source files", text); Assert.Contains("Current:", text); Assert.Contains("Proposed:", text);
            Assert.Contains("queries", text); Assert.DoesNotContain("\u202E", text, StringComparison.Ordinal);
            Assert.Contains("\\u202E", text, StringComparison.Ordinal); Assert.Contains("not secure erasure", text);
            Assert.Contains(nextEnabled ? "truncated snapshots" : "existing history is kept", text);
            (accept ? change : keep).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(accept, await choice); Assert.False(dialogs.IsOpen);
            Assert.Equal(enabled, before.Enabled); Assert.Equal(nextEnabled, next.Enabled);
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }
}
