using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Verification;

namespace FileCat.App.Tests;

/// <summary>D-57: files beside checksum files show what they match, in the list, its status line, and on request.</summary>
public sealed class VerificationUiTests
{
    [AvaloniaFact]
    public async Task Files_beside_a_checksum_file_show_their_result_and_a_large_one_is_checked_on_request()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files"); // a.txt ("alpha") and b.txt ("beta")
            var big = new byte[3 << 20];
            new Random(57).NextBytes(big);
            File.WriteAllBytes(Path.Combine(folder, "big.bin"), big);
            string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
            string Text(string text) => Sha(System.Text.Encoding.UTF8.GetBytes(text));
            File.WriteAllText(Path.Combine(folder, "SHA256SUMS"), $"{Text("alpha")}  a.txt\n{Text("not beta")}  b.txt\n{Sha(big)} *big.bin\n");
            services.Settings.VerifyAutomaticallyUpToMiB = 1; // big.bin waits for a request
            vm.VerifyKeysGuardedAfter = TimeSpan.MaxValue;      // Esc closes the results at once

            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 5); i++) await Task.Delay(20, ct);
            Assert.True(tab.HasSidecars);

            (EntryData Entry, int Store, int Row) Row(string name)
            {
                for (int i = 0; i < listing.VisibleCount; i++)
                    if (listing.GetVisible(i) is { } e && e.Name == name) return (e, listing.GetStoreIndex(i), i);
                throw new InvalidOperationException(name + " is not listed");
            }
            VerificationResult? Result(string name)
            {
                var (e, store, _) = Row(name);
                return tab.Verification(e, store);
            }
            async Task Until(Func<bool> done, string what)
            {
                for (int i = 0; i < 500 && !done(); i++)
                {
                    window.CaptureRenderedFrame(); // the rows drawn ask for their results
                    await Task.Delay(20, ct);
                }
                Assert.True(done(), what);
            }

            // Drawn rows ask; the answers come from the background.
            await Until(() => Result("a.txt") is { Settling: false } a && string.Join(" ", a.Details.Prepend(a.Text)).Contains("SHA-256", StringComparison.Ordinal)
                              && Result("b.txt") is { Settling: false } b && string.Join(" ", b.Details.Prepend(b.Text)).Contains("SHA-256", StringComparison.Ordinal)
                              && Result("big.bin")?.State == VerificationState.NotChecked && Result("SHA256SUMS") is not null, "the results arrive");
            Assert.Contains(Result("a.txt")!.State, new[] { VerificationState.Matches, VerificationState.NotChecked });
            Assert.Contains(Result("b.txt")!.State, new[] { VerificationState.Differs, VerificationState.NotChecked });
            Assert.Contains("SHA-256", string.Join(" ", Result("a.txt")!.Details.Prepend(Result("a.txt")!.Text)));
            Assert.Contains("SHA-256 differs", string.Join(" ", Result("b.txt")!.Details.Prepend(Result("b.txt")!.Text)));
            Assert.StartsWith("not checked: 3 MiB", Result("big.bin")!.Text, StringComparison.Ordinal);
            Assert.Contains("File → Verify checksums and signatures", Result("big.bin")!.Details.Single(), StringComparison.Ordinal);
            var sums = Result("SHA256SUMS")!;
            Assert.Equal((VerificationState.Sidecar, "checks 3 files"), (sums.State, sums.Text));
            // The tooltip says which line of which file, and what the file's own value is.
            string tip = tab.VerificationTip(Row("b.txt").Entry, Row("b.txt").Store)!;
            Assert.Contains("SHA256SUMS, line 2 says", tip, StringComparison.Ordinal);
            Assert.Contains(Text("beta"), tip, StringComparison.Ordinal);
            Assert.Contains("; File → Verify checksums and signatures reads it again.", tip, StringComparison.Ordinal); // when it was checked

            // The status line: the focused file's result, and the folder's sum.
            listing.SetFocus(Row("a.txt").Row);
            await Until(() => tab.StatusRight.EndsWith(" · " + Result("a.txt")!.Text, StringComparison.Ordinal), "the focused file's result is in the status line");
            await Until(() => tab.StatusLeft.Contains("checksums: 2 previously checked, 1 not checked", StringComparison.Ordinal),
                $"the folder's sum is in the status line ({tab.StatusLeft}; the service says {VerificationService.Current!.Summary(folder, ["a.txt", "b.txt", "big.bin", "SHA256SUMS"])})");

            // Verifying only reads: the folder is not listed again, and values of other rows stay (a probe stands for them).
            services.Metadata.Register(new MetadataField("probe", "Probe", MetadataCost.Cheap, _ => true, (p, _) => p, v => (string)v!));
            var (aEntry, _, _) = Row("a.txt");
            string aPath = Path.Combine(folder, "a.txt");
            await Until(() => services.Metadata.Get("probe", aPath, aEntry, "probe", false).State == MetadataState.Available, "the probe has its value");

            // On request: the large file is read, its result kept, and its row shows it.
            listing.SetFocus(Row("big.bin").Row);
            vm.Execute(CommandIds.VerifyChecksums);
            await Until(() => services.Jobs.Jobs.Count == 1 && services.Jobs.Jobs[0].State.IsFinished(), "the job finishes");
            var job = services.Jobs.Jobs[0];
            Assert.Equal((JobKind.VerifyBeside, JobState.Completed, "1 verified, all good"), (job.Kind, job.State, job.Summary));

            // Whatever it found, its results open: here the file, its value and the checksum file's, both in full.
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen, "the results open");
            window.UpdateLayout(); // Realize the modal's scrollable body before querying result details.
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "big.bin is intact: it matches its checksum");
            Assert.Equal(2, window.GetVisualDescendants().OfType<SelectableTextBlock>().Count(t => t.Text == Sha(big)));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "SHA-256 · SHA256SUMS, line 3: matches");
            Assert.Equal(MetadataState.Available, services.Metadata.Get("probe", aPath, aEntry, "probe", false).State);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Until(() => !dialogs.IsOpen, "the results close");

            await Until(() => Result("big.bin")?.State == VerificationState.Matches, "the freshly checked file has a good row badge");
            await Until(() => tab.StatusLeft.Contains("checksums: 3 previously checked", StringComparison.Ordinal), $"the sum follows ({tab.StatusLeft})");

            // Even a same-size rewrite with its mtime restored loses that good row badge on refresh.
            string bigPath = Path.Combine(folder, "big.bin");
            DateTime originalTime = File.GetLastWriteTimeUtc(bigPath);
            big[0] ^= 1;
            File.WriteAllBytes(bigPath, big);
            File.SetLastWriteTimeUtc(bigPath, originalTime);
            tab.Refresh();
            await Until(() => listing.State == ListingState.Complete && Result("big.bin")?.Text.StartsWith("previously checked ", StringComparison.Ordinal) == true,
                "refreshed substituted bytes have only a historical neutral mark");
            Assert.Equal(VerificationState.NotChecked, Result("big.bin")!.State);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Files_chosen_with_their_checksum_file_are_checked_once_and_shown_together()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files"); // a.txt ("alpha") and b.txt ("beta")
            string Text(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            File.WriteAllText(Path.Combine(folder, "c.txt"), "gamma"); // nothing says what it should be
            File.WriteAllText(Path.Combine(folder, "SHA256SUMS"), $"{Text("alpha")}  a.txt\n{Text("not beta")}  b.txt\n");
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 5); i++) await Task.Delay(20, ct);
            async Task Until(Func<bool> done, string what)
            {
                for (int i = 0; i < 500 && !done(); i++)
                {
                    window.CaptureRenderedFrame(); // what opened is laid out (a scrolled pane's content only then exists)
                    await Task.Delay(20, ct);
                }
                window.CaptureRenderedFrame();
                Assert.True(done(), what);
            }

            // The checksum file and every file: the files are checked against everything beside them (its lines among it),
            // then the checksum file, for what that did not compare (nothing here); the file nothing names is reported as such.
            vm.VerifyKeysGuardedAfter = TimeSpan.MaxValue;
            listing.MarkAll(true, includeDirectories: false);
            vm.Execute(CommandIds.VerifyChecksums);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen, "the results open");
            Assert.Equal([JobKind.VerifyBeside, JobKind.VerifyChecksums], services.Jobs.Jobs.OrderBy(j => j.QueueOrder).Select(j => j.Kind));
            var job = services.Jobs.Jobs.Single(j => j.Kind == JobKind.VerifyBeside);
            Assert.Equal(3, job.Request.Sources.Count);
            var lines = services.Jobs.Jobs.Single(j => j.Kind == JobKind.VerifyChecksums);
            Assert.Equal((0, "the files it lists were checked beside them"), (lines.VerificationReport!.Total, lines.Summary));
            // Each file once: the failure first, the match, then the file nothing names.
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => Avalonia.Automation.AutomationProperties.GetName(l) == "Files checked");
            var files = Assert.IsAssignableFrom<IReadOnlyList<VerifiedFile>>(list.ItemsSource);
            Assert.Equal([("b.txt", VerifiedOutcome.Failed), ("a.txt", VerifiedOutcome.Good), ("c.txt", VerifiedOutcome.NotChecked)], files.Select(f => (f.Name, f.Outcome)));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "1 of 3 files failed");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "1 intact · 1 failed · 1 not checked");
            Assert.Empty(AccessibilityTests.Unnamed(window));
            // The failure is selected, with both values.
            Assert.Equal(1, window.GetVisualDescendants().OfType<SelectableTextBlock>().Count(t => t.Text == Text("not beta")));
            Assert.Equal(1, window.GetVisualDescendants().OfType<SelectableTextBlock>().Count(t => t.Text == Text("beta")));

            // The report copies as text without closing the dialog: every file, both values in full.
            // The modal owns these actions; another part of the window may offer a same-named command.
            var overlay = window.FindControl<Panel>("OverlayHost")!;
            Button ButtonNamed(string text) => overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text);
            await ClipboardExtensions.SetTextAsync(Assert.IsAssignableFrom<IClipboard>(window.Clipboard), "");
            ButtonNamed("Copy report").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            string copied = "";
            for (int i = 0; i < 250 && copied.Length == 0; i++)
            {
                await Task.Delay(20, ct);
                copied = await ClipboardExtensions.TryGetTextAsync(window.Clipboard!) ?? "";
            }
            Assert.True(dialogs.IsOpen);
            Assert.Equal("Report copied", ButtonNamed("Report copied").Content);
            Assert.StartsWith("Verify checksums and signatures: 1 of 3 files failed", copied, StringComparison.Ordinal);
            Assert.Contains("b.txt: differs (✗ SHA-256 differs)", copied, StringComparison.Ordinal);
            Assert.Contains($"    expected {Text("not beta")}", copied, StringComparison.Ordinal);
            Assert.Contains($"    computed {Text("beta")}", copied, StringComparison.Ordinal);
            Assert.Contains("c.txt: not checked (nothing to check against)", copied, StringComparison.Ordinal);

            // The failing file opens in a panel.
            ButtonNamed("Show 1 failed file in a panel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !dialogs.IsOpen && vm.ActiveTab?.Location?.Scheme == Schemes.ResultSet, "the failing file is listed in a panel");
            var results = vm.ActiveTab!;
            await Until(() => results.Listing.State == ListingState.Complete && results.Listing.VisibleCount >= 1, "the list is filled");
            Assert.Equal((1, "b.txt"), (results.Listing.VisibleCount, results.Listing.GetVisible(0).Name));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Verify_from_the_command_search_with_nothing_marked_checks_the_whole_folder()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files"); // a.txt ("alpha") and b.txt ("beta")
            string Text(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            File.WriteAllText(Path.Combine(folder, "c.txt"), "gamma");                        // nothing names it
            File.WriteAllText(Path.Combine(folder, "d.bin"), "delta");
            File.WriteAllText(Path.Combine(folder, "d.bin.sha256"), Text("delta") + "\n");      // a bare checksum, as many sites publish them
            File.WriteAllText(Path.Combine(folder, "SHA256SUMS"), $"{Text("alpha")}  a.txt\n{Text("not beta")}  b.txt\n{Text("gone")}  gone.txt\n");
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 7); i++) await Task.Delay(20, ct);
            async Task Until(Func<bool> done, string what)
            {
                for (int i = 0; i < 500 && !done(); i++)
                {
                    window.CaptureRenderedFrame(); // what opened is laid out (a scrolled pane's content only then exists)
                    await Task.Delay(20, ct);
                }
                window.CaptureRenderedFrame();
                Assert.True(done(), what);
            }
            var dialogs = (OverlayDialogService)vm.Dialogs;
            IReadOnlyList<VerifiedFile> Listed() => Assert.IsAssignableFrom<IReadOnlyList<VerifiedFile>>(window.GetVisualDescendants().OfType<ListBox>()
                .Single(l => Avalonia.Automation.AutomationProperties.GetName(l) == "Files checked").ItemsSource);
            bool Shows(string text) => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text);
            async Task Close()
            {
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await Until(() => !dialogs.IsOpen, "the results close");
            }

            // The cursor on a file, nothing marked, Verify chosen in the search: the whole folder, each file once.
            vm.VerifyKeysGuardedAfter = TimeSpan.MaxValue;
            listing.SetFocus(1);
            Assert.False(listing.HasMarks);
            vm.RunFromSearch(CommandIds.VerifyChecksums);
            await Until(() => dialogs.IsOpen, "the folder's results open");
            Assert.Equal([JobKind.VerifyChecksums, JobKind.VerifyBeside], services.Jobs.Jobs.Select(j => j.Kind).Order());
            Assert.Equal([("b.txt", VerifiedOutcome.Failed), ("a.txt", VerifiedOutcome.Good), ("d.bin", VerifiedOutcome.Good), ("c.txt", VerifiedOutcome.NotChecked)],
                Listed().Select(f => (f.Name, f.Outcome)));
            await Until(() => Shows("Verify checksums and signatures in \"files\""), "Verify checksums and signatures in \"files\"");
            await Until(() => Shows("1 of 4 files failed"), "1 of 4 files failed");
            await Until(() => Shows("2 intact · 1 failed · 1 not checked"), "2 intact · 1 failed · 1 not checked");
            // The checksum files were found, not chosen: a line for a file that is not here is left out, and said so.
            await Until(() => Shows("SHA256SUMS also lists 1 file that is not here: it was not looked for (the folder was checked, not this checksum file)."), "SHA256SUMS also lists 1 file that is not here: it was not looked for (the folder was checked, not this checksum file).");
            await Close();

            // From anywhere else, the file under the cursor alone, as before.
            int jobs = services.Jobs.Jobs.Count;
            Assert.Equal("a.txt", listing.GetVisible(1).Name);
            vm.Execute(CommandIds.VerifyChecksums);
            await Until(() => dialogs.IsOpen, "the file's results open");
            Assert.Equal(jobs + 1, services.Jobs.Jobs.Count);
            await Until(() => Shows("a.txt is intact: it matches its checksum"), "a.txt is intact: it matches its checksum");
            await Close();

            // Nothing marked and nothing under the cursor (it is on ".."): the folder, from anywhere.
            listing.SetFocus(0);
            Assert.Equal(EntryKind.Parent, listing.GetVisible(0).Kind);
            vm.Execute(CommandIds.VerifyChecksums);
            await Until(() => dialogs.IsOpen, "the folder's results open again");
            await Until(() => Shows("Verify checksums and signatures in \"files\""), "Verify checksums and signatures in \"files\"");
            Assert.Equal(4, Listed().Count);
            await Close();

            // A checksum file holding only a bare checksum stands for the file it is named after.
            int bare = Enumerable.Range(0, listing.VisibleCount).First(i => listing.GetVisible(i).Name == "d.bin.sha256");
            listing.SetFocus(bare);
            vm.Execute(CommandIds.VerifyChecksums);
            await Until(() => dialogs.IsOpen, "the bare checksum's results open");
            Assert.Equal(JobKind.VerifyBeside, services.Jobs.Jobs[^1].Kind);
            await Until(() => Shows("d.bin is intact: it matches its checksum"), "d.bin is intact: it matches its checksum");
            await Close();

            // With nothing to check anywhere in the folder, the search says so.
            foreach (string sidecar in new[] { "d.bin.sha256", "SHA256SUMS" }) File.Delete(Path.Combine(folder, sidecar));
            tab.Refresh();
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 5 && !tab.HasSidecars, "the folder is listed again");
            vm.ClearNotification();
            vm.RunFromSearch(CommandIds.VerifyChecksums);
            await Until(() => vm.Notification is not null, "the search says why nothing is checked");
            Assert.StartsWith("Nothing in this folder says what its files should be", vm.Notification, StringComparison.Ordinal);
            Assert.False(dialogs.IsOpen);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Files_a_large_checksum_file_lists_are_checked_by_it_once_and_never_called_unnamed()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files"); // a.txt ("alpha") and b.txt ("beta")
            string Text(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            // Larger than the checks beside files read (4 MiB): only the list itself checks what it names.
            File.WriteAllText(Path.Combine(folder, "SHA256SUMS"), $"{Text("alpha")}  a.txt\n{Text("not beta")}  b.txt\n"
                + string.Concat(Enumerable.Repeat("# a long list of releases, padded past four mebibytes\n", 90_000)));
            Assert.True(new FileInfo(Path.Combine(folder, "SHA256SUMS")).Length > FolderSidecars.MaxChecksumFileBytes);
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 4); i++) await Task.Delay(20, ct);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            listing.MarkAll(true, includeDirectories: false);
            vm.Execute(CommandIds.VerifyChecksums);
            for (int i = 0; i < 500 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen, "the results open");
            Assert.Equal([JobKind.VerifyBeside, JobKind.VerifyChecksums], services.Jobs.Jobs.OrderBy(j => j.QueueOrder).Select(j => j.Kind));
            window.UpdateLayout();
            var files = Assert.IsAssignableFrom<IReadOnlyList<VerifiedFile>>(window.GetVisualDescendants().OfType<ListBox>()
                .Single(l => Avalonia.Automation.AutomationProperties.GetName(l) == "Files checked").ItemsSource);
            // Each once, as the list found them: that nothing beside them names them is moot.
            Assert.Equal([("b.txt", VerifiedOutcome.Failed), ("a.txt", VerifiedOutcome.Good)], files.Select(f => (f.Name, f.Outcome)));
            Assert.All(files, f => Assert.DoesNotContain(f.Notes, n => n.StartsWith("Nothing beside it says", StringComparison.Ordinal)));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "1 of 2 files failed");
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Results_show_again_from_the_operations_strip_and_a_name_cannot_turn_their_title_around()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files");
            // A right-to-left override would show "report" + "exe.txt" reversed: "reportexe.txt" for an executable.
            string spoof = "report" + (char)0x202E + "txt.exe";
            File.WriteAllText(Path.Combine(folder, spoof), "payload");
            File.WriteAllText(Path.Combine(folder, spoof + ".sha256"), Convert.ToHexStringLower(SHA256.HashData("payload"u8)) + "\n");
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 5); i++) await Task.Delay(20, ct);
            async Task Until(Func<bool> done, string what)
            {
                for (int i = 0; i < 500 && !done(); i++)
                {
                    window.CaptureRenderedFrame(); // what opened is laid out (a scrolled pane's content only then exists)
                    await Task.Delay(20, ct);
                }
                window.CaptureRenderedFrame();
                Assert.True(done(), what);
            }
            vm.VerifyKeysGuardedAfter = TimeSpan.MaxValue;
            listing.SetFocus(Enumerable.Range(0, listing.VisibleCount).First(i => listing.GetVisible(i).Name == spoof));
            vm.Execute(CommandIds.VerifyChecksums);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen, "the results open");
            // Every text the results show, the title first: the override is shown as such, never applied.
            var texts = window.FindControl<Panel>("OverlayHost")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            Assert.Contains("Verify \"report\\u202Etxt.exe\"", texts);
            Assert.DoesNotContain(texts, t => t.Contains((char)0x202E));
            // So does the status line, beside the file's result.
            Assert.DoesNotContain((char)0x202E, tab.StatusRight);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Until(() => !dialogs.IsOpen, "the results close");

            // Closed by mistake: the operations strip shows them again, from what was kept.
            var job = Assert.Single(services.Jobs.Jobs);
            var row = vm.Operations.Jobs.Single(j => j.Job == job);
            await Until(() => row.CanShowResults, "the operations strip offers the results");
            var again = vm.ShowVerificationResultsAsync(job);
            await Until(() => dialogs.IsOpen, "the results open again");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "report\\u202Etxt.exe is intact: it matches its checksum");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await again;
            Assert.False(dialogs.IsOpen);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Results_of_a_check_still_running_wait_for_it_and_a_canceled_check_says_what_it_left()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files"); // a.txt ("alpha") and b.txt ("beta")
            string Text(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            File.WriteAllText(Path.Combine(folder, "SHA256SUMS"), $"{Text("alpha")}  a.txt\n{Text("beta")}  b.txt\n");
            vm.VerifyKeysGuardedAfter = TimeSpan.MaxValue;
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 4); i++) await Task.Delay(20, ct);
            async Task Until(Func<bool> done, string what)
            {
                for (int i = 0; i < 500 && !done(); i++)
                {
                    window.CaptureRenderedFrame(); // what opened is laid out (a scrolled pane's content only then exists)
                    await Task.Delay(20, ct);
                }
                window.CaptureRenderedFrame();
                Assert.True(done(), what);
            }
            var dialogs = (OverlayDialogService)vm.Dialogs;
            bool Shows(string text) => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text);
            void Choose(params string[] names)
            {
                listing.MarkAll(false, includeDirectories: true);
                for (int i = 0; i < listing.VisibleCount; i++)
                    if (names.Contains(listing.GetVisible(i).Name)) listing.SetMark(i, true);
            }
            // The checksum file's job waits (held as it is added), while the files' job ends.
            Job? held = null;
            void Hold(Job job)
            {
                if (job.Kind == JobKind.VerifyChecksums) (held = job).Pause();
            }
            services.Jobs.JobAdded += Hold;
            Choose("a.txt", "SHA256SUMS");
            vm.Execute(CommandIds.VerifyChecksums);
            await Until(() => held is not null, "the checksum file's job follows the files' job");
            var files = services.Jobs.Jobs.Single(j => j.Kind == JobKind.VerifyBeside);
            Assert.True(files.State.IsFinished());
            vm.ClearNotification();
            await vm.ShowVerificationResultsAsync(files);
            Assert.False(dialogs.IsOpen);
            Assert.Equal("The rest of this check is still running: its results open by themselves when it ends.", vm.Notification);
            held!.Resume();
            await Until(() => dialogs.IsOpen, "the results open once the rest has ended");
            await Until(() => Shows("a.txt is intact: it matches its checksum"), "a.txt is intact: it matches its checksum");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Until(() => !dialogs.IsOpen, "the results close");

            // Canceled before it read a file: it says so, and that the chosen checksum file was not checked at all.
            services.Jobs.JobAdded -= Hold;
            void Stop(Job job)
            {
                if (job.Kind == JobKind.VerifyBeside) job.Pause();
            }
            services.Jobs.JobAdded += Stop;
            int before = services.Jobs.Jobs.Count;
            Choose("b.txt", "SHA256SUMS");
            vm.Execute(CommandIds.VerifyChecksums);
            await Until(() => services.Jobs.Jobs.Count == before + 1
                && services.Jobs.Jobs.OrderBy(j => j.QueueOrder).Last() is { IsPaused: true, State: JobState.Queued or JobState.Paused },
                "the files' job waits before reading");
            services.Jobs.Jobs.OrderBy(j => j.QueueOrder).Last().Cancel();
            await Until(() => dialogs.IsOpen, "the canceled check's results open");
            await Until(() => Shows("Canceled before any file was read"), "Canceled before any file was read");
            await Until(() => Shows("SHA256SUMS was not checked: the check was canceled first."), "SHA256SUMS was not checked: the check was canceled first.");
            Assert.Equal(before + 1, services.Jobs.Jobs.Count);
            services.Jobs.JobAdded -= Stop;
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Until(() => !dialogs.IsOpen, "the results close");

            // Canceled while it still waited its turn (nothing ran, nothing was reported): the results say the same.
            static void CancelQueued(Job job)
            {
                if (job.Kind == JobKind.VerifyBeside) job.Cancel();
            }
            services.Jobs.JobAdded += CancelQueued;
            Choose("a.txt", "SHA256SUMS");
            vm.Execute(CommandIds.VerifyChecksums);
            await Until(() => dialogs.IsOpen, "the results of a check canceled before it ran open");
            await Until(() => Shows("Canceled before any file was read"), "Canceled before any file was read, again");
            await Until(() => Shows("SHA256SUMS was not checked: the check was canceled first."), "what it left unchecked");
            services.Jobs.JobAdded -= CancelQueued;
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Results_that_open_by_themselves_hold_off_Enter_and_Esc_a_moment()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files"); // a.txt ("alpha") and b.txt ("beta")
            File.WriteAllText(Path.Combine(folder, "SHA256SUMS"), Convert.ToHexStringLower(SHA256.HashData("alpha"u8)) + "  a.txt\n");
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 4); i++) await Task.Delay(20, ct);
            async Task Until(Func<bool> done, string what)
            {
                for (int i = 0; i < 500 && !done(); i++)
                {
                    window.CaptureRenderedFrame(); // what opened is laid out (a scrolled pane's content only then exists)
                    await Task.Delay(20, ct);
                }
                window.CaptureRenderedFrame();
                Assert.True(done(), what);
            }
            // Every check counts as one that took a while: its results open while keys may be meant for something else. The
            // moment they hold Enter and Esc off is long here, so the keys below fall in it however busy the machine is.
            vm.VerifyKeysGuardedAfter = TimeSpan.Zero;
            VerificationResultsDialog.KeyGuard = TimeSpan.FromSeconds(3);
            window.GetVisualDescendants().OfType<FileCat.App.Controls.FileListControl>().First(c => c.IsEffectivelyVisible).Focus();
            listing.SetFocus(Enumerable.Range(0, listing.VisibleCount).First(i => listing.GetVisible(i).Name == "a.txt"));
            vm.Execute(CommandIds.VerifyChecksums);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            var host = window.FindControl<Panel>("OverlayHost")!;
            Avalonia.Visual? Focused() => window.FocusManager?.GetFocusedElement() as Avalonia.Visual;
            await Until(() => dialogs.IsOpen && Focused() is { } f && host.IsVisualAncestorOf(f), "the results open with the keyboard");
            // The keyboard is on the verdict, read out as such; Enter and Esc do nothing yet.
            Assert.Equal("a.txt is intact: it matches its checksum", Avalonia.Automation.AutomationProperties.GetName((Control)Focused()!));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(dialogs.IsOpen);
            // A moment later the list has the keyboard, and Esc closes.
            await Until(() => Focused() is { } f && f.FindAncestorOfType<ListBox>(includeSelf: true) is not null, "the list takes the keyboard");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Until(() => !dialogs.IsOpen, "the results close");
        }
        finally
        {
            VerificationResultsDialog.KeyGuard = TimeSpan.FromMilliseconds(700);
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Results_that_open_by_themselves_wait_while_the_user_types()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files"); // a.txt ("alpha") and b.txt ("beta")
            File.WriteAllText(Path.Combine(folder, "SHA256SUMS"), Convert.ToHexStringLower(SHA256.HashData("alpha"u8)) + "  a.txt\n");
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 4); i++) await Task.Delay(20, ct);
            async Task Until(Func<bool> done, string what)
            {
                for (int i = 0; i < 500 && !done(); i++)
                {
                    window.CaptureRenderedFrame();
                    await Task.Delay(20, ct);
                }
                window.CaptureRenderedFrame();
                Assert.True(done(), what);
            }
            // A check that ends while the user types (a rename, a path): a dialog would take the keyboard and end what they type.
            vm.VerifyKeysGuardedAfter = TimeSpan.Zero;
            var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.IsEffectivelyEnabled);
            box.Focus();
            await Until(() => ReferenceEquals(window.FocusManager?.GetFocusedElement(), box), "a box has the keyboard");
            listing.SetFocus(Enumerable.Range(0, listing.VisibleCount).First(i => listing.GetVisible(i).Name == "a.txt"));
            vm.ClearNotification();
            vm.Execute(CommandIds.VerifyChecksums);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => vm.Notification?.StartsWith("The check has ended: its results open once you are done typing", StringComparison.Ordinal) == true,
                "it says the results wait");
            Assert.False(dialogs.IsOpen);
            Assert.True(ReferenceEquals(window.FocusManager?.GetFocusedElement(), box));
            // Done typing: the keyboard is back on the files, and the results open.
            window.GetVisualDescendants().OfType<FileCat.App.Controls.FileListControl>().First(c => c.IsEffectivelyVisible).Focus();
            await Until(() => dialogs.IsOpen, "the results open once the typing is done");
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_check_stopped_by_an_error_is_never_shown_as_all_good()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var service = VerificationService.Current!;
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files"); // a.txt ("alpha") and b.txt ("beta")
            File.WriteAllText(Path.Combine(folder, "SHA256SUMS"), Convert.ToHexStringLower(SHA256.HashData("alpha"u8)) + "  a.txt\n");
            vm.VerifyKeysGuardedAfter = TimeSpan.MaxValue;
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 4); i++) await Task.Delay(20, ct);
            async Task Until(Func<bool> done, string what)
            {
                for (int i = 0; i < 500 && !done(); i++)
                {
                    window.CaptureRenderedFrame(); // what opened is laid out (a scrolled pane's content only then exists)
                    await Task.Delay(20, ct);
                }
                window.CaptureRenderedFrame();
                Assert.True(done(), what);
            }
            // A defect that strikes the check itself (on a job's thread; the rows' checks go on).
            VerificationService.Current = new VerificationService(service.Cache, () => long.MaxValue,
                () => Thread.CurrentThread.Name?.StartsWith("FileCat job", StringComparison.Ordinal) == true ? throw new InvalidOperationException("a defect") : []);
            listing.SetFocus(Enumerable.Range(0, listing.VisibleCount).First(i => listing.GetVisible(i).Name == "a.txt"));
            vm.Execute(CommandIds.VerifyChecksums);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen, "the results open");
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal((JobState.Failed, false), (job.State, job.VerificationReport!.IsComplete));
            bool Shows(string text) => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text);
            await Until(() => Shows("The check stopped on an error before it reached every file"), "The check stopped on an error before it reached every file");
            await Until(() => Shows("It stopped on: a defect"), "It stopped on: a defect");
            Assert.DoesNotContain("all good", job.Summary, StringComparison.Ordinal);
        }
        finally
        {
            VerificationService.Current = service;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_folder_without_checksum_files_asks_nothing_and_the_command_says_what_it_needs()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 3); i++) await Task.Delay(20, ct);
            Assert.False(tab.HasSidecars);
            listing.SetFocus(1);
            Assert.True(listing.TryGetFocused(out var a));
            Assert.Null(tab.Verification(a, listing.FocusedStoreIndex));
            Assert.Equal("", tab.GetMetadataText(a, listing.FocusedStoreIndex, "verified", out _));

            vm.Execute(CommandIds.VerifyChecksums);
            for (int i = 0; i < 250 && vm.Notification is null; i++) await Task.Delay(20, ct);
            Assert.StartsWith("\"a.txt\" is not a checksum file, and no checksum file or signature beside it names it", vm.Notification, StringComparison.Ordinal);
            Assert.Empty(services.Jobs.Jobs);

            // A signature chosen whose file is not here is no list of checksums to try: its result says what is missing.
            vm.VerifyKeysGuardedAfter = TimeSpan.MaxValue;
            File.WriteAllText(Path.Combine(root, "files", "gone.iso.asc"), "-----BEGIN PGP SIGNATURE-----\n");
            tab.Refresh();
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 4); i++) await Task.Delay(20, ct);
            listing.SetFocus(Enumerable.Range(0, listing.VisibleCount).First(i => listing.GetVisible(i).Name == "gone.iso.asc"));
            vm.Execute(CommandIds.VerifyChecksums);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen, "the results open");
            window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "gone.iso.asc: signs a file that is not here");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);

            // A checksum file whose one line is no checksum: nothing was checked, and the line named no file.
            File.WriteAllText(Path.Combine(root, "files", "notes.md5"), "see the website\n");
            tab.Refresh();
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 5); i++) await Task.Delay(20, ct);
            listing.SetFocus(Enumerable.Range(0, listing.VisibleCount).First(i => listing.GetVisible(i).Name == "notes.md5"));
            vm.Execute(CommandIds.VerifyChecksums);
            for (int i = 0; i < 250 && !(dialogs.IsOpen && window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Nothing was checked", StringComparison.Ordinal) == true)); i++)
            {
                window.CaptureRenderedFrame();
                await Task.Delay(20, ct);
            }
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Nothing was checked: the one line read names no file to check");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
