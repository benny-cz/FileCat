using System.Buffers.Binary;
using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

/// <summary>
/// Windows' Recycle Bin among the places (the owner: "Recycle Bin icon is missing in panels ... opens the Recycle Bin
/// windows, since only it can work with files inside"; "place the recycle bin icon next to Download icon"; then "do (b)":
/// FileCat's own read-only view of it). A test platform's Shell stands in for Windows', so nothing opens on the desktop,
/// and the view reads a bin made here.
/// </summary>
public sealed class RecycleBinPlaceTests
{
    private sealed class RecordingShell : PortableShellServices
    {
        public int Opened;
        public bool? HasItems;

        public override bool CanOpenRecycleBin => true;

        public override bool? RecycleBinHasItems() => HasItems;

        public override void OpenRecycleBin() => Opened++;
    }

    private sealed class TestPlatform : PortablePlatform
    {
        private readonly RecycleBinProvider? _bin;

        public TestPlatform(IShellServices shell, RecycleBinProvider? bin = null)
        {
            Shell = shell;
            _bin = bin;
        }

        public override void RegisterProviders(ProviderRegistry registry)
        {
            base.RegisterProviders(registry);
            if (_bin is not null) registry.Register(_bin);
        }
    }

    /// <summary>A bin as Windows lays it out: a deleted file and a deleted folder holding a file.</summary>
    private static string MakeBin()
    {
        string bin = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bin-place", Guid.NewGuid().ToString("N")[..8])).FullName;
        void Record(string id, string original)
        {
            var data = new byte[28 + (original.Length + 1) * 2];
            BinaryPrimitives.WriteInt64LittleEndian(data, 2);
            BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), 5);
            BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc());
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), original.Length + 1);
            Encoding.Unicode.GetBytes(original).CopyTo(data, 28);
            File.WriteAllBytes(Path.Combine(bin, "$I" + id), data);
        }
        File.WriteAllText(Path.Combine(bin, "$RAAAAAA.txt"), "report");
        Record("AAAAAA.txt", @"C:\Docs\report.txt");
        Directory.CreateDirectory(Path.Combine(bin, "$RBBBBBB"));
        File.WriteAllText(Path.Combine(bin, "$RBBBBBB", "a.txt"), "a");
        Record("BBBBBB", @"C:\Old");
        return bin;
    }

    [AvaloniaFact]
    public async Task The_Recycle_Bin_opens_FileCats_view_with_Windows_window_beside_it()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        var shell = new RecordingShell { HasItems = true };
        string fake = MakeBin();
        var platformBefore = PlatformFactory.WindowsFactory;
        PlatformFactory.WindowsFactory = () => new TestPlatform(shell, new RecycleBinProvider(() => [('C', fake)]));
        try
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            try
            {
                var ct = TestContext.Current.CancellationToken;
                var places = vm.Places(vm.DriveButtons);
                var bin = Assert.Single(places, p => p.Title == "Recycle Bin");
                int downloads = places.FindIndex(p => p.Title == "Downloads");
                if (downloads >= 0) Assert.Equal(downloads + 1, places.IndexOf(bin));
                Assert.True(bin.OpensInPanel);
                Assert.Equal(Schemes.RecycleBin, bin.Location!.Scheme);
                var windows = Assert.Single(bin.Variants);
                Assert.False(windows.OpensInPanel);

                // The view in the panel: what was deleted, by the names it had.
                var panel = vm.Workspace.Panels[0];
                await vm.OpenPlaceAsync(panel, bin, newTab: false);
                var tab = panel.ActiveTab!;
                List<EntryData> Rows() => Enumerable.Range(0, tab.Listing.VisibleCount).Select(tab.Listing.GetVisible).ToList();
                for (int i = 0; i < 300 && (tab.Location?.Scheme != Schemes.RecycleBin || tab.Listing.State != ListingState.Complete); i++) await Task.Delay(10, ct);
                Assert.Equal("Recycle Bin", tab.DisplayPath);
                Assert.Equal(["Old", "report.txt"], Rows().Where(e => e.Kind != EntryKind.Parent).Select(e => e.Name.ToString()).Order(StringComparer.Ordinal));
                Assert.Equal(@"C:\Docs", tab.GetDetailsText(Rows().Single(e => e.Name == "report.txt")));
                // FILECAT_RECYCLE_BIN_SHOT: the window as drawn, to look at the view (this bin holds no one's names).
                if (Environment.GetEnvironmentVariable("FILECAT_RECYCLE_BIN_SHOT") is { Length: > 0 } shot) window.CaptureRenderedFrame()?.Save(shot);
                // Read-only: deleting says where it is done.
                var delete = vm.GetAvailability(CommandIds.Delete);
                Assert.False(delete.Enabled);
                Assert.Contains("Windows' Recycle Bin", delete.Reason);
                // Into a deleted folder.
                tab.Listing.SetFocus(Rows().FindIndex(e => e.Name == "Old"));
                Assert.True(tab.TryEnterFocused(out _));
                for (int i = 0; i < 300 && (tab.Location?.Path.Length is null or 0 || tab.Listing.State != ListingState.Complete); i++) await Task.Delay(10, ct);
                Assert.Equal(@"Recycle Bin\Old", tab.DisplayPath);
                Assert.Contains(Rows(), e => e.Name == "a.txt");

                // Windows' own window, from the place's second entry: no tab, the panel stays where it is.
                var where = tab.Location;
                await vm.OpenPlaceAsync(panel, windows, newTab: false);
                Assert.Equal(1, shell.Opened);
                Assert.Equal(where, panel.ActiveTab!.Location);
            }
            finally
            {
                AccessibilityTests.Close(services, window, root);
            }
        }
        finally
        {
            PlatformFactory.WindowsFactory = platformBefore;
            try { Directory.Delete(fake, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task Without_FileCats_view_the_place_opens_Windows_window_and_no_tab()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        var shell = new RecordingShell { HasItems = false };
        var platformBefore = PlatformFactory.WindowsFactory;
        PlatformFactory.WindowsFactory = () => new TestPlatform(shell);
        try
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            try
            {
                var bin = Assert.Single(vm.Places(vm.DriveButtons), p => p.Title == "Recycle Bin");
                Assert.Null(bin.Location);
                Assert.False(bin.OpensInPanel);
                Assert.NotNull(bin.Icon());
                var panel = vm.Workspace.Panels[0];
                var where = panel.ActiveTab!.Location;
                int tabs = panel.Tabs.Count;
                await vm.OpenPlaceAsync(panel, bin, newTab: false);
                await vm.OpenPlaceAsync(panel, bin, newTab: true);
                Assert.Equal(2, shell.Opened);
                Assert.Equal(where, panel.ActiveTab!.Location);
                Assert.Equal(tabs, panel.Tabs.Count);
            }
            finally
            {
                AccessibilityTests.Close(services, window, root);
            }
        }
        finally
        {
            PlatformFactory.WindowsFactory = platformBefore;
        }
    }

    [AvaloniaFact]
    public void Where_the_system_has_no_such_window_there_is_no_such_place()
    {
        // The portable platform the tests run on: no recycle bin window to open.
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            Assert.False(services.Shell.CanOpenRecycleBin);
            Assert.DoesNotContain(vm.Places(vm.DriveButtons), p => p.Title == "Recycle Bin");
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
