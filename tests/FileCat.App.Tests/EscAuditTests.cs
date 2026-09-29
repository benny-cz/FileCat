using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>Esc closes every dialog and secondary window (UX-011), wherever the keyboard is.</summary>
public sealed class EscAuditTests
{
    /// <summary>
    /// Commands that open an overlay dialog, a prompt, or a chooser with a.txt focused (Ctrl+Shift+P goes to the search
    /// box in the menu bar, whose Esc CommandSearchTests covers).
    /// </summary>
    private static readonly string[] DialogCommands =
    [
        CommandIds.Help, CommandIds.LocationMenuSource, CommandIds.Bookmarks, CommandIds.TabList,
        CommandIds.ThemePick, CommandIds.Settings, CommandIds.About, CommandIds.Copy, CommandIds.Move, CommandIds.MakeDirectory,
        CommandIds.EditNew, CommandIds.MarkSelectMask, CommandIds.BulkRename, CommandIds.CreateLink,
        CommandIds.Checksum, CommandIds.ApplyCommand, CommandIds.SftpConnect, CommandIds.Attributes, CommandIds.CompareDirectories,
        CommandIds.Pack, CommandIds.DeletePermanent,
    ];

    [AvaloniaFact]
    public async Task Esc_closes_every_dialog_even_with_the_keyboard_outside_it()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var dialogs = (OverlayDialogService)vm.Dialogs;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++) await Task.Delay(20, ct);
            for (int i = 0; i < 250 && window.FocusManager?.GetFocusedElement() is not FileCat.App.Controls.FileListControl; i++) await Task.Delay(20, ct);
            var failures = new List<string>();
            foreach (var command in DialogCommands)
            {
                foreach (bool keyboardOutside in new[] { false, true })
                {
                    listing.FocusName("a.txt");
                    vm.Execute(command);
                    for (int i = 0; i < 150 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
                    if (!dialogs.IsOpen)
                    {
                        failures.Add($"{command}: no dialog opened");
                        break;
                    }
                    await Task.Delay(60, ct); // the dialog's own focus has arrived
                    if (keyboardOutside) window.ActiveList?.Focus();
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    for (int i = 0; i < 100 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
                    if (dialogs.IsOpen)
                    {
                        failures.Add($"{command}: still open after Esc{(keyboardOutside ? " with the keyboard outside it" : "")}");
                        // Close whatever is open before the next command.
                        for (int n = 0; n < 5 && dialogs.IsOpen; n++)
                        {
                            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                            await Task.Delay(50, ct);
                        }
                    }
                }
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Esc_closes_the_viewer_the_hex_editor_and_comparisons()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++) await Task.Delay(20, ct);

            async Task EscCloses<T>(Func<IReadOnlyList<T>> open, string what) where T : Avalonia.Controls.Window
            {
                for (int i = 0; i < 250 && open().Count == 0; i++) await Task.Delay(20, ct);
                var shown = Assert.Single(open());
                shown.Activate();
                await Task.Delay(100, ct);
                shown.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                for (int i = 0; i < 100 && open().Count > 0; i++) await Task.Delay(20, ct);
                Assert.True(open().Count == 0, what + " stayed open after Esc");
            }

            listing.FocusName("a.txt");
            vm.Execute(CommandIds.View);
            await EscCloses(() => ViewerWindow.OpenWindows, "The viewer");

            listing.FocusName("a.txt");
            vm.Execute(CommandIds.HexEdit);
            await EscCloses(() => HexEditorWindow.OpenWindows, "The hex editor");

            vm.Execute(CommandIds.MarkAll);
            vm.Execute(CommandIds.CompareFiles);
            await EscCloses(() => CompareWindow.OpenWindows, "The file comparison");
            vm.Execute(CommandIds.MarkNone);

            // Find is a window of its own; Esc closes it when no search runs.
            vm.Execute(CommandIds.FindFiles);
            await EscCloses(() => FindWindow.OpenWindows, "The Find window");
        }
        finally
        {
            foreach (var w in ViewerWindow.OpenWindows.ToList()) w.Close();
            foreach (var w in HexEditorWindow.OpenWindows.ToList()) w.Close();
            foreach (var w in CompareWindow.OpenWindows.ToList()) w.Close();
            foreach (var w in FindWindow.OpenWindows.ToList()) w.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }
}
