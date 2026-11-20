using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
}
