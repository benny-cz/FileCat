using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class ChecksumDialogGenerationTests
{
    [AvaloniaTheory]
    [InlineData("replace", 1)]
    [InlineData("replace", 4)]
    [InlineData("close", 1)]
    [InlineData("close", 4)]
    [InlineData("complete", 1)]
    [InlineData("complete", 4)]
    public async Task Checksum_file_error_completion_belongs_to_its_current_dialog(string action, int algorithmIndex)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        Task? dialog = null;
        HoldingContext? held = null;
        FileStream? exclusive = null;
        try
        {
            await ClipboardExtensions.SetTextAsync(Assert.IsAssignableFrom<IClipboard>(window.Clipboard), "");
            byte[] bytes = Enumerable.Range(0, 65536).Select(i => (byte)((i * 17 + 23) % 251)).ToArray();
            string path = Path.Join(root, "owned-generation.bin");
            File.WriteAllBytes(path, bytes);
            dialog = OperationDialogs.ShowChecksumsAsync(vm, [path]);
            await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(c => AutomationProperties.GetName(c) == "Checksums"), TimeSpan.FromSeconds(5));
            var output = window.GetVisualDescendants().OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == "Checksums");
            var body = Assert.IsType<StackPanel>(output.Parent);
            var progress = Assert.Single(body.Children.OfType<ProgressBar>());
            var save = Assert.Single(body.Children.OfType<Button>());
            var algorithm = Assert.Single(body.Children.OfType<ComboBox>());
            await Until(() => !progress.IsVisible && save.IsEnabled, TimeSpan.FromSeconds(5));
            string sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.Equal($"{sha256}  owned-generation.bin", output.Text);
            if (action != "complete") exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var context = Assert.IsAssignableFrom<SynchronizationContext>(SynchronizationContext.Current);
            held = new HoldingContext(context);
            SynchronizationContext.SetSynchronizationContext(held);
            try { algorithm.SelectedIndex = algorithmIndex; }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
            Assert.Equal(1, held.Started);
            // Hold only the actual UI continuation; the real Task.Run, file open and hashing execute.
            Assert.True(SpinWait.SpinUntil(() => held.Pending > 0, TimeSpan.FromSeconds(5)), "The actual checksum worker did not post its continuation.");
            exclusive?.Dispose(); exclusive = null;
            if (action == "replace")
            {
                algorithm.SelectedIndex = 0;
                await Until(() => !progress.IsVisible && save.IsEnabled, TimeSpan.FromSeconds(5));
            }
            else if (action == "close")
            {
                ((OverlayDialogService)vm.Dialogs).CancelAll();
                await dialog.WaitAsync(TimeSpan.FromSeconds(5));
            }
            string beforeRelease = output.Text ?? "";
            await held.DrainAsync();
            Dispatcher.UIThread.RunJobs();
            string afterRelease = output.Text ?? "";
            bool unchanged = File.ReadAllBytes(path).SequenceEqual(bytes);
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new
            {
                action, algorithmIndex, OutputBeforeOldContinuation = beforeRelease, OutputAfterOldContinuation = afterRelease,
                ProgressVisible = progress.IsVisible, SaveEnabled = save.IsEnabled, DialogOpen = ((OverlayDialogService)vm.Dialogs).IsOpen,
                AsyncClickStarted = held.Started, AsyncClickCompleted = held.Completed, HeldWorkerContinuationsReleased = held.Released,
                OwnedContentUnchanged = unchanged, OwnedBytes = bytes.Length,
                ExpectedSHA256 = sha256, ExpectedSelectedHash = Convert.ToHexStringLower(algorithmIndex == 1 ? SHA512.HashData(bytes) : MD5.HashData(bytes)),
                ActualCompleteDialogAndFileShareNoneWorkerError = action != "complete",
                OnlyActualUIContinuationHeldRealWorkerNotReplaced = true, HeadlessComponentOnly = true
            }));
            Assert.True(unchanged);
            Assert.Equal(1, held.Completed);
            if (action != "complete")
            {
                Assert.Equal(beforeRelease, afterRelease);
                Assert.DoesNotContain("Error:", afterRelease);
                if (action == "replace")
                {
                    Assert.Equal($"{sha256}  owned-generation.bin", afterRelease);
                    Assert.True(save.IsEnabled);
                    Assert.False(progress.IsVisible);
                    Assert.True(((OverlayDialogService)vm.Dialogs).IsOpen);
                }
                else Assert.False(((OverlayDialogService)vm.Dialogs).IsOpen);
            }
            else
            {
                string hash = Convert.ToHexStringLower(algorithmIndex == 1 ? SHA512.HashData(bytes) : MD5.HashData(bytes));
                Assert.Equal($"{hash}  owned-generation.bin", afterRelease);
                Assert.True(save.IsEnabled);
                Assert.False(progress.IsVisible);
            }
        }
        finally
        {
            exclusive?.Dispose();
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            if (dialog is not null) await dialog.WaitAsync(TimeSpan.FromSeconds(5));
            if (held is not null && held.Started > held.Completed) await held.DrainAsync();
            AccessibilityTests.Close(services, window, root);
            Assert.False(Directory.Exists(root));
        }
    }

    private static async Task Until(Func<bool> condition, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > limit) throw new TimeoutException("The actual checksum dialog did not reach its checkpoint.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    // Capture the actual async-void selection handler without replacing its work or file operations.
    private sealed class HoldingContext(SynchronizationContext inner) : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> queue = new();
        public int Started, Completed, Released;
        public int Pending => queue.Count;
        public override void OperationStarted() => Interlocked.Increment(ref Started);
        public override void OperationCompleted() => Interlocked.Increment(ref Completed);
        public override void Post(SendOrPostCallback d, object? state) => queue.Enqueue((d, state));
        public override void Send(SendOrPostCallback d, object? state) => inner.Send(d, state);
        public override SynchronizationContext CreateCopy() => this;
        public async Task DrainAsync()
        {
            var clock = Stopwatch.StartNew();
            while (Completed < Started || Pending > 0)
            {
                while (queue.TryDequeue(out var item)) { Interlocked.Increment(ref Released); inner.Post(item.Callback, item.State); }
                Dispatcher.UIThread.RunJobs();
                if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("The actual held checksum selection continuation did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
    }
}

