using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class HexEditorSmokeTests
{
    [AvaloniaFact]
    public void Hex_editor_types_both_columns_undoes_saves_in_place_and_holds_exit_while_dirty()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-hexui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "small.bin");
        File.WriteAllBytes(file, Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var vm = new MainViewModel(services);
        Assert.Null(HexEditorWindow.OpenOrActivate(services, file));
        var window = Assert.Single(HexEditorWindow.OpenWindows);
        try
        {
            Assert.Contains("Hex Editor", window.Title ?? "");
            Assert.Empty(AccessibilityTests.Unnamed(window));
            var hex = window.GetVisualDescendants().OfType<HexView>().Single();
            hex.Focus();

            // Two hex digits make one byte; the first is shown as pending.
            window.KeyTextInput("a");
            Assert.Equal(0xA, hex.PendingNibble);
            window.KeyTextInput("B");
            Assert.Equal(-1, hex.PendingNibble);
            Assert.Equal(1, hex.CursorOffset);
            Assert.True(window.HasUnsavedWork);
            Assert.False(vm.CanCloseImmediately());

            // Tab switches to the text column, where a character is one byte.
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(hex.TextColumnActive);
            window.KeyTextInput("Z");
            Assert.Equal(2, hex.CursorOffset);
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            Assert.Equal(1, hex.CursorOffset); // undo moves to the change

            // Opening the same file again brings this editor forward instead of failing on the protected handle.
            Assert.Null(HexEditorWindow.OpenOrActivate(services, file));
            Assert.Single(HexEditorWindow.OpenWindows);

            // Ctrl+S asks once, Enter confirms; the save runs in the background.
            window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (window.HasUnsavedWork && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            Assert.False(window.HasUnsavedWork);
            Assert.True(vm.CanCloseImmediately());
            using (var check = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var bytes = new byte[32];
                check.ReadExactly(bytes);
                Assert.Equal(0xAB, bytes[0]);
                Assert.Equal(1, bytes[1]);
            }
            Assert.Empty(FileCat.Platform.Windows.HexSaveJournal.Pending(services.Paths.HexRecoveryDirectory));
        }
        finally
        {
            window.CloseNow();
            Directory.Delete(root, recursive: true);
        }
    }
}
