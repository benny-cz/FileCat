using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.App.ViewModels;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.App.Tests;

public sealed class BulkRenameDialogTests
{
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenUnix(string path, int flags);

    [Fact]
    public async Task Editor_round_trip_refuses_a_Unix_fifo_without_waiting_for_a_writer()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix regular-file admission only."); return; }
        string fifo = Path.Combine(Path.GetTempPath(), $"filecat-edited-fifo-{Guid.NewGuid():N}");
        Assert.Equal(0, MkFifo(fifo, 0x180)); // owner-only 0600
        try
        {
            var ct = TestContext.Current.CancellationToken;
            Task read = Task.Run(() => MainViewModel.ReadEditedNames(fifo, 1), ct);
            bool promptly = await Task.WhenAny(read, Task.Delay(1200, ct)) == read;
            if (!promptly)
            {
                // Release the previous blocking-open behavior so the failed control remains finite.
                int flags = OperatingSystem.IsMacOS() ? 1 | 4 : 1 | 2048; // O_WRONLY | O_NONBLOCK
                int fd = -1;
                for (int i = 0; i < 50 && fd < 0; i++)
                {
                    fd = OpenUnix(fifo, flags);
                    if (fd < 0) await Task.Delay(20, ct);
                }
                Assert.True(fd >= 0, "The FIFO reader was unavailable for release.");
                using var writer = new FileStream(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.Write);
                writer.Write("one\n"u8);
            }
            if (promptly) await Assert.ThrowsAnyAsync<IOException>(() => read);
            else await read.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.True(promptly, "An edited-name FIFO waited for a writer during file admission.");
        }
        finally { File.Delete(fifo); }
    }

    [Theory]
    [InlineData("first\r\nsecond\r\n\r\n", 2, 2, "first", "second")]
    [InlineData("first\r\n\r\n", 2, 2, "first", "")]
    [InlineData("first\r\nextra\r\n\r\n", 1, 2, "first", null)]
    public void Editor_round_trip_preserves_name_positions_and_ignores_only_surplus_blank_lines(
        string content, int expected, int lineCount, string first, string? second)
    {
        string file = Path.Combine(Path.GetTempPath(), $"filecat-edited-names-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(file, content);
            var result = MainViewModel.ReadEditedNames(file, expected);
            Assert.Equal(lineCount, result.count);
            Assert.Equal(first, result.lines[0]);
            if (second is not null) Assert.Equal(second, result.lines[1]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Editor_round_trip_rejects_an_oversized_replacement_without_loading_it()
    {
        string file = Path.Combine(Path.GetTempPath(), $"filecat-edited-names-{Guid.NewGuid():N}.txt");
        try
        {
            using (var stream = File.Create(file)) stream.SetLength(8193); // one expected name: 8,192-byte cap
            Assert.Throws<InvalidDataException>(() => MainViewModel.ReadEditedNames(file, 1));
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public async Task Ctrl_M_previews_disables_rename_on_collisions_and_renames_the_marked_files()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            vm.Execute(CommandIds.MarkAll);
            vm.Execute(CommandIds.BulkRename);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            window.UpdateLayout(); // Realize the modal's scrollable body before querying visual controls.
            TextBox Box(string name) => window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == name);
            Button Confirm() => window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rename");
            var preview = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Preview of new names");

            // Both files getting one name is a collision: Rename is disabled until the rules make the names unique.
            Box("Name mask").Text = "same";
            for (int i = 0; i < 50 && Confirm().IsEnabled; i++) await Task.Delay(20, ct);
            Assert.False(Confirm().IsEnabled);
            Box("Name mask").Text = "doc_[C]";
            for (int i = 0; i < 50 && !Confirm().IsEnabled; i++) await Task.Delay(20, ct);
            Assert.True(Confirm().IsEnabled);
            Assert.Contains(preview.ItemsSource!.Cast<BulkRenameReview.Row>(), r => r.Plan.OldName == "a.txt" && r.Plan.NewName == "doc_1.txt");

            Confirm().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            for (int i = 0; i < 250 && services.Jobs.Jobs.Count == 0; i++) await Task.Delay(20, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            for (int i = 0; i < 250 && !job.State.IsFinished(); i++) await Task.Delay(20, ct);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(["doc_1.txt", "doc_2.txt"], Directory.GetFiles(Path.Combine(root, "files")).Select(Path.GetFileName).Order());
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
