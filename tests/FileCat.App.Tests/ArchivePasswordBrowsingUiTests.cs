using Avalonia.Headless.XUnit;
using FileCat.Archives;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Tests;
using Fixture = FileCat.App.Tests.ArchiveCreationWorkflowTests.Fixture;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

// Actual encrypted files and actual readers. This lane does not substitute a trusted synthetic listing.
public sealed class ArchivePasswordBrowsingUiTests
{
    public static TheoryData<bool, bool> Profiles => new() { { false, false }, { false, true }, { true, false }, { true, true } };
    public static IEnumerable<object[]> NestedProfiles()
    {
        foreach (bool rar in new[] { false, true }) foreach (bool hidden in new[] { false, true }) foreach (bool zip in new[] { false, true })
            yield return [rar, hidden, zip];
    }

    [AvaloniaTheory, MemberData(nameof(Profiles))]
    public async Task Browsing_password_retries_reads_real_bytes_reuses_tabs_and_forgets_on_last_close(bool rar, bool hidden)
    {
        using var f = await Fixture.Open(nativeWindowsOriginMarks: true);
        string path = Path.Combine(f.Root, rar ? "protected.rar" : "protected.7z");
        byte[] expected = ArchivePasswordReadFixture.Create(path, rar, hidden);
        var root = ArchiveProvider.ForFile(path); var first = f.Vm.ActiveTab!;
        first.Navigate(root);
        await Wait(() => f.Button("Unlock") is not null);
        var wrong = f.Input("Existing archive password"); Assert.Equal('•', wrong.PasswordChar); wrong.Text = "PUBLIC-WRONG"; f.Click("Unlock");
        await Wait(() => f.Button("Unlock") is not null && f.FindInput("Existing archive password") is { } next && !ReferenceEquals(wrong, next));
        Assert.Equal("", wrong.Text); Assert.Contains("Try again", f.Text());
        var correct = f.Input("Existing archive password"); correct.Text = ArchivePasswordReadFixture.Password; f.Click("Unlock");
        await Wait(() => first.Location == root && first.Listing.State == ListingState.Complete);
        Assert.Equal("", correct.Text);
        var second = first.Panel.OpenTab(root.WithPath("folder"));
        await Wait(() => second.Location == root.WithPath("folder") && second.Listing.State == ListingState.Complete);
        Assert.Null(f.Button("Unlock"));
        var entry = Enumerable.Range(0, second.Listing.Store.Count).Select(i => second.Listing.Store[i]).Single(entry => entry.Name == "private-界.bin");
        Assert.True(entry.Has(EntryFlags.Protected));
        using (var content = f.Services.Archives.OpenContent(f.Services.Archives.GetItemRef(second.Location!, entry)))
        {
            Assert.NotNull(content); byte[] actual = new byte[expected.Length]; Assert.Equal(actual.Length, content.Read(0, actual)); Assert.Equal(expected, actual);
        }
        first.Navigate(Location.FileSystem(f.Files)); Assert.Equal(1, f.Services.Archives.RetainedIndexes);
        second.Panel.CloseTab(second); Assert.Equal(0, f.Services.Archives.RetainedIndexes);
        first.Navigate(root);
        await Wait(() => f.Button("Unlock") is not null);
        f.Click("Cancel"); await Wait(() => !f.Vm.Dialogs.IsOpen && first.Banner?.StartsWith("Opening", StringComparison.Ordinal) != true);
        Assert.Equal(Location.FileSystem(f.Files), first.Location);
    }

    [AvaloniaTheory, InlineData(false), InlineData(true)]
    public async Task Navigation_or_tab_close_cancels_password_question_without_retaining_secret_or_changing_newer_location(bool close)
    {
        using var f = await Fixture.Open(nativeWindowsOriginMarks: true);
        string path = Path.Combine(f.Root, "protected.7z"); ArchivePasswordReadFixture.Create(path, false, true);
        var original = f.Vm.ActiveTab!;
        var tab = close ? original.Panel.OpenTab(Location.FileSystem(f.Files)) : original;
        tab.Navigate(ArchiveProvider.ForFile(path)); await Wait(() => f.Button("Unlock") is not null);
        var input = f.Input("Existing archive password"); input.Text = ArchivePasswordReadFixture.Password;
        if (close) tab.Panel.CloseTab(tab); else tab.Navigate(Location.FileSystem(f.Files));
        await Wait(() => f.Button("Unlock") is null && input.Text == "");
        Assert.Equal(0, f.Services.Archives.RetainedIndexes);
        Assert.Equal(Location.FileSystem(f.Files), original.Location);
    }

