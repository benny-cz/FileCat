using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class ConflictComparisonLifetimeTests
{
    [AvaloniaTheory]
    [InlineData("cancel", "equal")]
    [InlineData("skip", "equal")]
    [InlineData("replace", "equal")]
    [InlineData("complete", "equal")]
    [InlineData("complete", "different")]
    [InlineData("complete", "size")]
    [InlineData("complete", "missing")]
    public async Task Conflict_comparison_completion_belongs_to_its_live_dialog(string action, string content)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        Task<Decision>? decision = null;
        CompletionContext? completion = null;
        try
        {
            byte[] incoming = Enumerable.Range(0, 4 * 1024 * 1024).Select(i => (byte)((i * 17 + 23) % 251)).ToArray();
            byte[] existing = incoming.ToArray();
            if (content == "different") existing[existing.Length / 2] ^= 1;
            if (content == "size") existing = existing[..^1];
            string a = Path.Join(root, "owned-incoming.bin"), b = Path.Join(root, "owned-existing.bin");
            File.WriteAllBytes(a, incoming);
            File.WriteAllBytes(b, existing);
            var fs = new PortableFileOperations();
            var ai = Assert.IsType<FileSystemItemInfo>(fs.TryGetInfo(a));
            var bi = Assert.IsType<FileSystemItemInfo>(fs.TryGetInfo(b));
            if (content == "missing") File.Delete(b);
            var job = (Job)Activator.CreateInstance(typeof(Job), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [new JobRequest { Kind = JobKind.Copy }, "Owned comparison", "owned",
                    new[] { a }, new[] { b }], null)!;
            var conflict = new ConflictRequest("Owned conflict", "owned-existing.bin", ai, bi, a, b,
                true, false, false, false, null, null);
            decision = OperationDialogs.ShowDecisionAsync(vm, new PendingDecision(job, conflict));
            var compare = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Compare content");
            var body = Assert.IsType<StackPanel>(compare.Parent!.Parent);
            var result = body.Children.OfType<TextBlock>().Last();
            var context = Assert.IsAssignableFrom<SynchronizationContext>(SynchronizationContext.Current);
            completion = new CompletionContext(context);
            SynchronizationContext.SetSynchronizationContext(completion);
            try { compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
            Assert.Equal(1, completion.Started);
            Assert.Equal("Comparing…", result.Text);
            Assert.False(compare.IsEnabled);
            // Close in this UI turn, before the asynchronous click continuation can update controls.
            if (action == "cancel") ((OverlayDialogService)vm.Dialogs).CancelAll();
            else if (action != "complete")
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == (action == "skip" ? "Skip" : "Replace"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (action != "complete") await decision.WaitAsync(TimeSpan.FromSeconds(5));
            await completion.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            bool unchanged = File.ReadAllBytes(a).SequenceEqual(incoming) &&
                (content == "missing" ? !File.Exists(b) : File.ReadAllBytes(b).SequenceEqual(existing));
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new
            {
                action, content, Result = result.Text, CompareEnabled = compare.IsEnabled,
                DialogOpen = ((OverlayDialogService)vm.Dialogs).IsOpen,
                Decision = decision.IsCompletedSuccessfully ? decision.Result.Action.ToString() : null,
                AsyncClickStarted = completion.Started, AsyncClickCompleted = completion.Completed,
                OwnedContentUnchanged = unchanged,
                IncomingSHA256 = Convert.ToHexStringLower(SHA256.HashData(incoming)),
                ExistingSHA256 = Convert.ToHexStringLower(SHA256.HashData(existing)),
                IncomingBytes = incoming.Length, ExistingBytes = existing.Length,
                ActualCompleteDialogAndOwnedFileHashes = true, HeadlessComponentOnly = true
            }));
            Assert.True(unchanged);
            Assert.Equal(1, completion.Completed);
            if (action != "complete")
            {
                Assert.False(((OverlayDialogService)vm.Dialogs).IsOpen);
                Assert.Equal("Comparing…", result.Text);
                Assert.False(compare.IsEnabled);
                Assert.Equal(action switch { "skip" => DecisionAction.Skip, "replace" => DecisionAction.Replace, _ => DecisionAction.CancelJob },
                    decision.Result.Action);
            }
            else
            {
                Assert.True(((OverlayDialogService)vm.Dialogs).IsOpen);
                Assert.True(compare.IsEnabled);
                Assert.Equal(content switch
                {
                    "equal" => "Identical content (SHA-256 of both files matches).",
                    "different" => "Different content (same size, different SHA-256).",
                    "size" => $"Different: sizes differ ({FileCat.App.Services.Formatters.ExactSize(incoming.Length)} vs {FileCat.App.Services.Formatters.ExactSize(existing.Length)}).",
                    _ => result.Text
                }, result.Text);
                if (content == "missing") Assert.StartsWith("Could not compare:", result.Text);
            }
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            if (decision is not null) await decision.WaitAsync(TimeSpan.FromSeconds(5));
            if (completion is not null && completion.Started > 0) await completion.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AccessibilityTests.Close(services, window, root);
            Assert.False(Directory.Exists(root));
        }
    }

    // Observe the actual async-void button handler without replacing its worker, hashing or dialog.
    private sealed class CompletionContext(SynchronizationContext inner) : SynchronizationContext
    {
        public readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started, Completed;
        public override void OperationStarted() => Interlocked.Increment(ref Started);
        public override void OperationCompleted()
        {
            Interlocked.Increment(ref Completed);
            Finished.TrySetResult();
        }
        public override void Post(SendOrPostCallback d, object? state) => inner.Post(d, state);
        public override void Send(SendOrPostCallback d, object? state) => inner.Send(d, state);
        public override SynchronizationContext CreateCopy() => this;
    }
}

