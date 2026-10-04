using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;
public sealed class QuickViewFolderDemandTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(0, "sibling")]
    [InlineData(1000, "sibling")]
    [InlineData(0, "same-focus")]
    [InlineData(1000, "same-focus")]
    [InlineData(0, "quiet")]
    [InlineData(1000, "quiet")]
    [InlineData(0, "pending-sibling")]
    [InlineData(1000, "pending-sibling")]
    [InlineData(0, "pending-same-focus")]
    [InlineData(1000, "pending-same-focus")]
    [InlineData(0, "focused-change")]
    [InlineData(1000, "focused-change")]
    public async Task Folder_captions_follow_only_the_current_folder_demand(int bytes, string activity)
    {
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        string root = Path.GetFullPath(Path.Combine(parent, "filecat-caption-demand-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(parent, Path.GetDirectoryName(root));
        string files = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
        string counted = Directory.CreateDirectory(Path.Combine(files, "counted")).FullName;
        string sibling = Directory.CreateDirectory(Path.Combine(files, "sibling")).FullName;
        string payload = Path.Combine(counted, "one.bin");
        string other = Path.Combine(sibling, "one.bin");
        File.WriteAllBytes(payload, Enumerable.Repeat((byte)0x41, bytes).ToArray());
        File.WriteAllBytes(other, Enumerable.Repeat((byte)0x42, 37).ToArray());
        string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
        var before = new[] { Hash(payload), Hash(other) };
        var pane = new QuickViewPane();
        AppServices? services = null;
        MainViewModel? vm = null;
        MainWindow? window = null;
        var samples = new List<string>();
        var keys = new List<string>();
        var events = new List<object>();
        bool pending = activity.StartsWith("pending-", StringComparison.Ordinal);
        bool activeChange = activity == "focused-change";
        string mode = pending ? activity[8..] : activity;
        string[] expectedHashes = before;
        object? record = null;
        try
        {
            services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            vm = new MainViewModel(services);
            window = new MainWindow(vm, null) { Width = 1200, Height = 800 };
            vm.Initialize(null);
            foreach (var panel in vm.Workspace.Panels) panel.ActiveTab?.Navigate(Location.FileSystem(files));
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var tab = vm.ActiveTab!;
            var listing = tab.Listing;
            await Wait(() => listing.State == ListingState.Complete && listing.FocusName("counted"));
            vm.CountFolderSizes(tab);
            await Wait(() => tab.SizingFolders == 0 && listing.Store[listing.FindStoreIndex("counted")].Has(EntryFlags.SizeComputed));
            // Let the initial native metadata/monitor refresh settle before the controlled boundary.
            await Task.Delay(700, TestContext.Current.CancellationToken);
            await Wait(() => !listing.IsRefreshing && listing.State == ListingState.Complete);
            Assert.True(listing.FocusName("counted"));
            Assert.Equal(bytes, listing.Store[listing.FindStoreIndex("counted")].Size);
            Assert.Equal(bytes, DirectorySizer.Compute(counted, null, TestContext.Current.CancellationToken).Bytes);
            string Key()
            {
                if (!listing.TryGetFocused(out var e)) return "no-focus";
                var item = e.Kind == EntryKind.Parent ? null : listing.GetItemRef(listing.FocusedStoreIndex);
                return item is null ? "parent" : item.ToString() + "|" + e.Modified + "|" + e.Size
                    + (e.IsContainer ? "|" + (e.Flags & (EntryFlags.SizeComputed | EntryFlags.SizeLowerBound)) : "");
            }
            string initialKey = Key();
            listing.Changed += (_, change) => events.Add(new { Change = change.ToString(), Key = Key(), Refreshing = listing.IsRefreshing, State = listing.State.ToString() });
            pane.Attach(tab);
            string Caption() => ((TextBlock)typeof(QuickViewPane).GetField("_info", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!).Text ?? "";
            string expected = $"Folder · {Formatters.SizeWithUnit(bytes)}";
            if (!pending) await Wait(() => Caption() == expected);
            bool unchanged = true;
            bool positiveControl = false;
            if (activeChange)
            {
                listing.SetComputedSize("counted", bytes, true, Directory.GetLastWriteTimeUtc(counted).Ticks, lowerBound: true);
                string lower = $"Folder · at least {Formatters.SizeWithUnit(bytes)} (lower bound)";
                await Wait(() => Caption() == lower);
                samples.Add(Caption());
                Assert.NotEqual(initialKey, Key());
                File.WriteAllBytes(payload, Enumerable.Repeat((byte)0x43, bytes + 2345).ToArray());
                expectedHashes = new[] { Hash(payload), Hash(other) };
                Assert.Equal(bytes + 2345, DirectorySizer.Compute(counted, null, TestContext.Current.CancellationToken).Bytes);
                listing.SetComputedSize("counted", bytes + 2345, true, Directory.GetLastWriteTimeUtc(counted).Ticks);
                string changed = $"Folder · {Formatters.SizeWithUnit(bytes + 2345)}";
                await Wait(() => Caption() == changed);
                samples.Add(Caption());
                Assert.True(listing.FocusName("sibling"));
                listing.SetComputedSize("sibling", 37, true, Directory.GetLastWriteTimeUtc(sibling).Ticks);
                await Wait(() => Caption() == "Folder · 37 bytes");
                samples.Add(Caption());
                positiveControl = true;
            }
            else
            {
                for (int i = 0; i < 20; i++)
                {
                    if (mode == "sibling") listing.SetComputedSize("sibling", i, false);
                    if (mode == "same-focus") listing.SetComputedSize("counted", bytes, true, Directory.GetLastWriteTimeUtc(counted).Ticks);
                    await Task.Delay(25, TestContext.Current.CancellationToken);
                    keys.Add(Key());
                    unchanged &= Key() == initialKey;
                    Assert.True(listing.TryGetFocused(out var focus) && focus.Name == "counted");
                    Assert.Equal(bytes, listing.Store[listing.FindStoreIndex("counted")].Size);
                    samples.Add(Caption());
                }
                await Wait(() => Caption() == expected);
            }
            var relevant = pending ? samples.Skip(10).ToArray() : samples.ToArray();
            record = new { bytes, activity, Expected = expected, InitialKey = initialKey, Keys = keys.ToArray(), UnchangedKey = unchanged, Events = events.ToArray(), Samples = samples.ToArray(), WrongSamples = activeChange ? 0 : relevant.Count(s => s != expected), CaptionAfterQuiescence = Caption(), BeforeSHA256 = before, ExpectedAfterSHA256 = expectedHashes, AfterSHA256 = new[] { Hash(payload), Hash(other) }, PositiveControl = positiveControl, OwnedRealFiles = true, ControlledRowUpdates = activity != "quiet", PendingDemand = pending, NativeDesktop = false, PhysicalDevice = false };
            Assert.True(unchanged || activeChange, "A real focused preview key changed during this unchanged-demand case.");
            Assert.True(activeChange || relevant.All(s => s == expected), $"The unchanged focused caption was absent in {relevant.Count(s => s != expected)} of {relevant.Length} measured samples.");
        }
        finally
        {
            pane.Attach(null);
            if (vm is not null) foreach (var job in vm.Services.Jobs.Jobs) job.Cancel();
            window?.Close();
            if (vm is not null) foreach (var tab in vm.Workspace.Panels.SelectMany(p => p.Tabs).ToList()) tab.Dispose();
            services?.Dispose();
            Assert.Equal(expectedHashes, new[] { Hash(payload), Hash(other) });
            Assert.Equal(parent, Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
            if (record is not null) output.WriteLine(JsonSerializer.Serialize(record));
        }
    }
    private static async Task Wait(Func<bool> ready)
    {
        for (int i = 0; i < 500 && !ready(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready(), "Owned folder caption did not settle within ten seconds.");
    }
}
