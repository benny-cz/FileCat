using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.HiddenData;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>D-55 in the window: a file's streams and attributes open as a list, are copied out, and deleted.</summary>
public sealed class HiddenDataUiTests
{
    private static Task Until(Func<bool> condition, string what) => Until(condition, () => what);

    /// <summary>Waits for <paramref name="condition"/>; <paramref name="what"/> says, when it fails, what the window showed then.</summary>
    private static async Task Until(Func<bool> condition, Func<string> what)
    {
        for (int i = 0; i < 400 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), what());
    }

    private static async Task Click(Window window, string text)
    {
        var overlay = window.FindControl<Panel>("OverlayHost")!;
        await Until(() => overlay.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == text && b.IsEffectivelyEnabled), $"a {text} button");
        overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text && b.IsEffectivelyEnabled)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaFact]
    public async Task A_files_streams_and_attributes_open_as_a_list_are_copied_out_and_deleted()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var previousHiddenSource = BuiltInFields.HiddenDataSource;
        try
        {
            string folder = Path.Combine(root, "files");
            string file = Path.Combine(folder, "tool.bin");
            File.WriteAllText(file, "contents");
            string mark, payload;
            if (OperatingSystem.IsWindows())
            {
                // The headless services use the portable platform. This native-stream job also needs
                // actual Windows file identities for its backing-file safety review.
                UseWindowsFileOperations(services.Jobs);
                var nativeStreams = new FileCat.Platform.Windows.WindowsHiddenData();
                services.Providers.Register(new HiddenDataProvider(nativeStreams));
                BuiltInFields.HiddenDataSource = nativeStreams;
                File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
                File.WriteAllText(file + ":payload", "hidden payload");
                (mark, payload) = ("Zone.Identifier", "payload");
            }
            else
            {
                (mark, payload) = OperatingSystem.IsMacOS() ? ("com.apple.quarantine", "org.filecat.payload") : ("user.xdg.origin.url", "user.filecat.payload");
                byte[] markValue = Encoding.UTF8.GetBytes(OperatingSystem.IsMacOS() ? "0083;66f1c2a3;Safari;" : "https://example.com/tool.bin");
                if (SetAttribute(file, mark, markValue) != 0)
                {
                    Assert.Skip("This file system keeps no extended attributes here.");
                    return;
                }
                SetAttribute(file, payload, Encoding.UTF8.GetBytes("hidden payload"));
            }
            string outFolder = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            var panels = vm.Workspace.Panels;
            panels[1].ActiveTab!.Navigate(Location.FileSystem(outFolder));
            var tab = panels[0].ActiveTab!;
            tab.Refresh();
            await Until(() => tab.Listing.State == ListingState.Complete && Rows(tab).Any(e => e.Name == "tool.bin"), "the file is listed");
            var fileEntry = Assert.Single(Rows(tab), e => e.Name == "tool.bin");
            services.Metadata.Forget("hidden", file);
            string device = Core.FileSystem.PathUtil.GetDeviceKey(file);
            await Until(() => services.Metadata.Get("hidden", file, fileEntry, device, false).State == MetadataState.Available,
                "the Streams/Attributes count is available");
            var initialInventory = Assert.IsType<FileDetailsFields.Cell>(services.Metadata.Peek("hidden", file, fileEntry).Value);
            Assert.StartsWith("2", initialInventory.Text);
            vm.Workspace.Activate(panels[0]);
            tab.Listing.SetFocus(Rows(tab).FindIndex(e => e.Name == "tool.bin") + (tab.Listing.HasParentRow ? 1 : 0));
            Assert.Equal(initialInventory.Text,
                tab.GetMetadataText(fileEntry, tab.Listing.FocusedStoreIndex, "hidden", out _));

            // Alt+Shift+Enter: its streams and attributes, each with what it says.
            vm.Execute(CommandIds.HiddenData);
            await Until(() => tab.Location?.Scheme == Schemes.HiddenData && tab.Listing.State == ListingState.Complete && Rows(tab).Count >= 2,
                () => $"the hidden data is listed (location: {tab.Location}; state: {tab.Listing.State}; inventory: {tab.SupplementaryInventoryText}; notification: {vm.Notification})");
            var markRow = Assert.Single(Rows(tab), e => e.Name == mark);
            Assert.StartsWith("Not read — F3 to view", tab.GetDetailsText(markRow));
            var payloadRow = Assert.Single(Rows(tab), e => e.Name == payload);
            Assert.StartsWith("Not read — F3 to view", tab.GetDetailsText(payloadRow));
            if (OperatingSystem.IsMacOS())
            {
                Assert.Contains("Compression backing: not inventoried", tab.SupplementaryInventoryText);
                Assert.True(payloadRow.Has(EntryFlags.Unavailable));
                Assert.Throws<NotSupportedException>(() => services.Providers.For(tab.Location!).OpenContent(
                    services.Providers.For(tab.Location!).GetItemRef(tab.Location!, payloadRow)));
                return;
            }

            // F5: copied out as a file by a job.
            tab.Listing.SetFocus(Rows(tab).FindIndex(e => e.Name == payload) + (tab.Listing.HasParentRow ? 1 : 0));
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                var report = await OpenSelectedValueReport(window);
                try { Assert.Contains("hidden payload", report.Text); }
                finally { report.Close(); }
            }
            vm.Execute(CommandIds.Copy);
            await Click(window, "Copy");
            await Until(() => services.Jobs.Jobs.Count > 0 && services.Jobs.Jobs[^1].State.IsFinished(), "the copy finishes");
            Assert.Equal(JobState.Completed, services.Jobs.Jobs[^1].State);
            Assert.Equal("hidden payload", File.ReadAllText(Path.Combine(outFolder, payload)));

            // Delete key: deleted after one confirmation; the file's contents untouched.
            await Until(() => !((FileCat.App.Views.OverlayDialogService)vm.Dialogs).IsOpen, "the copy dialog closed");
            tab.Listing.SetFocus(Rows(tab).FindIndex(e => e.Name == payload) + (tab.Listing.HasParentRow ? 1 : 0));
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            await Click(window, "Delete");
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing && Rows(tab).All(e => e.Name != payload),
                () => $"the payload is gone (FileCat said: {vm.Notification ?? "nothing"}; listed: {string.Join(", ", Rows(tab).Select(e => e.Name))}; " +
                      $"on disk: {string.Join(", ", (OperatingSystem.IsWindows() ? (IHiddenData)new FileCat.Platform.Windows.WindowsHiddenData() : new UnixHiddenData()).List(file).Select(i => i.Name))})");
            Assert.Contains(Rows(tab), e => e.Name == mark);
            Assert.Equal("contents", File.ReadAllText(file));
            Assert.Equal(MetadataState.NotRequested, services.Metadata.Peek("hidden", file, fileEntry).State);

            // Up: the folder, with the file under the cursor.
            tab.GoUp();
            await Until(() => tab.Location?.IsFileSystem == true && tab.Listing.State == ListingState.Complete, "back in the folder");
            Assert.True(tab.Listing.TryGetFocused(out var focused));
            Assert.Equal("tool.bin", focused.Name);
            await Until(() => services.Metadata.Get("hidden", file, focused, device, false).State == MetadataState.Available,
                "the parent file's Streams/Attributes count refreshes");
            var after = Assert.IsType<FileDetailsFields.Cell>(services.Metadata.Peek("hidden", file, focused).Value);
            Assert.StartsWith("1", after.Text);
            Assert.Equal(after.Text,
                tab.GetMetadataText(focused, tab.Listing.FocusedStoreIndex, "hidden", out _));
        }
        finally
        {
            BuiltInFields.HiddenDataSource = previousHiddenSource;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Mac_held_directory_attribute_can_be_reported_copied_and_deleted()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires the held macOS directory xattr adapter.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            const string name = "org.filecat.payload";
            string directory = Directory.CreateDirectory(Path.Combine(root, "files", "held")).FullName;
            string destination = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            Assert.Equal(0, SetAttribute(directory, name, "held directory value"u8.ToArray()));
            var sourcePanel = vm.Workspace.Panels[0];
            var tab = sourcePanel.ActiveTab!;
            vm.Workspace.Panels[1].ActiveTab!.Navigate(Location.FileSystem(destination));
            tab.Navigate(HiddenDataProvider.Of(directory));
            vm.Workspace.Activate(sourcePanel);
            await Until(() => tab.Listing.State == ListingState.Complete && Rows(tab).Any(row => row.Name == name),
                () => "the held directory attribute is listed. " + tab.SupplementaryInventoryText);
            Assert.True(tab.Listing.SupplementaryInventory!.CanDelete);
            Assert.True(Assert.IsType<HiddenDataProvider>(tab.Listing.Provider)
                .GetCapabilities(tab.Location!).HasFlag(LocationCapabilities.Delete));
            var row = Assert.Single(Rows(tab), candidate => candidate.Name == name);
            Assert.False(row.Has(EntryFlags.Unavailable));
            tab.Listing.SetFocus(Rows(tab).FindIndex(candidate => candidate.Name == name) + (tab.Listing.HasParentRow ? 1 : 0));
            var report = await OpenSelectedValueReport(window);
            try { Assert.Contains("held directory value", report.Text); }
            finally { report.Close(); }

            vm.Execute(CommandIds.Copy);
            await Click(window, "Copy");
            await Until(() => services.Jobs.Jobs.Count > 0 && services.Jobs.Jobs[^1].State.IsFinished(),
                "the held directory attribute copy finishes");
            Assert.Equal(JobState.Completed, services.Jobs.Jobs[^1].State);
            Assert.Equal("held directory value", File.ReadAllText(Path.Combine(destination, name)));
            Assert.Equal("held directory value", Encoding.UTF8.GetString(new UnixHiddenData().Read(directory,
                new HiddenItem(name, HiddenKind.Attribute, -1), 64)));
            await Until(() => !((OverlayDialogService)vm.Dialogs).IsOpen, "the copy dialog closed");
            tab.Listing.SetFocus(Rows(tab).FindIndex(candidate => candidate.Name == name) + (tab.Listing.HasParentRow ? 1 : 0));
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            await Click(window, "Delete");
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing &&
                Rows(tab).All(candidate => candidate.Name != name),
                () => "the held directory attribute is gone. " + vm.Notification);
            Assert.Empty(new UnixHiddenData().ReadInventory(directory, new(), TestContext.Current.CancellationToken).Items);
            Assert.Equal("held directory value", File.ReadAllText(Path.Combine(destination, name)));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Delete_confirmation_keeps_the_file_identity_from_before_a_refresh()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "Requires a bound supplementary-data adapter.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string file = Path.Combine(root, "files", "reviewed.bin");
            string previous = Path.Combine(root, "files", "previous.bin");
            string name = OperatingSystem.IsWindows() ? "payload" : "user.filecat.payload";
            IHiddenData adapter = OperatingSystem.IsWindows() ? new FileCat.Platform.Windows.WindowsHiddenData() : new UnixHiddenData();
            if (OperatingSystem.IsWindows()) services.Providers.Register(new HiddenDataProvider(adapter));
            WriteFixture("original");

            var tab = vm.ActiveTab!;
            tab.Navigate(HiddenDataProvider.Of(file));
            await Until(HasPayload, "the original supplementary item is listed");
            var original = tab.Listing.SupplementaryInventory!.Identity;
            Assert.NotNull(original);
            tab.Listing.SetFocus(Rows(tab).FindIndex(e => e.Name == name) + (tab.Listing.HasParentRow ? 1 : 0));
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            await Until(() => ((OverlayDialogService)vm.Dialogs).IsOpen, "the delete confirmation is open");

            File.Move(file, previous);
            WriteFixture("replacement");
            tab.Refresh();
            await Until(() => HasPayload() && tab.Listing.SupplementaryInventory!.Identity != original,
                "the inventory refreshed to the replacement file while confirmation was open");
            await Click(window, "Delete");
            await Until(() => vm.Notification?.Contains("different file", StringComparison.Ordinal) == true,
                "the stale delete was refused");

            var item = new HiddenItem(name, OperatingSystem.IsWindows() ? HiddenKind.Stream : HiddenKind.Attribute, -1);
            Assert.Equal("original", Encoding.UTF8.GetString(adapter.Read(previous, item, 32)));
            Assert.Equal("replacement", Encoding.UTF8.GetString(adapter.Read(file, item, 32)));

            bool HasPayload() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing &&
                Rows(tab).Any(e => e.Name == name);

            void WriteFixture(string value)
            {
                File.WriteAllText(file, value + " main bytes");
                if (OperatingSystem.IsWindows()) File.WriteAllText(file + ":" + name, value);
                else Assert.Equal(0, SetAttribute(file, name, Encoding.UTF8.GetBytes(value)));
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Copying_a_protected_stream_refuses_before_creating_any_output()
    {
        Assert.SkipUnless(FileCat.Platform.Windows.WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Requires a disposable Windows VM with EFS enabled.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string file = Path.Combine(root, "files", "protected.bin");
            string destination = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(file, "original main data");
            File.WriteAllText(file + ":payload", "protected supplementary data");
            File.Encrypt(file);
            UseWindowsFileOperations(services.Jobs);
            services.Providers.Register(new HiddenDataProvider(new FileCat.Platform.Windows.WindowsHiddenData()));
            var sourcePanel = vm.Workspace.Panels[0];
            var tab = sourcePanel.ActiveTab!;
            vm.Workspace.Panels[1].ActiveTab!.Navigate(Location.FileSystem(destination));
            tab.Navigate(HiddenDataProvider.Of(file));
            vm.Workspace.Activate(sourcePanel);
            await Until(() => tab.Listing.State == ListingState.Complete && Rows(tab).Any(row => row.Name == "payload"),
                () => "the protected stream is listed. " + tab.SupplementaryInventoryText);
            tab.Listing.SetFocus(Rows(tab).FindIndex(row => row.Name == "payload") + (tab.Listing.HasParentRow ? 1 : 0));

            var report = await OpenSelectedValueReport(window);
            try
            {
                Assert.Contains("Windows EFS", report.Text);
                Assert.DoesNotContain("protected supplementary data", report.Text);
                Assert.False(Assert.Single(report.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "Copy all").IsEnabled);
            }
            finally { report.Close(); }

            vm.Execute(CommandIds.Copy);
            await Click(window, "Copy");
            await Until(() => services.Jobs.Jobs.Count > 0 && services.Jobs.Jobs[^1].State.IsFinished(), "the EFS copy refusal finishes");

            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobState.Failed, job.State);
            Assert.Contains(job.Issues, issue => issue.Message.Contains("Windows EFS", StringComparison.Ordinal));
            Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
            Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.Encrypted));
            Assert.Equal("protected supplementary data", File.ReadAllText(file + ":payload"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task<ReportWindow> OpenSelectedValueReport(Window window)
    {
        await Until(() => window.GetVisualDescendants().OfType<Button>().Any(ReportButton), "the focused supplementary-report action is enabled");
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), ReportButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var report = Assert.Single(window.OwnedWindows.OfType<ReportWindow>());
        await Until(() => report.Reading?.IsCompleted == true, "the supplementary report finishes");
        return report;

        static bool ReportButton(Button button) => button.Content as string == "Report selected value…" && button.IsEffectivelyEnabled;
    }

    private static void UseWindowsFileOperations(JobManager jobs)
    {
        Assert.Empty(jobs.Jobs);
        Assert.False(jobs.HasActiveWork);
        var field = typeof(JobManager).GetField("_fs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(jobs, new FileCat.Platform.Windows.WindowsFileOperations());
        Assert.IsType<FileCat.Platform.Windows.WindowsFileOperations>(field.GetValue(jobs));
    }

    private static List<EntryData> Rows(FileCat.App.ViewModels.TabViewModel tab) =>
        Enumerable.Range(0, tab.Listing.VisibleCount).Select(tab.Listing.GetVisible).Where(e => e.Kind != EntryKind.Parent).ToList();

    private static int SetAttribute(string path, string name, byte[] value) =>
        OperatingSystem.IsMacOS() ? MacSet(path, name, value, value.Length, 0, 1) : LinuxSet(path, name, value, value.Length, 0);

    [DllImport("libc", EntryPoint = "setxattr", SetLastError = true)]
    private static extern int MacSet(string path, string name, byte[] value, nint size, uint position, int options);

    [DllImport("libc", EntryPoint = "lsetxattr", SetLastError = true)]
    private static extern int LinuxSet(string path, string name, byte[] value, nint size, int flags);
}
