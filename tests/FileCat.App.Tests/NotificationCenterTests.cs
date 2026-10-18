using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>FCUX-029: severity, occurrence history, deliberate dismissal, and accessible review.</summary>
public sealed class NotificationCenterTests
{
    [AvaloniaFact]
    public void Severity_changes_and_repeated_messages_have_coherent_lifetimes_and_history()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            vm.Notify("A notice");
            Assert.False(vm.NotificationIsError);
            Assert.True(window.NotificationTimerEnabled);
            Assert.Equal(TimeSpan.FromSeconds(6), window.NotificationTimerInterval);
            vm.Notify("A failure", true);
            Assert.True(vm.NotificationIsError);
            Assert.False(window.NotificationTimerEnabled); // errors stay until deliberately dismissed
            Assert.Equal(1, vm.CurrentNotification!.Occurrences);

            int speechChanges = 0;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Notification)) speechChanges++;
            };
            vm.Notify("A failure", true);
            Assert.Equal(2, vm.CurrentNotification!.Occurrences);
            Assert.Equal("Repeated 2 times", vm.NotificationRepeatText);
            Assert.Equal(0, speechChanges); // the live message text does not change for an equivalent repeat
            Assert.Single(vm.RecentNotifications, n => n.Message == "A failure" && n.IsError);

            vm.Notify("A failure"); // the same text with a new severity is a distinct message
            Assert.False(vm.NotificationIsError);
            Assert.True(window.NotificationTimerEnabled);
            Assert.Equal(TimeSpan.FromSeconds(6), window.NotificationTimerInterval);
            Assert.Equal(2, vm.RecentNotifications.Count(n => n.Message == "A failure"));
            long earlier = vm.CurrentNotification!.Id;
            vm.Notify("Another failure", true);
            Assert.False(window.NotificationTimerEnabled);
            Assert.Equal("Another failure", vm.Notification);
            vm.ClearNotification(earlier);
            Assert.Equal("Another failure", vm.Notification);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Message_can_be_copied_and_read_later_after_dismissal_or_an_unrelated_command()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            await Until(() => vm.ActiveTab?.Listing.State == ListingState.Complete);
            vm.Notify("Review this exact failure path: C:\\Data\\file.txt", true);
            window.CaptureRenderedFrame();
            var close = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotificationCloseButton");
            var copy = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotificationCopyButton");
            var message = window.GetVisualDescendants().OfType<SelectableTextBlock>().Single(b => b.Name == "NotificationErrorText");
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(message));
            Assert.True(message.IsVisible);
            var point = message.TranslatePoint(new Point(Math.Min(20, message.Bounds.Width / 2), message.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.NotNull(vm.Notification); // reading or selecting the message is not a dismissal gesture
            Assert.True(copy.Focus());
            await vm.ExecuteAsync(CommandIds.SortName);
            Assert.NotNull(vm.Notification); // unrelated commands do not erase an error
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            string expected = vm.Notification!;
            string? copied = null;
            for (int i = 0; i < 100 && copied != expected; i++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                copied = await ClipboardExtensions.TryGetTextAsync(window.Clipboard!);
            }
            Assert.Equal(expected, copied);

            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(vm.Notification);
            var history = vm.ExecuteAsync(CommandIds.RecentNotifications);
            await Until(() => ((OverlayDialogService)vm.Dialogs).IsOpen);
            window.CaptureRenderedFrame();
            var report = window.GetVisualDescendants().OfType<TextBox>()
                .Single(b => AutomationProperties.GetName(b)?.StartsWith("Recent notifications", StringComparison.Ordinal) == true);
            Assert.True(report.IsReadOnly);
            Assert.Contains("C:\\Data\\file.txt", report.Text);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await history.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Focused_action_pauses_info_expiry_and_Escape_dismisses_before_other_work()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            vm.Notify("Workspace switched", actionCommandId: CommandIds.RestorePreviousWorkspace);
            var restore = window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Name == "RestoreWorkspaceNotificationButton");
            Assert.True(restore.IsVisible);
            Assert.True(restore.Focus());
            window.TryExpireNotification(vm.CurrentNotification!.LastAt.AddSeconds(7));
            Assert.Equal("Workspace switched", vm.Notification);
            Assert.True(window.NotificationTimerEnabled);
            Assert.True(window.GetVisualDescendants().OfType<FileCat.App.Controls.FileListControl>().First().Focus());
            window.TryExpireNotification(vm.CurrentNotification!.LastAt.AddSeconds(7));
            Assert.Null(vm.Notification);
            vm.Notify("Another notice");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Null(vm.Notification);
            Assert.Contains(vm.RecentNotifications, n => n.Message == "Workspace switched" && n.ActionCommandId == CommandIds.RestorePreviousWorkspace);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Recent_review_is_bounded_to_one_hundred_groups()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            for (int i = 0; i < 105; i++) vm.Notify($"notice-{i:D3}");
            Assert.Equal(100, vm.RecentNotifications.Count);
            Assert.DoesNotContain(vm.RecentNotifications, n => n.Message == "notice-000");
            Assert.Equal("notice-104", vm.RecentNotifications[^1].Message);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Hovering_a_notice_keeps_it_available_for_reading()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            vm.Notify("A long explanation to inspect");
            window.CaptureRenderedFrame();
            var banner = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NotificationBorder");
            var point = banner.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseMove(point);
            Assert.True(banner.IsPointerOver);
            window.TryExpireNotification(vm.CurrentNotification!.LastAt.AddSeconds(7));
            Assert.NotNull(vm.Notification);
            window.MouseMove(new Point(2, 2));
            Assert.False(banner.IsPointerOver);
            window.TryExpireNotification(vm.CurrentNotification!.LastAt.AddSeconds(7));
            Assert.Null(vm.Notification);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
