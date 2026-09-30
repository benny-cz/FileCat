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
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
