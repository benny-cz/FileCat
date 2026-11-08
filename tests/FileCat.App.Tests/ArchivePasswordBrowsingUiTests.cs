using Avalonia.Headless.XUnit;
using FileCat.Archives;
using FileCat.App.ViewModels;
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

    private static async Task Wait(Func<bool> ready)
    {
        for (int i = 0; i < 500 && !ready(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready());
    }
}
