using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.State;
using FileCat.Platform.Windows;
using Microsoft.Win32.SafeHandles;

namespace FileCat.App.Tests;

public sealed class HexEfsJournalUiTests
{
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition()) { timeout.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); await Task.Delay(10, timeout.Token); }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Efs_output_defaults_to_cancel_and_requires_explicit_activation_after_showing_both_paths(bool saveAs, bool sparse)
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Explicit EFS output UI qualification in the disposable Windows VM.");
        string root = Directory.CreateTempSubdirectory("filecat-hex-efs-export-ui-").FullName;
        string path = Path.Join(root, "target.bin"), destination = Path.Join(root, saveAs ? "output.bin" : "output.json");
        string stagePrefix = saveAs ? ".filecat-hex-" : ".filecat-patch-";
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        if (sparse)
        {
            using var fixture = File.OpenHandle(path, access: FileAccess.ReadWrite);
            Assert.True(SetSparseControl(fixture, 0x000900c4 /* FSCTL_SET_SPARSE */, 0, 0, 0, 0, out _, 0));
            RandomAccess.SetLength(fixture, 4 * 1024 * 1024);
        }
        File.Encrypt(path);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        HexEditorWindow? window = null;
        try
        {
            Assert.Null(HexEditorWindow.OpenOrActivate(services, path));
            window = Assert.Single(HexEditorWindow.OpenWindows);
            window.GetVisualDescendants().OfType<HexView>().Single().Focus();
            window.KeyTextInput("a");
            window.KeyTextInput("b");
            bool ReviewVisible() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == destination);
            Task Start() => saveAs ? window.SaveAsToAsync(destination) : window.ExportPatchToAsync(destination);
            var attempt = Start();
            await Until(ReviewVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith(Path.Join(root, stagePrefix), StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains(
                saveAs ? "edited copy contains file bytes" : "original and replacement file bytes", StringComparison.Ordinal) == true);
            Assert.Equal(sparse, window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains(
                "may allocate space for the entire file", StringComparison.Ordinal) == true));
            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.EnumerateFiles(root, stagePrefix + "*.tmp"));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.IsDefault && button.Content as string == "Cancel");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await attempt.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(File.Exists(destination));
            Assert.True(window.HasUnsavedWork);

            attempt = Start();
            await Until(ReviewVisible);
            string label = saveAs ? "Save copy" : "Export patch";
            var approve = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.Content as string == label && button.IsVisible && !button.IsDefault);
            approve.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await attempt.WaitAsync(TimeSpan.FromSeconds(10));
            if (saveAs)
            {
                using var copy = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                Assert.Equal(0xab, copy.ReadByte());
                Assert.Equal(sparse, File.GetAttributes(destination).HasFlag(FileAttributes.SparseFile));
            }
            else Assert.Single(HexPatchExport.Read(destination).Ranges);
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(!saveAs, window.HasUnsavedWork);
        }
        finally { window?.CloseNow(); Directory.Delete(root, recursive: true); }
    }

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSparseControl(SafeFileHandle file, uint operation, nint input, uint inputBytes,
        nint output, uint outputBytes, out uint written, nint overlapped);

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
