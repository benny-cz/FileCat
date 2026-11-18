using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;

namespace FileCat.App.Tests;

public sealed class SupplementaryReportWindowTests
{
    private const BindingFlags InstanceFields = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FileIdentity Original = new(1, "0123456789ABCDEF0123456789ABCDEF");

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Protected_or_unverified_report_text_cannot_reach_a_picker_or_clipboard(bool encrypted, bool unknown)
    {
        const string text = "Fixture supplementary report";
        var window = ReportWindow.ForSupplementaryValue("fixture", _ => Task.FromResult(Report(text, encrypted, unknown)));
        int pickerCalls = 0;
        InstallPicker(window, () => { pickerCalls++; return Task.FromResult<IStorageFile?>(null); });
        try
        {
            window.Show();
            await Until(() => window.Text == text);
            var clipboard = window.Clipboard!;
            await ClipboardExtensions.SetTextAsync(clipboard, "existing clipboard text");
            await CopyAll(window);
            await window.CopyTextAsync(displayed: false, explicitVisible: true, clipboard);
            await window.CopyTextAsync(displayed: true, explicitVisible: true, clipboard);
            await Save(window);
            Assert.Equal("existing clipboard text", await ClipboardExtensions.TryGetTextAsync(clipboard));
            Assert.Equal(0, pickerCalls);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Saving_a_value_report_cancels_a_stale_picker_and_only_creates_new_files()
    {
        string root = Directory.CreateTempSubdirectory("filecat-report-output-").FullName;
        string target = Path.Combine(root, "report.txt");
        int generation = 0;
        var window = ReportWindow.ForSupplementaryValue("fixture", _ => Task.FromResult(Report($"Value revision {++generation}")));
        var pending = new TaskCompletionSource<IStorageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int pickerCalls = 0;
        var picker = InstallPicker(window, () => { pickerCalls++; return pending.Task; });
        try
        {
            window.Show();
            await Until(() => window.Text == "Value revision 1");
            var staleSave = Save(window);
            Assert.Equal(1, pickerCalls);
            await window.RefreshDetailsForTestAsync();
            Assert.Equal("Value revision 2", window.Text);
            pending.SetResult(Chosen(target));
            await staleSave;
            Assert.False(File.Exists(target));

            picker.Save = () => Task.FromResult<IStorageFile?>(Chosen(target));
            File.WriteAllText(target, "Existing report");
            bool testEfs = OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1";
            if (testEfs) File.Encrypt(target);
            await Save(window);
            Assert.Equal("Existing report", File.ReadAllText(target));
            Assert.Contains("could not be saved", window.StatusText);
            if (testEfs) Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            File.Delete(target);
            await Save(window);
            Assert.Equal("Value revision 2", File.ReadAllText(target));
            await CopyAll(window);
            Assert.Equal("Value revision 2", await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
        }
        finally
        {
            pending.TrySetResult(null);
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static SupplementaryValueReport Report(string text, bool encrypted = false, bool unknown = false) =>
        new(text, Original, Guid.NewGuid(), new(encrypted ? OutputEfsObservation.State.Encrypted : OutputEfsObservation.State.Clear,
            [new(Original, "known revision")], unknown));

    private static EditSaveCopyTests.StorageProxy InstallPicker(ReportWindow window, Func<Task<IStorageFile?>> save)
    {
        var storage = DispatchProxy.Create<IStorageProvider, EditSaveCopyTests.StorageProxy>();
        var proxy = (EditSaveCopyTests.StorageProxy)(object)storage;
        proxy.Save = save;
        typeof(TopLevel).GetField("_storageProvider", InstanceFields)!.SetValue(window, storage);
        return proxy;
    }

    private static IStorageFile Chosen(string path)
    {
        var file = DispatchProxy.Create<IStorageFile, EditSaveCopyTests.StorageProxy>();
        ((EditSaveCopyTests.StorageProxy)(object)file).Uri = new Uri(path);
        return file;
    }

    private static Task Save(ReportWindow window) => (Task)typeof(ReportWindow).GetMethod("SaveAsync", InstanceFields)!.Invoke(window, [])!;
    private static Task CopyAll(ReportWindow window) => (Task)typeof(ReportWindow).GetMethod("CopyAsync", InstanceFields)!.Invoke(window, [true])!;

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
