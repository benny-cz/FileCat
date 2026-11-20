using System.Reflection;
using Avalonia.Automation;
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
        // Replace is offered only where it binds to the reviewed destination: through Windows' native adapter.
        bool replaceable = OperatingSystem.IsWindows();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(nativeWindowsOriginMarks: replaceable);
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
            var replace = window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Content as string == "Replace");
            Assert.True(skip.IsDefault);
            Assert.True(skip.IsFocused);
            if (replaceable)
            {
                Assert.NotNull(replace);
                Assert.False(replace.IsDefault);
                Assert.Contains("danger", replace.Classes);
            }
            else Assert.Null(replace);
            await vm.ReviewDecisionAsync(job); // duplicate admission does not create another dialog
            Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Skip");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await review;
            await Until(() => job.State.IsFinished());
            Assert.Equal("keep existing", File.ReadAllText(Path.Combine(destination, "a.txt")));
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(root, "files", "a.txt")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    /// <summary>
    /// The decision notice sends keyboard users to Operations with Ctrl+J (NVDA and Orca journeys could not reach "Review
    /// decision…": Ctrl+J only showed the drawer and Tab switched panels). Ctrl+J now takes the keyboard to the waiting
    /// decision; inside Operations Tab stays there, panel keys do not act on the panel's file, Esc returns to the panel, and
    /// Ctrl+J from inside closes Operations.
    /// </summary>
    [AvaloniaFact]
    public async Task Ctrl_J_takes_the_keyboard_to_a_waiting_decision_and_back()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string destination = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            File.WriteAllText(Path.Combine(destination, "a.txt"), "keep existing");
            string source = Path.Combine(root, "files", "a.txt");
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(destination),
            });
            await Until(() => job.State == JobState.AwaitingDecision);
            Dispatcher.UIThread.RunJobs();
            var pane = window.GetVisualDescendants().OfType<OperationsView>().Single();
            object? Focused() => window.FocusManager!.GetFocusedElement();
            bool InPane() => Focused() is Avalonia.Visual visual && pane.IsVisualAncestorOf(visual);
            async Task Settle() { for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); await Task.Delay(10, TestContext.Current.CancellationToken); } }
            void Press(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
            { window.KeyPress(key, modifiers, physical, null); window.KeyRelease(key, modifiers, physical, null); }

            Press(Key.J, PhysicalKey.J, RawInputModifiers.Control); await Settle();
            Assert.True(vm.Operations.IsOpen);
            Assert.Equal("Review decision…", Assert.IsType<Button>(Focused()).Content);
            Assert.True(InPane());

            // Panel keys stay out of Operations: Delete here does not ask to delete the panel's file.
            Press(Key.Delete, PhysicalKey.Delete); await Settle();
            Assert.False(vm.Dialogs.IsOpen); Assert.True(File.Exists(source));
            Press(Key.Tab, PhysicalKey.Tab); await Settle();
            Assert.True(InPane(), "Tab left Operations: " + Focused());
            Press(Key.Escape, PhysicalKey.Escape); await Settle();
            Assert.IsType<FileCat.App.Controls.FileListControl>(Focused());
            Assert.True(vm.Operations.IsOpen);

            // Back again, Space opens the decision with its safe first choice focused.
            Press(Key.J, PhysicalKey.J, RawInputModifiers.Control); await Settle();
            Assert.Equal("Review decision…", Assert.IsType<Button>(Focused()).Content);
            Press(Key.Space, PhysicalKey.Space); await Settle();
            var skip = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Skip");
            Assert.True(skip.IsFocused);
            Press(Key.Escape, PhysicalKey.Escape);
            await Until(() => job.State.IsFinished());
            Assert.Equal("keep existing", File.ReadAllText(Path.Combine(destination, "a.txt")));
            await Settle();

            // Ctrl+J from inside Operations closes it and gives the keyboard back to the panel.
            Press(Key.J, PhysicalKey.J, RawInputModifiers.Control); await Settle();
            Assert.True(InPane());
            Press(Key.J, PhysicalKey.J, RawInputModifiers.Control); await Settle();
            Assert.False(vm.Operations.IsOpen);
            Assert.IsType<FileCat.App.Controls.FileListControl>(Focused());
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

    /// <summary>
    /// Plan §11.8: an archive review's counts and its collision choice reach a screen reader from the choice focused
    /// first (it reads that button, not the message); a whole-operation review offers no "do the same" option.
    /// </summary>
    [AvaloniaFact]
    public async Task An_archive_review_speaks_its_counts_from_the_focused_choice_and_offers_no_apply_to_all()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var job = NewJob();
            string message = "• Replace 1 existing entry under 1 name.\n• 2 links, special files or unrepresentable names will be left out of the archive. " +
                "Their originals stay where they are.\n\"docs/a.txt\"\n\"docs/link\"\n… additional names omitted from this preview.";
            var request = new ConfirmRequest("Review this archive change", message, "owned.tar", "archive-review-1", [DecisionAction.CancelJob, DecisionAction.Proceed])
            { InitialAction = DecisionAction.CancelJob, ProceedLabel = "Replace 1 existing member and continue", OffersApplyToAll = false };
            var decision = OperationDialogs.ShowDecisionAsync(vm, new PendingDecision(job, request));
            Dispatcher.UIThread.RunJobs();
            var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel operation");
            Assert.True(cancel.IsDefault); Assert.True(cancel.IsFocused);
            Assert.Equal("Replace 1 existing entry under 1 name. 2 links, special files or unrepresentable names will be left out of the archive. " +
                "Their originals stay where they are. Example: \"docs/a.txt\"", AutomationProperties.GetHelpText(cancel));
            var proceed = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Replace 1 existing member and continue");
            Assert.Null(AutomationProperties.GetHelpText(proceed));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CheckBox>(), box => box.IsEffectivelyVisible);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(DecisionAction.CancelJob, (await decision).Action);

            // An item question still offers to answer the operation's similar questions alike.
            var item = OperationDialogs.ShowDecisionAsync(vm, new PendingDecision(NewJob(),
                new ConfirmRequest("Read-only item", "\"owned.txt\" is read-only. Delete it anyway?", "owned.txt", "readonly",
                    [DecisionAction.Proceed, DecisionAction.Skip, DecisionAction.CancelJob])));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(), box => box.IsEffectivelyVisible && box.Content as string == "Do the same for similar items in this operation");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(DecisionAction.CancelJob, (await item).Action);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [Fact]
    public void A_spoken_summary_keeps_sentences_and_the_first_example_within_its_bound()
    {
        Assert.Equal("", OperationDialogs.SpokenSummary(""));
        Assert.Equal("One. Two.", OperationDialogs.SpokenSummary("• One.\r\n\n• Two."));
        Assert.Equal("Example: \"x\"", OperationDialogs.SpokenSummary("\"x\"\n\"y\""));
        string longText = new string('a', 598) + "😀" + new string('b', 50);
        string bounded = OperationDialogs.SpokenSummary(longText);
        Assert.True(bounded.Length <= 600); Assert.EndsWith("…", bounded); Assert.False(char.IsHighSurrogate(bounded[^2]));
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
