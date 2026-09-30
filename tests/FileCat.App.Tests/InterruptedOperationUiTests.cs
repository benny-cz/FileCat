using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// Release issue I19 (release plan V03-PARTIAL): Run again after an interrupted copy names every file it deletes first,
/// deletes only copies the interruption provably cut short, completes them, and leaves a file the user changed since as
/// it is (the heuristic it replaced deleted such a file permanently).
/// </summary>
public sealed class InterruptedOperationUiTests
{
    [AvaloniaFact]
    public async Task Run_again_names_what_it_deletes_completes_cut_short_copies_and_keeps_changed_files()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var src = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            var dst = Directory.CreateDirectory(Path.Combine(root, "dst")).FullName;
            File.WriteAllText(Path.Combine(src, "cut.txt"), "the whole of cut.txt");
            File.WriteAllText(Path.Combine(src, "edited.txt"), "as copied");
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(Path.Combine(src, "cut.txt"), EntryKind.File), ItemRef.ForFileSystemPath(Path.Combine(src, "edited.txt"), EntryKind.File)],
                Destination = Location.FileSystem(dst),
            });
            for (int i = 0; i < 500 && !job.State.IsFinished(); i++) await Task.Delay(20, ct);
            Assert.Equal(JobState.Completed, job.State);

            // The crash: the journal loses its end record; one copy is cut short, and the user edits the other afterwards.
            var journal = Assert.Single(Directory.GetFiles(services.Paths.JournalDirectory, "job-*.fcj"));
            File.WriteAllLines(journal, File.ReadAllLines(journal).Where(l => !l.Contains("\"t\":\"end\"", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(dst, "cut.txt"), "the whole");
            File.WriteAllText(Path.Combine(dst, "edited.txt"), "the user's own edit");
            var interrupted = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory));

            var dialogs = (OverlayDialogService)vm.Dialogs;
            var run = vm.RunInterruptedAgainAsync(interrupted);
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen, "Run again asks first");
            var lines = window.GetVisualDescendants().OfType<TextBlock>().Select(b => b.Text ?? string.Empty).ToList();
            int deleting = lines.FindIndex(l => l.Contains("will be deleted", StringComparison.Ordinal));
            int leaving = lines.FindIndex(l => l.Contains("leaves them as they are", StringComparison.Ordinal));
            Assert.True(deleting >= 0 && leaving > deleting, string.Join(" | ", lines));
            // The file to delete is named under "will be deleted"; the edited one only under "leaves them as they are".
            Assert.Equal("• " + Path.Combine(dst, "cut.txt"), lines[deleting + 1]);
            Assert.Equal("• " + Path.Combine(dst, "edited.txt"), lines[leaving + 1]);
            Assert.DoesNotContain("• " + Path.Combine(dst, "edited.txt"), lines.Take(leaving));

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(await run);
            for (int i = 0; i < 500 && !(services.Jobs.Jobs.Count == 2 && services.Jobs.Jobs.All(j => j.State.IsFinished())); i++) await Task.Delay(20, ct);
            Assert.True(services.Jobs.Jobs.All(j => j.State.IsFinished()));

            Assert.Equal("the whole of cut.txt", File.ReadAllText(Path.Combine(dst, "cut.txt")));
            Assert.Equal("the user's own edit", File.ReadAllText(Path.Combine(dst, "edited.txt")));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
