using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class UpdateDialogTests
{
    [AvaloniaFact]
    public async Task Skipping_a_release_is_saved_and_a_later_release_can_still_be_offered()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            const string url = "https://github.com/benny-cz/FileCat/releases/tag/v1.0.0-preview.5";
            var release = new UpdateCheck.Result(true, "1.0.0-preview.4", "1.0.0-preview.5", url, null);
            var showing = vm.ShowAvailableUpdateAsync(release, url);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            Assert.Contains(card.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Open release page"));
            Assert.Contains(card.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Not now"));
            card.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Skip this version"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await showing;
            Assert.Equal("1.0.0-preview.5", services.Settings.SkippedUpdateVersion);
            Assert.False(UpdateCheck.ShouldOfferOnStartup(services.Settings, release));

            var later = release with { Latest = "1.0.0-preview.6" };
            Assert.True(UpdateCheck.ShouldOfferOnStartup(services.Settings, later));
            showing = vm.ShowAvailableUpdateAsync(later, url);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            card.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Not now"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await showing;
            Assert.Equal("1.0.0-preview.5", services.Settings.SkippedUpdateVersion);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
