using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// The hex editor on Linux and macOS (ADR-05): no program can be kept from writing the file there, so links are refused
/// when it opens and a save first checks the bytes it replaces.
/// </summary>
public sealed class HexEditorPosixTests
{
    [AvaloniaFact]
    public void A_link_is_refused_and_a_byte_another_program_changed_blocks_the_save()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Windows keeps other writers out instead (HexEditorSmokeTests).");
        string root = Path.Combine(Path.GetTempPath(), "filecat-hexposix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "small.bin");
        File.WriteAllBytes(file, Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        HexEditorWindow? window = null;
        try
        {
            string link = Path.Combine(root, "link.bin");
            File.CreateSymbolicLink(link, file);
            Assert.Contains("link", HexEditorWindow.OpenOrActivate(services, link));
            Assert.Empty(HexEditorWindow.OpenWindows);

            Assert.Null(HexEditorWindow.OpenOrActivate(services, file));
            window = Assert.Single(HexEditorWindow.OpenWindows);
            var hex = window.GetVisualDescendants().OfType<HexView>().Single();
            hex.Focus();
            window.KeyTextInput("a");
            window.KeyTextInput("b");
            Assert.True(window.HasUnsavedWork);

            // Another program writes the byte being edited (other programs may write here, and read too).
            using (var other = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                other.WriteByte(0x77);
                other.Flush(flushToDisk: true);
            }

            window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            bool Blocked() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("save is blocked", StringComparison.Ordinal) == true);
            while (!Blocked() && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            Assert.True(Blocked(), "the save says why it did not write");
            Assert.True(window.HasUnsavedWork);
            Assert.Equal(0x77, File.ReadAllBytes(file)[0]); // the other program's byte stays
        }
        finally
        {
            window?.CloseNow();
            Directory.Delete(root, recursive: true);
        }
    }
}
