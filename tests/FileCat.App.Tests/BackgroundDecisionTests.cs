using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Listing;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class BackgroundDecisionTests
{
    [AvaloniaFact]
    public async Task A_background_conflict_cannot_steal_a_prompt_or_its_queued_Enter()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string destination = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            File.WriteAllText(Path.Combine(destination, "a.txt"), "keep existing");
            var dialogs = (OverlayDialogService)vm.Dialogs;
            var prompt = dialogs.PromptAsync(new PromptOptions("User's current input", "Owned draft") { Text = "preserved draft" });
            Dispatcher.UIThread.RunJobs();
            var input = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Text == "preserved draft");
            input.CaretIndex = 5;
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(Path.Combine(root, "files", "a.txt"), EntryKind.File)],
                Destination = Location.FileSystem(destination),
            });
            await Until(() => job.State == JobState.AwaitingDecision);
            Dispatcher.UIThread.RunJobs();
            Assert.True(input.IsFocused);
            Assert.Equal(5, input.CaretIndex);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Replace");
            await vm.ReviewDecisionAsync(job); // even explicit review cannot stack over the unfinished input
            Assert.True(input.IsFocused);
            Assert.False(prompt.IsCompleted);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal("preserved draft", (await prompt)!.Text);
            Assert.Equal(JobState.AwaitingDecision, job.State);
            Assert.False(job.Decision!.Task.IsCompleted);
            Assert.Equal("keep existing", File.ReadAllText(Path.Combine(destination, "a.txt")));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Review decision…" && b.IsVisible);

            var review = vm.ReviewDecisionAsync(job);
            Dispatcher.UIThread.RunJobs();
            var skip = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Skip");
            var replace = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Replace");
            Assert.True(skip.IsDefault);
            Assert.True(skip.IsFocused);
            Assert.False(replace.IsDefault);
            Assert.Contains("danger", replace.Classes);
            await vm.ReviewDecisionAsync(job); // duplicate admission does not create another dialog
            Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Replace");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await review;
            await Until(() => job.State.IsFinished());
            Assert.Equal("keep existing", File.ReadAllText(Path.Combine(destination, "a.txt")));
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(root, "files", "a.txt")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("Moving would lose file metadata", "Move anyway", "metadata-loss")]
    [InlineData("Read-only item", "Delete anyway", "readonly")]
    [InlineData("Encrypted file", "Copy decrypted", "decrypt")]
    public async Task Consequential_confirmations_choose_safe_initial_action_independently_of_order(string title, string label, string kind)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var job = NewJob();
            var request = new ConfirmRequest(title, "Explicitly review the consequence", "owned.txt", kind,
                [DecisionAction.Proceed, DecisionAction.KeepSource, DecisionAction.Skip, DecisionAction.CancelJob]) { ProceedLabel = label };
            var decision = OperationDialogs.ShowDecisionAsync(vm, new PendingDecision(job, request));
            Dispatcher.UIThread.RunJobs();
            var proceed = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == label);
            var skip = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Skip");
            Assert.Contains("danger", proceed.Classes);
            Assert.False(proceed.IsDefault);
            Assert.True(skip.IsDefault);
            Assert.True(skip.IsFocused);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(DecisionAction.Skip, (await decision).Action);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Canceling_an_operation_retires_its_open_decision()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var job = NewJob();
            var decision = OperationDialogs.ShowDecisionAsync(vm, new PendingDecision(job,
                new ConfirmRequest("Owned decision", "Owned question", "file", "readonly", [DecisionAction.Proceed, DecisionAction.Skip, DecisionAction.CancelJob])));
            job.Cancel();
            await Until(() => decision.IsCompleted);
            Assert.Equal(DecisionAction.CancelJob, (await decision).Action);
            Assert.False(vm.Dialogs.IsOpen);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static Job NewJob() => (Job)Activator.CreateInstance(typeof(Job), BindingFlags.Instance | BindingFlags.NonPublic,
        null, [new JobRequest { Kind = JobKind.Copy }, "Owned decision", "owned", Array.Empty<string>(), Array.Empty<string>()], null)!;

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), "Expected asynchronous state did not arrive.");
    }
}
