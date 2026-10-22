using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class DialogViewportTests
{
    [AvaloniaFact]
    public async Task Undo_review_highlights_entire_quoted_paths_on_each_supported_path_shape()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 720);
        try
        {
            ProductStyles(window);
            string[] paths = [@"C:\review\folder\a.txt", "/home/review/folder/a.txt",
                @"\\server\share\folder\a.txt"];
            var lines = paths.Select(path => OverlayDialogService.ReviewLine(
                $"Move back: \"{path}\" → \"{path}\"")).ToArray();
            var body = new StackPanel();
            foreach (var line in lines) body.Children.Add(line);
            var pending = ((OverlayDialogService)vm.Dialogs).ShowCustomAsync("Review Undo", body,
                [new("Cancel", false, IsCancel: true)], resizable: true);
            Layout(window);
            foreach (var (line, path) in lines.Zip(paths))
            {
                var runs = Assert.IsType<InlineCollection>(line.Inlines).OfType<Run>().ToArray();
                Assert.Equal($"Move back: \"{path}\" → \"{path}\"", string.Concat(runs.Select(run => run.Text)));
                int slash = path.LastIndexOfAny(['\\', '/']);
                string directory = path[..(slash + 1)], name = path[(slash + 1)..];
                Assert.Equal(2, runs.Count(run => run.Text == directory &&
                    run.FontWeight == FontWeight.SemiBold && run.Foreground is not null));
                Assert.Equal(2, runs.Count(run => run.Text == name &&
                    run.FontWeight == FontWeight.Bold && run.Foreground is not null));
            }
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await pending;
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Registry_import_preview_wraps_long_change_paths_and_reflows_with_dialog_width()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1100, height: 760);
        try
        {
            ProductStyles(window);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            string change = "+ value " + string.Join("\\", Enumerable.Repeat("long-registry-subkey", 16)) +
                "\\SelectedValue · REG_SZ, 40 bytes";
            var detail = MainViewModel.RegistryImportPreview(change);
            var pending = dialogs.ShowCustomAsync("Review Registry import", new StackPanel
            {
                Children = { OverlayDialogService.ReviewLine("File: C:\\review\\changes.reg"), detail },
            }, [new("Cancel", false, IsCancel: true)], resizable: true);
            Layout(window);
            var card = Card(window);
            var grip = Assert.Single(card.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Name == "DialogResizeGrip");
            var presenter = Assert.Single(detail.GetVisualDescendants().OfType<TextPresenter>());
            int lines = presenter.TextLayout.TextLines.Count;
            double width = detail.Bounds.Width;
            Assert.True(lines > 1, $"The full Registry path should wrap, but it occupied {lines} line.");
            Assert.Equal(TextWrapping.Wrap, detail.TextWrapping);
            grip.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent,
                Vector = new Vector(160, 0) });
            Layout(window);
            Assert.True(detail.Bounds.Width >= width + 100);
            Assert.True(presenter.TextLayout.TextLines.Count < lines,
                "More review width should reduce the number of wrapped Registry path lines.");
            dialogs.CancelAll();
            await pending;
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Operation_issue_details_keeps_field_keys_and_paths_accessible_while_resizing()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 720);
        try
        {
            ProductStyles(window);
            var job = new Job(new JobRequest { Kind = JobKind.Copy }, "Copy", "local", [], []);
            var issue = new JobIssue(IssueSeverity.Warning, @"C:\review\source.txt", "The target changed",
                StepOutcome.Skipped)
            {
                SourcePath = @"C:\review\source.txt", TargetPath = @"C:\review\target.txt",
                SourceIsLocal = true, TargetIsLocal = true, RequestedAction = "Copy source to target"
            };
            var pending = OperationIssueDialog.ShowAsync(vm, job, issue);
            Layout(window);
            var card = Card(window);
            var grip = Assert.Single(card.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Name == "DialogResizeGrip");
            Assert.Contains(card.GetVisualDescendants().OfType<SelectableTextBlock>(),
                block => block.Inlines?.OfType<Run>().Any(run => run.Text == "Requested action:" &&
                    run.FontWeight == FontWeight.SemiBold) == true);
            Assert.Contains(card.GetVisualDescendants().OfType<TextBox>(),
                box => AutomationProperties.GetName(box) == "Source path with hidden characters escaped" &&
                    box.Text == @"C:\review\source.txt");
            double oldWidth = Body(card).Viewport.Width;
            grip.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent,
                Vector = new Vector(100, 0) });
            Layout(window);
            Assert.InRange(Body(card).Viewport.Width - oldWidth, 80, 110);
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await pending;
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Ordinary_long_confirmation_highlights_the_complete_path_and_reflows_when_resized()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 720);
        try
        {
            ProductStyles(window);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            string path = @"C:\review\selected\file.txt";
            string message = "Destination: " + path + "\nSHA-256 fingerprint: ABCD\n\n" + new string('x', 700);
            var pending = dialogs.ConfirmAsync("Review destination", message, "Proceed", danger: true);
            Layout(window);
            var card = Card(window);
            var grip = Assert.Single(card.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Name == "DialogResizeGrip");
            var line = Assert.Single(card.GetVisualDescendants().OfType<SelectableTextBlock>(),
                block => string.Concat(block.Inlines?.OfType<Run>().Select(run => run.Text) ?? [block.Text]) ==
                         "Destination: " + path);
            var runs = Assert.IsType<InlineCollection>(line.Inlines).OfType<Run>().ToArray();
            Assert.Contains(runs, run => run.Text?.Contains(@"C:\review\selected\", StringComparison.Ordinal) == true &&
                run.FontWeight == FontWeight.SemiBold && run.Foreground is not null);
            Assert.Contains(runs, run => run.Text == "file.txt" &&
                run.FontWeight == FontWeight.Bold && run.Foreground is not null);
            Assert.Contains(card.GetVisualDescendants().OfType<SelectableTextBlock>(), block =>
                block.Inlines?.OfType<Run>().Any(run => run.Text == "SHA-256 fingerprint:" &&
                    run.FontWeight == FontWeight.SemiBold) == true);
            double oldWidth = line.Bounds.Width;
            grip.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent,
                Vector = new Vector(100, 0) });
            Layout(window);
            Assert.InRange(line.Bounds.Width - oldWidth, 80, 110);
            dialogs.CancelAll();
            Assert.False(await pending);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Long_review_and_alert_can_resize_by_pointer_and_keyboard(bool alert)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 720);
        try
        {
            ProductStyles(window);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            string message = string.Join("\n\n", Enumerable.Range(1, 16)
                .Select(index => $"Review detail {index}: source and destination remain visible."));
            Task pending = alert
                ? dialogs.AlertAsync("Review result", message)
                : dialogs.ConfirmReviewAsync("Review transfer", message,
                    ["Action: Copy the reviewed files", "Destination: reviewed folder"], "Copy", danger: true);
            Layout(window);
            var card = Card(window);
            var grip = Assert.Single(card.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Name == "DialogResizeGrip");
            Assert.Equal("Resize dialog", AutomationProperties.GetName(grip));
            Control content = alert
                ? Body(card).GetVisualDescendants().OfType<StackPanel>().First()
                : Assert.Single(card.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("reviewHighlights"));
            double width = card.Bounds.Width, height = card.Bounds.Height;
            double contentWidth = content.Bounds.Width;
            grip.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent,
                Vector = new Vector(32, 24) });
            Layout(window);
            Assert.InRange(card.Bounds.Width - width, 31, 33);
            Assert.InRange(card.Bounds.Height - height, 23, 25);
            Assert.InRange(content.Bounds.Width - contentWidth, 20, 40);
            Assert.True(grip.Focus());
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Layout(window);
            Assert.InRange(card.Bounds.Width - width, 15, 17);
            Assert.InRange(card.Bounds.Height - height, 7, 9);
            window.Width = 520;
            window.Height = 380;
            Layout(window);
            AssertChrome(window, card);
            dialogs.CancelAll();
            await pending;
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(14, false)]
    [InlineData(21, false)]
    [InlineData(28, false)]
    [InlineData(14, true)]
    [InlineData(21, true)]
    [InlineData(28, true)]
    public async Task Highlighted_review_keeps_actions_visible_and_full_details_reachable_at_minimum_size(double fontSize, bool collapseDetails)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420, fontSize: fontSize);
        try
        {
            ProductStyles(window);
            var message = string.Join("\n\n", Enumerable.Range(1, 16)
                .Select(index => $"Detail {index}: the selected disk may change while mounted; recover files elsewhere."));
            var dialogs = (OverlayDialogService)vm.Dialogs;
            var keyPoints = new[] { "Source: sample disk", "FileCat only reads this source", "Recover files to another disk" };
            var review = collapseDetails
                ? dialogs.ConfirmWithCancellationAsync("Recover deleted files", message, "Scan", true,
                    CancellationToken.None, keyPoints, collapseDetails: true)
                : dialogs.ConfirmReviewAsync("Recover deleted files", message, keyPoints, "Scan", danger: true);
            Layout(window);
            var card = Card(window);
            AssertChrome(window, card);
            if (collapseDetails)
            {
                var toggle = Assert.Single(card.GetVisualDescendants().OfType<ToggleButton>(),
                    button => button.Name == "ReviewDetailsToggle");
                Assert.False(toggle.IsChecked);
                toggle.IsChecked = true;
                Layout(window);
            }
            var scroll = Body(card);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.InRange(scroll.Extent.Width, 1, scroll.Viewport.Width + 1);
            var last = card.GetVisualDescendants().OfType<SelectableTextBlock>().Last();
            Assert.StartsWith("Detail 16:", AutomationProperties.GetName(last) ?? last.Text);
            scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
            Layout(window);
            AssertInside(last, Viewport(scroll));
            AssertChrome(window, card);
            dialogs.CancelAll();
            Assert.False(await review);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("copy", 14)]
    [InlineData("copy", 21)]
    [InlineData("copy", 28)]
    [InlineData("delete", 14)]
    [InlineData("delete", 21)]
    [InlineData("delete", 28)]
    [InlineData("bulk rename", 14)]
    [InlineData("bulk rename", 21)]
    [InlineData("bulk rename", 28)]
    public async Task Operations_keep_actions_visible_and_the_last_field_reachable_at_the_minimum_window(string operation, double fontSize)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420, fontSize: fontSize);
        try
        {
            ProductStyles(window);
            string longName = new string('n', 200) + ".txt";
            var item = ItemRef.ForFileSystemPath(Path.Combine(root, "files", longName), EntryKind.File);
            Task? pending = null;
            if (operation == "copy")
            {
                pending = OperationDialogs.ShowTransferAsync(vm, new TransferDialogInput(JobKind.Copy, [item], "1 item",
                    Path.Combine(root, "target", new string('d', 200)), "a long destination " + longName, 3, true)
                    { RecoveryReviewNotice = "Review all options before starting this new copy." });
            }
            else if (operation == "delete")
            {
                pending = OperationDialogs.ShowDeleteAsync(vm, new DeleteDialogInput([item], "1 item", false, 3,
                    Enumerable.Range(0, 12).Select(i => (longName, "This location does not support recycling: " + i)).ToArray(), true));
            }
            else
            {
                await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete && vm.ActiveTab.Listing.VisibleCount == 3);
                vm.ActiveTab!.Listing.MarkNames(["a.txt", "b.txt"], true);
                vm.Execute(CommandIds.BulkRename);
                await Until(() => vm.Dialogs.IsOpen);
                Layout(window);
                Named<TextBox>(window, "Name mask").Text = new string('n', 180) + "_[C]";
                await Until(() => window.GetVisualDescendants().OfType<Button>().Any(b =>
                    b.Content as string == "Edit names in editor…" && b.IsEnabled));
            }
            Layout(window);
            Assert.Equal(new Size(640, 420), window.ClientSize);
            var card = Card(window);
            AssertChrome(window, card);
            var scroll = Body(card);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "This long form must scroll within the modal.");
            Assert.InRange(scroll.Extent.Width, 1, scroll.Viewport.Width + 1);
            Control last = operation switch
            {
                "copy" => Named<TextBox>(card, "Speed limit (MB/s):"),
                "delete" => card.GetVisualDescendants().OfType<RadioButton>().Last(),
                _ => Named<TextBox>(card, "Full old and new paths of selected rename"),
            };
            Assert.True(last.Focus(NavigationMethod.Tab));
            // Caret and outer-dialog reveal each post at Loaded priority; wait for
            // both layout passes before checking the final viewport geometry.
            for (int attempt = 0; attempt < 300; attempt++)
            {
                Layout(window);
                var viewport = Viewport(scroll);
                if (last.TranslatePoint(default, viewport) is { } p &&
                    p.X >= -0.6 && p.Y >= -0.6 &&
                    p.X + last.Bounds.Width <= viewport.Bounds.Width + 0.6 &&
                    p.Y + last.Bounds.Height <= viewport.Bounds.Height + 0.6) break;
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.True(scroll.Offset.Y > 0, "Keyboard focus must reveal the lower field.");
            AssertInside(last, Viewport(scroll));
            AssertChrome(window, card);
            if (operation == "bulk rename")
            {
                var preview = Named<ListBox>(card, "Preview of new names");
                var editor = card.GetVisualDescendants().OfType<Button>().Single(b =>
                    b.Content as string == "Edit names in editor…");
                var previousMapping = Named<Button>(card, "Previous rename mapping");
                var nextMapping = Named<Button>(card, "Next rename mapping");
                Assert.True(editor.TranslatePoint(default, card)?.Y < preview.TranslatePoint(default, card)?.Y,
                    "The external-editor action must precede the long preview.");
                Assert.True(previousMapping.TranslatePoint(default, card)?.Y < preview.TranslatePoint(default, card)?.Y &&
                            nextMapping.TranslatePoint(default, card)?.Y < preview.TranslatePoint(default, card)?.Y,
                    "The mapping navigation must remain discoverable before the long preview.");
                Assert.True(Math.Abs((previousMapping.TranslatePoint(default, card)?.Y ?? -1) -
                                     (nextMapping.TranslatePoint(default, card)?.Y ?? -2)) <= 1,
                    "Previous and Next mapping must stay together when the review narrows.");
                Assert.InRange(preview.Bounds.Width, 1, scroll.Viewport.Width + 1);
                Assert.Equal(280, preview.Bounds.Height);
                Assert.Equal(2, preview.ItemCount);
            }
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            if (pending is not null) await pending;
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(14, false)]
    [InlineData(21, false)]
    [InlineData(28, false)]
    [InlineData(21, true)]
    public async Task Conflict_actions_wrap_in_keyboard_order_and_full_suggested_names_remain_selectable(double fontSize, bool invisibleCharacters)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420, fontSize: fontSize);
        try
        {
            ProductStyles(window);
            string incomingName = invisibleCharacters ? "invoice\u202Efdp.exe\u0001" : new string('i', 200) + " (copy).txt";
            string existingName = invisibleCharacters ? "existing\u2028file.txt" : new string('e', 200) + " (existing).txt";
            string incomingDisplay = invisibleCharacters ? @"invoice\u202Efdp.exe\u0001" : incomingName;
            string existingDisplay = invisibleCharacters ? @"existing\u2028file.txt" : existingName;
            string a = Path.Combine(root, "incoming.txt"), b = Path.Combine(root, "existing.txt");
            var info = new FileSystemItemInfo(a, false, false, 1000, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal);
            var job = (Job)Activator.CreateInstance(typeof(Job), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [new JobRequest { Kind = JobKind.Copy }, "Copy to " + new string('d', 200), "viewport",
                    new[] { a }, new[] { b }], null)!;
            var pending = OperationDialogs.ShowDecisionAsync(vm, new PendingDecision(job,
                new ConflictRequest("File conflict", new string('n', 200), info, info with { Path = b }, a, b,
                    true, false, false, false, incomingName, existingName)));
            Layout(window);
            var card = Card(window);
            var footer = Footer(card);
            var buttons = footer.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(["Replace", "Replace if newer", "Skip", "Keep both", "Rename existing", "Cancel operation"],
                buttons.Select(button => button.Content as string));
            Assert.True(buttons.Select(button => button.TranslatePoint(default, footer)!.Value.Y).Distinct().Count() > 1);
            AssertChrome(window, card);
            Assert.True(buttons[2].IsDefault);
            Assert.True(buttons[2].IsFocused);
            // A physical Tab follows the same action order even after the footer has wrapped.
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(buttons[3].IsFocused);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(buttons[4].IsFocused);
            var name = Named<SelectableTextBlock>(card, "Keep both: incoming copy name");
            Assert.Equal(incomingDisplay, name.Text);
            name.SelectAll();
            Assert.Equal(incomingDisplay, name.SelectedText);
            Assert.Equal(existingDisplay, Named<SelectableTextBlock>(card, "Rename existing: existing item name").Text);
            if (invisibleCharacters) Assert.Contains("escape sequences", AutomationProperties.GetHelpText(name));
            name.BringIntoView();
            Layout(window);
            Assert.InRange(name.Bounds.Width, 1, Body(card).Viewport.Width + 1);
            AssertChrome(window, card);
            buttons[2].Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(DecisionAction.Skip, (await pending).Action);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(21)]
    [InlineData(28)]
    public async Task Every_settings_page_and_its_inputs_fit_and_keyboard_focus_reveals_lower_fields(double fontSize)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420, fontSize: fontSize);
        try
        {
            ProductStyles(window);
            var pending = SettingsDialog.ShowAsync(vm);
            Layout(window);
            var card = Card(window);
            var tabs = Assert.Single(card.GetVisualDescendants().OfType<TabControl>());
            for (int i = 0; i < tabs.ItemCount; i++)
            {
                tabs.SelectedIndex = i;
                Body(card).Offset = default;
                Layout(window);
                AssertChrome(window, card);
                Assert.InRange(Body(card).Extent.Width, 1, Body(card).Viewport.Width + 1);
                // Multi-line editors may be taller than the viewport, but they never need horizontal page scrolling.
                foreach (var box in card.GetVisualDescendants().OfType<TextBox>().Where(c => c.IsEffectivelyVisible))
                    Assert.InRange(box.Bounds.Width, 1, Body(card).Viewport.Width + 1);
                var controls = card.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled &&
                    c.Focusable && c is TextBox or ComboBox or CheckBox or NumericUpDown).ToArray();
                var last = controls.Last();
                bool wasBelow = last.TranslatePoint(default, Viewport(Body(card)))!.Value.Y + Math.Min(20, last.Bounds.Height) > Body(card).Viewport.Height;
                last.Focus(NavigationMethod.Tab);
                Layout(window);
                if (wasBelow) Assert.True(Body(card).Offset.Y > 0, "A lower Settings field must be reached by scrolling the body.");
                AssertFocusedEntry(last, Viewport(Body(card)));
                AssertChrome(window, card);
            }
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await pending;
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_fixed_width_content_has_real_horizontal_reachability_and_the_footer_stays_put(bool nested)
    {
        var host = new Grid();
        var window = new Window { Width = 640, Height = 420, Content = host };
        ProductStyles(window);
        var dialogs = new OverlayDialogService(host, () => null);
        window.Show();
        try
        {
            var far = new Button { Content = "Far edge", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
            var oversized = new Grid { MinWidth = 1300, Height = 900, Children = { far } };
            Control body = nested ? new StackPanel { Children = { oversized } } : oversized;
            var pending = dialogs.ShowCustomAsync("Oversized custom content", body, [new DialogButton("Close", "close", IsCancel: true)]);
            Layout(window);
            var card = Card(window);
            var scroll = Body(card);
            Assert.True(scroll.Extent.Width >= 1300, $"Extent {scroll.Extent}, viewport {scroll.Viewport}, body desired {body.DesiredSize}, body bounds {body.Bounds}.");
            AssertChrome(window, card);
            Assert.True(far.Focus(NavigationMethod.Tab));
            Layout(window);
            Assert.True(scroll.Offset.X > 0);
            AssertInside(far, Viewport(scroll));
            AssertChrome(window, card);
            dialogs.CancelAll();
            Assert.Equal("close", await pending);
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Resizing_the_host_reflows_a_live_dialog_without_losing_its_draft_or_focus()
    {
        var host = new Grid { Width = 1100, Height = 700 };
        var window = new Window { Width = 1200, Height = 800, Content = host };
        ProductStyles(window);
        var dialogs = new OverlayDialogService(host, () => null);
        window.Show();
        try
        {
            var input = new TextBox { Text = "owned draft" };
            var form = new DialogForm(("Draft:", input), ("Details", new TextBlock { Text = new string('x', 4000), TextWrapping = TextWrapping.Wrap }));
            var pending = dialogs.ShowCustomAsync("Resize review", form,
                [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Keep draft", "ok", IsDefault: true)], input);
            Layout(window);
            input.CaretIndex = 5;
            Assert.True(input.IsFocused);
            var card = Card(window);
            double wide = card.Bounds.Width;
            host.Width = 640;
            host.Height = 420;
            Layout(window);
            Assert.True(card.Bounds.Width < wide);
            Assert.InRange(card.Bounds.Width, 1, 592);
            Assert.InRange(card.Bounds.Height, 1, 372);
            AssertInside(card, host);
            AssertChrome(window, card);
            Assert.True(input.IsFocused);
            Assert.Equal("owned draft", input.Text);
            Assert.Equal(5, input.CaretIndex);
            dialogs.CancelAll();
            Assert.Equal("cancel", await pending);
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refocusing_a_tall_editor_reveals_its_retained_caret_in_both_scroll_viewports(bool nearEnd)
    {
        var host = new Grid();
        var window = new Window { Width = 640, Height = 420, Content = host };
        ProductStyles(window);
        var dialogs = new OverlayDialogService(host, () => null);
        window.Show();
        try
        {
            var editor = new TextBox { AcceptsReturn = true, Height = 600,
                Text = string.Join('\n', Enumerable.Range(0, 120).Select(i => $"Retained line {i}")) };
            var body = new StackPanel { Children = { new Border { Height = 350 }, editor } };
            var pending = dialogs.ShowCustomAsync("Retained caret", body, [new DialogButton("Close", "close", IsCancel: true)]);
            Layout(window);
            int caret = nearEnd ? editor.Text!.Length - 2 : 2;
            editor.CaretIndex = caret;
            editor.Focus(NavigationMethod.Pointer);
            Layout(window);
            var card = Card(window);
            var inner = Assert.Single(editor.GetVisualDescendants().OfType<ScrollViewer>());
            AssertFocusedEntry(editor, Viewport(Body(card)));
            AssertFocusedEntry(editor, Viewport(inner));
            var close = Assert.Single(Footer(card).GetVisualDescendants().OfType<Button>());
            close.Focus();
            // Both scroll positions can change independently while the unchanged caret is elsewhere.
            inner.Offset = new Vector(0, nearEnd ? 0 : inner.Extent.Height);
            Body(card).Offset = default;
            Layout(window);
            editor.Focus(NavigationMethod.Pointer);
            Layout(window);
            Assert.Equal(caret, editor.CaretIndex);
            AssertFocusedEntry(editor, Viewport(inner));
            AssertFocusedEntry(editor, Viewport(Body(card)));
            AssertChrome(window, card);
            // A later focus owner wins over already queued reveal callbacks.
            close.Focus();
            Body(card).Offset = default;
            editor.Focus(NavigationMethod.Pointer);
            close.Focus();
            Layout(window);
            Assert.True(close.IsFocused);
            Assert.Equal(0, Body(card).Offset.Y);
            dialogs.CancelAll();
            Assert.Equal("close", await pending);
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }

    private static void ProductStyles(Window window) => window.Styles.Add(new StyleInclude(new Uri("avares://FileCat/"))
        { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });

    private static T Named<T>(Control root, string name) where T : Control => Assert.Single(root.GetVisualDescendants().OfType<T>(),
        c => AutomationProperties.GetName(c) == name);
    private static DialogCard Card(Control root) => Assert.Single(root.GetVisualDescendants().OfType<DialogCard>());
    private static Border Footer(Control card) => Assert.Single(card.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("dialogFooter"));
    private static ScrollViewer Body(Control card) => Assert.Single(card.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "DialogBodyScroll");
    private static ScrollContentPresenter Viewport(ScrollViewer scroll) => Assert.Single(scroll.GetVisualDescendants().OfType<ScrollContentPresenter>(),
        p => ReferenceEquals(p.TemplatedParent, scroll));

    private static void AssertChrome(Window window, Control card)
    {
        AssertInside(card, window);
        var footer = Footer(card);
        AssertInside(footer, card);
        var header = Assert.Single(card.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("dialogHeader"));
        AssertInside(header, card);
        var viewport = Viewport(Body(card));
        Assert.True(viewport.Bounds.Height >= 24, "The body must retain a readable viewport.");
        var headerBottom = header.TranslatePoint(new Point(0, header.Bounds.Height), card)!.Value.Y;
        var bodyTop = viewport.TranslatePoint(default, card)!.Value.Y;
        var bodyBottom = viewport.TranslatePoint(new Point(0, viewport.Bounds.Height), card)!.Value.Y;
        var footerTop = footer.TranslatePoint(default, card)!.Value.Y;
        Assert.True(headerBottom <= bodyTop + 0.5 && bodyBottom <= footerTop + 0.5, "Title, body and actions must not overlap.");
        var buttons = footer.GetVisualDescendants().OfType<Button>().ToArray();
        foreach (var button in buttons)
        {
            Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
            AssertInside(button, footer);
            AssertInside(button, window);
        }
    }

    private static void AssertInside(Control child, Control viewport)
    {
        var p = child.TranslatePoint(default, viewport)!.Value;
        Assert.True(p.X >= -0.6 && p.Y >= -0.6 && p.X + child.Bounds.Width <= viewport.Bounds.Width + 0.6 &&
            p.Y + child.Bounds.Height <= viewport.Bounds.Height + 0.6,
            $"{child.GetType().Name} {child.Name} at {p}, size {child.Bounds.Size}, is outside {viewport.GetType().Name} {viewport.Bounds.Size}.");
    }

    private static void AssertFocusedEntry(Control child, Control viewport)
    {
        if (child is TextBox box)
        {
            var presenter = Assert.Single(box.GetVisualDescendants().OfType<TextPresenter>());
            var caret = presenter.TextLayout.HitTestTextPosition(box.CaretIndex);
            var position = presenter.TranslatePoint(caret.TopLeft, viewport)!.Value;
            Assert.True(position.X >= -0.6 && position.X + caret.Width <= viewport.Bounds.Width + 0.6 &&
                position.Y >= -0.6 && position.Y + caret.Height <= viewport.Bounds.Height + 0.6,
                $"Caret in {AutomationProperties.GetName(child)} at {position}, size {caret.Size}, is outside {viewport.Bounds.Size}.");
            return;
        }
        var p = child.TranslatePoint(default, viewport)!.Value;
        Assert.True(p.X >= -0.6 && p.X + child.Bounds.Width <= viewport.Bounds.Width + 0.6 && p.Y < viewport.Bounds.Height &&
            p.Y + Math.Min(child.Bounds.Height, 20) > 0,
            $"The focused input {child.GetType().Name} {AutomationProperties.GetName(child)} {(child as ContentControl)?.Content} at {p}, size {child.Bounds.Size}, must have an accessible, visible entry point in {viewport.Bounds.Size}.");
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), "The dialog did not reach its expected state.");
    }
}
