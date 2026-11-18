using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
                { Kind = FileObjectKind.File, Notes = ["Summary " + Summaries] };
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
    public async Task Narrow_details_reflows_real_toolbar_and_spoken_line_controls()
    {
        using var io = new DeviceIoScheduler();
        using var records = new Records();
        using var reader = new FileDetailsReader(records, io, "details-layout", "/captured");
        var window = new ReportWindow("/captured", reader) { Width = 320, Height = 900 };
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
        using var reader = new FileDetailsReader(new UnixFileRecords(), io, "details-link", path);
        var window = new ReportWindow(path, reader, openTarget: (target, _) =>
        {
            destination.SetResult(target);
            return Task.CompletedTask;
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
        }
        finally { window.Close(); Directory.Delete(root, recursive: true); }
    }
}