    [AvaloniaFact]
    public async Task Locked_tab_prompts_once_in_the_new_destination_tab()
    {
        using var f = await Fixture.Open(nativeWindowsOriginMarks: true);
        string path = Path.Combine(f.Root, "protected.rar"); ArchivePasswordReadFixture.Create(path, true, true);
        var locked = f.Vm.ActiveTab!; locked.IsLocked = true; int count = locked.Panel.Tabs.Count;
        var root = ArchiveProvider.ForFile(path); locked.Navigate(root);
        await Wait(() => f.Button("Unlock") is not null);
        Assert.Equal(count + 1, locked.Panel.Tabs.Count); var destination = locked.Panel.ActiveTab!;
        f.Input("Existing archive password").Text = ArchivePasswordReadFixture.Password; f.Click("Unlock");
        await Wait(() => destination.Location == root && destination.Listing.State == ListingState.Complete);
        Assert.Null(f.Button("Unlock")); Assert.Equal(Location.FileSystem(f.Files), locked.Location);
        destination.Panel.CloseTab(destination); Assert.Equal(0, f.Services.Archives.RetainedIndexes);
    }

    [AvaloniaTheory, MemberData(nameof(Profiles))]
    public async Task Quiet_restoration_keeps_archive_address_and_explicit_refresh_unlocks_without_losing_marks(bool rar, bool hidden)
    {
        using var f = await Fixture.Open(nativeWindowsOriginMarks: true);
        string path = Path.Combine(f.Root, rar ? "restored.rar" : "restored.7z"); ArchivePasswordReadFixture.Create(path, rar, hidden);
        var root = ArchiveProvider.ForFile(path); var tab = f.Vm.ActiveTab!;
        f.Vm.QuietConnect = true; tab.Navigate(root);
        await Wait(() => tab.Location == root && tab.Listing.State is ListingState.Complete or ListingState.Failed);
        Assert.Null(f.Button("Unlock"));
        f.Vm.QuietConnect = false; tab.Refresh(); await Wait(() => f.Button("Unlock") is not null);
        f.Input("Existing archive password").Text = ArchivePasswordReadFixture.Password; f.Click("Unlock");
        await Wait(() => tab.Location == root && tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing &&
            tab.Banner?.StartsWith("Opening", StringComparison.Ordinal) != true);
        int folder = Enumerable.Range(0, tab.Listing.Store.Count).Single(i => tab.Listing.Store[i].Name == "folder");
        tab.Listing.SelectStoreMark(folder); Assert.True(tab.Listing.FocusName("folder"));
        var history = tab.BackHistory.ToArray(); tab.Refresh();
        await Wait(() => !tab.Listing.IsRefreshing && tab.Banner?.StartsWith("Opening", StringComparison.Ordinal) != true);
        Assert.Null(f.Button("Unlock")); Assert.Equal(history, tab.BackHistory);
        int refreshed = Enumerable.Range(0, tab.Listing.Store.Count).Single(i => tab.Listing.Store[i].Name == "folder");
        Assert.True(tab.Listing.IsMarked(refreshed)); Assert.True(tab.Listing.TryGetFocused(out var focused)); Assert.Equal("folder", focused.Name);
    }

