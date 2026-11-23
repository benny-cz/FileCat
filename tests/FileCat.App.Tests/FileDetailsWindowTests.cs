using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using FileCat.Core.Threading;

namespace FileCat.App.Tests;

public sealed class FileDetailsWindowTests
{
    private sealed class Records : IFileRecords, IDisposable
    {
        public bool IsSupported => true;
        public int Summaries, Fulls;
        public bool Fail;
        public bool BlockFull;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Unrestricted legacy reader invoked.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            if (Fail) throw new IOException("fixture failure");
            return new(path, DateTimeOffset.Parse("2026-11-17T10:00:00Z").AddMinutes(++Summaries), Guid.NewGuid())
            {
                Kind = FileObjectKind.File,
                Identity = ObservedFact<DetailsIdentity>.Known(new("fixture-volume", "recycled-id"), FactScope.Entry, "fixture"),
                LogicalSize = ObservedFact<long>.Known(64, FactScope.MainData, "fixture"),
                Notes = ["Summary " + Summaries],
            };
        }
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct)
        {
            Fulls++;
            if (BlockFull) { Entered.Set(); Release.Wait(TestContext.Current.CancellationToken); }
            return FileDetailsReport.Metadata(ReadSummary(path, ct), new InspectionSection("Full fixture", []));
        }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Tier_switch_cancels_stale_publication_pairs_full_with_its_summary_and_retains_failed_refresh()
    {
        using var io = new DeviceIoScheduler();
        using var records = new Records();
        using var reader = new FileDetailsReader(records, io, "details-ui", "/captured");
        var window = new ReportWindow("/captured", reader);
        window.Show();
        try
        {
            await window.Reading!;
            Assert.Contains("Summary 1", window.Text);
            records.BlockFull = true;
            var blocked = window.SwitchDetailsTierForTestAsync();
            Assert.True(records.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Contains("Previous observation", window.Text);
            await window.SwitchDetailsTierForTestAsync();
            records.Release.Set();
            await blocked;
            Assert.Contains("Summary 1", window.Text);
            Assert.DoesNotContain("Full fixture", window.Text);
            records.BlockFull = false;
            await window.SwitchDetailsTierForTestAsync();
            Assert.Contains("Full fixture", window.Text);
            int reads = records.Summaries;
            string paired = window.Text;
            await window.SwitchDetailsTierForTestAsync();
            Assert.Contains("Summary " + reads, window.Text);
            await window.SwitchDetailsTierForTestAsync();
            Assert.Equal(paired, window.Text);
            Assert.Equal(reads, records.Summaries);
            await window.SwitchDetailsTierForTestAsync();
            await window.RefreshDetailsForTestAsync();
            Assert.Contains("Summary " + (reads + 1), window.Text);
            var prior = window.Text;
            records.Fail = true;
            await window.RefreshDetailsForTestAsync();
            Assert.Contains("Read failed", window.Text);
            Assert.EndsWith(prior, window.Text);
            Assert.Equal("Read full record", window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Read full record").Content);
        }
        finally { records.Release.Set(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Mac_live_provider_details_window_keeps_dataless_files_metadata_only()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires a prepared macOS File Provider domain.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_MAC_PROVIDER_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Set FILECAT_TEST_MAC_PROVIDER_ROOT to an owned, enabled File Provider root.");

        const ulong dataless = 0x40000000; // Darwin SF_DATALESS.
        using var io = new DeviceIoScheduler();
        var records = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        string outputRoot = Directory.CreateTempSubdirectory("filecat-provider-report-").FullName;
        try
        {
            int index = 0;
            foreach (string path in new[] { Path.Join(root!, "dataless-file.bin"), Path.Join(root, "dataless-folder", "child.bin") })
            {
                var before = records.ReadSummary(path, ct);
                Assert.Equal(FactState.Known, before.NativeAttributes.State);
                Assert.NotEqual(0UL, before.NativeAttributes.Value & dataless);

                using var reader = new FileDetailsReader(records, io, "mac-file-provider-ui", path);
                var window = new ReportWindow(path, reader);
                string target = Path.Join(outputRoot, $"report-{index++}.txt");
                var storage = DispatchProxy.Create<IStorageProvider, EditSaveCopyTests.StorageProxy>();
                var picked = DispatchProxy.Create<IStorageFile, EditSaveCopyTests.StorageProxy>();
                ((EditSaveCopyTests.StorageProxy)(object)picked).Uri = new Uri(target);
                ((EditSaveCopyTests.StorageProxy)(object)storage).Save = () => Task.FromResult<IStorageFile?>(picked);
                typeof(TopLevel).GetField("_storageProvider", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(window, storage);
                window.Show();
                try
                {
                    await window.Reading!;
                    Assert.Contains("dataless", window.Text, StringComparison.OrdinalIgnoreCase);
                    await window.SwitchDetailsTierForTestAsync();
                    Assert.Contains("Full record · metadata only", window.Text);
                    Assert.Contains("Native getattrlist metadata", window.Text);
                    Assert.Contains("Downloads were disabled", window.Text);
                    string report = window.Text;
                    var binding = BindingFlags.NonPublic | BindingFlags.Instance;
                    await (Task)typeof(ReportWindow).GetMethod("CopyAsync", binding)!.Invoke(window, [true])!;
                    Assert.Equal(report, await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
                    await (Task)typeof(ReportWindow).GetMethod("SaveAsync", binding)!.Invoke(window, [])!;
                    Assert.Equal(report, File.ReadAllText(target));
                }
                finally { window.Close(); }

                var after = records.ReadSummary(path, ct);
                Assert.Equal(before.Identity.Value, after.Identity.Value);
                Assert.Equal(FactState.Known, after.NativeAttributes.State);
                Assert.NotEqual(0UL, after.NativeAttributes.Value & dataless);
            }
        }
        finally { Directory.Delete(outputRoot, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Full_report_replaces_the_summary_when_the_path_has_a_new_object_with_the_same_size_and_time()
    {
        using var io = new DeviceIoScheduler();
        IFileRecords records = OperatingSystem.IsWindows()
            ? new FileCat.Platform.Windows.WindowsFileRecords()
            : new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-details-replaced-").FullName;
        string path = Path.Join(root, "entry.txt");
        string previousPath = Path.Join(root, "retired.txt");
        const string oldPayload = "original-payload";
        const string newPayload = "replaced-payload";
        using var reader = new FileDetailsReader(records, io, "details-replaced", path);
        var window = new ReportWindow(path, reader);
        try
        {
            File.WriteAllText(path, oldPayload);
            var oldSummary = records.ReadSummary(path, ct);
            Assert.Equal(FactState.Known, oldSummary.Identity.State);
            var oldId = oldSummary.Identity.Value!;
            string oldEvidence = oldId.Device + " / " + oldId.Object;
            window.Show();
            await window.Reading!;
            Assert.Contains(oldEvidence, window.Text);

            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            File.Move(path, previousPath);
            File.WriteAllText(path, newPayload);
            File.SetLastWriteTimeUtc(path, timestamp);
            var replacement = records.ReadSummary(path, ct);
            Assert.Equal(FactState.Known, replacement.Identity.State);
            Assert.Equal(FactState.Known, replacement.LogicalSize.State);
            Assert.Equal(oldSummary.LogicalSize.Value, replacement.LogicalSize.Value);
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
            var newId = replacement.Identity.Value!;
            Assert.NotEqual(oldId, newId);
            string newEvidence = newId.Device + " / " + newId.Object;

            await window.SwitchDetailsTierForTestAsync();
            string full = window.Text;
            Assert.Contains(path, full);
            Assert.Contains("Full record · metadata only", full);
            Assert.Contains(newEvidence, full);
            Assert.DoesNotContain(oldEvidence, full);
            Assert.Contains("Changed object: identity differs from the previous observation.", full);
            Assert.DoesNotContain(oldPayload, full);
            Assert.DoesNotContain(newPayload, full);
            await window.SwitchDetailsTierForTestAsync();
            Assert.Contains(newEvidence, window.Text);
            Assert.DoesNotContain(oldEvidence, window.Text);
            await window.SwitchDetailsTierForTestAsync();
            Assert.Equal(full, window.Text);
            Assert.Equal(oldPayload, File.ReadAllText(previousPath));
            Assert.Equal(newPayload, File.ReadAllText(path));
        }
        finally
        {
            window.Close();
            File.Delete(path);
            File.Delete(previousPath);
            Directory.Delete(root);
        }
    }

    [AvaloniaFact]
    public async Task Narrow_details_reflows_real_toolbar_and_spoken_line_controls()
    {
        using var io = new DeviceIoScheduler();
        using var records = new Records();
        using var reader = new FileDetailsReader(records, io, "details-layout", "/captured");
        var window = new ReportWindow("/captured", reader) { Width = 320, Height = 420, FontSize = 20 };
        window.Show();
        try
        {
            await window.Reading!;
            window.UpdateLayout();
            string[] expected = ["Refresh", "Copy all", "Copy visible source text", "Copy displayed text", "Save as…", "Read full record", "Previous report line", "Next report line"];
            foreach (string name in expected)
            {
                var control = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == name);
                Assert.True(control.Bounds.Width <= 304, name);
                Assert.True(control.IsEffectivelyVisible, name);
            }
            var controls = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "ReportControlsScroll");
            var viewport = Assert.Single(controls.GetVisualDescendants().OfType<ScrollContentPresenter>(),
                p => ReferenceEquals(p.TemplatedParent, controls));
            Assert.True(controls.Extent.Height > controls.Viewport.Height, "Wrapped controls need a scrollable viewport.");
            Assert.True(viewport.Bounds.Height <= window.ClientSize.Height * 0.6);
            var viewer = Assert.Single(window.GetVisualDescendants().OfType<FileCat.App.Controls.TextViewer>());
            Assert.True(viewer.Bounds.Height >= 80, "The report text must retain a readable viewport.");
            var last = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Next report line");
            Assert.True(last.Focus(NavigationMethod.Tab));
            for (int attempt = 0; attempt < 100; attempt++)
            {
                window.UpdateLayout();
                if (last.TranslatePoint(default, viewport) is { } point &&
                    point.Y >= -0.6 && point.Y + last.Bounds.Height <= viewport.Bounds.Height + 0.6) break;
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.True(controls.Offset.Y > 0, "Focusing the last control must scroll it into view.");
            var visible = last.TranslatePoint(default, viewport)!.Value;
            Assert.InRange(visible.Y, -0.6, viewport.Bounds.Height - last.Bounds.Height + 0.6);
            var copy = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Copy all");
            Assert.True(copy.Focus(NavigationMethod.Tab));
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var notice = Assert.Single(window.GetVisualDescendants().OfType<ContentCopyNotice>());
            for (int attempt = 0; attempt < 100 && !notice.IsVisible; attempt++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            window.UpdateLayout();
            Assert.Contains("complete original report", notice.ResultText);
            var noticeAt = notice.TranslatePoint(default, window)!.Value;
            Assert.InRange(noticeAt.Y, 0, window.ClientSize.Height - notice.Bounds.Height + 0.6);
            var status = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("status"));
            Assert.True(status.Bounds.Height > 0 && status.TranslatePoint(default, window)!.Value.Y >= 0);
            Assert.True(viewer.Bounds.Height >= 80, "Copy feedback must leave the report text readable.");
            Assert.True(window.MinWidth <= 320);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Unix_link_navigation_requires_a_new_activation_after_the_source_changes()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Uses a native Unix symbolic link.");
        using var io = new DeviceIoScheduler();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-link-ui-").FullName;
        string path = Path.Combine(root, "link");
        var destination = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkedAt = DateTimeOffset.Parse("2026-11-17T10:00:00Z");
        using var reader = new FileDetailsReader(new UnixFileRecords(), io, "details-link", path);
        var window = new ReportWindow(path, reader, openTarget: (target, _) =>
        {
            destination.SetResult(target);
            return Task.FromResult(new FileDetailsTargetCheck(target, checkedAt,
                ObservedFact<FileObjectKind>.Known(FileObjectKind.File, FactScope.Entry, "fixture")));
        });
        try
        {
            File.CreateSymbolicLink(path, "first.txt");
            window.Show();
            await window.Reading!;
            Assert.Contains("first.txt", window.Text);
            var open = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Open target location");
            Assert.True(open.IsEffectivelyVisible);
            Assert.False(destination.Task.IsCompleted);

            string replacement = Path.Combine(root, "replacement");
            File.CreateSymbolicLink(replacement, "second.txt");
            File.Move(replacement, path, overwrite: true);
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            string notice = "Stored target or source identity changed";
            for (int i = 0; i < 300 && (!open.IsEnabled || !window.StatusText.Contains(notice)); i++)
                await Task.Delay(20, ct);
            Assert.Contains(notice, window.StatusText);
            Assert.True(open.IsEnabled);
            Assert.Contains("second.txt", window.Text);
            Assert.False(destination.Task.IsCompleted);

            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Path.Combine(root, "second.txt"), await destination.Task.WaitAsync(TimeSpan.FromSeconds(6), ct));
            await WaitForTargetActionAsync(open, ct);
            Assert.Matches(@"Target status\s+File", window.Text);
            Assert.Contains(checkedAt.ToString("O"), window.Text);
            await window.RefreshDetailsForTestAsync();
            Assert.Matches(@"Target status\s+Not checked", window.Text);
            Assert.DoesNotContain("Target checked", window.Text);
        }
        finally { window.Close(); Directory.Delete(root, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData(FactState.Missing)]
    [InlineData(FactState.Denied)]
    public async Task Target_failure_is_separate_from_the_link_and_refresh_retires_a_pending_check(FactState state)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Uses a native Unix symbolic link.");
        using var io = new DeviceIoScheduler();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-target-status-").FullName;
        string path = Path.Combine(root, "link");
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<FileDetailsTargetCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new FileDetailsTargetCheck(Path.Combine(root, "target"), DateTimeOffset.UtcNow,
            ObservedFact<FileObjectKind>.Unavailable(state, FactScope.Entry, "fixture", "fixture target unavailable"));
        using var reader = new FileDetailsReader(new UnixFileRecords(), io, "details-link-status", path);
        bool first = true;
        var window = new ReportWindow(path, reader, openTarget: (_, token) =>
        {
            if (first) { first = false; return Task.FromResult(failure); }
            entered.SetResult(token);
            return delayed.Task;
        });
        try
        {
            File.CreateSymbolicLink(path, "target");
            window.Show();
            await window.Reading!;
            var open = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Open target location");
            Assert.True(open.IsEffectivelyVisible);
            Assert.Matches(@"Target status\s+Not checked", window.Text);
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForTargetActionAsync(open, ct);
            Assert.Matches(@"Target status\s+" + state + @": fixture target unavailable", window.Text);
            Assert.Matches(@"Stored path\s+target", window.Text);

            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var targetToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(6), ct);
            await window.RefreshDetailsForTestAsync();
            Assert.True(targetToken.IsCancellationRequested);
            delayed.SetResult(failure);
            await WaitForTargetActionAsync(open, ct);
            Assert.Matches(@"Target status\s+Not checked", window.Text);
            Assert.DoesNotContain("fixture target unavailable", window.Text);
        }
        finally
        {
            delayed.TrySetCanceled();
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitForTargetActionAsync(Button targetAction, CancellationToken ct)
    {
        for (int i = 0; i < 300 && !targetAction.IsEnabled; i++) await Task.Delay(20, ct);
        Assert.True(targetAction.IsEnabled);
    }

    /// <summary>Counts every reader entry point; full reads build their own snapshot without touching the summary count.</summary>
    internal sealed class CountingRecords : IFileRecords
    {
        private int _summaries, _fulls, _legacy;
        public bool IsSupported => true;
        public int Summaries => Volatile.Read(ref _summaries);
        public int Fulls => Volatile.Read(ref _fulls);
        public int Legacy => Volatile.Read(ref _legacy);
        public System.Collections.Concurrent.ConcurrentQueue<string> Paths { get; } = new();
        public InspectionReport Read(string path, CancellationToken ct)
        {
            Interlocked.Increment(ref _legacy);
            throw new InvalidOperationException("Unrestricted legacy reader invoked.");
        }
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            Paths.Enqueue(path);
            return Snapshot(path, "summary", Interlocked.Increment(ref _summaries));
        }
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct)
        {
            if (policy != FileDetailsPolicy.MetadataOnly) throw new InvalidOperationException("Only the metadata-only policy is qualified.");
            Paths.Enqueue(path);
            int call = Interlocked.Increment(ref _fulls);
            return FileDetailsReport.Metadata(Snapshot(path, "full", call), new InspectionSection("Counting full fixture", [("Full read", $"#full-{call}#")]));
        }
        private static FileDetailsSnapshot Snapshot(string path, string tier, int call) => new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Kind = FileObjectKind.File,
            Identity = ObservedFact<DetailsIdentity>.Known(new("fixture-volume", "fixture-object"), FactScope.Entry, "fixture"),
            Notes = [$"#{tier}-{call}#"],
        };
    }

    /// <summary>Names-only inventory; any value, open or combined legacy list is counted as a violation.</summary>
    internal sealed class CountingHiddenData : Core.HiddenData.IHiddenData
    {
        private int _inventories, _values, _opens, _lists;
        public bool IsSupported => true;
        public int Inventories => Volatile.Read(ref _inventories);
        public int ValueReads => Volatile.Read(ref _values);
        public int Opens => Volatile.Read(ref _opens);
        public int LegacyLists => Volatile.Read(ref _lists);
        public IReadOnlyList<Core.HiddenData.HiddenItem> List(string path)
        {
            Interlocked.Increment(ref _lists);
            throw new InvalidOperationException("The combined legacy list reads values.");
        }
        public Core.HiddenData.HiddenInventory ReadInventory(string path, Core.HiddenData.HiddenInventoryRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _inventories);
            return new(path, DateTimeOffset.UtcNow, [new("fixture-stream", Core.HiddenData.HiddenKind.Stream, 4)],
                [new(Core.HiddenData.HiddenKind.Stream, Core.HiddenData.HiddenInventoryState.Complete, 1, "fixture"),
                 new(Core.HiddenData.HiddenKind.NtfsAttribute, Core.HiddenData.HiddenInventoryState.NotInspected, 0, "fixture")], "fixture")
                { BoundToObject = true };
        }
        public byte[] Read(string path, Core.HiddenData.HiddenItem item, int max) { Interlocked.Increment(ref _values); return []; }
        public Stream Open(string path, Core.HiddenData.HiddenItem item) { Interlocked.Increment(ref _opens); return new MemoryStream(); }
        public void Delete(string path, Core.HiddenData.HiddenItem item) => throw new InvalidOperationException("No deletion in this fixture.");
    }

    internal sealed class AdapterPlatform : Core.Platform.PortablePlatform
    {
        public AdapterPlatform(IFileRecords records, Core.HiddenData.IHiddenData? hidden)
        {
            FileRecords = records;
            if (hidden is not null) HiddenData = hidden;
        }
    }

    /// <summary>
    /// The main workspace with an observing reader. The Windows factory supplies it as the platform adapter (so command
    /// availability sees it); every OS also routes the view model's details dispatch through the same fake.
    /// </summary>
    internal static (FileCat.App.Services.AppServices Services, FileCat.App.ViewModels.MainViewModel Vm, MainWindow Window, string Root)
        OpenWithRecords(IFileRecords records, Core.HiddenData.IHiddenData? hidden = null)
    {
        var previous = Core.Platform.PlatformFactory.WindowsFactory;
        (FileCat.App.Services.AppServices, FileCat.App.ViewModels.MainViewModel, MainWindow, string) fixture;
        try
        {
            if (OperatingSystem.IsWindows()) Core.Platform.PlatformFactory.WindowsFactory = () => new AdapterPlatform(records, hidden);
            fixture = AccessibilityTests.OpenMainWindow();
        }
        finally { Core.Platform.PlatformFactory.WindowsFactory = previous; }
        fixture.Item2.FileRecordsForTests = records;
        return fixture;
    }

    internal static IDisposable TrackReportWindows(List<ReportWindow> opened) =>
        Window.WindowOpenedEvent.AddClassHandler<ReportWindow>((window, _) => opened.Add(window));

    internal static async Task Until(Func<bool> condition, string what)
    {
        for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), what);
    }

    /// <summary>Blocks one numbered summary read until released; every call is counted.</summary>
    private sealed class HeldRecords(int blockedCall) : IFileRecords, IDisposable
    {
        private int _summaries, _fulls, _legacy;
        public bool IsSupported => true;
        public int Summaries => Volatile.Read(ref _summaries);
        public int Other => Volatile.Read(ref _fulls) + Volatile.Read(ref _legacy);
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public InspectionReport Read(string path, CancellationToken ct)
        {
            Interlocked.Increment(ref _legacy);
            throw new InvalidOperationException("Unrestricted legacy reader invoked.");
        }
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref _summaries);
            if (call == blockedCall)
            {
                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
            }
            return new(path, DateTimeOffset.Parse("2026-11-17T10:00:00Z").AddMinutes(call), Guid.NewGuid())
            {
                Kind = FileObjectKind.File,
                Identity = ObservedFact<DetailsIdentity>.Known(new("fixture-volume", "fixture-object"), FactScope.Entry, "fixture"),
                Notes = [$"#summary-{call}#"],
            };
        }
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct)
        {
            Interlocked.Increment(ref _fulls);
            throw new InvalidOperationException("Unexpected full read.");
        }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Summary_open_refresh_and_tier_switches_issue_no_legacy_value_or_inventory_reads_even_after_the_supplementary_tab()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only the Windows platform factory accepts an injected supplementary-data adapter.");
        var records = new CountingRecords();
        var hidden = new CountingHiddenData();
        var (services, vm, main, root) = OpenWithRecords(records, hidden);
        var opened = new List<ReportWindow>();
        using var tracking = TrackReportWindows(opened);
        try
        {
            var tab = vm.ActiveTab!;
            await Until(() => tab.Listing.State == Core.Listing.ListingState.Complete && tab.Listing.FindStoreIndex("a.txt") >= 0, "the folder is listed");
            tab.Listing.FocusName("a.txt");
            string subject = Path.Join(root, "files", "a.txt");
            vm.Execute(Core.Commands.CommandIds.FileRecord);
            await Until(() => opened.Count == 1 && opened[0].Text.Contains("#summary-1#"), "the summary is published");
            var details = opened[0];
            Assert.Equal([subject], records.Paths);
            Assert.Equal((1, 0, 0), (records.Summaries, records.Fulls, records.Legacy));
            Assert.Equal(0, hidden.Inventories + hidden.ValueReads + hidden.Opens + hidden.LegacyLists);

            // F5: the explicit supplementary tab inventories names, then refreshes; the details window never does.
            var extra = Assert.Single(details.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Open streams and attributes");
            extra.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Workspace.ActiveTab is { Location.Scheme: Core.Resources.Schemes.HiddenData } t &&
                t.Listing.State == Core.Listing.ListingState.Complete && hidden.Inventories == 1, "the supplementary tab inventories once");
            var inventoryTab = vm.Workspace.ActiveTab!;
            inventoryTab.Refresh();
            await Until(() => hidden.Inventories == 2 && !inventoryTab.Listing.IsRefreshing &&
                inventoryTab.Listing.State == Core.Listing.ListingState.Complete, "the supplementary tab refreshes");

            await details.RefreshDetailsForTestAsync();
            Assert.Contains("#summary-2#", details.Text);
            Assert.Matches(@"Streams, EAs, xattrs and forks\s+Not inspected", details.Text);
            await details.SwitchDetailsTierForTestAsync(); // read the full record
            Assert.Contains("#full-1#", details.Text);
            Assert.Contains(FileDetailsReport.OmissionNotice, details.Text);
            await details.SwitchDetailsTierForTestAsync(); // cached summary
            await details.SwitchDetailsTierForTestAsync(); // cached full record
            Assert.Contains("#full-1#", details.Text);
            await details.SwitchDetailsTierForTestAsync(); // cached summary
            await details.RefreshDetailsForTestAsync();   // a summary refresh discards the paired full result
            Assert.Contains("#summary-3#", details.Text);
            await details.SwitchDetailsTierForTestAsync(); // a new full read
            Assert.Contains("#full-2#", details.Text);

            Assert.Equal((3, 2, 0), (records.Summaries, records.Fulls, records.Legacy));
            Assert.All(records.Paths, path => Assert.Equal(subject, path));
            Assert.Equal(2, hidden.Inventories);
            Assert.Equal(0, hidden.ValueReads + hidden.Opens + hidden.LegacyLists);
        }
        finally
        {
            foreach (var window in opened.Where(w => w.IsVisible)) window.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task Real_summary_and_full_reads_never_start_the_legacy_supplementary_value_acquisition()
    {
        IFileRecords records = OperatingSystem.IsWindows() ? new FileCat.Platform.Windows.WindowsFileRecords() : new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-details-omitted-").FullName;
        string path = Path.Join(root, "carrier.txt");
        const string Sentinel = "FILECAT-OMITTED-SUPPLEMENTARY-VALUE";
        File.WriteAllText(path, "main data");
        if (OperatingSystem.IsWindows()) File.WriteAllText(path + ":fixture-note", Sentinel);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var acquired = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Observe(string subject)
        {
            if (string.Equals(Path.GetFullPath(subject), Path.GetFullPath(path), comparison)) acquired.Enqueue(subject);
        }
        Core.HiddenData.HiddenDataSection.Acquiring += Observe;
        try
        {
            using var io = new DeviceIoScheduler();
            using var reader = new FileDetailsReader(records, io, "details-omitted-" + Guid.NewGuid().ToString("N"), path);
            var window = new ReportWindow(path, reader);
            window.Show();
            try
            {
                await window.Reading!;
                await window.SwitchDetailsTierForTestAsync();
                Assert.Contains("Full record · metadata only", window.Text);
                Assert.Contains("Not acquired:", window.Text); // Linux names the general supplementary data it omits
                Assert.Contains("supplementary-data values and previews", window.Text);
                Assert.DoesNotContain(Sentinel, window.Text);
                await window.RefreshDetailsForTestAsync(); // full again
                await window.SwitchDetailsTierForTestAsync(); // cached summary
                await window.RefreshDetailsForTestAsync(); // summary again
                Assert.DoesNotContain(Sentinel, window.Text);
                Assert.DoesNotContain("Read failed", window.Text);
                Assert.Empty(acquired);
            }
            finally { window.Close(); }

            // Positive control: the unrestricted legacy reader starts exactly this acquisition for the same object.
            var legacy = records.Read(path, ct);
            Assert.Single(acquired);
            if (OperatingSystem.IsWindows()) Assert.Contains(Sentinel, legacy.ToText());
        }
        finally
        {
            Core.HiddenData.HiddenDataSection.Acquiring -= Observe;
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Refresh_storm_from_the_button_F5_and_Control_R_keeps_one_running_read_and_publishes_only_the_latest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var io = new DeviceIoScheduler();
        using var records = new HeldRecords(blockedCall: 2);
        using var reader = new FileDetailsReader(records, io, "details-storm-" + Guid.NewGuid().ToString("N"), "/captured");
        var window = new ReportWindow("/captured", reader);
        window.Show();
        try
        {
            await window.Reading!;
            Assert.Contains("#summary-1#", window.Text);
            var status = Assert.IsType<TextBlock>(Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("status")).Child);
            var published = new List<string>();
            status.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) published.Add(window.Text); };
            var refresh = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Refresh");
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(records.Entered.Wait(TimeSpan.FromSeconds(10), ct));
            for (int i = 0; i < 12; i++)
            {
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
                window.KeyPress(Key.R, RawInputModifiers.Control, PhysicalKey.R, "r");
            }
            // One running native read; every later request only replaced the single pending one.
            Assert.Equal(2, records.Summaries);
            Assert.Contains("Previous observation", window.Text);
            Assert.Contains("#summary-1#", window.Text);
            var latest = window.Reading!;
            records.Release.Set();
            await latest.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Contains("#summary-3#", window.Text);
            Assert.DoesNotContain("Previous observation", window.Text);
            Assert.Equal(3, records.Summaries);
            Assert.Equal(0, records.Other);
            Assert.NotEmpty(published);
            Assert.DoesNotContain(published, text => text.Contains("#summary-2#"));
        }
        finally { records.Release.Set(); if (window.IsVisible) window.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_during_a_held_read_retires_its_permit_once_and_publishes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        string device = "details-close-" + Guid.NewGuid().ToString("N");
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var records = new HeldRecords(blockedCall: 2);
        var reader = new FileDetailsReader(records, io, device, "/captured");
        var window = new ReportWindow("/captured", reader);
        var leases = new List<IDisposable>();
        window.Show();
        try
        {
            await window.Reading!;
            for (int i = 0; i < 3; i++) leases.Add(await DetailsAdmission.AcquireAsync(device, ct));
            var refresh = window.RefreshDetailsForTestAsync();
            Assert.True(records.Entered.Wait(TimeSpan.FromSeconds(10), ct));
            Assert.Contains("Previous observation", window.Text);

            window.Close();
            await refresh.WaitAsync(TimeSpan.FromSeconds(10), ct); // publication retires without waiting for the native call
            Assert.Equal(string.Empty, window.Text);
            Assert.True(reader.ReadAsync(FileDetailsTier.Summary, ct).IsCanceled); // the window retired its reader
            var waiting = DetailsAdmission.AcquireAsync(device, ct);
            Assert.False(waiting.IsCompleted); // the started native call still owns the fourth device slot

            records.Release.Set();
            leases.Add(await waiting.WaitAsync(TimeSpan.FromSeconds(10), ct));
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var fifth = DetailsAdmission.AcquireAsync(device, cancel.Token);
            Assert.False(fifth.IsCompleted); // the slot was released exactly once
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fifth);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(string.Empty, window.Text);
            Assert.Equal(2, records.Summaries);
            Assert.Equal(0, records.Other);
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
    public async Task Unsupported_records_and_the_Properties_fallback_dispatch_without_native_reads()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var opened = new List<ReportWindow>();
        using var tracking = TrackReportWindows(opened);
        try
        {
            var tab = vm.ActiveTab!;
            await Until(() => tab.Listing.State == Core.Listing.ListingState.Complete && tab.Listing.FindStoreIndex("a.txt") >= 0, "the folder is listed");
            tab.Listing.FocusName("a.txt");
            string subject = Path.Join(root, "files", "a.txt");
            const string NoProperties = "System properties could not be opened for this item.";
            if (services.Platform.FileRecords is NoFileRecords)
            {
                // The platform has no reader: the command explains why, and executing it opens nothing.
                var availability = vm.GetAvailability(Core.Commands.CommandIds.FileRecord);
                Assert.False(availability.Enabled);
                Assert.Equal("File-system details are unsupported on this system.", availability.Reason);
                vm.ClearNotification();
                await vm.ExecuteAsync(Core.Commands.CommandIds.FileRecord);
                Assert.Equal(availability.Reason, vm.Notification);
            }
            // An unsupported reader refuses before any read, also through the Properties fallback.
            vm.FileRecordsForTests = new NoFileRecords();
            vm.ClearNotification();
            vm.ShowPropertiesForPath(subject);
            Assert.Equal(NoProperties, vm.Notification);
            Assert.Empty(opened);

            var records = new CountingRecords();
            vm.FileRecordsForTests = records;
            vm.ClearNotification();
            vm.ShowPropertiesForPath(subject);
            if (OperatingSystem.IsWindows())
            {
                // Windows Properties is the native sheet; FileCat's details are never a silent substitute.
                Assert.Equal(NoProperties, vm.Notification);
                Assert.Empty(opened);
                Assert.Empty(records.Paths);
            }
            else
            {
                // Linux and macOS fall back to the summary-first details for the exact path.
                await Until(() => opened.Count == 1 && opened[0].Text.Contains("#summary-1#"), "the fallback summary is published");
                Assert.Null(vm.Notification);
                Assert.Equal([subject], records.Paths);
            }
            Assert.Equal(0, records.Fulls + records.Legacy);
        }
        finally
        {
            foreach (var window in opened.Where(w => w.IsVisible)) window.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }
}
