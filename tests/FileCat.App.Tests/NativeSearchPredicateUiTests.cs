using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Search;

namespace FileCat.App.Tests;

public sealed class NativeSearchPredicateUiTests
{
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task Restored_unsupported_set_and_clear_criteria_remain_visible_until_explicitly_removed()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            window.Styles.Add(new StyleInclude(new Uri("avares://FileCat/")) { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
            var criteria = new AdvancedSearchCriteria { AttributesSet = (FileAttributes)0x40000000, AttributesClear = FileAttributes.Encrypted };
            var pending = FindDialogs.AdvancedAsync(vm.Dialogs, criteria, nativeScope: false);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var overlay = window.FindControl<Panel>("OverlayHost")!;
            await Until(() => overlay.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "OK"));
            var encrypted = Assert.Single(overlay.GetVisualDescendants().OfType<CheckBox>(), c => c.Content as string == NativeSearchPredicates.Name(FileAttributes.Encrypted));
            Assert.False(encrypted.IsEnabled); Assert.False(encrypted.IsChecked);
            var ok = overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "OK");
            Assert.False(ok.IsEffectivelyEnabled);
            foreach (var name in new[] { NativeSearchPredicates.Name(FileAttributes.Encrypted), NativeSearchPredicates.Name((FileAttributes)0x40000000) })
            {
                var remove = Assert.Single(overlay.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Remove this criterion: " + name);
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await Until(() => ok.IsEffectivelyEnabled);
            Assert.Null(encrypted.IsChecked);
            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var accepted = await pending;
            Assert.NotNull(accepted); Assert.Equal((FileAttributes)0, accepted.AttributesSet); Assert.Equal((FileAttributes)0, accepted.AttributesClear);
            Assert.Equal(FileAttributes.Encrypted, criteria.AttributesClear); // canceled/edited state never mutates the saved object
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [Fact]
    public void Finished_unexamined_scope_has_exact_coverage_wording_without_calling_it_canceled()
    {
        var feedback = FindFeedback.Create(true, false, 0, 2, 0, 0, null, [], false, "test", predicatesUnexamined: 7000);
        Assert.Contains("coverage limited", feedback.Headline); Assert.DoesNotContain("Stopped", feedback.Headline);
        Assert.Contains(7000.ToString("N0"), feedback.Qualifications); Assert.Contains("unexamined", feedback.Qualifications);
    }
}