    private static async Task Wait(Func<bool> ready)
    {
        for (int i = 0; i < 500 && !ready(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready());
    }

    [AvaloniaTheory, MemberData(nameof(NestedProfiles))]
    public async Task Entering_nested_ZIP_or_TAR_keeps_outer_password_alive_until_its_new_tab_leaves(bool rar, bool hidden, bool zip)
    {
        using var f = await Fixture.Open(nativeWindowsOriginMarks: true);
        using var data = new MemoryStream();
        if (zip)
        {
            using var archive = new System.IO.Compression.ZipArchive(data, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true);
            using var entry = archive.CreateEntry("text.txt").Open(); entry.Write("nested content"u8);
        }
        else
        {
            using var archive = new System.Formats.Tar.TarWriter(data, System.Formats.Tar.TarEntryFormat.Ustar, leaveOpen: true);
            archive.WriteEntry(new System.Formats.Tar.UstarTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "text.txt")
                { DataStream = new MemoryStream("nested content"u8.ToArray()) });
        }
        string path = Path.Combine(f.Root, rar ? "outer.rar" : "outer.7z"); string name = zip ? "nested.zip" : "nested.tar";
        ArchivePasswordReadFixture.Create(path, rar, hidden, payload: data.ToArray(), name: name);
        var root = ArchiveProvider.ForFile(path); var tab = f.Vm.ActiveTab!; tab.Navigate(root);
        await Wait(() => f.Button("Unlock") is not null); f.Input("Existing archive password").Text = ArchivePasswordReadFixture.Password; f.Click("Unlock");
        await Wait(() => tab.Location == root && tab.Listing.State == ListingState.Complete && tab.Listing.FocusName(name));
        Assert.True(tab.TryEnterFocused(out _));
        await Wait(() => tab.Location != root && tab.Listing.State == ListingState.Complete);
        Assert.Equal(zip ? Schemes.Zip : Schemes.Archive, tab.Location!.Scheme);
        using (var content = f.Services.Providers.For(tab.Location).OpenContent(new(tab.Location, "text.txt", EntryKind.File)))
        {
            Assert.NotNull(content); byte[] bytes = new byte[32]; int read = content.Read(0, bytes);
            Assert.Equal("nested content", System.Text.Encoding.UTF8.GetString(bytes, 0, read));
        }
        Assert.True(f.Services.Archives.RetainedIndexes >= 1);
        tab.Navigate(Location.FileSystem(f.Files));
        Assert.Throws<ArchiveReadPasswordRequiredException>(() => f.Services.Archives.OpenReadView(root, null, TestContext.Current.CancellationToken));
    }

    [AvaloniaTheory, MemberData(nameof(Profiles))]
    public async Task F3_viewer_retains_password_for_reopen_after_tab_navigation_and_forgets_on_viewer_close(bool rar, bool hidden)
    {
        using var f = await Fixture.Open(nativeWindowsOriginMarks: true);
        string path = Path.Combine(f.Root, rar ? "viewer.rar" : "viewer.7z"); ArchivePasswordReadFixture.Create(path, rar, hidden);
        var root = ArchiveProvider.ForFile(path); var tab = f.Vm.ActiveTab!; var before = ViewerWindow.OpenWindows.ToHashSet();
        try
        {
            tab.Navigate(root.WithPath("folder")); await Wait(() => f.Button("Unlock") is not null);
            f.Input("Existing archive password").Text = ArchivePasswordReadFixture.Password; f.Click("Unlock");
            await Wait(() => tab.Location == root.WithPath("folder") && tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("private-界.bin"));
            await f.Vm.ExecuteAsync(CommandIds.ViewAlternate);
            await Wait(() => ViewerWindow.OpenWindows.Any(window => !before.Contains(window)));
            var viewer = Assert.Single(ViewerWindow.OpenWindows, window => !before.Contains(window));
            tab.Navigate(Location.FileSystem(f.Files)); Assert.Equal(1, f.Services.Archives.RetainedIndexes);
            using (var reopened = f.Services.Archives.OpenContent(new(root.WithPath("folder"), "private-界.bin", EntryKind.File))) Assert.NotNull(reopened);
            viewer.Close(); await Wait(() => f.Services.Archives.RetainedIndexes == 0);
            tab.Navigate(root); await Wait(() => f.Button("Unlock") is not null); f.Click("Cancel");
        }
        finally { foreach (var viewer in ViewerWindow.OpenWindows.Where(window => !before.Contains(window)).ToArray()) viewer.Close(); }
    }
}
