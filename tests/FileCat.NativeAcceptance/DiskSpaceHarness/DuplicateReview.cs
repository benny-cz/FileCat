using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.Core.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private object? _duplicateReviewProbe;
    private string? _extraDataPath, _extraDataDigest;

    private void CreateDuplicateProbeFiles()
    {
        const string original = "duplicate-review-primary.bin";
        Add(original, 4);
        Add("duplicate-review-unique.bin", 1);
        Add("duplicate-review-same-size.bin", 4);
        if (Program.CleanupReviewProbe)
        {
            string preview = Path.Combine(_fixture, "primary-preview.txt");
            File.WriteAllText(preview, "Read from the selected live file.");
            _originals.Add(preview, Hash(preview));
        }
        string source = Path.Combine(_fixture, original), alias = Path.Combine(_fixture, "duplicate-review-alias.bin");
        if (!(OperatingSystem.IsWindows() ? HardLink(alias, source, 0) : UnixLink(source, alias) == 0))
            throw new IOException("The native duplicate-review fixture needs an actual hard link.");
        _originals.Add(alias, _originals[source]);
        string? suffix = Program.DetailedStreamProbe && OperatingSystem.IsWindows() ? ":validation:$DATA" :
            Program.DetailedForkProbe && OperatingSystem.IsMacOS() ? "/..namedfork/rsrc" : null;
        if (suffix is not null)
        {
            _extraDataPath = Path.Combine(_fixture, "Archives", "archive.zip") + suffix;
            File.WriteAllBytes(_extraDataPath, Enumerable.Repeat((byte)5, 72 * 1024 + 5).ToArray());
            _extraDataDigest = Hash(_extraDataPath);
        }
    }

    private async Task DuplicateReviewProbeAsync()
    {
        const string filter = "name:duplicate-review-";
        try
        {
            var window = _window!;
            if (OperatingSystem.IsWindows()) WindowsGuestInput.DesktopGuard();
            Click("Start analysis");
            await ReadyAsync(() => Button("Open recorded result")?.IsEnabled == true);
            Click("Open recorded result");
            await ReadyAsync(() => window.SessionCount == 2 && Button("Apply filter")?.IsEnabled == true);

            Control<TextBox>("Filter indexed observations")!.Text = filter;
            Click("Apply filter");
            await ReadyAsync(() => Matches(4) &&
                Button("Mark matching files in this scope")?.IsEnabled == true);
            Click("Mark matching files in this scope");
            await ReadyAsync(() => Marks(4, 0));
            Click("Add to review basket");
            await ReadyAsync(() => Marks(4, 4));
            Control<TextBox>("Filter indexed observations")!.Text = "name:duplicate-review-unique.bin";
            Click("Apply filter");
            await ReadyAsync(() => Matches(1) && Marks(4, 4));
            Click("Review duplicate candidates…");
            await ReadyAsync(() => Control<ListBox>("Captured duplicate candidates and exclusions")?.ItemCount == 4);

            var rows = Control<ListBox>("Captured duplicate candidates and exclusions")!.Items.Cast<string>().ToArray();
            var close = Button("Close review")!;
            bool preservingDefault = close.IsDefault && close.IsCancel;
            bool correct = rows.Count(row => row.Contains("Same-size candidate", StringComparison.Ordinal)) == 2 &&
                rows.Count(row => row.Contains("Additional name for the same recorded file", StringComparison.Ordinal)) == 1 &&
                rows.Count(row => row.Contains("No separate marked object with this size", StringComparison.Ordinal)) == 1 &&
                window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
                    text.Text?.Contains("8 B for 2 distinct candidate objects", StringComparison.Ordinal) == true);
            if (!correct || !preservingDefault) throw new IOException("Duplicate review changed its exact cost, alias treatment or preserving default.");
            RenderNativeFrame("duplicate-review-estimate.png");
            int contentGroups = 0;
            bool contentAvailable = Button("Review content reads…")?.IsEnabled == true;
            if (OperatingSystem.IsWindows())
            {
                if (!contentAvailable) throw new IOException("The qualified NTFS content review is unavailable.");
                Click("Review content reads…");
                await ReadyAsync(() => Control<ListBox>("Eligible primary-content reads")?.ItemCount == 2 &&
                    Button("Read selected contents")?.IsEnabled == true);
                if (Control<TextBlock>("Approved primary-content read scope")?.Text?.Contains("Read 8 B from 2 files", StringComparison.Ordinal) != true ||
                    Button("Close read review") is not { IsDefault: true, IsCancel: true } || Button("Read selected contents")!.IsDefault)
                    throw new IOException("The native consent scope or preserving default differs.");
                RenderNativeFrame("duplicate-review-consent.png");
                Click("Read selected contents");
                await ReadyAsync(() => Control<ListBox>("Primary-content evidence rows")?.ItemCount == 2);
                var result = Control<ListBox>("Primary-content evidence rows")!;
                var matches = result.Items.Select(item => item?.ToString() ?? "").ToArray();
                if (!matches.All(label => label.StartsWith("Primary match 1", StringComparison.Ordinal)) ||
                    matches.Count(label => label.Contains("Proposed keeper", StringComparison.Ordinal)) != 1)
                    throw new IOException("The native content review did not return the single approved match group.");
                result.SelectedIndex = 0;
                await ReadyAsync(() => Control<TextBox>("Primary-content evidence details")?.Text?.Contains("SHA-256 ", StringComparison.Ordinal) == true);
                RenderNativeFrame("duplicate-review-content.png");
                contentGroups = 1;
                Click("Close content results");
            }
            else
            {
                if (contentAvailable) throw new IOException("An unqualified platform exposed a content-read command.");
                Click("Close review");
            }
            await ReadyAsync(() => Control<ListBox>("Captured duplicate candidates and exclusions") is null && Marks(4, 4));
            await OpenInsightAsync("Folders with many small files");
            await ReadyAsync(() => Control<ListBox>("Captured recipe candidates") is { ItemCount: > 0 } small &&
                small.Items.Any(item => item?.ToString()?.Contains("500 small file paths", StringComparison.Ordinal) == true));
            await RecipeFrameAsync("recorded-insights-small-files.png", false, _indexedLifetime.Token);
            Click("Close preview");
            await ReadyAsync(() => Control<ListBox>("Captured recipe candidates") is null);

            await OpenInsightAsync("Logical and allocated size differences");
            await ReadyAsync(() => Control<ListBox>("Captured recipe candidates") is { ItemCount: > 0 } &&
                HasText("ranked by absolute difference"));
            Click("Close preview");
            await ReadyAsync(() => Control<ListBox>("Captured recipe candidates") is null);

            await OpenInsightAsync("Large extra data");
            bool namedAllocationAvailable = Program.DetailedStreamProbe && OperatingSystem.IsWindows();
            bool resourceAllocationAvailable = Program.DetailedForkProbe && OperatingSystem.IsMacOS();
            if (namedAllocationAvailable || resourceAllocationAvailable)
            {
                await ReadyAsync(() => Control<ListBox>("Captured recipe candidates")?.ItemCount == 1);
                var extraRow = Control<ListBox>("Captured recipe candidates")!.Items[0]?.ToString() ?? "";
                string evidence = resourceAllocationAvailable ? "Reported APFS resource fork allocation. Included in the reported file total." :
                    "Reported NTFS named data stream allocation";
                if (!extraRow.StartsWith("archive.zip\n", StringComparison.Ordinal) ||
                    !extraRow.Contains(evidence, StringComparison.Ordinal) ||
                    Button("Add included to review basket") is not null)
                    throw new IOException("The native extra-data preview did not separate the named allocation from the main file.");
            }
            else
            {
                await ReadyAsync(() => HasText("Extra-data size is unavailable"));
                if (Control<ListBox>("Captured recipe candidates") is not null)
                    throw new IOException("The unavailable extra-data preview displayed a measured candidate.");
            }
            if (Program.DetailedStreamProbe)
                await RecipeFrameAsync(Program.DetailedForkProbe ? "apfs-resource-fork-allocation.png" : "ntfs-stream-allocation.png",
                    true, _indexedLifetime.Token);
            Click("Close preview");
            await ReadyAsync(() => Button("Close preview") is null && Marks(4, 4));

            Control<ComboBox>("Indexed metric")!.SelectedIndex = (int)AnalysisMetric.Files;
            await ReadyAsync(() => HasText("Files: ") && Matches(1) && Marks(4, 4));
            await OpenInsightAsync("Largest folders");
            await ReadyAsync(() => Control<ListBox>("Captured recipe candidates") is { ItemCount: > 2 } &&
                HasText("by recorded file count"));
            var folders = Control<ListBox>("Captured recipe candidates")!;
            if (folders.Items[1] is not string ranked || !ranked.StartsWith("Small files\n", StringComparison.Ordinal) ||
                !ranked.Contains("500 file paths", StringComparison.Ordinal) || Button("Close preview") is not { IsDefault: true, IsCancel: true })
                throw new IOException("The native folder ranking did not use the selected measure across the captured location.");
            folders.SelectedIndex = 1;
            Click("Inspect recorded candidate");
            await ReadyAsync(() => Control<TextBlock>("Recorded candidate evidence")?.Text?.Contains("/Small files", StringComparison.Ordinal) == true);
            await RecipeFrameAsync("recorded-insights-largest-folders.png", false, _indexedLifetime.Token);
            Click("Close preview");
            await ReadyAsync(() => Button("Close preview") is null && Marks(4, 4));

            await OpenInsightAsync("Downloads and installers");
            await ReadyAsync(() => Control<ListBox>("Captured recipe candidates")?.ItemCount == 1);
            var downloads = Control<ListBox>("Captured recipe candidates")!;
            if (downloads.Items[0] is not string archive || !archive.StartsWith("archive.zip\n", StringComparison.Ordinal) ||
                Button("Add included to review basket")?.IsEnabled != false)
                throw new IOException("Recipe staging did not start with the candidate excluded.");
            downloads.SelectedIndex = 0;
            Click("Include selected candidate");
            Click("Exclude selected candidate");
            if (Button("Add included to review basket")?.IsEnabled != false)
                throw new IOException("Excluding the only candidate did not disable the staging command.");
            Click("Include selected candidate");
            Click("Inspect recorded candidate");
            await ReadyAsync(() => Control<TextBlock>("Recorded candidate evidence")?.Text?.Contains("Archives/archive.zip", StringComparison.Ordinal) == true);
            if (Button("Close preview") is not { IsDefault: true, IsCancel: true })
                throw new IOException("Recipe staging changed the preserving default.");
            await RecipeFrameAsync("recipe-staging-included.png", !Program.DetailedStreamProbe && !Program.CleanupReviewProbe, _indexedLifetime.Token);
            Click("Add included to review basket");
            await ReadyAsync(() => Button("Close preview") is null && Marks(4, 5) && HasText("Added 1 captured reference"));
            Click("Review captured basket");
            await ReadyAsync(() => Control<ListBox>("Captured basket entries")?.ItemCount == 5);
            var basket = Control<ListBox>("Captured basket entries")!;
            basket.SelectedItem = basket.Items.Cast<object>().Single(row => row.ToString()!.Contains("archive.zip", StringComparison.Ordinal));
            Click("Remove selected from basket");
            Click("Keep files / Close review");
            await ReadyAsync(() => Control<ListBox>("Captured basket entries") is null && Marks(4, 4));
            if (Program.CleanupReviewProbe)
            {
                await CheckCleanupReviewAsync();
                await CheckPrimaryPreviewAsync();
            }
            if (_extraDataPath is { } extraPath && Hash(extraPath) != _extraDataDigest)
                throw new IOException("Extra data changed during the preview and review.");

            _duplicateReviewProbe = new
            {
                finished = true, passed = true, sessions = window.SessionCount, candidateObjects = 2, knownReadBytes = 8,
                capturedMarks = 4, additionalHardLinkNames = 1, preservingDefault, retainedHiddenMarksAndBasket = true,
                contentAvailable, contentGroups, recordedInsightsPassed = true, largestFoldersPassed = true, recipeStagingPassed = true,
                detailedStreamProbe = Program.DetailedStreamProbe, namedAllocationAvailable, namedStreamBytesPreserved = true,
                resourceAllocationAvailable, resourceForkBytesPreserved = true, cleanupReviewPassed = Program.CleanupReviewProbe,
                primaryPreviewPassed = Program.CleanupReviewProbe
            };
        }
        catch (Exception error)
        {
            _duplicateReviewProbe = new { finished = true, passed = false, error = error.ToString() };
            try { RenderNativeFrame("duplicate-review-failure.png"); }
            catch { /* Preserve the original failure if rendering also fails. */ }
        }
        WriteReport("duplicate-review-complete");

        T? Control<T>(string name) where T : Avalonia.Controls.Control => _window!.GetVisualDescendants().OfType<T>()
            .SingleOrDefault(control => control.IsEffectivelyVisible &&
                (AutomationProperties.GetName(control) == name || AutomationProperties.GetAutomationId(control) == name));
        Button? Button(string name) => _window!.GetVisualDescendants().OfType<Button>()
            .SingleOrDefault(button => button.IsEffectivelyVisible && Equals(button.Content, name));
        bool Matches(int count) => _window!.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
            AutomationProperties.GetAutomationId(text) == "Indexed analysis status" &&
            text.Text?.Contains($" {count:N0} definite matches;", StringComparison.Ordinal) == true);
        bool Marks(int marked, int basket) => _window!.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
            AutomationProperties.GetAutomationId(text) == "Indexed captured selection" &&
            text.Text?.StartsWith($"{marked:N0} marked · {basket:N0} in review basket", StringComparison.Ordinal) == true);
        bool HasText(string value) => _window!.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
            text.Text?.Contains(value, StringComparison.Ordinal) == true);
        async Task CheckCleanupReviewAsync()
        {
            Click("Review cleanup");
            await ReadyAsync(() => Control<ListBox>("Cleanup review entries")?.ItemCount == 4 && HasText("3 hidden by filters"));
            await ReadyAsync(() => HasText("13 bytes across 4 measured file paths") && HasText("3 distinct verified objects measured"));
            if (Button("Cancel / Keep files") is not { IsDefault: true, IsCancel: true } ||
                Button("Recycle included entries") is not { IsEnabled: false, IsDefault: false } ||
                !HasText("Marked entries · 4 paths · 0 included · 4 kept"))
                throw new IOException("The native cleanup review changed its membership or preserving default.");
            Control<ComboBox>("Cleanup action")!.SelectedIndex = 1;
            if (Button("Permanently delete included entries") is not { IsEnabled: false, IsDefault: false })
                throw new IOException("The exact-entry removal capability is unavailable and must be disabled.");
            var review = Control<ListBox>("Cleanup review entries")!;
            review.SelectedItem = review.Items.Single(row => row?.ToString()?.Contains("duplicate-review-unique.bin", StringComparison.Ordinal) == true);
            Click("Inspect recorded entry");
            await ReadyAsync(() => Control<TextBlock>("Recorded cleanup entry details")?.Text?.Contains("Recorded file size · 1 byte", StringComparison.Ordinal) == true &&
                HasText("Reported allocation"));
            await RecipeFrameAsync("cleanup-review-preserving.png", true, _indexedLifetime.Token);
            Click("Cancel / Keep files");
            await ReadyAsync(() => Control<ListBox>("Cleanup review entries") is null && Marks(4, 4));
            Click("Clear marks");
            await ReadyAsync(() => Marks(0, 4));
            Click("Review basket actions…");
            await ReadyAsync(() => Control<ListBox>("Cleanup review entries")?.ItemCount == 4 && HasText("Review basket · 4 paths"));
            Click("Cancel / Keep files");
            await ReadyAsync(() => Control<ListBox>("Cleanup review entries") is null && Marks(0, 4));
            if (_originals.Any(file => !File.Exists(file.Key) || Hash(file.Key) != file.Value))
                throw new IOException("Cleanup review changed an original file.");
        }
        async Task CheckPrimaryPreviewAsync()
        {
            var table = Control<ListBox>("Indexed analysis entries")!;
            var oldRows = table.ItemsSource;
            Control<TextBox>("Filter indexed observations")!.Text = "name:primary-preview.txt";
            Click("Apply filter");
            await ReadyAsync(() => !ReferenceEquals(table.ItemsSource, oldRows) && Matches(1));
            table.SelectedItem = table.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Kind == AnalysisKind.Directory);
            Click("Explore selected folder");
            await ReadyAsync(() => table.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "primary-preview.txt"));
            table.SelectedItem = table.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Name == "primary-preview.txt");
            await ReadyAsync(() => HasText("Captured metadata ready for primary-preview.txt"));
            if (!OperatingSystem.IsWindows())
            {
                if (Button("Preview file") is not { IsEnabled: false } unavailable ||
                    !AutomationProperties.GetHelpText(unavailable)!.Contains("qualified live file binding", StringComparison.Ordinal))
                    throw new IOException("An unsupported native source exposed live file preview.");
                return;
            }
            Click("Preview file");
            await ReadyAsync(() => Control<TextBox>("Live primary file preview")?.Text == "Read from the selected live file.");
            if (Button("Close preview") is not { IsDefault: true, IsCancel: true })
                throw new IOException("The native file preview changed its preserving default.");
            RenderNativeFrame("primary-preview.png");
            Click("Close preview");
            await ReadyAsync(() => Control<TextBox>("Live primary file preview") is null);
        }
        async Task OpenInsightAsync(string title)
        {
            Click("Candidate recipes…");
            await ReadyAsync(() => Control<ListBox>("Locations for candidate preview") is not null);
            Control<ComboBox>("Candidate recipe")!.SelectedItem = title;
            Control<ListBox>("Locations for candidate preview")!.SelectedIndex = 0;
            await ReadyAsync(() => Button("View candidates")?.IsEnabled == true);
            Click("View candidates");
        }
        void Click(string name)
        {
            var button = Button(name);
            if (button?.IsEnabled != true) throw new InvalidOperationException($"Native command unavailable: {name}");
            button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }
    }
}
