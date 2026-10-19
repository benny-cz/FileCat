using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class InterruptedTransferIntentUiTests
{
    private static string ReviewText(TextBlock block) => block.Inlines is { Count: > 0 } inlines
        ? string.Concat(inlines.OfType<Run>().Select(run => run.Text))
        : block.Text ?? string.Empty;

    [AvaloniaFact]
    public async Task New_copy_review_shows_consequences_first_and_discloses_complete_mapping_on_keyboard_request()
    {
        using var f = await Fixture.Open();
        string source = Path.Combine(f.Source, "draft.txt");
        File.WriteAllText(source, "original content");
        var interrupted = Fixture.MakeLegacy(await f.Interrupt(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(f.Destination),
        }));
        var run = f.Vm.ReviewNewInterruptedTransferAsync(interrupted);
        await f.WaitButton("Copy");
        await Fixture.Wait(() => f.Button("Copy")?.IsEnabled == true);
        f.Click("Copy");
        await f.WaitButton("Start new copy");
        Dispatcher.UIThread.RunJobs();
        var summary = Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("reviewHighlights"));
        var keyPoints = string.Join("\n", summary.GetVisualDescendants()
            .OfType<SelectableTextBlock>().Select(ReviewText));
        Assert.Contains("Action: Start a new copy", keyPoints);
        var primaryAction = Assert.Single(summary.GetVisualDescendants().OfType<SelectableTextBlock>(),
            block => block.Classes.Contains("reviewPrimaryAction"));
        Assert.StartsWith("Action: Start a new copy", ReviewText(primaryAction));
        Assert.True(primaryAction.Bounds.Top < summary.GetVisualDescendants()
            .OfType<SelectableTextBlock>().First(block => ReviewText(block).StartsWith("Destination:")).Bounds.Top);
        var destinationLine = Assert.Single(summary.GetVisualDescendants().OfType<SelectableTextBlock>(),
            block => ReviewText(block).StartsWith("Destination:"));
        var destinationRuns = Assert.IsType<InlineCollection>(destinationLine.Inlines).OfType<Run>().ToArray();
        Assert.Contains(destinationRuns, run => run.Text == "Destination:" &&
            run.FontWeight == FontWeight.SemiBold);
        Assert.Contains(destinationRuns, run => run.Text?.Contains(Path.GetDirectoryName(f.Destination)!, StringComparison.Ordinal) == true &&
            run.FontWeight == FontWeight.SemiBold && run.Foreground is not null);
        Assert.Contains(destinationRuns, run => run.Text?.Contains(Path.GetFileName(f.Destination)) == true &&
            run.FontWeight == FontWeight.Bold && run.Foreground is not null);
        Assert.Contains(summary.GetVisualDescendants().OfType<SelectableTextBlock>(),
            block => block.Classes.Contains("reviewDecision") && ReviewText(block).StartsWith("Existing items:"));
        var choices = Assert.Single(summary.GetVisualDescendants().OfType<SelectableTextBlock>(),
            block => ReviewText(block).StartsWith("Existing items:"));
        var choiceRuns = Assert.IsType<InlineCollection>(choices.Inlines).OfType<Run>().ToArray();
        Assert.Contains(choiceRuns, run => run.Text == "Existing items:" &&
            run.FontWeight == FontWeight.SemiBold);
        Assert.Contains(choiceRuns, run => run.Text == "Verification:" &&
            run.FontWeight == FontWeight.SemiBold);
        var nameMapping = Assert.Single(summary.GetVisualDescendants().OfType<SelectableTextBlock>(),
            block => ReviewText(block).StartsWith("Source → target name:"));
        var mappingRuns = Assert.IsType<InlineCollection>(nameMapping.Inlines).OfType<Run>().ToArray();
        Assert.Contains(mappingRuns, run => run.Text == "draft.txt" &&
            run.FontWeight == FontWeight.Bold);
        Assert.Equal(2, mappingRuns.Count(run => run.Text == "draft.txt" &&
            run.FontWeight == FontWeight.Bold));
        var effects = Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("reviewWarnings"));
        var warnings = string.Join("\n", effects.GetVisualDescendants()
            .OfType<SelectableTextBlock>().Select(ReviewText));
        Assert.Contains("Original mapping and options are incomplete", warnings);
        Assert.Contains("Skipped existing files are kept, and their contents are not verified", warnings);
        Assert.DoesNotContain("Original mapping and options are incomplete", keyPoints);
        Assert.True(summary.Bounds.Top < effects.Bounds.Top);
        var toggle = Assert.Single(f.Window.GetVisualDescendants().OfType<ToggleButton>(),
            button => button.Name == "ReviewDetailsToggle");
        Assert.False(toggle.IsChecked);
        Assert.Equal("Show full review and source mapping", toggle.Content);
        Assert.True(effects.Bounds.Bottom <= toggle.Bounds.Top);
        var mapping = Assert.Single(f.Window.GetVisualDescendants().OfType<SelectableTextBlock>(),
            block => ReviewText(block).StartsWith("Source → destination mapping for every available root:", StringComparison.Ordinal));
        Assert.False(mapping.IsEffectivelyVisible);
        Assert.True(toggle.Focus());
        f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(run.IsCompleted);
        Assert.True(toggle.IsChecked);
        Assert.True(mapping.IsEffectivelyVisible);
        Assert.Contains(mapping.GetVisualAncestors().OfType<Border>(),
            border => border.Classes.Contains("reviewDetailSection"));
        Assert.Contains(f.Window.GetVisualDescendants().OfType<SelectableTextBlock>(),
            block => block.Classes.Contains("reviewDetailHeading") &&
                     ReviewText(block) == "Source → destination mapping for every available root:");
        var mappedPaths = Assert.Single(f.Window.GetVisualDescendants().OfType<SelectableTextBlock>(),
            block => ReviewText(block).StartsWith("• ") &&
                     ReviewText(block).Contains("draft.txt → ", StringComparison.Ordinal));
        Assert.True(Assert.IsType<InlineCollection>(mappedPaths.Inlines).OfType<Run>()
            .Count(run => run.Text == "draft.txt" && run.FontWeight == FontWeight.Bold) >= 2);
        Assert.True(f.Window.GetVisualDescendants().OfType<Border>()
            .Count(border => border.Classes.Contains("reviewDetailSection")) >= 3);
        Assert.Contains("draft.txt", f.Text);
        f.Click("Cancel");
        Assert.False(await run);
        Assert.Equal("original content", File.ReadAllText(source));
    }

    [AvaloniaTheory]
    [InlineData("move", -1)]
    [InlineData("copy-partial", -1)]
    [InlineData("cleanup", -1)]
    [InlineData("new-copy", 2)] // Replace
    [InlineData("new-copy", 3)] // Replace if newer
    [InlineData("new-copy", 5)] // Rename the existing item
    public async Task Consequential_recovery_reviews_initially_cancel_and_Enter_does_not_mutate_or_retire_the_journal(string flow, int policy)
    {
        using var f = await Fixture.Open();
        const string content = "owned complete contents";
        string source = Path.Combine(f.Source, "owned.txt"), target = Path.Combine(f.Destination, "owned.txt");
        File.WriteAllText(source, content);
        var interrupted = await f.Interrupt(new JobRequest { Kind = flow == "move" ? JobKind.Move : JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], Destination = Location.FileSystem(f.Destination) });
        if (flow == "move") File.WriteAllText(source, content);
        if (flow is "copy-partial" or "cleanup") File.WriteAllText(target, "owned");
        if (flow == "new-copy") interrupted = Fixture.MakeLegacy(interrupted);
        Task<bool> run = flow switch
        {
            "cleanup" => f.Vm.CleanupInterruptedAsync(interrupted),
            "new-copy" => f.Vm.ReviewNewInterruptedTransferAsync(interrupted),
            _ => f.Vm.RunInterruptedAgainAsync(interrupted),
        };
        if (flow == "new-copy")
        {
            await f.WaitButton("Copy");
            bool IsConflict(ComboBox c) => c.ItemsSource?.Cast<object>().Any(item => item as string == "Replace") == true;
            await Fixture.Wait(() => f.Window.GetVisualDescendants().OfType<ComboBox>().Any(IsConflict));
            var conflict = Assert.Single(f.Window.GetVisualDescendants().OfType<ComboBox>(), IsConflict);
            conflict.SelectedIndex = policy;
            await Fixture.Wait(() => f.Button("Copy")?.IsEnabled == true);
            f.Click("Copy");
        }
        string action = flow switch { "move" => "Continue move", "cleanup" => "Delete partial files", "new-copy" => "Start new copy", _ => "Continue copy" };
        await f.WaitButton(action);
        Dispatcher.UIThread.RunJobs();
        var highlights = Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("reviewHighlights"));
        var keyChoices = string.Join("\n", highlights.GetVisualDescendants()
            .OfType<SelectableTextBlock>().Select(ReviewText));
        Assert.Contains("Action: ", keyChoices);
        if (flow == "cleanup")
        {
            Assert.Contains("Reviewed partial files to delete: 1", keyChoices);
            Assert.Contains("Reviewed interrupted renames to finish: 0", keyChoices);
        }
        else
        {
            Assert.Contains("Destination: ", keyChoices);
            Assert.Contains("Files matching: ", keyChoices);
            Assert.Contains("Existing items: ", keyChoices);
            Assert.Contains("Verification: ", keyChoices);
        }
        if (flow == "new-copy") Assert.Contains("Start a new copy", keyChoices);
        if (flow == "new-copy") Assert.Contains("Source → target name: owned.txt → owned.txt", keyChoices);
        if (flow != "cleanup")
        {
            var effects = Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("reviewWarnings"));
            var warnings = string.Join("\n", effects.GetVisualDescendants()
                .OfType<SelectableTextBlock>().Select(ReviewText));
            if (flow == "copy-partial") Assert.Contains("First delete 1 interrupted partial file", warnings);
            if (flow == "move") Assert.Contains("Successfully moved source items are removed", warnings);
            if (policy == 2) Assert.Contains("Existing destination files may be replaced", warnings);
            if (policy == 3) Assert.Contains("replaced when the source is newer", warnings);
            if (policy == 5) Assert.Contains("renamed to keep both copies", warnings);
        }
        var cancel = Assert.IsType<Button>(f.Button("Cancel"));
        var proceed = Assert.IsType<Button>(f.Button(action));
        Assert.True(cancel.IsDefault); Assert.True(cancel.IsFocused);
        Assert.False(proceed.IsDefault); Assert.Contains("danger", proceed.Classes);
        f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.False(await run);
        Assert.Equal(content, File.ReadAllText(source));
        Assert.Equal(flow is "copy-partial" or "cleanup" ? "owned" : content, File.ReadAllText(target));
        Assert.Single(f.Services.Jobs.Jobs);
        Assert.True(JournalRecovery.IsInterrupted(interrupted));
    }

    [AvaloniaFact]
    public async Task Filtered_move_preserves_scope_verification_and_excluded_source_and_destination_files()
    {
        using var f = await Fixture.Open();
        string sourceFolder = Directory.CreateDirectory(Path.Combine(f.Source, "photos")).FullName;
        string image = Path.Combine(sourceFolder, "photo.jpg"), notes = Path.Combine(sourceFolder, "notes.txt");
        File.WriteAllText(image, "photo complete content"); File.WriteAllText(notes, "notes remain at source");
        var interrupted = await f.Interrupt(new JobRequest { Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(sourceFolder, EntryKind.Directory)], Destination = Location.FileSystem(f.Destination),
            Options = new TransferOptions { Filter = Mask.Parse("*.jpg"), Verify = VerifyMode.ReadBack, Conflicts = ConflictPolicy.Replace } });
        // The fixture retains the real writer's intent and fill records; both sides now model the interrupted copy/delete phase.
        File.WriteAllText(image, "photo complete content");
        string arrived = Path.Combine(f.Destination, "photos"), target = Path.Combine(arrived, "photo.jpg"), unrelated = Path.Combine(arrived, "notes.txt");
        File.WriteAllText(target, "photo"); File.WriteAllText(unrelated, "notes");
        f.Services.Settings.DefaultVerify = "Native";
        var run = f.Vm.RunInterruptedAgainAsync(interrupted);
        await f.WaitButton("Continue move");
        var highlighted = Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("reviewHighlights"));
        var summary = string.Join("\n", highlighted.GetVisualDescendants()
            .OfType<SelectableTextBlock>().Select(ReviewText));
        Assert.Contains("Files matching: *.jpg", summary);
        Assert.Contains("Verification: Read back copied content", summary);
        Assert.Contains("Existing items: Skip existing files", summary);
        var effects = Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("reviewWarnings"));
        Assert.Contains("First delete 1 interrupted partial file", string.Join("\n",
            effects.GetVisualDescendants().OfType<SelectableTextBlock>().Select(ReviewText)));
        Assert.Contains("*.jpg", f.Text);
        Assert.Contains("Read back and compare copied content", f.Text);
        Assert.Contains("skipping does not verify", f.Text);
        Assert.Contains("does not authorize deleting a remaining source", f.Text);
        f.Click("Continue move"); Assert.True(await run);
        await Fixture.Wait(() => !f.Services.Jobs.HasActiveWork);
        var resumed = f.Services.Jobs.Jobs.Last();
        Assert.Equal(JobKind.Move, resumed.Kind);
        Assert.Equal(VerifyMode.ReadBack, resumed.Request.Options.Verify);
        Assert.Equal(ConflictPolicy.Skip, resumed.Request.Options.Conflicts);
        Assert.False(resumed.Request.Options.Filter!.IsMatch("notes.txt"));
        Assert.Equal("photo complete content", File.ReadAllText(target));
        Assert.False(File.Exists(image));
        Assert.Equal("notes remain at source", File.ReadAllText(notes));
        Assert.Equal("notes", File.ReadAllText(unrelated));
    }

    [AvaloniaFact]
    public async Task Renamed_copy_reviews_and_reuses_the_exact_name_and_original_transfer_options()
    {
        using var f = await Fixture.Open();
        string source = Path.Combine(f.Source, "draft.txt"); File.WriteAllText(source, "the complete draft");
        var interrupted = await f.Interrupt(new JobRequest { Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], Destination = Location.FileSystem(f.Destination), NewName = "approved.txt",
            Options = new TransferOptions { Verify = VerifyMode.ReadBack, PreserveTimestamps = false, PreserveAttributes = false,
                RateLimit = 1048576, Conflicts = ConflictPolicy.Replace }, Mode = QueueMode.Queue });
        string target = Path.Combine(f.Destination, "approved.txt");
        // Renamed copies publish from a staged file. Model interruption before publication by removing the complete
        // fixture result; a later edit of a published file must never be misclassified as this job's partial copy.
        File.Delete(target);
        f.Services.Settings.DefaultVerify = "Native";
        var run = f.Vm.RunInterruptedAgainAsync(interrupted);
        await f.WaitButton("Continue copy");
        Assert.Contains(source + " → " + target, f.Text);
        Assert.Contains("(recovery policy)", f.Text);
        Assert.Contains("Timestamps: Do not preserve", f.Text);
        f.Click("Continue copy"); Assert.True(await run);
        await Fixture.Wait(() => !f.Services.Jobs.HasActiveWork);
        var request = f.Services.Jobs.Jobs.Last().Request;
        Assert.Equal("approved.txt", request.NewName);
        Assert.Equal(VerifyMode.ReadBack, request.Options.Verify);
        Assert.Equal(1048576, request.Options.RateLimit);
        Assert.False(request.Options.PreserveTimestamps);
        Assert.False(request.Options.PreserveAttributes);
        Assert.Equal(QueueMode.Queue, request.Mode);
        Assert.Equal("the complete draft", File.ReadAllText(target));
        Assert.False(File.Exists(Path.Combine(f.Destination, "draft.txt")));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Result_set_relative_folders_and_flatten_choice_survive_recovery(bool flatten)
    {
        using var f = await Fixture.Open();
        var items = new List<ItemRef>();
        foreach (string name in new[] { "alpha", "beta" })
        {
            string parent = Directory.CreateDirectory(Path.Combine(f.Source, name)).FullName;
            File.WriteAllText(Path.Combine(parent, name + ".txt"), name);
            items.Add(new ItemRef(Location.FileSystem(parent), name + ".txt", EntryKind.File) { RelativeFolder = "results/" + name });
        }
        var interrupted = await f.Interrupt(new JobRequest { Kind = JobKind.Copy, Sources = items,
            Destination = Location.FileSystem(f.Destination), Options = new TransferOptions { Flatten = flatten } });
        foreach (var file in Directory.GetFiles(f.Destination, "*.txt", SearchOption.AllDirectories)) File.Delete(file);
        var run = f.Vm.RunInterruptedAgainAsync(interrupted);
        await f.WaitButton("Continue copy"); f.Click("Continue copy"); Assert.True(await run);
        await Fixture.Wait(() => !f.Services.Jobs.HasActiveWork);
        var request = f.Services.Jobs.Jobs.Last().Request;
        Assert.Equal(flatten, request.Options.Flatten);
        Assert.Equal(items.Select(i => i.RelativeFolder), request.Sources.Select(i => i.RelativeFolder));
        foreach (string name in new[] { "alpha", "beta" })
        {
            string folder = flatten ? f.Destination : Path.Combine(f.Destination, "results", name);
            Assert.Equal(name, File.ReadAllText(Path.Combine(folder, name + ".txt")));
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_records_require_an_explicit_new_transfer_review_and_keep_the_journal_until_submission(bool approve)
    {
        using var f = await Fixture.Open();
        string source = Path.Combine(f.Source, "draft.txt"); File.WriteAllText(source, "the complete draft");
        var interrupted = await f.Interrupt(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(f.Destination), NewName = "approved.txt" });
        interrupted = Fixture.MakeLegacy(interrupted);
        string target = Path.Combine(f.Destination, "approved.txt"); File.WriteAllText(target, "the complete");
        var row = new InterruptedJobViewModel(interrupted);
        Assert.False(row.CanRunAgain); Assert.True(row.CanReviewNewTransfer);
        Assert.False(await f.Vm.RunInterruptedAgainAsync(interrupted));
        Assert.False(f.HasDialog); Assert.Single(f.Services.Jobs.Jobs);
        Assert.Equal("the complete", File.ReadAllText(target));
        f.Services.Settings.DefaultVerify = "ReadBack";
        var review = f.Vm.ReviewNewInterruptedTransferAsync(interrupted);
        await f.WaitButton("Copy");
        Assert.Contains("This is a new transfer", f.Text);
        Assert.Contains("original relative-folder mappings and filtering cannot be assumed", f.Text);
        Assert.Contains(f.Window.GetVisualDescendants().OfType<Expander>(), e => e.Header as string == "Options" && e.IsExpanded);
        var destination = Assert.Single(f.Window.GetVisualDescendants().OfType<TextBox>(), t => AutomationProperties.GetName(t) == "Destination");
        Assert.Equal(target, destination.Text);
        await Fixture.Wait(() => f.Button("Copy")?.IsEnabled == true);
        f.Click("Copy");
        await f.WaitButton("Start new copy");
        var highlighted = Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("reviewHighlights"));
        Assert.Contains("Source → target name: draft.txt → approved.txt",
            string.Join("\n", highlighted.GetVisualDescendants()
                .OfType<SelectableTextBlock>().Select(ReviewText)));
        Assert.True(JournalRecovery.IsInterrupted(interrupted));
        Assert.Single(f.Services.Jobs.Jobs);
        Assert.Equal("the complete", File.ReadAllText(target));
        f.Click(approve ? "Start new copy" : "Cancel");
        Assert.Equal(approve, await review);
        await Fixture.Wait(() => !f.Services.Jobs.HasActiveWork);
        Assert.Equal(approve ? 2 : 1, f.Services.Jobs.Jobs.Count);
        Assert.Equal(!approve, JournalRecovery.IsInterrupted(interrupted));
        if (approve)
        {
            Assert.Equal("approved.txt", f.Services.Jobs.Jobs.Last().Request.NewName);
            Assert.Equal(VerifyMode.ReadBack, f.Services.Jobs.Jobs.Last().Request.Options.Verify);
            // Legacy cleanup cannot establish a renamed mapping: the existing file is kept under the explicit Skip policy.
            Assert.Equal("the complete", File.ReadAllText(target));
            Assert.Equal("the complete draft", File.ReadAllText(source));
        }
    }

    [AvaloniaFact]
    public async Task A_replaced_manifest_cannot_change_the_reviewed_transfer_scope_or_trigger_cleanup()
    {
        using var f = await Fixture.Open();
        var items = Enumerable.Range(0, 65).Select(i =>
        {
            string path = Path.Combine(f.Source, $"item-{i}.txt"); File.WriteAllText(path, "owned complete bytes");
            return ItemRef.ForFileSystemPath(path, EntryKind.File);
        }).ToArray();
        var interrupted = await f.Interrupt(new JobRequest { Kind = JobKind.Copy, Sources = items, Destination = Location.FileSystem(f.Destination) });
        string manifest = JobJournal.ManifestPathOf(interrupted.JournalPath);
        var paths = items.Select(i => i.FileSystemPath!).ToArray(); paths[^1] = Path.Combine(f.Source, "unexpected.txt");
        File.WriteAllText(paths[^1], "not selected"); File.WriteAllLines(manifest, paths);
        interrupted = Assert.Single(JournalRecovery.Scan(f.Services.Paths.JournalDirectory));
        string target = Path.Combine(f.Destination, "item-0.txt"); File.WriteAllText(target, "owned");
        Assert.False(await f.Vm.RunInterruptedAgainAsync(interrupted));
        Assert.False(f.HasDialog); Assert.Single(f.Services.Jobs.Jobs);
        Assert.Equal("owned", File.ReadAllText(target));
        Assert.True(JournalRecovery.IsInterrupted(interrupted));
        Assert.Contains("manifest", f.Vm.Notification);
    }

    private sealed class Fixture : IDisposable
    {
        public required AppServices Services;
        public required MainViewModel Vm;
        public required MainWindow Window;
        public required string Root, Source, Destination;

        public static async Task<Fixture> Open()
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            if (typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) is Task startup) await startup;
            return new Fixture { Services = services, Vm = vm, Window = window, Root = root,
                Source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName,
                Destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName };
        }

        public async Task<InterruptedJob> Interrupt(JobRequest request)
        {
            var initial = Services.Jobs.Submit(request);
            await Wait(() => initial.State.IsFinished() && !Services.Jobs.HasActiveWork);
            Assert.DoesNotContain(initial.Issues, issue => issue.Severity == IssueSeverity.Error);
            string journal = Assert.Single(Directory.GetFiles(Services.Paths.JournalDirectory, "job-*.fcj"));
            File.WriteAllLines(journal, File.ReadAllLines(journal).Where(line => !line.Contains("\"t\":\"end\"", StringComparison.Ordinal)));
            return Assert.Single(JournalRecovery.Scan(Services.Paths.JournalDirectory));
        }

        public static InterruptedJob MakeLegacy(InterruptedJob job)
        {
            var records = JobJournal.ReadAll(job.JournalPath);
            var lines = new List<string>();
            foreach (var record in records)
            {
                if (record.Type == "begin") { record.Node.Remove("transferIntent"); record.Node.Remove("transferIntentProblem"); }
                string json = record.Node.ToJsonString();
                lines.Add($"{Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)):x8} {json}");
            }
            File.WriteAllLines(job.JournalPath, lines);
            return Assert.Single(JournalRecovery.Scan(Path.GetDirectoryName(job.JournalPath)!));
        }

        public string Text => string.Join("\n", Window.GetVisualDescendants().OfType<TextBlock>().Select(ReviewText));
        public bool HasDialog => Window.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("backdrop"));
        public Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
        public async Task WaitButton(string text)
        {
            await Wait(() => Button(text) is not null);
            Window.UpdateLayout(); // The fixed footer can appear before the scrolled review body is realized.
            var body = Assert.Single(Window.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "DialogBodyScroll");
            Assert.True(body.IsArrangeValid && body.Viewport.Width > 0 && body.Viewport.Height > 0);
        }
        public void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public static async Task Wait(Func<bool> done)
        {
            var clock = Stopwatch.StartNew();
            while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "Owned interrupted transfer checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); }
        }
        public void Dispose() => AccessibilityTests.Close(Services, Window, Root);
    }
}
