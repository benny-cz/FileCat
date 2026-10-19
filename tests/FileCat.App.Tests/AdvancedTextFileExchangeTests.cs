using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class AdvancedTextFileExchangeTests
{
    [Fact]
    public async Task Import_is_bounded_strict_utf8_and_accepts_a_utf8_bom()
    {
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("fc-settings-text-").FullName;
        try
        {
            string path = Path.Combine(root, "definitions.txt");
            string definition = "název = *.cs|*.g.cs\neditor = \"space path\"";
            await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(definition)], ct);
            Assert.Equal(definition, await AdvancedTextFileExchange.ReadUtf8Async(path, ct));
            await File.WriteAllBytesAsync(path, [0xC3, 0x28], ct);
            await Assert.ThrowsAsync<DecoderFallbackException>(() => AdvancedTextFileExchange.ReadUtf8Async(path, ct));
            await File.WriteAllBytesAsync(path, new byte[AdvancedTextFileExchange.MaxBytes + 1], ct);
            await Assert.ThrowsAsync<InvalidDataException>(() => AdvancedTextFileExchange.ReadUtf8Async(path, ct));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Export_replaces_atomically_and_preserves_the_old_file_on_rejected_or_canceled_writes()
    {
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("fc-settings-export-").FullName;
        try
        {
            string path = Path.Combine(root, "definitions.txt");
            await File.WriteAllTextAsync(path, "original", ct);
            string definition = "editor = \"C:\\My Editor\\app.exe\"\nnon-ASCII = žluťoučký";
            await AdvancedTextFileExchange.WriteUtf8AtomicAsync(path, definition, ct);
            Assert.Equal(Encoding.UTF8.GetBytes(definition), await File.ReadAllBytesAsync(path, ct));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                AdvancedTextFileExchange.WriteUtf8AtomicAsync(path, new string('x', AdvancedTextFileExchange.MaxBytes + 1), ct));
            using var canceled = CancellationTokenSource.CreateLinkedTokenSource(ct);
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                AdvancedTextFileExchange.WriteUtf8AtomicAsync(path, "not written", canceled.Token));
            Assert.Equal(definition, await File.ReadAllTextAsync(path, ct));
            Assert.Equal([path], Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task All_advanced_editors_expose_import_export_without_committing_the_settings_draft()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string original = JsonSerializer.Serialize(services.Settings);
            var showing = SettingsDialog.ShowAsync(vm);
            Dispatcher.UIThread.RunJobs();
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            void Select(string name)
            {
                tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList().FindIndex(tab => tab.Header as string == name);
                Dispatcher.UIThread.RunJobs();
            }
            void Check(string choice, string subject)
            {
                var advanced = window.GetVisualDescendants().OfType<CheckBox>()
                    .Single(box => box.Content as string == choice);
                advanced.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                foreach (string verb in new[] { "Import", "Export" })
                {
                    var button = window.GetVisualDescendants().OfType<Button>()
                        .Single(b => b.Content as string == $"{verb} {subject} text…");
                    Assert.True(button.IsEffectivelyVisible);
                    Assert.True(button.IsEffectivelyEnabled);
                }
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block =>
                    AutomationProperties.GetName(block) == $"{subject} text file status");
            }
            Select("Behavior");
            Check("Edit filter definitions as text", "filter");
            Select("Tools");
            Check("Edit user commands as text", "command");
            Check("Edit associations as text", "association");
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(original, JsonSerializer.Serialize(services.Settings));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
