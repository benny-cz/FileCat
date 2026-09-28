using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Remote.Tests;

namespace FileCat.App.Tests;

public sealed class SftpConnectTests
{
    private sealed record Session(AppServices Services, MainViewModel Vm, MainWindow Window, string Root, FakeSftpServer Server, TabViewModel Remote);

    private static async Task Click(Window window, string text)
    {
        var ct = TestContext.Current.CancellationToken;
        for (int i = 0; i < 250 && !window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == text && b.IsEffectivelyEnabled); i++)
            await Task.Delay(20, ct);
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
    }

    /// <summary>Connects the first panel to an in-memory server: host key, then password.</summary>
    private static async Task<Session> ConnectAsync()
    {
        var server = new FakeSftpServer();
        server.File("/home/user/remote.txt", "hello");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(new FakeConnector(server));
        Assert.True(services.Providers.TryParse("sftp://user@files.example/~", null, out var location));
        // The server in the first panel; the second keeps the local test folder.
        vm.Workspace.Activate(vm.Workspace.Panels[0]);
        var tab = vm.Workspace.Panels[0].ActiveTab!;
        tab.Navigate(location!);
        await Click(window, "Trust and connect");
        await WaitAsync(() => window.GetVisualDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "Password"));
        window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Password").Text = "secret";
        await Click(window, "Connect");
        // Opening "~" connects first, then shows the home folder by its absolute path.
        await WaitAsync(() => tab.Location is { Scheme: Core.Resources.Schemes.Sftp, Path: "/home/user" } && tab.Listing.State == Core.Listing.ListingState.Complete);
        return new Session(services, vm, window, root, server, tab);
    }

    private static async Task<Job> LastJobAsync(AppServices services, int count)
    {
        await WaitAsync(() => services.Jobs.Jobs.Count >= count);
        var job = services.Jobs.Jobs[count - 1];
        await WaitAsync(() => job.State.IsFinished());
        return job;
    }

    [AvaloniaFact]
    public async Task An_sftp_address_asks_about_the_server_key_and_password_then_lists_the_server()
    {
        var s = await ConnectAsync();
        try
        {
            Assert.False(((OverlayDialogService)s.Vm.Dialogs).IsOpen);
            Assert.Equal(Core.Listing.ListingState.Complete, s.Remote.Listing.State);
            Assert.Equal(["..", "remote.txt"], Enumerable.Range(0, s.Remote.Listing.VisibleCount).Select(i => s.Remote.Listing.GetVisible(i).Name));
            Assert.Equal("sftp://user@files.example/home/user", s.Services.Providers.Display(s.Remote.Location!));
            Assert.Contains("files.example ssh-ed25519 ", File.ReadAllText(Path.Combine(s.Services.Paths.LocalDirectory, "known_hosts")));
        }
        finally
        {
            AccessibilityTests.Close(s.Services, s.Window, s.Root);
        }
    }

    [AvaloniaFact]
    public async Task F7_F8_and_F5_work_on_the_server()
    {
        var s = await ConnectAsync();
        try
        {
            // F7: a folder on the server.
            Assert.Same(s.Remote, s.Vm.ActiveTab);
            s.Vm.Execute(CommandIds.MakeDirectory);
            await WaitAsync(() => s.Window.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsFocused));
            s.Window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsFocused).Text = "made";
            await Click(s.Window, "Create");
            Assert.Equal(JobState.Completed, (await LastJobAsync(s.Services, 1)).State);
            Assert.True(s.Server.Lookup("/home/user/made", false)!.IsDirectory);

            // F5 from the local panel into the server's folder (the target panel).
            var local = s.Vm.Workspace.Panels[1];
            s.Vm.Workspace.Activate(local);
            Assert.True(local.ActiveTab!.Location!.IsFileSystem);
            Assert.Same(local.ActiveTab, s.Vm.ActiveTab);
            await WaitAsync(() => local.ActiveTab!.Listing.State == Core.Listing.ListingState.Complete);
            Assert.True(local.ActiveTab!.Listing.FocusName("a.txt"));
            s.Vm.Execute(CommandIds.Copy);
            await Click(s.Window, "Copy");
            Assert.Equal(JobState.Completed, (await LastJobAsync(s.Services, 2)).State);
            Assert.Equal("alpha", s.Server.Read("/home/user/a.txt"));

            // F8 on the server always deletes permanently, after one confirmation. First let the closed copy dialog
            // return focus to the local panel (it does so asynchronously), then move to the server's panel.
            await WaitAsync(() => !((OverlayDialogService)s.Vm.Dialogs).IsOpen);
            await Task.Delay(150, TestContext.Current.CancellationToken);
            s.Vm.Workspace.Activate(s.Vm.Workspace.Panels[0]);
            s.Vm.View.FocusActivePanel();
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.Same(s.Remote, s.Vm.ActiveTab);
            await WaitAsync(() => s.Remote.Listing.State == Core.Listing.ListingState.Complete && s.Remote.Listing.FocusName("made"));
            Assert.True(s.Remote.Listing.FocusName("made"));
            s.Vm.Execute(CommandIds.Delete);
            await Click(s.Window, "Delete permanently");
            Assert.Equal(JobState.Completed, (await LastJobAsync(s.Services, 3)).State);
            Assert.False(s.Server.Exists("/home/user/made"));
        }
        finally
        {
            AccessibilityTests.Close(s.Services, s.Window, s.Root);
        }
    }
}
