using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class TransferDestinationValidationTests
{
    [AvaloniaFact]
    public async Task Invalid_filter_stays_in_the_same_form_until_queue_options_are_corrected()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string source = Path.Combine(root, "files", "a.txt");
            string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var pending = OperationDialogs.ShowTransferAsync(vm, new TransferDialogInput(JobKind.Copy,
                [ItemRef.ForFileSystemPath(source, EntryKind.File)], "1 file",
                target + Path.DirectorySeparatorChar, null, 0, false)
                { BaseLocation = vm.ActiveTab?.Location });
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(dialogs.IsOpen);
            window.UpdateLayout();
            window.GetVisualDescendants().OfType<Expander>()
                .Single(e => Equals(e.Header, "Options")).IsExpanded = true;
            window.UpdateLayout();
            var filter = window.GetVisualDescendants().OfType<TextBox>()
                .Single(c => AutomationProperties.GetName(c) == "Only files matching:");
            var queue = window.GetVisualDescendants().OfType<Button>()
                .Single(b => Equals(b.Content, "Queue"));
            var error = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(c => AutomationProperties.GetName(c) == "Transfer options error");
            for (int i = 0; i < 250 && !queue.IsEnabled; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(queue.IsEnabled);
            filter.Text = "@missing";
            queue.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialogs.IsOpen);
            Assert.False(pending.IsCompleted);
            Assert.True(error.IsVisible);
            Assert.Contains("Filter:", error.Text);
            Assert.Same(filter, window.GetVisualDescendants().OfType<TextBox>()
                .Single(c => AutomationProperties.GetName(c) == "Only files matching:"));

            filter.Text = "*.txt";
            queue.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var result = Assert.IsType<TransferDialogResult>(await pending);
            Assert.True(result.Queue);
            Assert.NotNull(result.Options.Filter);
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("NaN", "0.0625")]
    [InlineData("Infinity", "0,0625")]
    [InlineData("-1", "0.0625")]
    [InlineData("0.0000001", "0,0625")]
    [InlineData("100000000000000000000", "0.0625")]
    public async Task Invalid_speed_stays_in_the_same_form_and_local_or_dot_decimal_can_be_corrected(
        string invalidSpeed, string correctedSpeed)
    {
        var priorCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string source = Path.Combine(root, "files", "a.txt");
            string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var pending = OperationDialogs.ShowTransferAsync(vm, new TransferDialogInput(JobKind.Copy,
                [ItemRef.ForFileSystemPath(source, EntryKind.File)], "1 file",
                target + Path.DirectorySeparatorChar, null, 0, false)
                { BaseLocation = vm.ActiveTab?.Location });
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen);
            window.UpdateLayout();
            var options = window.GetVisualDescendants().OfType<Expander>()
                .Single(e => Equals(e.Header, "Options"));
            options.IsExpanded = true;
            window.UpdateLayout();
            var speed = window.GetVisualDescendants().OfType<TextBox>()
                .Single(c => AutomationProperties.GetName(c) == "Speed limit (MB/s):");
            var start = window.GetVisualDescendants().OfType<Button>()
                .Single(b => Equals(b.Content, "Copy"));
            var error = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(c => AutomationProperties.GetName(c) == "Transfer options error");
            await Until(() => start.IsEnabled);
            speed.Text = invalidSpeed;
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialogs.IsOpen);
            Assert.False(pending.IsCompleted);
            Assert.True(error.IsVisible);
            Assert.Contains("Speed limit", error.Text);
            Assert.Same(speed, window.GetVisualDescendants().OfType<TextBox>()
                .Single(c => AutomationProperties.GetName(c) == "Speed limit (MB/s):"));

            speed.Text = correctedSpeed;
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var result = Assert.IsType<TransferDialogResult>(await pending);
            Assert.Equal(65536, result.Options.RateLimit);
            Assert.False(result.Queue);
            Assert.Empty(services.Jobs.Jobs);

            async Task Until(Func<bool> ready)
            {
                for (int i = 0; i < 250 && !ready(); i++)
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                Assert.True(ready(), "The transfer form did not settle.");
            }
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
            CultureInfo.CurrentCulture = priorCulture;
        }
    }

    [AvaloniaFact]
    public async Task Unsupported_destination_keeps_options_and_both_submission_buttons_in_the_form()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string source = Path.Combine(root, "files", "a.txt");
            string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var input = new TransferDialogInput(JobKind.Copy, [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                "1 file", target + Path.DirectorySeparatorChar, null, 0, false) { BaseLocation = vm.ActiveTab?.Location };
            var pending = OperationDialogs.ShowTransferAsync(vm, input);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen);
            window.UpdateLayout();
            var destination = Named<TextBox>("Destination");
            var error = Named<TextBlock>("Destination validation error");
            var start = ActionButton("Copy");
            var queue = ActionButton("Queue");
            var options = window.GetVisualDescendants().OfType<Expander>().Single(e => Equals(e.Header, "Options"));
            options.IsExpanded = true;
            window.UpdateLayout();
            var combos = window.GetVisualDescendants().OfType<ComboBox>().ToArray();
            var conflict = combos.Single(c => c.ItemsSource?.Cast<object>().Any(x => x as string == "Replace if newer") == true);
            var verify = combos.Single(c => c.ItemsSource?.Cast<object>().Any(x => x as string == "Read back and compare content") == true);
            var filter = Named<TextBox>("Only files matching:");
            var speed = Named<TextBox>("Speed limit (MB/s):");
            conflict.SelectedIndex = 3;
            verify.SelectedIndex = 1;
            filter.Text = "*.txt";
            speed.Text = "2";

            destination.Text = "This PC";
            await Until(() => error.IsVisible && !start.IsEnabled && !queue.IsEnabled);
            Assert.Contains("destination", error.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(error.Text!, AutomationProperties.GetHelpText(destination));
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            queue.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialogs.IsOpen);
            Assert.False(pending.IsCompleted);
            Assert.Equal(3, conflict.SelectedIndex);
            Assert.Equal(1, verify.SelectedIndex);
            Assert.Equal("*.txt", filter.Text);
            Assert.Equal("2", speed.Text);

            destination.Text = target + Path.DirectorySeparatorChar;
            await Until(() => start.IsEnabled && queue.IsEnabled);
            Assert.False(error.IsVisible);
            queue.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var result = Assert.IsType<TransferDialogResult>(await pending);
            Assert.True(result.Queue);
            Assert.Equal(target, result.ResolvedDestination!.Path.TrimEnd(Path.DirectorySeparatorChar));
            Assert.Null(result.NewName);
            Assert.Equal(ConflictPolicy.ReplaceIfNewer, result.Options.Conflicts);
            Assert.Equal(VerifyMode.ReadBack, result.Options.Verify);
            Assert.Equal(2L * 1024 * 1024, result.Options.RateLimit);
            Assert.Empty(services.Jobs.Jobs);

            T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>()
                .Single(c => AutomationProperties.GetName(c) == name);
            Button ActionButton(string label) => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label));
            async Task Until(Func<bool> ready)
            {
                for (int i = 0; i < 250 && !ready(); i++) await Task.Delay(20, ct);
                Assert.True(ready(), "Destination validation did not settle.");
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_single_item_name_uses_the_mapping_shown_before_start()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string parent = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            string newName = "renamed.txt";
            var input = new TransferDialogInput(JobKind.Move,
                [ItemRef.ForFileSystemPath(Path.Combine(root, "files", "a.txt"), EntryKind.File)], "1 file",
                Path.Combine(parent, newName), null, 0, false) { BaseLocation = vm.ActiveTab?.Location };
            var pending = OperationDialogs.ShowTransferAsync(vm, input);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen);
            window.UpdateLayout();
            var start = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Move"));
            await Until(() => start.IsEnabled);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), b =>
                b.Text?.Contains("→ as “" + newName + "”", StringComparison.Ordinal) == true);
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var result = Assert.IsType<TransferDialogResult>(await pending);
            Assert.False(result.Queue);
            Assert.Equal(parent, result.ResolvedDestination!.Path);
            Assert.Equal(newName, result.NewName);
            Assert.Empty(services.Jobs.Jobs);

            async Task Until(Func<bool> ready)
            {
                for (int i = 0; i < 250 && !ready(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
                Assert.True(ready(), "The destination mapping did not settle.");
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Cancel_does_not_wait_for_a_blocked_destination_probe()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            vm.TransferDestinationDirectoryExists = path =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return Directory.Exists(path);
            };
            var pending = OperationDialogs.ShowTransferAsync(vm, new TransferDialogInput(JobKind.Copy,
                [ItemRef.ForFileSystemPath(Path.Combine(root, "files", "a.txt"), EntryKind.File)], "1 file",
                target + Path.DirectorySeparatorChar, null, 0, false) { BaseLocation = vm.ActiveTab?.Location });
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !entered.IsSet; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(entered.IsSet);
            dialogs.CancelAll();
            var completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.Same(pending, completed);
            Assert.Null(await pending);
            Assert.False(release.IsSet);
            Assert.Empty(services.Jobs.Jobs);
        }
        finally
        {
            release.Set();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_delayed_destination_probe_does_not_block_correction_or_cancel()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            vm.TransferDestinationDirectoryExists = path =>
            {
                if (Path.GetFileName(path) == "slow")
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
                return Directory.Exists(path);
            };
            var input = new TransferDialogInput(JobKind.Copy,
                [ItemRef.ForFileSystemPath(Path.Combine(root, "files", "a.txt"), EntryKind.File)], "1 file",
                target + Path.DirectorySeparatorChar, null, 0, false) { BaseLocation = vm.ActiveTab?.Location };
            var pending = OperationDialogs.ShowTransferAsync(vm, input);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen);
            window.UpdateLayout();
            var destination = window.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "Destination");
            var start = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Copy"));
            destination.Text = Path.Combine(target, "slow");
            await Until(() => entered.IsSet);
            Assert.False(start.IsEnabled);
            destination.Text = target + Path.DirectorySeparatorChar;
            await Until(() => start.IsEnabled);
            Assert.False(release.IsSet);
            dialogs.CancelAll();
            Assert.Null(await pending);
            release.Set();
            Assert.Empty(services.Jobs.Jobs);

            async Task Until(Func<bool> ready)
            {
                for (int i = 0; i < 250 && !ready(); i++) await Task.Delay(20, ct);
                Assert.True(ready(), "The transfer form did not respond before the old destination probe returned.");
            }
        }
        finally
        {
            release.Set();
            AccessibilityTests.Close(services, window, root);
        }
    }
}
