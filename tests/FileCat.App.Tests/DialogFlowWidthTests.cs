using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

public sealed class DialogFlowWidthTests
{
    [AvaloniaFact]
    public async Task Command_archive_link_and_connection_forms_fit_without_page_horizontal_scrolling()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 455);
        try
        {
            window.Styles.Add(new StyleInclude(new Uri("avares://FileCat/"))
                { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
            var dialogs = (OverlayDialogService)vm.Dialogs;
            async Task Check(string command)
            {
                var pending = vm.ExecuteAsync(command);
                for (int i = 0; i < 250 && !dialogs.IsOpen; i++)
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                Assert.True(dialogs.IsOpen, command + " did not open its dialog.");
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var scroll = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "DialogBodyScroll");
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1,
                    $"{command}: horizontal page extent {scroll.Extent.Width} exceeds viewport {scroll.Viewport.Width}.");
                dialogs.CancelAll();
                await pending;
            }

            await Check(CommandIds.SftpConnect);
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && listing.State != FileCat.Core.Listing.ListingState.Complete; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(listing.FocusName("a.txt"));
            vm.Execute(CommandIds.MarkAll);
            await Check(CommandIds.ApplyCommand);
            await Check(CommandIds.Pack);
            await Check(CommandIds.CreateLink);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
