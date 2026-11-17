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
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

public sealed class HexEfsJournalUiTests
{
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition()) { timeout.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); await Task.Delay(10, timeout.Token); }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task An_Efs_hex_save_shows_its_refusal_keeps_unsaved_edits_and_writes_no_recovery_journal()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Explicit EFS output UI qualification in the disposable Windows VM.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        string root = Directory.CreateTempSubdirectory("filecat-hex-efs-ui-").FullName;
        string path = Path.Join(root, "target.bin"); byte[] original = [1, 2, 3, 4];
        File.WriteAllBytes(path, original); File.Encrypt(path);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        HexEditorWindow? window = null;
        try
        {
            Assert.Null(HexEditorWindow.OpenOrActivate(services, path)); window = Assert.Single(HexEditorWindow.OpenWindows);
            window.GetVisualDescendants().OfType<HexView>().Single().Focus();
            window.KeyTextInput("a"); window.KeyTextInput("b"); Assert.True(window.HasUnsavedWork);
            window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Save in place"));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            bool Refused() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains("EFS files cannot be saved in place", StringComparison.Ordinal) == true);
            await Until(Refused);
            Assert.True(window.HasUnsavedWork); Assert.Empty(HexSaveJournal.Pending(services.Paths.HexRecoveryDirectory));
            Assert.Empty(Directory.EnumerateFiles(root, "*.fcj", SearchOption.AllDirectories));
            using (var check = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] actual = new byte[original.Length]; check.ReadExactly(actual); Assert.Equal(original, actual);
            }
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Until(() => !Refused());
        }
        finally { window?.CloseNow(); File.Decrypt(path); Directory.Delete(root, true); }
    }
}
