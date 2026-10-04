using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>V13: saved criteria survive a disk reload, and duplicates within results keep the original scope.</summary>
public sealed class FindRemainingScopeTests
{
    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), "The controlled Find state did not complete.");
    }

    [AvaloniaFact]
    public Task Saved_literal_search_reloads_case_words_masks_and_size_limits() => SavedSearch("literal");

    [AvaloniaFact]
    public Task Saved_regex_search_reloads_the_expression_and_word_boundaries() => SavedSearch("regex");

    [AvaloniaFact]
    public Task Saved_hex_search_reloads_exact_bytes_and_exclusions() => SavedSearch("hex");

    [AvaloniaFact]
    public Task Saved_unicode_search_reloads_UTF8_and_both_UTF16_orders() => SavedSearch("unicode");

    private static async Task SavedSearch(string mode)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string files = Path.Combine(root, "files");
            byte[] positive;
            string[] expected;
            var criteria = new SearchCriteria
            {
                Names = mode + "*.bin|*skip*",
                LookIn = files,
                Subfolders = false,
                IncludeHidden = false,
                InsideArchives = true,
                MatchCase = true,
                Advanced = new AdvancedSearchCriteria
                {
                    AttributesClear = FileAttributes.Directory,
                    SizeAtLeast = 4,
                    SizeAtLeastUnit = SizeUnit.Bytes,
                    SizeAtMost = 64,
                    SizeAtMostUnit = SizeUnit.Bytes,
                },
            };
            switch (mode)
            {
                case "literal":
                    criteria.Text = "Needle";
                    criteria.WholeWords = true;
                    positive = Encoding.UTF8.GetBytes("prefix Needle suffix");
                    File.WriteAllText(Path.Combine(files, "literal-case.bin"), "prefix needle suffix");
                    File.WriteAllText(Path.Combine(files, "literal-word.bin"), "prefix xNeedlex suffix");
                    expected = ["literal-hit.bin"];
                    break;
                case "regex":
                    criteria.Text = "needle_[0-9]{2}";
                    criteria.Regex = true;
                    criteria.WholeWords = true;
                    positive = Encoding.UTF8.GetBytes("prefix needle_12 suffix");
                    File.WriteAllText(Path.Combine(files, "regex-word.bin"), "prefix needle_12x suffix");
                    File.WriteAllText(Path.Combine(files, "regex-negative.bin"), "prefix needle_AB suffix");
                    expected = ["regex-hit.bin"];
                    break;
                case "hex":
                    criteria.Text = "4D 5A";
                    criteria.Hex = true;
                    positive = [0, 0x4d, 0x5a, 0];
                    File.WriteAllBytes(Path.Combine(files, "hex-negative.bin"), [0, 0x4d, 0x50, 0x5a, 0]);
                    File.WriteAllBytes(Path.Combine(files, "hex-small.bin"), [0x4d, 0x5a]);
                    expected = ["hex-hit.bin"];
                    break;
                default:
                    Assert.Equal("unicode", mode);
                    criteria.Text = "Český";
                    criteria.Unicode = true;
                    positive = Encoding.UTF8.GetBytes("prefix Český suffix");
                    // Odd offsets and no BOM: the option also searches text embedded in binary files.
                    File.WriteAllBytes(Path.Combine(files, "unicode-le.bin"), new byte[] { 0xff }.Concat(Encoding.Unicode.GetBytes("Český")).Concat(new byte[] { 0 }).ToArray());
                    File.WriteAllBytes(Path.Combine(files, "unicode-be.bin"), new byte[] { 0xff }.Concat(Encoding.BigEndianUnicode.GetBytes("Český")).Concat(new byte[] { 0 }).ToArray());
                    File.WriteAllText(Path.Combine(files, "unicode-negative.bin"), "Cesky");
                    expected = ["unicode-be.bin", "unicode-hit.bin", "unicode-le.bin"];
                    break;
            }
            File.WriteAllBytes(Path.Combine(files, mode + "-hit.bin"), positive);
            File.WriteAllBytes(Path.Combine(files, mode + "-skip.bin"), positive);
            File.WriteAllBytes(Path.Combine(files, mode + "-large.bin"), positive.Concat(new byte[128]).ToArray());
            string sub = Directory.CreateDirectory(Path.Combine(files, "sub")).FullName;
            File.WriteAllBytes(Path.Combine(sub, mode + "-nested.bin"), positive);
            Directory.CreateDirectory(Path.Combine(files, mode + "-folder.bin"));
            services.Settings.SearchLogOnErrors = false;

            var find = FindWindow.Open(vm, files, null);
            find.Apply(criteria);
            Assert.Equal(JsonSerializer.Serialize(criteria), JsonSerializer.Serialize(find.Criteria));
            find.OpenSaveSearch();
            await WaitFor(() => find.Dialogs.IsOpen);
            var name = find.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b)?.StartsWith("Name for these criteria", StringComparison.Ordinal) == true);
            await WaitFor(() => name.IsFocused);
            name.Text = "Owned " + mode;
            find.GetVisualDescendants().OfType<CheckBox>().Single(b => b.Content as string == "Load it whenever Find opens").IsChecked = true;
            find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitFor(() => !find.Dialogs.IsOpen && services.Settings.SavedSearches.Count == 1 && File.Exists(services.Paths.SettingsFile));
            find.Close();
            await WaitFor(() => FindWindow.OpenWindows.Count == 0);

            // Reload the actual persisted settings, discarding the in-memory saved searches before reopening Find.
            var loaded = JsonFileStore.Load(services.Paths.SettingsFile, StateJsonContext.Default.AppSettings,
                AppSettings.CurrentSchema, () => new AppSettings(), out var status);
            Assert.Equal(StateLoadStatus.Loaded, status);
            var saved = Assert.Single(loaded.SavedSearches);
            Assert.Equal("Owned " + mode, saved.Name);
            Assert.True(saved.LoadOnOpen);
            Assert.Equal(JsonSerializer.Serialize(criteria), JsonSerializer.Serialize(saved.Criteria));
            services.Settings.SavedSearches.Clear();
            services.Settings.SavedSearches.AddRange(loaded.SavedSearches);
            var reopened = FindWindow.Open(vm, root, null);
            Assert.Equal(JsonSerializer.Serialize(criteria), JsonSerializer.Serialize(reopened.Criteria));
            reopened.StartSearch(RefineMode.Replace);
            await WaitFor(() => reopened.Session is not null && reopened.IsIdle);
            Assert.Empty(reopened.Error);
            Assert.Equal(expected, reopened.Found.Order(StringComparer.Ordinal));
            Assert.Empty(reopened.Session!.Log);
            Assert.True(services.ResultSets.Get(reopened.ResultsTab!.Location!)!.IsComplete);
        }
        finally
        {
            foreach (var find in FindWindow.OpenWindows.ToList()) find.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Duplicates_within_results_never_add_excluded_files_and_keep_original_relative_paths()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string files = Path.Combine(root, "files");
            var corpus = new Dictionary<string, byte[]>
            {
                ["a.txt"] = Encoding.UTF8.GetBytes("alpha"),
                ["a-copy.txt"] = Encoding.UTF8.GetBytes("alpha"),
                ["g.txt"] = Encoding.UTF8.GetBytes("gamma"),
                ["g-copy.txt"] = Encoding.UTF8.GetBytes("gamma"),
                ["collision-ab.txt"] = Encoding.UTF8.GetBytes("ab"),
                ["collision-ba.txt"] = Encoding.UTF8.GetBytes("ba"),
                ["excluded.txt"] = Encoding.UTF8.GetBytes("alpha"),
            };
            foreach (var (name, bytes) in corpus) File.WriteAllBytes(Path.Combine(files, name), bytes);
            var original = services.ResultSets.Create("Owned subset", "Six independently selected original files");
            original.AddRange(corpus.Keys.Where(n => n != "excluded.txt").Select(n =>
                (ItemRef.ForFileSystemPath(Path.Combine(files, n), EntryKind.File), "original/" + n)));
            original.IsComplete = true;
            var before = original.Snapshot();
            var find = FindWindow.Open(vm, files, original);
            find.Apply(new SearchCriteria { Names = "*.txt" });
            find.OpenDuplicates();
            await WaitFor(() => find.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Content as string == "Same content" && c.IsFocused));
            find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitFor(() => find.Groups.Count == 2 && find.ResultsTab!.Listing.VisibleCount == 4);
            Assert.Equal(new[] { "a-copy.txt,a.txt", "g-copy.txt,g.txt" },
                find.Groups.Select(g => string.Join(',', g.Select(i => i.Name).Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal));
            Assert.Equal(before, original.Snapshot());
            var result = services.ResultSets.Get(find.ResultsTab!.Location!)!;
            Assert.True(result.IsComplete);
            Assert.True(result.ShowsGroups);
            Assert.Equal(4, result.Count);
            Assert.Contains("duplicates by content", result.Provenance, StringComparison.Ordinal);
            foreach (var (item, relative) in result.Snapshot())
            {
                Assert.Equal(before.Single(s => s.Item.Equals(item)).Relative, relative);
                Assert.NotNull(result.NoteOf(item));
                Assert.Equal(Path.Combine(files, item.Name), item.FileSystemPath);
            }
            find.SelectDuplicateCopies();
            var selected = find.ResultsTab.Listing.GetSelection();
            Assert.Equal(2, selected.Count);
            foreach (var group in find.Groups) Assert.Single(selected, group.Contains);
            Assert.Contains("2 extra copies", find.Message, StringComparison.Ordinal);
            // Grouping and marking preserve all originals, including the deliberately excluded matching file.
            foreach (var (name, bytes) in corpus) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(files, name)));
        }
        finally
        {
            foreach (var find in FindWindow.OpenWindows.ToList()) find.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }
}
