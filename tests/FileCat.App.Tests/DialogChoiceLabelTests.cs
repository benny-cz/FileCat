using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.App.Tests;

/// <summary>
/// A dialog's check box or radio button shows a label that is a control (a wrapping TextBlock) as that control, and a string
/// label through the dialog's wrapping-text template. The template was set for every label: it met a control it does not
/// match, and the presenter fell back to the control's type name, so the folder choices of the NTFS compression review read
/// "Avalonia.Controls.TextBlock".
/// </summary>
public sealed class DialogChoiceLabelTests
{
    [AvaloniaFact]
    public async Task Choices_show_control_labels_as_themselves_and_string_labels_as_wrapping_text()
    {
        var host = new Grid();
        var fallback = new TextBox();
        var window = new Window { Content = new Grid { Children = { fallback, host } }, Width = 640, Height = 420 };
        var dialogs = new OverlayDialogService(host, () => fallback);
        window.Show();
        try
        {
            var controlLabel = new TextBlock { Text = "A label that is a control", TextWrapping = TextWrapping.Wrap };
            var radio = new RadioButton { Content = controlLabel, GroupName = "labels" };
            var stringRadio = new RadioButton { Content = "A string label", GroupName = "labels" };
            var checkLabel = new TextBlock { Text = "A check box label that is a control" };
            var check = new CheckBox { Content = checkLabel };
            var pending = dialogs.ShowCustomAsync("Labels", new StackPanel { Children = { radio, stringRadio, check } },
                [new DialogButton("Close", "close", IsDefault: true, IsCancel: true)], radio);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(radio.GetVisualDescendants(), visual => ReferenceEquals(visual, controlLabel));
            Assert.Contains(check.GetVisualDescendants(), visual => ReferenceEquals(visual, checkLabel));
            Assert.Contains(stringRadio.GetVisualDescendants().OfType<AccessText>(),
                text => text.Text == "A string label" && text.TextWrapping == TextWrapping.Wrap);
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.StartsWith("Avalonia.Controls.", StringComparison.Ordinal) == true);

            dialogs.CancelAll();
            await pending;
        }
        finally { window.Close(); }
    }
}
