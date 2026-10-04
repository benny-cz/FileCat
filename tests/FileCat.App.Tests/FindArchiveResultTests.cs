using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.App.Tests;

public sealed class FindArchiveResultTests
{
    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), "The controlled Find state did not complete.");
    }

    private static string WriteArchive(string root)
    {
        string path = Path.Combine(root, "find-members.zip");
        using var archive = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        foreach (var (name, size) in new (string, int)[] { ("keep.txt", 10), ("dir/skip.bin", 7), ("dir/dup.txt", 1), ("dir/dup.txt", 12) })
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(Enumerable.Repeat((byte)'x', size).ToArray());
        }
        return path;
    }

    [AvaloniaFact]
    public async Task Find_within_archive_results_works_with_inside_archives_off_and_keeps_content_and_relative_references()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        string? archive = null;
        try
        {
            services.Settings.SearchLogOnErrors = false;
            archive = WriteArchive(root);
            var members = new ProviderArchiveMembers(services.Providers).List(archive, TestContext.Current.CancellationToken).Where(i => !i.IsContainer).ToList();
            var original = services.ResultSets.Create("Original archive members", "owned ZIP fixture");
            original.AddRange(members.Select(i => (i, "original archive relative path")));
            var find = FindWindow.Open(vm, Path.Combine(root, "files"), original);
            find.Apply(new SearchCriteria { Names = "*.txt", InsideArchives = false,
                Advanced = new AdvancedSearchCriteria { SizeAtLeast = 8, SizeAtLeastUnit = SizeUnit.Bytes } });
            find.StartSearch(RefineMode.Replace);
            await WaitFor(() => find.IsIdle);
            Assert.Equal(["dup.txt", "keep.txt"], find.Found.Order());
            Assert.Equal(4, original.Count);
            var result = services.ResultSets.Get(find.ResultsTab!.Location!)!;
            Assert.Equal(2, result.Count);
            foreach (var (item, relative) in result.Snapshot())
            {
                Assert.Contains(item, members);
                Assert.Equal("original archive relative path", relative);
                using var content = services.ResultSets.OpenContent(item);
                Assert.NotNull(content);
                var bytes = new byte[item.Size];
                Assert.Equal(bytes.Length, content.Read(0, bytes));
                Assert.All(bytes, b => Assert.Equal((byte)'x', b));
            }
            Assert.Equal(1, result.Snapshot().Single(r => r.Item.Name == "dup.txt").Item.Ordinal);
            Assert.Contains("warnings (see the log)", find.Status, StringComparison.Ordinal);
            Assert.DoesNotContain("not searched", find.Status, StringComparison.Ordinal);
            find.OpenLog();
            await WaitFor(() => find.Dialogs.IsOpen);
            await WaitFor(() => find.GetVisualDescendants().OfType<TextBlock>().Any(b => b.Text?.Contains("Search warning", StringComparison.Ordinal) == true));
            find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitFor(() => !find.Dialogs.IsOpen);
            Assert.Equal(find.Session!.Log[0].Parent, vm.Workspace.ActiveTab!.Location);
        }
        finally
        {
            foreach (var find in FindWindow.OpenWindows.ToList()) find.Close();
            if (archive is not null) services.Zip.Release(archive);
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Content_exclusions_are_visible_and_the_log_opens_the_original_archive_parent()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        string? archive = null;
        try
        {
            services.Settings.SearchLogOnErrors = false;
            archive = WriteArchive(root);
            var members = new ProviderArchiveMembers(services.Providers).List(archive, TestContext.Current.CancellationToken).Where(i => !i.IsContainer).ToList();
            var original = services.ResultSets.Create("Original archive members", "owned ZIP fixture");
            original.AddRange(members.Select(i => (i, "original archive relative path")));
            var find = FindWindow.Open(vm, Path.Combine(root, "files"), original);
            find.Apply(new SearchCriteria { Names = "*.txt", Text = "x", InsideArchives = false });
            find.StartSearch(RefineMode.Replace);
            await WaitFor(() => find.IsIdle);
            Assert.Empty(find.Found);
            Assert.Equal(4, original.Count);
            Assert.Equal(4, find.Session!.Log.Count);
            Assert.All(find.Session.Log, e => Assert.Equal(SearchLogKind.Inaccessible, e.Kind));
            find.OpenLog();
            await WaitFor(() => find.Dialogs.IsOpen);
            await WaitFor(() => find.GetVisualDescendants().OfType<TextBlock>().Any(b => b.Text?.Contains("Archive member contents are not searched", StringComparison.Ordinal) == true));
            find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitFor(() => !find.Dialogs.IsOpen);
            Assert.Equal(members[0].Parent, vm.Workspace.ActiveTab!.Location);
        }
        finally
        {
            foreach (var find in FindWindow.OpenWindows.ToList()) find.Close();
            if (archive is not null) services.Zip.Release(archive);
            AccessibilityTests.Close(services, window, root);
        }
    }
}
