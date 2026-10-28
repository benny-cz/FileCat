using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>Help → About: who made FileCat, what it runs on, and the details copied whole for a bug report.</summary>
public sealed class AboutDialogTests
{
    [AvaloniaFact]
    public async Task About_names_the_author_says_what_it_runs_on_and_copies_the_details()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            vm.Execute(CommandIds.About);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            await Task.Delay(50, ct);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).OfType<string>().ToList();
            Assert.Contains("FileCat", texts);
            Assert.Contains("Made by Marek Střihavka", texts);
            Assert.Contains(texts, t => t.StartsWith("Version ", StringComparison.Ordinal) || t.StartsWith("$ filecat --version", StringComparison.Ordinal));
            Assert.Contains(services.Paths.SettingsDirectory, texts);
            var mail = window.GetVisualDescendants().OfType<HyperlinkButton>().Single();
            Assert.Equal(new Uri("mailto:marek.strihavka@gmail.com"), mail.NavigateUri);
            var problems = AccessibilityTests.Unnamed(window);
            Assert.True(problems.Count == 0, string.Join("; ", problems));
            // A screen reader reaching a value (each is a Tab stop) hears what it is, "Theme: Classic", not "Classic";
            // the label beside it is not read again (found with NVDA on Windows).
            var spoken = window.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Select(t => Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(t).GetName()).ToList();
            foreach (var (label, value) in AboutDialog.Facts(vm))
            {
                Assert.Contains($"{label}: {value}", spoken);
                Assert.All(window.GetVisualDescendants().OfType<TextBlock>().Where(t => t is not SelectableTextBlock && t.Text == label),
                    t => Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(t)));
            }

            // Copy details: the whole of it as text, for a bug report.
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Copy details").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            string? copied = null;
            for (int i = 0; i < 250 && copied is null; i++)
            {
                await Task.Delay(20, ct);
                copied = await ClipboardExtensions.TryGetTextAsync(window.Clipboard!);
            }
            Assert.NotNull(copied);
            Assert.StartsWith("FileCat " + AboutDialog.Version(), copied, StringComparison.Ordinal);
            Assert.Contains("Made by Marek Střihavka (marek.strihavka@gmail.com)", copied, StringComparison.Ordinal);
            Assert.Contains("System: " + services.Platform.Name, copied, StringComparison.Ordinal);
            Assert.Contains("Settings: " + services.Paths.SettingsDirectory, copied, StringComparison.Ordinal);
            Assert.Equal(AboutDialog.DetailsText(AboutDialog.Version(), AboutDialog.Facts(vm)), copied);
            Assert.True(dialogs.IsOpen);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Copied details.");

            var panel = vm.Workspace.ActivePanel!;
            var original = panel.ActiveTab!;
            var originalLocation = original.Location;
            foreach (var (label, path) in AboutDialog.SupportFolders(services.Paths.SettingsDirectory, services.Paths.LocalDirectory))
            {
                var open = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == label);
                Assert.Contains(path, AutomationProperties.GetHelpText(open));
                open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(dialogs.IsOpen);
                Assert.Equal(FileCat.Core.Resources.Location.FileSystem(path), panel.ActiveTab!.Location);
                Assert.Contains(original, panel.Tabs);
                Assert.Equal(originalLocation, original.Location);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == $"Opened {path} in a new tab.");
            }
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Close")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await FileListFixture.Wait(() => !dialogs.IsOpen);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [Fact]
    public void Support_folder_actions_name_their_exact_targets_and_collapse_identical_paths()
    {
        string settings = Path.Combine(Path.GetTempPath(), "filecat-settings");
        string local = Path.Combine(Path.GetTempPath(), "filecat-local");
        Assert.Equal([("Open settings folder", settings), ("Open local data folder", local)],
            AboutDialog.SupportFolders(settings, local));
        Assert.Equal([("Open data folder", settings)],
            AboutDialog.SupportFolders(settings, settings + Path.DirectorySeparatorChar));
    }
}
