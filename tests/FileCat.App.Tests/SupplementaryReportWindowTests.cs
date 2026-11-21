using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saving_a_general_metadata_report_preserves_an_existing_Efs_destination(bool protectDestination)
    {
        Assert.SkipWhen(protectDestination && (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") != "1"),
            "The encrypted destination requires an opt-in Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-general-report-").FullName;
        string target = Path.Combine(root, "report.txt");
        var window = new ReportWindow("File list", "fixture", _ => Task.FromResult("Current metadata report"));
        InstallPicker(window, () => Task.FromResult<IStorageFile?>(Chosen(target)));
        try
        {
            const string original = "Existing report";
            File.WriteAllText(target, original);
            if (protectDestination) File.Encrypt(target);
            window.Show();
            await window.Reading!;
            await Save(window);
            if (protectDestination)
            {
                Assert.Contains("EFS protection", window.StatusText);
                Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
                Assert.Equal(original, File.ReadAllText(target));
            }
            else
            {
                Assert.Equal("Current metadata report", File.ReadAllText(target));
                Assert.True(File.ReadAllBytes(target).AsSpan().StartsWith(Encoding.UTF8.Preamble));
                Assert.StartsWith("Saved as", window.StatusText);
            }
            Assert.Equal([target], Directory.GetFiles(root));
        }
        finally { window.Close(); Directory.Delete(root, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Saving_metadata_reports_a_partial_file_when_the_native_volume_fills()
    {
        string? prepared = Environment.GetEnvironmentVariable("FILECAT_TEST_REPORT_SPACE_LIMIT");
        bool mac = OperatingSystem.IsMacOS();
        Assert.SkipWhen((!OperatingSystem.IsLinux() && !mac) || string.IsNullOrWhiteSpace(prepared),
            "Requires an isolated low-space Linux or macOS test volume.");
        var volume = new DriveInfo(prepared!);
        if (mac)
        {
            Assert.Equal("apfs", volume.DriveFormat);
            Assert.InRange(volume.TotalSize, 64L << 20, 512L << 20);
            Assert.InRange(volume.AvailableFreeSpace, 256L << 10, 5500L << 10);
        }
        else Assert.InRange(volume.TotalSize, 4L << 10, 64L << 10);
        string root = Directory.CreateDirectory(Path.Combine(prepared!, "report-" + Guid.NewGuid().ToString("N"))).FullName;
        string target = Path.Combine(root, "partial.txt");
        using var io = new DeviceIoScheduler();
        // Three-byte UTF-8 characters make a larger export without reaching the report's character limit.
        string note = mac ? new string('界', 1900 << 10) : new string('x', 256 << 10);
        using var reader = new FileDetailsReader(new MetadataRecords(note), io, "report-output", "/observation");
        var window = new ReportWindow("observation", reader);
        InstallPicker(window, () => Task.FromResult<IStorageFile?>(Chosen(target)));
        try
        {
            window.Show();
            await window.Reading!;
            byte[] text = Encoding.UTF8.GetBytes(window.Text);
            long expectedLength = text.LongLength + Encoding.UTF8.Preamble.Length;
            Assert.True(expectedLength > volume.AvailableFreeSpace, "The captured UTF-8 report must exceed the fixture's available space.");
            await Save(window);
            Assert.Contains("may be incomplete", window.StatusText);
            byte[] partial = File.ReadAllBytes(target);
            Assert.InRange(partial.LongLength, Encoding.UTF8.Preamble.Length + 1L, expectedLength - 1);
            Assert.True(partial.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.True(text.AsSpan().StartsWith(partial.AsSpan(Encoding.UTF8.Preamble.Length)), "The file should contain only the captured report prefix.");
        }
        finally { window.Close(); Directory.Delete(root, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Saving_metadata_on_full_NTFS_reports_the_written_prefix_and_rolls_back_replacement()
    {
        string? prepared = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_REPORT_SPACE_LIMIT");
        Assert.SkipWhen(!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(prepared),
            "Requires an opt-in NTFS image with less than 2 MiB available space.");
        var volume = new DriveInfo(Path.GetPathRoot(prepared!)!);
        Assert.Equal("NTFS", volume.DriveFormat);
        Assert.InRange(volume.TotalSize, 32L << 20, 128L << 20);
        Assert.InRange(volume.AvailableFreeSpace, 256L << 10, 2L << 20);

        string root = Directory.CreateDirectory(Path.Combine(prepared!, "report-" + Guid.NewGuid().ToString("N"))).FullName;
        string target = Path.Combine(root, "report.txt");
        using var io = new DeviceIoScheduler();
        var records = new MetadataRecords(new string('x', 3 << 20));
        using var reader = new FileDetailsReader(records, io, "report-output", Path.Combine(root, "observation"));
        var window = new ReportWindow("observation", reader);
        InstallPicker(window, () => Task.FromResult<IStorageFile?>(Chosen(target)));
        try
        {
            window.Show();
            await window.Reading!;
            byte[] text = Encoding.UTF8.GetBytes(window.Text);
            long expectedLength = text.LongLength + Encoding.UTF8.Preamble.Length;
            Assert.True(expectedLength > volume.AvailableFreeSpace);

            await Save(window);
            Assert.Contains("may be incomplete", window.StatusText);
            byte[] partial = File.ReadAllBytes(target);
            Assert.InRange(partial.LongLength, 64L << 10, expectedLength - 1);
            Assert.True(partial.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.True(text.AsSpan().StartsWith(partial.AsSpan(Encoding.UTF8.Preamble.Length)), "The file should contain only the captured report prefix.");
            File.Delete(target);

            byte[] original = Encoding.UTF8.GetBytes("Original metadata report");
            File.WriteAllBytes(target, original);
            var before = new FileInfo(target).LastWriteTimeUtc;
            await Save(window);
            Assert.StartsWith("It could not be saved", window.StatusText);
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.Equal(before, new FileInfo(target).LastWriteTimeUtc);
            Assert.Single(Directory.EnumerateFileSystemEntries(root));
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

    /// <summary>"tier" reads the full record; "cached" switches between two already-acquired tiers without any read.</summary>
    private static async Task PrepareRetirement(ReportWindow window, string retire)
    {
        if (retire != "cached") return;
        await window.SwitchDetailsTierForTestAsync(); // read the full record: both tiers are now cached
        await window.SwitchDetailsTierForTestAsync(); // back to the cached summary
    }

    private static async Task Retire(ReportWindow window, MetadataRecords records, string retire)
    {
        int reads = records.Reads;
        if (retire == "close")
        {
            window.Close();
            return;
        }
        await window.SwitchDetailsTierForTestAsync();
        Assert.Contains(UnqualifiedPayload, window.Text); // the full record is displayed while the output is still pending
        if (retire == "cached") Assert.Equal(reads, records.Reads);
    }

    [AvaloniaTheory]
    [InlineData("tier")]
    [InlineData("cached")]
    [InlineData("close")]
    public async Task A_tier_switch_or_close_cancels_a_pending_details_save_before_its_first_write(string retire)
    {
        string root = Directory.CreateTempSubdirectory("filecat-details-pending-save-").FullName;
        string target = Path.Combine(root, "report.txt");
        using var io = new DeviceIoScheduler();
        var records = new MetadataRecords("Pending save fixture", ReportContentKind.MetadataOnly);
        using var reader = new FileDetailsReader(records, io, "report-pending-save", Path.Combine(root, "observation"));
        var window = new ReportWindow("observation", reader);
        var pending = new TaskCompletionSource<IStorageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int pickerCalls = 0;
        InstallPicker(window, () => { pickerCalls++; return pending.Task; });
        try
        {
            window.Show();
            await window.Reading!;
            await PrepareRetirement(window, retire);
            var save = Save(window);
            Assert.Equal(1, pickerCalls);
            await Retire(window, records, retire);
            pending.SetResult(Chosen(target));
            await save.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(File.Exists(target));
            Assert.Empty(Directory.GetFiles(root));
            if (retire != "close") Assert.DoesNotContain("Saved as", window.StatusText);
        }
        finally
        {
            pending.TrySetResult(null);
            if (window.IsVisible) window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData("tier", "all")]
    [InlineData("tier", "source")]
    [InlineData("tier", "displayed")]
    [InlineData("cached", "all")]
    [InlineData("cached", "source")]
    [InlineData("cached", "displayed")]
    [InlineData("close", "all")]
    [InlineData("close", "source")]
    [InlineData("close", "displayed")]
    public async Task A_tier_switch_or_close_cancels_a_pending_details_copy_before_publication(string retire, string variant)
    {
        using var io = new DeviceIoScheduler();
        var records = new MetadataRecords("Pending copy fixture", ReportContentKind.MetadataOnly);
        using var reader = new FileDetailsReader(records, io, "report-pending-copy", "/observation");
        var window = new ReportWindow("observation", reader);
        SemaphoreSlim? held = null;
        try
        {
            window.Show();
            await window.Reading!;
            await PrepareRetirement(window, retire);
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var clipboard = window.Clipboard!;
            Task Copy() => variant switch
            {
                "all" => CopyAll(window),
                "source" => window.CopyTextAsync(displayed: false, explicitVisible: true, clipboard),
                _ => window.CopyTextAsync(displayed: true, explicitVisible: true, clipboard),
            };
            // Control: the same variant publishes when nothing retires it.
            await ClipboardExtensions.SetTextAsync(clipboard, "existing clipboard text");
            await Copy();
            string? copied = await ClipboardExtensions.TryGetTextAsync(clipboard);
            Assert.NotEqual("existing clipboard text", copied);
            Assert.Contains("File-system details", copied); // the report's first line is always captured

            await ClipboardExtensions.SetTextAsync(clipboard, "existing clipboard text");
            held = (SemaphoreSlim)typeof(ReportWindow).GetField("_copyWrites", InstanceFields)!.GetValue(window)!;
            await held.WaitAsync(TestContext.Current.CancellationToken); // hold publication at its gate
            var copy = Copy();
            await Retire(window, records, retire);
            held.Release();
            held = null;
            await copy.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("existing clipboard text", await ClipboardExtensions.TryGetTextAsync(clipboard));
        }
        finally
        {
            held?.Release();
            if (window.IsVisible) window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task A_real_text_selection_copy_is_classified_like_every_other_copy_variant(bool encrypted, bool unknown)
    {
        const string text = "Fixture supplementary report\nsecond value line";
        var window = ReportWindow.ForSupplementaryValue("fixture", _ => Task.FromResult(Report(text, encrypted, unknown)));
        try
        {
            window.Show();
            await Until(() => window.Text == text);
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var viewer = Assert.Single(window.GetVisualDescendants().OfType<FileCat.App.Controls.TextViewer>());
            Assert.True(viewer.Focus());
            var clipboard = window.Clipboard!;
            await ClipboardExtensions.SetTextAsync(clipboard, "existing clipboard text");
            // The viewer's own select-all gesture, then the window's Ctrl+C route for a source selection.
            window.KeyPress(Avalonia.Input.Key.A, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.A, "a");
            Assert.True(viewer.HasSelection);
            window.KeyPress(Avalonia.Input.Key.C, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.C, "c");
            bool allowed = !encrypted && !unknown;
            if (allowed)
            {
                for (int i = 0; i < 400 && await ClipboardExtensions.TryGetTextAsync(clipboard) != text; i++)
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                Assert.Equal(text, await ClipboardExtensions.TryGetTextAsync(clipboard));
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await Task.Delay(100, TestContext.Current.CancellationToken);
                Assert.Equal("existing clipboard text", await ClipboardExtensions.TryGetTextAsync(clipboard));
                Assert.True(viewer.HasSelection); // refused output leaves the selection for an explicit later choice
            }
        }
        finally { window.Close(); }
    }

    private sealed class MetadataRecords(string? note = null, ReportContentKind? fullContent = null) : IFileRecords
    {
        private int _reads;
        internal string? Note { get; set; } = note;
        /// <summary>Every snapshot acquisition, including the one paired with a full record.</summary>
        internal int Reads => Volatile.Read(ref _reads);
        public bool IsSupported => true;
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy report read.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            Interlocked.Increment(ref _reads);
            return new(path, DateTimeOffset.UtcNow, Guid.NewGuid()) { Kind = FileObjectKind.File, Notes = Note is { } current ? [current] : [] };
        }
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
