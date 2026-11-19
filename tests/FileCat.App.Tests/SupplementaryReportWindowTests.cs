using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using FileCat.Core.Threading;

namespace FileCat.App.Tests;

public sealed class SupplementaryReportWindowTests
{
    private const BindingFlags InstanceFields = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string UnqualifiedPayload = "FILECAT-UNQUALIFIED-DETAILS-PAYLOAD";
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saving_file_system_details_replaces_a_clear_file_and_preserves_an_Efs_destination(bool protectDestination)
    {
        Assert.SkipWhen(protectDestination && (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") != "1"),
            "The encrypted destination requires an opt-in Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-details-output-").FullName;
        string target = Path.Combine(root, "report.txt");
        using var io = new DeviceIoScheduler();
        var records = new MetadataRecords("Previous fixture observation");
        using var reader = new FileDetailsReader(records, io, "report-output", Path.Combine(root, "observation"));
        var window = new ReportWindow("observation", reader);
        var pending = new TaskCompletionSource<IStorageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var picker = InstallPicker(window, () => pending.Task);
        try
        {
            window.Show();
            await window.Reading!;
            Assert.Contains("Previous fixture observation", window.Text);
            var staleSave = Save(window);
            records.Note = "Refreshed fixture observation";
            await window.RefreshDetailsForTestAsync();
            pending.SetResult(Chosen(target));
            await staleSave;
            Assert.False(File.Exists(target));

            picker.Save = () => Task.FromResult<IStorageFile?>(Chosen(target));
            await Save(window);
            Assert.Contains("Refreshed fixture observation", window.Text);
            Assert.Equal(window.Text, File.ReadAllText(target));
            Assert.True(File.ReadAllBytes(target).AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.StartsWith("Saved as", window.StatusText);

            File.WriteAllText(target, new string('x', window.Text.Length + 256));
            await Save(window);
            Assert.Equal(window.Text, File.ReadAllText(target));
            Assert.StartsWith("Saved as", window.StatusText);

            if (protectDestination)
            {
                const string original = "Existing encrypted report";
                File.WriteAllText(target, original);
                File.Encrypt(target);
                await Save(window);
                Assert.Contains("EFS protection", window.StatusText);
                Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
                Assert.Equal(original, File.ReadAllText(target));
            }
        }
        finally
        {
            pending.TrySetResult(null);
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Saving_metadata_reports_a_partial_file_when_the_native_volume_fills()
    {
        string? prepared = Environment.GetEnvironmentVariable("FILECAT_TEST_REPORT_SPACE_LIMIT");
        Assert.SkipWhen(!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(prepared), "Requires an isolated small Linux test volume.");
        Assert.InRange(new DriveInfo(prepared!).TotalSize, 4L << 10, 64L << 10);
        string root = Directory.CreateDirectory(Path.Combine(prepared!, "report-" + Guid.NewGuid().ToString("N"))).FullName;
        string target = Path.Combine(root, "partial.txt");
        using var io = new DeviceIoScheduler();
        using var reader = new FileDetailsReader(new MetadataRecords(new string('x', 256 << 10)), io, "report-output", "/observation");
        var window = new ReportWindow("observation", reader);
        InstallPicker(window, () => Task.FromResult<IStorageFile?>(Chosen(target)));
        try
        {
            window.Show();
            await window.Reading!;
            await Save(window);
            Assert.Contains("may be incomplete", window.StatusText);
            Assert.InRange(new FileInfo(target).Length, 1, (256L << 10) - 1);
        }
        finally { window.Close(); Directory.Delete(root, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData(ReportContentKind.Payload)]
    [InlineData(ReportContentKind.Unknown)]
    public async Task Canonical_details_refuses_unqualified_full_content_before_display_copy_or_save(ReportContentKind content)
    {
        string root = Directory.CreateTempSubdirectory("filecat-unqualified-details-").FullName;
        string target = Path.Combine(root, "report.txt");
        using var io = new DeviceIoScheduler();
        using var reader = new FileDetailsReader(new MetadataRecords("Accepted summary", content), io, "report-content", Path.Combine(root, "observation"));
        var window = new ReportWindow("observation", reader);
        InstallPicker(window, () => Task.FromResult<IStorageFile?>(Chosen(target)));
        try
        {
            window.Show();
            await window.Reading!;
            string summary = window.Text;
            Assert.Contains("Accepted summary", summary);
            await window.SwitchDetailsTierForTestAsync();
            Assert.Contains("Unqualified report content", window.Text);
            Assert.EndsWith(summary, window.Text);
            Assert.DoesNotContain(UnqualifiedPayload, window.Text);

            await CopyAll(window);
            Assert.Equal(window.Text, await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            await Save(window);
            Assert.Equal(window.Text, File.ReadAllText(target));
        }
        finally { window.Close(); Directory.Delete(root, recursive: true); }
    }

    private sealed class MetadataRecords(string? note = null, ReportContentKind? fullContent = null) : IFileRecords
    {
        internal string? Note { get; set; } = note;
        public bool IsSupported => true;
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy report read.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct) => new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
            { Kind = FileObjectKind.File, Notes = Note is { } current ? [current] : [] };
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) =>
            fullContent is { } kind ? FileDetailsReport.Metadata(ReadSummary(path, ct),
                new InspectionSection("Fixture", [("Detail", UnqualifiedPayload)])) with { ContentKind = kind }
                : throw new InvalidOperationException("Unexpected full report read.");
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
