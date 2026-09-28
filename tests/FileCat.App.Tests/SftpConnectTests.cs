using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Remote.Tests;

namespace FileCat.App.Tests;

public sealed class SftpConnectTests
{
    [AvaloniaFact]
    public async Task An_sftp_address_asks_about_the_server_key_and_password_then_lists_the_server()
    {
        var server = new FakeSftpServer();
        server.File("/home/user/remote.txt", "hello");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(new FakeConnector(server));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var dialogs = (OverlayDialogService)vm.Dialogs;
            async Task Click(string text)
            {
                for (int i = 0; i < 250 && !window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == text); i++) await Task.Delay(20, ct);
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            Assert.True(services.Providers.TryParse("sftp://user@files.example/~", null, out var location));
            var tab = vm.ActiveTab!;
            tab.Navigate(location!);
            await Click("Trust and connect");
            for (int i = 0; i < 250 && !window.GetVisualDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "Password"); i++) await Task.Delay(20, ct);
            window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Password").Text = "secret";
            await Click("Connect");
            for (int i = 0; i < 250 && !(tab.Listing.State == Core.Listing.ListingState.Complete && tab.Listing.VisibleCount >= 2); i++) await Task.Delay(20, ct);
            Assert.False(dialogs.IsOpen);
            Assert.Equal(Core.Listing.ListingState.Complete, tab.Listing.State);
            Assert.Contains("remote.txt", Enumerable.Range(0, tab.Listing.VisibleCount).Select(i => tab.Listing.GetVisible(i).Name));
            Assert.Contains("files.example ssh-ed25519 ", File.ReadAllText(Path.Combine(services.Paths.LocalDirectory, "known_hosts")));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
