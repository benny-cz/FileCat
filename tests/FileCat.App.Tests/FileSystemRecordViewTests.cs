using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using FileCat.Core.Threading;

namespace FileCat.App.Tests;

/// <summary>
/// DEC-05: File-system details keeps its metadata-first summary and full tiers, and offers the item's full file-system
/// record (D-56, <see cref="IFileRecords.Read"/>) only through its own explicit action. The record is read on the item's
/// device through the details reader (off the UI thread, admitted, cancellable), shown with its sections, tables, warnings
/// and notices under accessible names, and copied or saved only while it is bound to one object whose values cannot be
/// decrypted EFS content.
/// </summary>
public sealed class FileSystemRecordViewTests
{
    private const string ReadRecord = "Read file-system record";
    private const string ShowRecord = "Show file-system record";
    private const BindingFlags InstanceMembers = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Metadata tiers like the details fakes, plus a legacy record that says where and how it was read.</summary>
    private sealed class RecordFixture : IFileRecords, IDisposable
    {
        private int _summaries, _fulls, _records, _replaced;
        public bool IsSupported => true;
        public int Summaries => Volatile.Read(ref _summaries);
        public int Fulls => Volatile.Read(ref _fulls);
        public int Records => Volatile.Read(ref _records);
        public ConcurrentQueue<string> RecordPaths { get; } = new();
        public volatile bool ReadOnUiThread, ReadOffDeviceWorker, ReadWithoutAdmission;
        public volatile bool Block, ReplaceDuringRead;
        /// <summary>The summary's EFS observation: null is Unknown (a failed query), otherwise Known.</summary>
        public bool? Encrypted = false;
        public CancellationToken LastToken;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref _summaries);
            string id = Volatile.Read(ref _replaced) == 0 ? "fixture-object" : "replacement-object";
            return new(path, DateTimeOffset.Parse("2026-11-25T09:00:00Z").AddMinutes(call), Guid.NewGuid())
            {
                Kind = FileObjectKind.File,
                Identity = ObservedFact<DetailsIdentity>.Known(new("fixture-volume", id), FactScope.Entry, "fixture"),
                FileEncryption = Encrypted is { } known ? ObservedFact<bool>.Known(known, FactScope.Entry, "fixture")
                    : ObservedFact<bool>.Unavailable(FactState.Failed, FactScope.Entry, "fixture", "fixture query failed"),
                Notes = [$"#summary-{call}#"],
            };
        }

        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref _fulls);
            return FileDetailsReport.Metadata(ReadSummary(path, ct), new InspectionSection("Full fixture", [("Full read", $"#full-{call}#")]));
        }

        public InspectionReport Read(string path, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref _records);
            RecordPaths.Enqueue(path);
            if (Dispatcher.UIThread.CheckAccess()) ReadOnUiThread = true;
            if (!DeviceIoScheduler.IsDeviceWorker) ReadOffDeviceWorker = true;
            if (!FileObservationRequests.HasAdmission) ReadWithoutAdmission = true;
            LastToken = ct;
            if (Block)
            {
                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
            }
            if (ReplaceDuringRead) Interlocked.Exchange(ref _replaced, 1);
            return Sample($"#record-{call}#");
        }

        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }

    /// <summary>A record with every part the window must render: a warning, fields, a notice, a table with more rows, lines and a child.</summary>
    internal static InspectionReport Sample(string marker) => new("File-system record · fixture",
    [
        new("Item", [("Path", "fixture"), ("Marker", marker)]),
        new("Needs administrator rights", []) { Lines = ["Run FileCat as administrator to add the MFT record and its history in the change journal."] },
        new("Times", [("Created", "2026-11-25 10:00:00.0000000"), ("Modified", "2026-11-25 10:00:01.0000000")]),
        new("Layout", [("Clusters", "10")])
        {
            Table = new InspectionTable(["VCN", "LCN", "Clusters"], [["0", "4,096", "8"], ["8", "9,000", "2"]]) { More = "No more runs." },
            Lines = ["Runs as the volume keeps them."],
            Children = [new InspectionSection("Named stream", [("Runs", "resident")])],
        },
    ], ["The MFT record's sequence number changed while being read."]);

    /// <summary>The one visible action with this label (a hidden details action may carry the same words).</summary>
    private static Button Action(Window window, string content) =>
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == content && b.IsEffectivelyVisible);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task<ReportWindow> OpenDetails(FileCat.App.ViewModels.MainViewModel vm, List<ReportWindow> opened, string subject)
    {
        Assert.True(vm.ShowFileRecordForPath(subject));
        await FileDetailsWindowTests.Until(() => opened.Count == 1 && opened[0].Text.Contains("#summary-1#"), "the summary is published");
        return opened[0];
    }

    private static List<string> SpeakEveryLine(ReportWindow window)
    {
        var next = Action(window, "Next report line");
        var spoken = new List<string> { window.SpokenLine };
        for (int i = 0; i < 400 && next.IsEnabled; i++)
        {
            Click(next);
            spoken.Add(window.SpokenLine);
        }
        Assert.False(next.IsEnabled, "Every report line can be reached.");
        return spoken;
    }

    [AvaloniaFact]
    public async Task The_full_record_is_read_only_on_request_off_the_UI_thread_and_shown_with_sections_tables_warnings_and_notice()
    {
        using var records = new RecordFixture();
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(records);
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            var tab = vm.ActiveTab!;
            await FileDetailsWindowTests.Until(() => tab.Listing.State == Core.Listing.ListingState.Complete && tab.Listing.FindStoreIndex("a.txt") >= 0, "the folder is listed");
            string subject = Path.Join(root, "files", "a.txt");
            var details = await OpenDetails(vm, opened, subject);
            var viewer = Assert.Single(details.GetVisualDescendants().OfType<TextViewer>());
            Assert.Equal("File-system details", AutomationProperties.GetName(viewer));
            // Opening, refreshing and the metadata tiers never read the record.
            await details.RefreshDetailsForTestAsync();
            await details.SwitchDetailsTierForTestAsync();
            Assert.Contains("#full-1#", details.Text);
            await details.SwitchDetailsTierForTestAsync();
            Assert.Equal(0, records.Records);

            var read = Action(details, ReadRecord);
            Assert.True(read.IsEnabled);
            string help = AutomationProperties.GetHelpText(read) ?? "";
            Assert.Contains("administrator", help);
            Assert.Contains("root", help);
            Assert.Contains("Read full record", help); // it says how it differs from the metadata tier
            Click(read);
            await FileDetailsWindowTests.Until(() => details.Text.Contains("#record-1#"), "the record is published");

            Assert.Equal(1, records.Records);
            Assert.Equal([subject], records.RecordPaths);
            Assert.False(records.ReadOnUiThread, "The record must never be read on the UI thread.");
            Assert.False(records.ReadOffDeviceWorker, "The record is read by the item's device worker.");
            Assert.False(records.ReadWithoutAdmission, "The record read holds the details observation admission.");
            // Every part of the report, exactly as the report writes itself, under the record's own names.
            Assert.Contains(Sample("#record-1#").ToText(), details.Text);
            Assert.Equal("File-system record", AutomationProperties.GetName(viewer));
            Assert.Contains("File-system record", details.Title);
            Assert.StartsWith("File-system record", details.StatusText);
            Assert.DoesNotContain(details.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Read full record" && b.IsEffectivelyVisible);

            // The spoken line names each part: headings, warnings, fields, the notice, table columns per row.
            var spoken = SpeakEveryLine(details);
            void Said(string expected) => Assert.Contains(spoken, line => line.EndsWith(". " + expected, StringComparison.Ordinal));
            Said("File-system record · fixture");
            Said("Warning: The MFT record's sequence number changed while being read.");
            Said("Section: Needs administrator rights");
            Said("Run FileCat as administrator to add the MFT record and its history in the change journal.");
            Said("Marker: #record-1#");
            Said("Table with 2 rows. Columns: VCN, LCN, Clusters.");
            Said("Row 1 of 2. VCN: 0; LCN: 4,096; Clusters: 8");
            Said("Row 2 of 2. VCN: 8; LCN: 9,000; Clusters: 2");
            Said("No more runs.");
            Said("Subsection: Named stream");
            Said("Runs: resident");
            Assert.DoesNotContain(spoken, line => line.Contains('─'));

            // Back to the metadata tier it came from, then the cached record again without another read.
            var back = Action(details, "Show summary");
            Click(back);
            await FileDetailsWindowTests.Until(() => details.Text.Contains("#summary-") && !details.Text.Contains("#record-1#"), "the summary returns");
            Assert.Equal("File-system details", AutomationProperties.GetName(viewer));
            Assert.DoesNotContain("File-system record", details.Title);
            Click(Action(details, ShowRecord));
            await FileDetailsWindowTests.Until(() => details.Text.Contains("#record-1#"), "the cached record returns");
            Assert.Equal(1, records.Records);
            // F5 in the record reads the record again, and only the record.
            int summaries = records.Summaries, fulls = records.Fulls;
            await details.RefreshDetailsForTestAsync();
            Assert.Contains("#record-2#", details.Text);
            Assert.Equal(2, records.Records);
            Assert.Equal(fulls, records.Fulls);
            Assert.Equal(summaries + 2, records.Summaries); // the item's identity is read just before and just after the record
        }
        finally
        {
            foreach (var window in opened.Where(w => w.IsVisible)) window.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task The_real_record_shows_the_items_times_checks_stream_values_and_rights_notice_and_copies_when_bound()
    {
        IFileRecords records = OperatingSystem.IsWindows() ? new FileCat.Platform.Windows.WindowsFileRecords() : new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-real-record-").FullName;
        string path = Path.Join(root, "carrier.txt");
        const string Sentinel = "FILECAT-RECORD-STREAM-VALUE";
        File.WriteAllText(path, "main data");
        bool ntfs = OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(path)!).DriveFormat == "NTFS";
        if (ntfs) File.WriteAllText(path + ":fixture-note", Sentinel);
        using var io = new DeviceIoScheduler();
        using var reader = new FileDetailsReader(records, io, "real-record-" + Guid.NewGuid().ToString("N"), path);
        var window = new ReportWindow(path, reader);
        window.Show();
        try
        {
            await window.Reading!;
            Assert.DoesNotContain(Sentinel, window.Text); // the summary reads no values
            Click(Action(window, ReadRecord));
            await FileDetailsWindowTests.Until(() => window.Text.Contains("File-system record ·") || window.Text.Contains("Record read failed"), "the real record is published");
            string text = window.Text;
            Assert.DoesNotContain("Record read failed", text);
            Assert.Contains("Timestamp checks", text);
            Assert.DoesNotContain("Copy and Save are unavailable", text);
            if (ntfs)
            {
                // As the record always did: where a file came from is one look away, in its streams' values. The legacy
                // value reader may refuse one it cannot qualify (on a volume whose external-backing query fails, say); it
                // then says why in the same place.
                Assert.Contains("Streams and attributes (1)", text);
                Assert.Matches(@"fixture-note\s+Stream\s+27\s+(" + Sentinel + "|not read: )", text);
                Assert.Contains(Environment.IsPrivilegedProcess ? "MFT record" : "Needs administrator rights", text);
            }
            // The words for every line come from the real record's structure.
            var real = records.Read(path, ct);
            Assert.NotNull(FileCat.App.Services.SpokenReport.Lines(real, real.ToText()));
            var spoken = new List<string> { window.SpokenLine };
            var next = Action(window, "Next report line");
            for (int i = 0; i < 2000 && next.IsEnabled; i++) { Click(next); spoken.Add(window.SpokenLine); }
            Assert.Contains(spoken, line => line.Contains(". Section: Timestamp checks", StringComparison.Ordinal));
            if (ntfs) Assert.Contains(spoken, line => line.Contains(". Row 1 of 1. Name: fixture-note; Kind: Stream", StringComparison.Ordinal));

            var clipboard = window.Clipboard!;
            await ClipboardExtensions.SetTextAsync(clipboard, "existing clipboard text");
            await (Task)typeof(ReportWindow).GetMethod("CopyAsync", InstanceMembers)!.Invoke(window, [true])!;
            Assert.Equal(text, await ClipboardExtensions.TryGetTextAsync(clipboard));
        }
        finally
        {
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData("back")]
    [InlineData("close")]
    public async Task Leaving_or_closing_during_a_held_record_read_cancels_it_publishes_nothing_and_keeps_the_permit_until_it_returns(string leave)
    {
        var ct = TestContext.Current.CancellationToken;
        string device = "record-cancel-" + Guid.NewGuid().ToString("N");
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var records = new RecordFixture { Block = true };
        var reader = new FileDetailsReader(records, io, device, "/captured");
        var window = new ReportWindow("/captured", reader);
        var leases = new List<IDisposable>();
        window.Show();
        try
        {
            await window.Reading!;
            Assert.Contains("#summary-1#", window.Text);
            for (int i = 0; i < 3; i++) leases.Add(await DetailsAdmission.AcquireAsync(device, ct));
            Click(Action(window, ReadRecord));
            Assert.True(records.Entered.Wait(TimeSpan.FromSeconds(10), ct));
            Assert.Contains("Reading the file-system record", window.Text);
            Assert.Contains("Reading the file-system record", window.StatusText);
            // While it reads, the way back is the way out; nothing can be copied from a placeholder.
            Assert.False(Action(window, "Copy all").IsEnabled);

            if (leave == "close") window.Close();
            else Click(Action(window, "Show summary"));
            Assert.True(records.LastToken.IsCancellationRequested, "Leaving the record cancels its read.");
            if (leave == "back")
            {
                Assert.Contains("#summary-1#", window.Text);
                Assert.DoesNotContain("Reading the file-system record", window.Text);
                Assert.True(Action(window, ReadRecord).IsEnabled);
            }
            var waiting = DetailsAdmission.AcquireAsync(device, ct);
            Assert.False(waiting.IsCompleted); // the started native read still owns the fourth device slot

            records.Release.Set();
            leases.Add(await waiting.WaitAsync(TimeSpan.FromSeconds(10), ct));
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var fifth = DetailsAdmission.AcquireAsync(device, cancel.Token);
            Assert.False(fifth.IsCompleted); // the slot was released exactly once
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fifth);
            for (int i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, ct); }
            Assert.DoesNotContain("#record-", window.Text);
            Assert.Equal(1, records.Records);
            if (leave == "close") Assert.Equal(string.Empty, window.Text);
            else Assert.Contains("#summary-1#", window.Text);
        }
        finally
        {
            records.Release.Set();
            if (window.IsVisible) window.Close();
            reader.Dispose();
            foreach (var lease in leases) lease.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task A_second_window_says_it_waits_for_FileCats_one_record_read_and_reads_when_that_ends()
    {
        string id = Guid.NewGuid().ToString("N");
        using var io = new DeviceIoScheduler();
        using var held = new RecordFixture { Block = true };
        using var other = new RecordFixture();
        using var heldReader = new FileDetailsReader(held, io, "record-held-" + id, "/held");
        using var otherReader = new FileDetailsReader(other, io, "record-other-" + id, "/other");
        var first = new ReportWindow("/held", heldReader);
        var second = new ReportWindow("/other", otherReader);
        first.Show();
        second.Show();
        try
        {
            await first.Reading!;
            await second.Reading!;
            Click(Action(first, ReadRecord));
            Assert.True(held.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Click(Action(second, ReadRecord));
            await FileDetailsWindowTests.Until(() => second.Text.Contains("Waiting for another file-system record read to finish"), "the second window says why it waits");
            Assert.Contains("Waiting for another file-system record read to finish", second.StatusText);
            Assert.False(Action(second, "Copy all").IsEnabled);
            Assert.Equal(0, other.Records);
            held.Release.Set();
            await FileDetailsWindowTests.Until(() => first.Text.Contains("#record-1#") && second.Text.Contains("#record-1#"), "both records are read in turn");
            Assert.True(Action(second, "Copy all").IsEnabled);
        }
        finally
        {
            held.Release.Set();
            first.Close();
            second.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("bound")]
    [InlineData("encrypted")]
    [InlineData("unknown")]
    [InlineData("replaced")]
    public async Task The_record_is_copied_and_saved_like_the_details_only_while_bound_to_one_object_without_EFS_values(string kind)
    {
        // EFS exists only on Windows: elsewhere the encryption observation does not decide anything.
        bool refused = kind == "replaced" || (kind is "encrypted" or "unknown" && OperatingSystem.IsWindows());
        string root = Directory.CreateTempSubdirectory("filecat-record-output-").FullName;
        string target = Path.Combine(root, "record.txt");
        using var io = new DeviceIoScheduler();
        using var records = new RecordFixture
        {
            Encrypted = kind switch { "encrypted" => true, "unknown" => null, _ => false },
            ReplaceDuringRead = kind == "replaced",
        };
        using var reader = new FileDetailsReader(records, io, "record-output-" + Guid.NewGuid().ToString("N"), Path.Combine(root, "observation"));
        var window = new ReportWindow(Path.Combine(root, "observation"), reader);
        int pickerCalls = 0;
        var storage = DispatchProxy.Create<IStorageProvider, EditSaveCopyTests.StorageProxy>();
        ((EditSaveCopyTests.StorageProxy)(object)storage).Save = () =>
        {
            pickerCalls++;
            var file = DispatchProxy.Create<IStorageFile, EditSaveCopyTests.StorageProxy>();
            ((EditSaveCopyTests.StorageProxy)(object)file).Uri = new Uri(target);
            return Task.FromResult<IStorageFile?>(file);
        };
        typeof(TopLevel).GetField("_storageProvider", InstanceMembers)!.SetValue(window, storage);
        try
        {
            window.Show();
            await window.Reading!;
            Click(Action(window, ReadRecord));
            await FileDetailsWindowTests.Until(() => window.Text.Contains("#record-1#"), "the record is published");
            var clipboard = window.Clipboard!;
            await ClipboardExtensions.SetTextAsync(clipboard, "existing clipboard text");
            var copyAll = Action(window, "Copy all");
            var save = Action(window, "Save as…");
            await (Task)typeof(ReportWindow).GetMethod("CopyAsync", InstanceMembers)!.Invoke(window, [true])!;
            await window.CopyTextAsync(displayed: false, explicitVisible: true, clipboard);
            await window.CopyTextAsync(displayed: true, explicitVisible: true, clipboard);
            if (refused)
            {
                Assert.False(copyAll.IsEnabled);
                Assert.False(save.IsEnabled);
                Assert.Contains("Copy and Save are unavailable for this record", window.Text);
                if (kind == "replaced") Assert.Contains("changed while its record was read", window.Text);
                else Assert.Contains("EFS", window.Text);
                Assert.Equal("existing clipboard text", await ClipboardExtensions.TryGetTextAsync(clipboard));
                await (Task)typeof(ReportWindow).GetMethod("SaveAsync", InstanceMembers)!.Invoke(window, [])!;
                Assert.Equal(0, pickerCalls);
                Assert.False(File.Exists(target));
                // The metadata summary of the same window stays as publishable as before.
                Click(Action(window, "Show summary"));
                await FileDetailsWindowTests.Until(() => !window.Text.Contains("#record-1#"), "the summary returns");
                Assert.True(copyAll.IsEnabled);
                await (Task)typeof(ReportWindow).GetMethod("CopyAsync", InstanceMembers)!.Invoke(window, [true])!;
                Assert.Equal(window.Text, await ClipboardExtensions.TryGetTextAsync(clipboard));
            }
            else
            {
                Assert.True(copyAll.IsEnabled);
                Assert.True(save.IsEnabled);
                Assert.DoesNotContain("Copy and Save are unavailable", window.Text);
                string record = window.Text;
                await (Task)typeof(ReportWindow).GetMethod("CopyAsync", InstanceMembers)!.Invoke(window, [true])!;
                Assert.Equal(record, await ClipboardExtensions.TryGetTextAsync(clipboard));
                await (Task)typeof(ReportWindow).GetMethod("SaveAsync", InstanceMembers)!.Invoke(window, [])!;
                Assert.Equal(1, pickerCalls);
                Assert.Equal(record, File.ReadAllText(target));
                Assert.True(File.ReadAllBytes(target).AsSpan().StartsWith(Encoding.UTF8.Preamble));
                Assert.StartsWith("Saved as", window.StatusText);
            }
        }
        finally
        {
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }
}
