using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.Core.Edit;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// Remaining-work ledger rows 4 and 8: a native copy that never completed (FileCat ended while it copied) is listed as an
/// interrupted edit, said so at startup, and removed only through its review: FileCat checks its folder again and removes
/// exactly the unfinished copy, or keeps everything when the folder holds anything else.
/// </summary>
public sealed class EditInterruptedCopyUiTests
{
    [AvaloniaTheory]
    [InlineData("remove")]
    [InlineData("cancel")]
    [InlineData("kept")]
    public async Task An_interrupted_copy_is_listed_and_removed_only_after_its_review(string mode)
    {
        var (services, vm, window, root) = EditTestStores.OpenMainWindow(EditStoreKind.Native);
        try
        {
            var ct = TestContext.Current.CancellationToken;
            using var content = new MemoryContentSource("notes.txt", "partly copied bytes"u8.ToArray());
            var made = services.EditSessions.CreateRemote("owned", "owned", "/notes.txt", content, content.GetRevision()!.Value, null, ct);
            string folder = services.EditSessions.SessionDirectory(made);
            // FileCat ended while it copied: a partial working copy, and only its marker says what the folder is.
            File.Delete(Path.Combine(folder, "session.json"));
            File.WriteAllText(made.WorkingPath, "partly");
            if (mode == "kept") File.WriteAllText(Path.Combine(folder, "files", "another.txt"), "another program's file");
            var row = Assert.Single(services.EditSessions.LoadAll());
            Assert.Equal(EditSessionStore.InterruptedCreationReason, row.UnsupportedReason);
            string? startup = await vm.RestoreEditSessionsAsync();
            Assert.Contains("1 edit was left incomplete", startup, StringComparison.Ordinal);

            var task = (Task)typeof(MainViewModel).GetMethod("ShowSessionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [row])!;
            await Wait(() => Button(window, "Remove unfinished copy…") is not null || task.IsCompleted);
            Assert.False(task.IsCompleted);
            window.UpdateLayout(); // The dialog's scrollable body is realized by layout.
            Assert.Contains("interrupted while FileCat was making or removing it",
                string.Join(" ", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)), StringComparison.Ordinal);
            Click(window, "Remove unfinished copy…");
            await Wait(() => Button(window, "Remove") is not null || task.IsCompleted);
            Assert.False(task.IsCompleted);
            Click(window, mode == "cancel" ? "Cancel" : "Remove");
            await task.WaitAsync(TimeSpan.FromSeconds(30), ct);

            if (mode == "remove")
            {
                Assert.Empty(services.EditSessions.LoadAll());
                Assert.False(Directory.Exists(folder));
                Assert.Equal("The unfinished copy was removed.", vm.Notification);
            }
            else
            {
                Assert.Equal(row.Id, Assert.Single(services.EditSessions.LoadAll()).Id);
                Assert.True(File.Exists(Path.Combine(folder, "creation.json")));
                Assert.Equal("partly", File.ReadAllText(made.WorkingPath));
                if (mode == "kept")
                {
                    Assert.Contains("kept", vm.Notification, StringComparison.Ordinal);
                    Assert.True(File.Exists(Path.Combine(folder, "files", "another.txt")));
                }
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static Button? Button(Window window, string text) => window.GetVisualDescendants().OfType<Button>()
        .FirstOrDefault(b => b.Content as string == text && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
    private static void Click(Window window, string text) =>
        Assert.IsType<Button>(Button(window, text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static async Task Wait(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Interrupted copy checkpoint timed out.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
