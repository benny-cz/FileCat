using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Records;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// A folder that keeps changing (C:\Windows, where logs are written every second) flashed every column read beside the
/// listing to "…" and the tab's ⟳ and "refreshing…" on each reread. A reread the folder's own changes start now keeps
/// what was read for unchanged items and shows nothing unless it is slow; one the user asks for still shows at once.
/// </summary>
public sealed class QuietRefreshTests
{
    private sealed class CountingRecords : IFileRecords
    {
        public ConcurrentDictionary<string, int> Reads { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsSupported => true;
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy reader invoked.");
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) => throw new InvalidOperationException("Full reader invoked.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            Reads.AddOrUpdate(Path.GetFileName(path), 1, (_, n) => n + 1);
            return new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
            {
                Kind = FileObjectKind.File,
                Allocated = ObservedFact<long>.Known(4096, FactScope.Entry, "fixture"),
            };
        }
    }

    [AvaloniaFact]
    public async Task A_reread_the_folder_starts_keeps_values_and_stays_quiet_while_one_asked_for_shows_at_once()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var records = new CountingRecords();
        foreach (var field in FileDetailsFields.Create(records, OperatingSystem.IsWindows())) services.Metadata.Register(field);
        try
        {
            var tab = vm.Workspace.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing && tab.Listing.TotalCount == 2);
            MetadataCellPresentation Allocated(bool demand)
            {
                int index = tab.Listing.FindStoreIndex("a.txt");
                var entry = tab.Listing.Store[index];
                return tab.MetadataPresentation(entry, index, FileDetailsFields.Allocated, tab.CaptureMetadataContext(entry, index), demand);
            }
            // Asked for as the list asks, once it has drawn the row: a read for a row not shown is dropped.
            await Until(() => tab.VisibleStoreIndices.Contains(tab.Listing.FindStoreIndex("a.txt")));
            Allocated(demand: true);
            await Until(() => Allocated(demand: false).State == MetadataState.Available);
            int readsBefore = records.Reads["a.txt"];
            var busy = new List<bool>();
            tab.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TabViewModel.ShowsBusy)) busy.Add(tab.ShowsBusy); };

            // The folder changes by itself: its watcher rereads it. a.txt did not change, so its value stays shown.
            File.WriteAllText(Path.Combine(root, "files", "c.txt"), "c");
            await Until(() => tab.Listing.TotalCount == 3 && !tab.Listing.IsRefreshing && tab.Listing.State == ListingState.Complete);
            var kept = Allocated(demand: false);
            Assert.Equal((MetadataState.Available, 4096L.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)), (kept.State, kept.Text));
            Assert.Equal(readsBefore, records.Reads["a.txt"]);
            Assert.DoesNotContain(true, busy);
            Assert.DoesNotContain("refreshing", tab.StatusLeft, StringComparison.Ordinal);

            // A reread the user asks for shows at once, and reads the values again.
            tab.Refresh();
            Assert.True(tab.ShowsBusy);
            await Until(() => !tab.Listing.IsRefreshing && tab.Listing.State == ListingState.Complete);
            Assert.False(tab.ShowsBusy);
            Assert.Contains(true, busy);
            await Until(() => tab.VisibleStoreIndices.Contains(tab.Listing.FindStoreIndex("a.txt")));
            Allocated(demand: true);
            await Until(() => records.Reads["a.txt"] > readsBefore && Allocated(demand: false).State == MetadataState.Available);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    /// <summary>
    /// A value past its field's interval is read again when the list draws it. Meanwhile it shows as it was, not with
    /// "(last known)" flashing through the column every interval; the inspection report, which reads nothing, still
    /// says it is last known.
    /// </summary>
    [AvaloniaFact]
    public async Task A_value_past_its_interval_shows_as_it_was_while_the_list_reads_it_again()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        using var hold = new ManualResetEventSlim(true);
        int reads = 0;
        services.Metadata.Register(new MetadataField("test-revalidate", "Revalidate", MetadataCost.Cheap, _ => true, (_, _) =>
        {
            int read = Interlocked.Increment(ref reads);
            if (read > 1) hold.Wait(TimeSpan.FromSeconds(10));
            return "v" + read;
        }, v => v as string ?? "", RefreshAfter: TimeSpan.FromMilliseconds(150), ObservationLane: true));
        try
        {
            var tab = vm.Workspace.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing && tab.Listing.TotalCount == 2);
            MetadataCellPresentation Cell(bool demand)
            {
                int index = tab.Listing.FindStoreIndex("a.txt");
                var entry = tab.Listing.Store[index];
                return tab.MetadataPresentation(entry, index, "test-revalidate", tab.CaptureMetadataContext(entry, index), demand);
            }
            await Until(() => tab.VisibleStoreIndices.Contains(tab.Listing.FindStoreIndex("a.txt")));
            Cell(demand: true);
            await Until(() => Cell(demand: false) is { State: MetadataState.Available, Text: "v1" });
            hold.Reset(); // the next read waits
            await Task.Delay(300, TestContext.Current.CancellationToken); // past the interval
            var drawn = Cell(demand: true); // the list draws it: the read again starts, and is held
            Assert.Equal("v1", drawn.Text);
            Assert.Contains("Being read again", drawn.Detail);
            Assert.Equal("v1 (last known)", Cell(demand: false).Text);
            hold.Set();
            // The new value (which may already be past its short interval again on a slow machine).
            await Until(() => Cell(demand: false).Text.StartsWith("v2", StringComparison.Ordinal));
        }
        finally
        {
            hold.Set();
            AccessibilityTests.Close(services, window, root);
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 1000 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
