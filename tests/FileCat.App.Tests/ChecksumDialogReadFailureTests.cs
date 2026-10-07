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

public sealed class ChecksumDialogReadFailureTests
{
    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Initial_checksum_file_errors_are_reported_and_leave_no_busy_indicator(bool missing, bool multiple)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        Task? dialog = null;
        try
        {
            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            await ClipboardExtensions.SetTextAsync(clipboard, "");
            byte[] bytes = Enumerable.Range(0, 65536).Select(i => (byte)((i * 17 + 23) % 251)).ToArray();
            string first = Path.Join(root, "owned-one.bin"), second = Path.Join(root, "owned-two.bin");
            File.WriteAllBytes(first, bytes);
            File.WriteAllBytes(second, bytes);
            string[] files = multiple ? [first, second] : [first];
            if (missing) File.Delete(multiple ? second : first);
            dialog = OperationDialogs.ShowChecksumsAsync(vm, files);
            await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(c => AutomationProperties.GetName(c) == "Checksums"), TimeSpan.FromSeconds(5));
            var output = window.GetVisualDescendants().OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == "Checksums");
            var body = Assert.IsType<StackPanel>(output.Parent);
            var progress = Assert.Single(body.Children.OfType<ProgressBar>());
            var save = Assert.Single(body.Children.OfType<Button>());
            var clock = Stopwatch.StartNew();
            while (progress.IsVisible && clock.Elapsed < TimeSpan.FromSeconds(3))
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            string text = output.Text ?? "";
            bool unchanged = files.Where(File.Exists).All(p => File.ReadAllBytes(p).SequenceEqual(bytes));
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { missing, multiple, Output = text, ProgressVisible = progress.IsVisible, SaveEnabled = save.IsEnabled, DialogStillOpen = ((OverlayDialogService)vm.Dialogs).IsOpen, OwnedExistingContentUnchanged = unchanged, ExpectedSHA256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), ActualCompleteDialog = true, HeadlessComponentOnly = true }));
            Assert.True(unchanged);
            Assert.False(progress.IsVisible);
            if (missing) { Assert.Contains("Error:", text); Assert.False(save.IsEnabled); }
            else
            {
                string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                Assert.Equal(multiple ? $"{hash}  owned-one.bin{Environment.NewLine}{hash}  owned-two.bin" : $"{hash}  owned-one.bin", text);
                Assert.True(save.IsEnabled);
            }
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            if (dialog is not null) await dialog.WaitAsync(TimeSpan.FromSeconds(5));
            AccessibilityTests.Close(services, window, root);
            Assert.False(Directory.Exists(root));
        }
    }

    private static async Task Until(Func<bool> condition, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > limit) throw new TimeoutException("The owned checksum dialog did not reach its checkpoint.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
