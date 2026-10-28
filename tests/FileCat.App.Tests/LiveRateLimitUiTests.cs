using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class LiveRateLimitUiTests
{
    [AvaloniaFact]
    public async Task Live_limit_keeps_exact_bytes_and_rejects_invalid_edits_in_the_same_prompt()
    {
        var priorCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Jobs.MaxConcurrent = 0;
            string source = Path.Combine(root, "files", "a.txt");
            string target = Directory.CreateDirectory(Path.Combine(root, "rate-target")).FullName;
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(target),
                Options = new TransferOptions { RateLimit = 65536 },
                Mode = QueueMode.Queue,
            });
            var other = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(target),
                Options = new TransferOptions { RateLimit = 262144 },
                Mode = QueueMode.Queue,
            });
            await Until(() => vm.Operations.Jobs.Any(row => row.Job == job) &&
                              vm.Operations.Jobs.Any(row => row.Job == other));
            vm.Operations.IsOpen = true;
            vm.Operations.Selected = vm.Operations.Jobs.Single(row => row.Job == job);
            window.UpdateLayout();
            var action = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Limit transfer speed…"));
            Assert.True(action.IsVisible);

            async Task<TextBox> Open()
            {
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => vm.Dialogs.IsOpen);
                window.UpdateLayout();
                return window.GetVisualDescendants().OfType<TextBox>().Single(box =>
                    AutomationProperties.GetName(box) == "Maximum speed in MB/s (0 removes the limit):");
            }

            async Task Confirm()
            {
                window.GetVisualDescendants().OfType<Button>().Single(button =>
                    Equals(button.Content, "OK") && button.IsVisible)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => !vm.Dialogs.IsOpen);
            }

            var box = await Open();
            Assert.Equal("0,0625", box.Text);
            await Confirm();
            Assert.Equal(65536, job.RateLimit);
            Assert.Equal(262144, other.RateLimit);

            job.RateLimit = 1;
            box = await Open();
            Assert.Equal("0,00000095367431640625", box.Text);
            await Confirm();
            Assert.Equal(1, job.RateLimit);

            job.RateLimit = long.MaxValue - 1;
            box = await Open();
            await Confirm();
            Assert.Equal(long.MaxValue - 1, job.RateLimit);

            box = await Open();
            foreach (var invalid in new[] { "Infinity", "-1", "0,0000001", "100000000000000000000" })
            {
                box.Text = invalid;
                await Until(() => AutomationProperties.GetHelpText(box)?.StartsWith("Speed limit:", StringComparison.Ordinal) == true);
                window.UpdateLayout();
                var error = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.IsVisible && text.Text?.StartsWith("Speed limit:", StringComparison.Ordinal) == true);
                Assert.Equal(error.Text, AutomationProperties.GetHelpText(box));
                Assert.False(window.GetVisualDescendants().OfType<Button>().Single(button =>
                    Equals(button.Content, "OK") && button.IsVisible).IsEnabled);
                Assert.Equal(long.MaxValue - 1, job.RateLimit);
            }
            box.Text = "0.125"; // invariant decimal remains usable under Czech culture
            await Confirm();
            Assert.Equal(131072, job.RateLimit);
            Assert.Equal(262144, other.RateLimit);

            box = await Open();
            box.Text = "0";
            await Confirm();
            Assert.Equal(0, job.RateLimit);

            job.RateLimit = 131072;
            box = await Open();
            box.Text = "0,5";
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await Until(() => !vm.Dialogs.IsOpen);
            Assert.Equal(131072, job.RateLimit);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
            CultureInfo.CurrentCulture = priorCulture;
        }
    }

    [AvaloniaFact]
    public async Task Speed_action_is_absent_for_jobs_with_no_paced_byte_work()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Jobs.MaxConcurrent = 0;
            string source = Path.Combine(root, "files", "a.txt");
            string target = Directory.CreateDirectory(Path.Combine(root, "rate-target")).FullName;
            Job Submit(JobKind kind) => services.Jobs.Submit(new JobRequest
            {
                Kind = kind,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(target),
                Mode = QueueMode.Queue,
            });
            var copy = Submit(JobKind.Copy);
            var delete = Submit(JobKind.Delete);
            var check = Submit(JobKind.ArchiveTest);
            await Until(() => vm.Operations.Jobs.Count(row => row.Job == copy || row.Job == delete || row.Job == check) == 3);
            vm.Operations.IsOpen = true;
            window.UpdateLayout();
            var action = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Limit transfer speed…"));
            foreach (var (job, visible) in new[] { (copy, true), (delete, false), (check, false) })
            {
                vm.Operations.Selected = vm.Operations.Jobs.Single(row => row.Job == job);
                window.UpdateLayout();
                Assert.Equal(visible, action.IsVisible);
            }
            Assert.False(JobExecutors.MayThrottle(new JobRequest
            {
                Kind = JobKind.Move,
                Sources = [new ItemRef(new Location(Schemes.Sftp, "/source", session: "server"), "a.txt", EntryKind.File)],
                Destination = new Location(Schemes.Sftp, "/target", session: "server"),
            }));
            Assert.False(JobExecutors.MayThrottle(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = new Location(Schemes.Mtp, "device"),
            }));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Until(Func<bool> ready)
    {
        for (int i = 0; i < 250 && !ready(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready(), "The Operations control did not settle.");
    }
}
