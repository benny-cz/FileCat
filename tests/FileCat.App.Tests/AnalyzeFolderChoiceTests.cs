using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

public sealed class AnalyzeFolderChoiceTests
{
    [AvaloniaFact]
    public async Task A_sorted_folder_opens_the_visible_analyze_chooser_again_after_escape()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.Workspace.ActiveTab!;
            tab.SortByMetadata("hidden", analyzing: true);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var pending = vm.ExecuteAsync(CommandIds.AnalyzeFolder);
                for (int i = 0; i < 100 && !dialogs.IsOpen; i++)
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                Assert.True(dialogs.IsOpen);
                TextBox? filter = null;
                ListBox? list = null;
                for (int i = 0; i < 100 && (filter is null || list is null); i++)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    filter = window.GetVisualDescendants().OfType<TextBox>()
                        .FirstOrDefault(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Filter Analyze folder");
                    list = window.GetVisualDescendants().OfType<ListBox>()
                        .FirstOrDefault(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Analyze folder");
                    if (filter is null || list is null) await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                Assert.NotNull(filter);
                Assert.NotNull(list);
                Assert.True(filter.IsVisible);
                Assert.Equal("hidden", tab.Listing.Sort.MetadataId);
                Assert.NotNull(list.SelectedItem);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.StartsWith("Current sort ·", StringComparison.Ordinal) == true);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await pending.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
                Assert.False(dialogs.IsOpen);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Analyze_folder_reopens_the_field_chooser_without_changing_the_current_sort_on_cancel_or_rerun()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.Workspace.ActiveTab!;
            for (int i = 0; i < 200 && tab.Listing.State != ListingState.Complete; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(ListingState.Complete, tab.Listing.State);

            var dialogs = new ChoiceDialogs();
            vm.Dialogs = dialogs;
            dialogs.Choose = options => options.Items.ToList().FindIndex(item => item.Title ==
                (OperatingSystem.IsWindows() ? "Streams" : "Attributes"));
            await vm.ExecuteAsync(CommandIds.AnalyzeFolder);
            Assert.Equal("hidden", tab.Listing.Sort.MetadataId);
            Assert.Single(dialogs.Calls);

            // A user can return to the menu without restarting FileCat. Escape preserves the sort.
            dialogs.Choose = _ => -1;
            await vm.ExecuteAsync(CommandIds.AnalyzeFolder);
            Assert.Equal(2, dialogs.Calls.Count);
            Assert.Equal("hidden", tab.Listing.Sort.MetadataId);
            Assert.Equal(dialogs.Calls[1].SelectedIndex, dialogs.Calls[1].Items.ToList().FindIndex(i => i.Title ==
                (OperatingSystem.IsWindows() ? "Streams" : "Attributes")));
            Assert.Contains("Current sort", dialogs.Calls[1].Items[dialogs.Calls[1].SelectedIndex].Detail);

            // Rerunning the selected analysis must not toggle descending order.
            tab.SortByMetadata("hidden", analyzing: true);
            Assert.True(tab.Listing.Sort.Descending);
            dialogs.Choose = options => options.SelectedIndex;
            await vm.ExecuteAsync(CommandIds.AnalyzeFolder);
            Assert.Equal(3, dialogs.Calls.Count);
            Assert.True(tab.Listing.Sort.Descending);

            dialogs.Choose = options => options.Items.ToList().FindIndex(item => item.Title == "Dimensions");
            await vm.ExecuteAsync(CommandIds.AnalyzeFolder);
            Assert.Equal(4, dialogs.Calls.Count);
            Assert.Equal("dimensions", tab.Listing.Sort.MetadataId);
            Assert.False(tab.Listing.Sort.Descending);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private sealed class ChoiceDialogs : IDialogService
    {
        public Func<ChoiceOptions, int> Choose { get; set; } = _ => -1;
        public List<ChoiceOptions> Calls { get; } = [];
        public bool IsOpen => false;
        public Task<ChoiceResult> ChooseAsync(ChoiceOptions options)
        {
            Calls.Add(options);
            var index = Choose(options);
            return Task.FromResult(index < 0 ? ChoiceResult.Canceled : new ChoiceResult(index, false, []));
        }
        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool danger = false, string cancelText = "Cancel") => throw new NotSupportedException();
        public Task<PromptResult?> PromptAsync(PromptOptions options) => throw new NotSupportedException();
        public Task AlertAsync(string title, string message) => throw new NotSupportedException();
        public Task<KeyboardReferenceChoice?> KeyboardReferenceAsync(IReadOnlyList<KeyboardHelpEntry> commands, string? selectedId = null, bool remapOnly = false) => throw new NotSupportedException();
        public Task<object?> ShowCustomAsync(string title, Control content, IReadOnlyList<DialogButton> buttons, Control? initialFocus = null,
            Func<bool>? canConfirm = null, DialogCloser? closer = null, bool resizable = false,
            Control? pinnedReview = null, DialogBehavior? behavior = null) => throw new NotSupportedException();
    }
}
