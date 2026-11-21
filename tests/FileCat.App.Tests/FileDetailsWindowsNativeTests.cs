using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Records;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using FileCat.Platform.Windows;
using Microsoft.Win32.SafeHandles;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// F-SCOPE on Windows: Open target location for native junctions and symbolic links, read by the real metadata reader.
/// Each activation first re-reads the source; only a confirmed ordinary file or folder named by the stored substitute
/// name is opened. Network targets are labelled before activation, and device, WSL, app-alias and unknown reparse
/// entries are never offered or followed.
/// </summary>
public sealed class FileDetailsWindowsNativeTests
{
    private const string OpenAction = "Open target location", CopyAction = "Copy stored target";
    private const string ChangedNotice = "Stored target or source identity changed";
    private const string ScopeLine = "The link object was inspected. Target contents were not read.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task Eventually(Func<bool> condition, string what, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10, Ct);
        Assert.True(condition(), what);
    }

    private static Button Action(ReportWindow window, string prefix) => Assert.Single(window.GetVisualDescendants().OfType<Button>(),
        button => button.Content is string text && text.StartsWith(prefix, StringComparison.Ordinal));

    private static bool SamePath(string? a, string b) => a is not null &&
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Lists <paramref name="folder"/> in the active tab, focuses <paramref name="name"/> and opens its details from the panel.</summary>
    private static async Task<ReportWindow> OpenDetailsAsync(MainViewModel vm, List<ReportWindow> opened, string folder, string name, string expected)
    {
        var tab = vm.ActiveTab!;
        if (SamePath(tab.Location?.Path, folder)) tab.Refresh();
        else tab.Navigate(Location.FileSystem(folder));
        await Eventually(() => SamePath(tab.Location?.Path, folder) && tab.Listing.State == ListingState.Complete &&
            !tab.Listing.IsRefreshing && tab.Listing.FindStoreIndex(name) >= 0, name + " is listed");
        Assert.True(tab.Listing.FocusName(name));
        int before = opened.Count;
        vm.Execute(CommandIds.FileRecord);
        await Eventually(() => opened.Count == before + 1 && opened[^1].Text.Contains(expected, StringComparison.Ordinal), "the summary is published");
        return opened[^1];
    }

    /// <summary>Activates the target action and waits until that explicit check has finished.</summary>
    private static async Task ActivateAsync(Button open)
    {
        Assert.True(open.IsEffectivelyVisible && open.IsEnabled);
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Eventually(() => open.IsEnabled, "the target check finishes");
    }

    private static (string Folder, string Name) Focused(TabViewModel tab) =>
        (tab.Location?.Path ?? "", tab.Listing.FocusedIndex >= 0 ? tab.Listing.GetVisible(tab.Listing.FocusedIndex).Name : "");

    /// <summary>
    /// Closes the details windows and the workspace, then deletes the fixture. Reparse points go first: a recursive
    /// delete asks the mount manager to unmount each junction, which a standard user may not do. Closed tabs can also
    /// release their folder watchers a moment after the workspace closes.
    /// </summary>
    private static async Task FinishAsync(List<ReportWindow> opened, Services.AppServices services, MainWindow main, string root)
    {
        foreach (var window in opened.Where(w => w.IsVisible)) window.Close();
        RemoveReparsePoints(root);
        AccessibilityTests.Close(services, main, root);
        for (int attempt = 0; attempt < 50 && Directory.Exists(root); attempt++)
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { await Task.Delay(100, Ct); }
        }
    }

    private static void RemoveReparsePoints(string folder)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                if (entry is DirectoryInfo) RemoveReparsePoints(entry.FullName);
            }
            else if (entry is DirectoryInfo) Directory.Delete(entry.FullName);
            else File.Delete(entry.FullName);
        }
    }

    [AvaloniaFact]
    public async Task A_junction_opens_its_target_folder_in_a_new_tab_only_on_explicit_activation()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Native Windows junction.");
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(new WindowsFileRecords());
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            string files = Path.Join(root, "files"), target = Directory.CreateDirectory(Path.Join(root, "targets", "folder-target")).FullName;
            File.WriteAllText(Path.Join(target, "inside.txt"), "inside");
            Junction.Create(Path.Join(files, "junction"), target);
            var window = await OpenDetailsAsync(vm, opened, files, "junction", "Junction or volume mount");
            var panel = vm.Workspace.ActivePanel!;
            int tabs = panel.Tabs.Count;
            Assert.Matches(@"Stored path\s+\\\?\?\\" + Regex.Escape(target), window.Text);
            Assert.Matches(@"Calculated path\s+" + Regex.Escape(target), window.Text);
            Assert.Matches(@"Target status\s+Not checked", window.Text);
            Assert.Contains(ScopeLine, window.Text);
            Assert.True(Action(window, CopyAction).IsEffectivelyVisible);
            var open = Action(window, OpenAction);
            Assert.Equal(OpenAction, open.Content);
            Assert.Equal(tabs, panel.Tabs.Count); // nothing is opened before the explicit activation

            await ActivateAsync(open);
            await Eventually(() => SamePath(panel.ActiveTab?.Location?.Path, target), "the target folder opens");
            Assert.Equal(tabs + 1, panel.Tabs.Count);
            Assert.Matches(@"Target status\s+Directory", window.Text);
            Assert.Contains("Target checked", window.Text);
            Assert.True(Directory.Exists(target));
            Assert.Equal("inside", File.ReadAllText(Path.Join(target, "inside.txt")));
        }
        finally { await FinishAsync(opened, services, main, root); }
    }

    [AvaloniaFact]
    public async Task A_file_link_opens_the_target_parent_with_the_name_focused_without_opening_the_file()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Native Windows symbolic link.");
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(new WindowsFileRecords());
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            string files = Path.Join(root, "files"), targets = Directory.CreateDirectory(Path.Join(root, "targets")).FullName;
            string target = Path.Join(targets, "file-target.txt");
            File.WriteAllText(target, "target payload");
            File.CreateSymbolicLink(Path.Join(files, "file-link"), target);
            var window = await OpenDetailsAsync(vm, opened, files, "file-link", "Symbolic link");
            var panel = vm.Workspace.ActivePanel!;
            int tabs = panel.Tabs.Count;
            Assert.Contains(ScopeLine, window.Text);

            // No other open may read, write or delete the target meanwhile; metadata-only opens are not share-checked.
            using (var hold = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await ActivateAsync(Action(window, OpenAction));
                await Eventually(() => SamePath(Focused(panel.ActiveTab!).Folder, targets) && Focused(panel.ActiveTab!).Name == "file-target.txt",
                    "the target's folder opens with the target focused");
                Assert.Equal(tabs + 1, panel.Tabs.Count);
                Assert.Matches(@"Target status\s+File", window.Text);
            }
            Assert.Equal("target payload", File.ReadAllText(target));
        }
        finally { await FinishAsync(opened, services, main, root); }
    }

    [AvaloniaFact]
    public async Task A_junction_retargeted_in_place_keeps_its_identity_and_needs_a_new_activation()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Native Windows junction.");
        var records = new WindowsFileRecords();
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(records);
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            string files = Path.Join(root, "files"), junction = Path.Join(files, "moving");
            string first = Directory.CreateDirectory(Path.Join(root, "targets", "first")).FullName;
            string second = Directory.CreateDirectory(Path.Join(root, "targets", "second")).FullName;
            Junction.Create(junction, first);
            var window = await OpenDetailsAsync(vm, opened, files, "moving", "Junction or volume mount");
            var panel = vm.Workspace.ActivePanel!;
            int tabs = panel.Tabs.Count;
            Assert.Contains(first, window.Text, StringComparison.OrdinalIgnoreCase);
            var before = records.ReadSummary(junction, Ct).Identity;

            // The payload changes on the same object: only the stored-target comparison can notice.
            SetMountPoint(junction, @"\??\" + second, second);
            Assert.Equal(before, records.ReadSummary(junction, Ct).Identity);
            var open = Action(window, OpenAction);
            await ActivateAsync(open);
            await Eventually(() => window.StatusText.Contains(ChangedNotice, StringComparison.Ordinal), "the change is announced");
            Assert.Contains(second, window.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Matches(@"Target status\s+Not checked", window.Text);
            Assert.Equal(tabs, panel.Tabs.Count);

            await ActivateAsync(open);
            await Eventually(() => SamePath(panel.ActiveTab?.Location?.Path, second), "the new target opens after a second activation");
            Assert.Equal(tabs + 1, panel.Tabs.Count);
            Assert.DoesNotContain(panel.Tabs, tab => SamePath(tab.Location?.Path, first));
        }
        finally { await FinishAsync(opened, services, main, root); }
    }

    [AvaloniaFact]
    public async Task A_replaced_junction_with_the_same_stored_target_is_a_new_object_and_needs_a_new_activation()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Native Windows junction.");
        var records = new WindowsFileRecords();
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(records);
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            string files = Path.Join(root, "files"), junction = Path.Join(files, "replaced");
            string target = Directory.CreateDirectory(Path.Join(root, "targets", "same")).FullName;
            Junction.Create(junction, target);
            var window = await OpenDetailsAsync(vm, opened, files, "replaced", "Junction or volume mount");
            var panel = vm.Workspace.ActivePanel!;
            int tabs = panel.Tabs.Count;
            var before = records.ReadSummary(junction, Ct);

            // Same name, same stored payload, a new filesystem object: only the identity comparison can notice.
            Directory.Delete(junction);
            Junction.Create(junction, target);
            var after = records.ReadSummary(junction, Ct);
            Assert.Equal(before.Target, after.Target);
            Assert.NotEqual(before.Identity, after.Identity);
            var open = Action(window, OpenAction);
            await ActivateAsync(open);
            await Eventually(() => window.StatusText.Contains(ChangedNotice, StringComparison.Ordinal), "the change is announced");
            Assert.Contains(after.Identity.Value!.Object, window.Text, StringComparison.Ordinal);
            Assert.Equal(tabs, panel.Tabs.Count);

            await ActivateAsync(open);
            await Eventually(() => SamePath(panel.ActiveTab?.Location?.Path, target), "the target opens after a second activation");
            Assert.Equal(tabs + 1, panel.Tabs.Count);
        }
        finally { await FinishAsync(opened, services, main, root); }
    }

    [AvaloniaFact]
    public async Task Missing_and_link_targets_are_reported_and_open_no_tab()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Native Windows symbolic links and junctions.");
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(new WindowsFileRecords());
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            string files = Path.Join(root, "files"), targets = Directory.CreateDirectory(Path.Join(root, "targets")).FullName;
            string missing = Path.Join(targets, "missing.txt");
            File.CreateSymbolicLink(Path.Join(files, "dangling"), missing);
            string real = Directory.CreateDirectory(Path.Join(targets, "real")).FullName;
            string inner = Path.Join(targets, "inner-junction");
            Junction.Create(inner, real);
            Directory.CreateSymbolicLink(Path.Join(files, "chain"), inner);
            var panel = vm.Workspace.ActivePanel!;

            var dangling = await OpenDetailsAsync(vm, opened, files, "dangling", "Symbolic link");
            int tabs = panel.Tabs.Count;
            await ActivateAsync(Action(dangling, OpenAction));
            await Eventually(() => dangling.StatusText.Contains("Target could not be opened: Missing", StringComparison.Ordinal), "the missing target is reported");
            Assert.Matches(@"Target status\s+Missing", dangling.Text);
            Assert.Matches(@"Calculated path\s+" + Regex.Escape(missing), dangling.Text);
            Assert.Equal(tabs, panel.Tabs.Count);
            Assert.False(File.Exists(missing));

            // The stored target is itself a reparse point: it is never confirmed as an ordinary file or folder.
            var chain = await OpenDetailsAsync(vm, opened, files, "chain", "Symbolic link");
            await ActivateAsync(Action(chain, OpenAction));
            await Eventually(() => chain.StatusText.Contains("Target could not be opened: Unsupported", StringComparison.Ordinal), "the link target is refused");
            Assert.Contains("could not be confirmed as an ordinary file or folder", chain.Text);
            Assert.Equal(tabs, panel.Tabs.Count);
            Assert.DoesNotContain(panel.Tabs, tab => SamePath(tab.Location?.Path, real) || SamePath(tab.Location?.Path, inner));
        }
        finally { await FinishAsync(opened, services, main, root); }
    }

    [AvaloniaFact]
    public async Task A_relative_link_under_a_redirected_parent_opens_where_Windows_resolves_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Native Windows symbolic link under a junction.");
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(new WindowsFileRecords());
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            string files = Path.Join(root, "files");
            string real = Directory.CreateDirectory(Path.Join(root, "a", "b", "real")).FullName;
            string redirect = Path.Join(files, "redirect");
            Junction.Create(redirect, real);
            File.CreateSymbolicLink(Path.Join(real, "link"), @"..\landed.txt");
            // Two candidates: lexical (beside the junction as named) and physical (beside the junction's target).
            File.WriteAllText(Path.Join(files, "landed.txt"), "lexical");
            File.WriteAllText(Path.Join(root, "a", "b", "landed.txt"), "physical");
            // Windows resolves a relative link against the path it was opened by, not the junction's target.
            Assert.Equal("lexical", File.ReadAllText(Path.Join(redirect, "link")));

            var window = await OpenDetailsAsync(vm, opened, redirect, "link", "Symbolic link");
            Assert.Matches(@"Relative\s+Yes", window.Text);
            Assert.Matches(@"Calculated path\s+" + Regex.Escape(Path.Join(files, "landed.txt")), window.Text);
            Assert.Contains("Only the final component is inspected without following links; parent resolution can traverse mounts or providers.", window.Text);
            var panel = vm.Workspace.ActivePanel!;
            await ActivateAsync(Action(window, OpenAction));
            await Eventually(() => SamePath(Focused(panel.ActiveTab!).Folder, files) && Focused(panel.ActiveTab!).Name == "landed.txt",
                "the lexical target opens focused");
            Assert.DoesNotContain(panel.Tabs, tab => SamePath(tab.Location?.Path, Path.Join(root, "a", "b")));
            Assert.Equal("physical", File.ReadAllText(Path.Join(root, "a", "b", "landed.txt")));
        }
        finally { await FinishAsync(opened, services, main, root); }
    }

    [AvaloniaFact]
    public async Task A_forged_print_name_is_shown_but_copy_and_navigation_use_the_stored_substitute_name()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Native Windows junction.");
        var (services, vm, main, root) = FileDetailsWindowTests.OpenWithRecords(new WindowsFileRecords());
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            string files = Path.Join(root, "files");
            string actual = Directory.CreateDirectory(Path.Join(root, "targets", "actual")).FullName;
            string decoy = Directory.CreateDirectory(Path.Join(root, "targets", "decoy")).FullName;
            string junction = Directory.CreateDirectory(Path.Join(files, "forged")).FullName;
            SetMountPoint(junction, @"\??\" + actual, decoy);
            var window = await OpenDetailsAsync(vm, opened, files, "forged", "Junction or volume mount");
            Assert.Matches(@"Print name\s+" + Regex.Escape(decoy), window.Text);
            Assert.Matches(@"Calculated path\s+" + Regex.Escape(actual), window.Text);

            Action(window, CopyAction).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            string? copied = null;
            for (int i = 0; i < 500 && copied != @"\??\" + actual; i++)
            {
                await Task.Delay(10, Ct);
                copied = await ClipboardExtensions.TryGetTextAsync(window.Clipboard!);
            }
            Assert.Equal(@"\??\" + actual, copied);
            var panel = vm.Workspace.ActivePanel!;
            await ActivateAsync(Action(window, OpenAction));
            await Eventually(() => SamePath(panel.ActiveTab?.Location?.Path, actual), "the substitute target opens");
            Assert.DoesNotContain(panel.Tabs, tab => SamePath(tab.Location?.Path, decoy));
        }
        finally { await FinishAsync(opened, services, main, root); }
    }

    /// <summary>A details window on the real reader whose target callback only records; none of these may be activated.</summary>
    private static async Task<(ReportWindow Window, List<string> Calls)> ShowDetailsAsync(DeviceIoScheduler io, string path)
    {
        var calls = new List<string>();
        var reader = new FileDetailsReader(new WindowsFileRecords(), io, "details-native-" + Guid.NewGuid().ToString("N"), path);
        var window = new ReportWindow(path, reader, openTarget: (target, _) =>
        {
            calls.Add(target);
            return Task.FromResult(new FileDetailsTargetCheck(target, DateTimeOffset.UtcNow,
                ObservedFact<FileObjectKind>.Unavailable(FactState.Unsupported, FactScope.Entry, "fixture", "No navigation was expected.")));
        });
        window.Closed += (_, _) => reader.Dispose();
        window.Show();
        await window.Reading!;
        return (window, calls);
    }

    [AvaloniaFact]
    public async Task Network_targets_are_labelled_and_device_and_pipe_targets_are_not_offered()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Native Windows symbolic links.");
        using var io = new DeviceIoScheduler();
        string root = Directory.CreateTempSubdirectory("fc-details-scope-").FullName;
        var windows = new List<ReportWindow>();
        try
        {
            const string share = @"\\filecat-fixture.invalid\share\folder";
            string network = Path.Join(root, "network");
            Directory.CreateSymbolicLink(network, share);
            var (window, calls) = await ShowDetailsAsync(io, network);
            windows.Add(window);
            var open = Action(window, OpenAction);
            Assert.True(open.IsEffectivelyVisible);
            Assert.Equal(OpenAction + " (network)", open.Content);
            Assert.True(Action(window, CopyAction).IsEffectivelyVisible);
            Assert.Matches(@"Stored path\s+\\\?\?\\UNC\\filecat-fixture\.invalid\\share\\folder", window.Text);
            Assert.Matches(@"Calculated path\s+" + Regex.Escape(share), window.Text);
            Assert.Matches(@"Target status\s+Not checked", window.Text);
            Assert.Empty(calls);

            // A device and a remote pipe are stored targets that can be copied, never navigated.
            foreach (var (name, stored) in new[] { ("device", @"\\.\NUL"), ("pipe", @"\\filecat-fixture.invalid\pipe\fixture") })
            {
                string link = Path.Join(root, name);
                File.CreateSymbolicLink(link, stored);
                var (refused, refusedCalls) = await ShowDetailsAsync(io, link);
                windows.Add(refused);
                Assert.True(Action(refused, CopyAction).IsEffectivelyVisible, name);
                Assert.False(Action(refused, OpenAction).IsVisible, name);
                Assert.Matches(@"Calculated path\s+Unavailable", refused.Text);
                Assert.Empty(refusedCalls);
            }
        }
        finally
        {
            foreach (var window in windows.Where(w => w.IsVisible)) window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData(0xA000001Du, "WSL symbolic link")]
    [InlineData(0x8000001Bu, "App execution alias")]
    [InlineData(0x00007A11u, "Reparse point · unknown type")]
    public async Task WSL_app_alias_and_unknown_reparse_entries_offer_neither_copy_nor_navigation(uint tag, string label)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Native Windows reparse points.");
        using var io = new DeviceIoScheduler();
        string root = Directory.CreateTempSubdirectory("fc-details-tags-").FullName;
        string path = Path.Join(root, "entry");
        ReportWindow? window = null;
        try
        {
            File.WriteAllBytes(path, []);
            byte[] data = tag switch
            {
                // LX symlink: version 2, then the UTF-8 target that WSL would follow.
                0xA000001Du => [2, 0, 0, 0, .. Encoding.UTF8.GetBytes("/mnt/c/filecat-fixture")],
                // App execution alias: version 3, then package, application and target strings.
                0x8000001Bu => [3, 0, 0, 0, .. Encoding.Unicode.GetBytes("FileCat.Fixture_1\0FileCat.Fixture!App\0C:\\filecat-fixture.exe\0")],
                _ => Encoding.ASCII.GetBytes("third-party payload"),
            };
            int error = SetReparse(path, tag, data, directory: false);
            Assert.SkipWhen(error is 5 or 1314, $"This token may not set reparse tag 0x{tag:X8} (Win32 error {error}).");
            Assert.True(error == 0, $"FSCTL_SET_REPARSE_POINT 0x{tag:X8}: {new Win32Exception(error).Message}");
            List<string> calls;
            (window, calls) = await ShowDetailsAsync(io, path);
            Assert.Matches(@"Kind\s+" + Regex.Escape(label), window.Text);
            Assert.DoesNotContain("Stored path", window.Text);
            Assert.False(Action(window, CopyAction).IsVisible);
            Assert.False(Action(window, OpenAction).IsVisible);
            Assert.Empty(calls);
        }
        finally
        {
            if (window?.IsVisible == true) window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Writes a mount-point payload with independent substitute and print names onto an existing directory.</summary>
    private static void SetMountPoint(string directory, string substitute, string print)
    {
        byte[] sub = Encoding.Unicode.GetBytes(substitute), shown = Encoding.Unicode.GetBytes(print);
        byte[] data = new byte[8 + sub.Length + 2 + shown.Length + 2];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)sub.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)(sub.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)shown.Length);
        sub.CopyTo(data, 8);
        shown.CopyTo(data, 8 + sub.Length + 2);
        int error = SetReparse(directory, 0xA0000003, data, directory: true);
        Assert.True(error == 0, $"FSCTL_SET_REPARSE_POINT (mount point): {new Win32Exception(error).Message}");
    }

    /// <summary>FSCTL_SET_REPARSE_POINT on the object itself; a tag without the Microsoft bit carries the GUID header.</summary>
    private static int SetReparse(string path, uint tag, byte[] data, bool directory)
    {
        bool microsoft = (tag & 0x80000000) != 0;
        int header = microsoft ? 8 : 24;
        byte[] buffer = new byte[header + data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), checked((ushort)data.Length));
        if (!microsoft) new Guid("6f2a3c51-8a1e-4f0b-9d2e-5c7b1a0f3e11").TryWriteBytes(buffer.AsSpan(8));
        data.CopyTo(buffer, header);
        using var handle = CreateFile(path, 0x40000000 /* GENERIC_WRITE */, 7, 0, 3 /* OPEN_EXISTING */,
            0x00200000 /* OPEN_REPARSE_POINT */ | (directory ? 0x02000000u /* BACKUP_SEMANTICS */ : 0), 0);
        if (handle.IsInvalid) return Marshal.GetLastWin32Error();
        return DeviceIoControl(handle, 0x000900A4 /* FSCTL_SET_REPARSE_POINT */, buffer, buffer.Length, null, 0, out _, 0)
            ? 0 : Marshal.GetLastWin32Error();
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputLength, byte[]? output, int outputLength,
        out int returned, nint overlapped);
}
