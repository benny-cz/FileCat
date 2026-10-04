using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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

public sealed class FolderCountLowerBoundTests
{
    [AvaloniaTheory]
    [InlineData(false, false, 1000)]
    [InlineData(false, true, 1000)]
    [InlineData(true, false, 1000)]
    [InlineData(true, true, 1000)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 0)]
    public async Task An_inaccessible_subtree_keeps_the_count_labeled_as_a_lower_bound(bool deny, bool refresh, int visibleBytes)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Owned directory-enumeration denial uses Windows ACLs.");
            return;
        }
        string fixtureParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        string root = Path.GetFullPath(Path.Combine(fixtureParent, "filecat-count-lower-bound-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(fixtureParent, Path.GetDirectoryName(root));
        string files = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
        string counted = Directory.CreateDirectory(Path.Combine(files, "counted")).FullName;
        var blocked = Directory.CreateDirectory(Path.Combine(counted, "blocked"));
        string visibleFile = Path.Combine(counted, "visible.bin");
        string hiddenFile = Path.Combine(blocked.FullName, "hidden.bin");
        File.WriteAllBytes(visibleFile, Enumerable.Repeat((byte)0x41, visibleBytes).ToArray());
        File.WriteAllBytes(hiddenFile, Enumerable.Repeat((byte)0x42, 234).ToArray());
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        var beforeHashes = new[] { Hash(visibleFile), Hash(hiddenFile) };
        var acl = blocked.GetAccessControl(AccessControlSections.Access);
        string originalAcl = acl.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        AppServices? services = null;
        MainWindow? window = null;
        MainViewModel? vm = null;
        var pane = new QuickViewPane();
        bool denied = false, restored = false;
        SizeProgress result = default;
        var problems = new List<string>();
        try
        {
            if (deny)
            {
                var deniedAcl = blocked.GetAccessControl(AccessControlSections.Access);
                deniedAcl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
                blocked.SetAccessControl(deniedAcl);
                try { _ = blocked.EnumerateFileSystemInfos().ToArray(); }
                catch (UnauthorizedAccessException) { denied = true; }
                Assert.True(denied, "The owned directory was not denied: this fixture cannot prove a lower bound.");
            }
            result = DirectorySizer.Compute(counted, null, TestContext.Current.CancellationToken);
            Assert.Equal(deny ? visibleBytes : visibleBytes + 234, result.Bytes);
            Assert.Equal(deny ? 1 : 0, result.Inaccessible);
            Assert.True(result.Complete);
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
            listing.MarkNames(["counted"], true);
            pane.Attach(tab);
            vm.CountFolderSizes(tab);
            await Wait(() => tab.SizingFolders == 0 && listing.GetItemRef(listing.FindStoreIndex("counted")) is not null);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            async Task Snapshot(string stage)
            {
                listing.FocusName("counted");
                var e = listing.Store[listing.FindStoreIndex("counted")];
                var stats = listing.GetMarkStats();
                string Caption() => ((TextBlock)typeof(QuickViewPane).GetField("_info", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!).Text ?? "";
                // The pane was attached before counting: its real debounce/event route must update the caption.
                await Wait(() => deny ? Caption().Contains("lower bound", StringComparison.Ordinal) : Caption() == $"Folder · {Formatters.SizeWithUnit(visibleBytes + 234)}");
                string info = ((TextBlock)typeof(QuickViewPane).GetField("_info", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!).Text ?? "";
                string column = Formatters.SizeCell(e);
                if (deny)
                {
                    if (!column.StartsWith("≥", StringComparison.Ordinal)) problems.Add(stage + ": row looks exact: " + column);
                    if (!tab.StatusMarked.Contains("lower bound", StringComparison.Ordinal)) problems.Add(stage + ": marked total lacks lower-bound wording: " + tab.StatusMarked);
                    if (!tab.CanCountMarked) problems.Add(stage + ": cannot retry Count");
                    if (!stats.SizesIncomplete) problems.Add(stage + ": stats claim complete");
                    if (!info.Contains("lower bound", StringComparison.Ordinal)) problems.Add(stage + ": quick view looks exact: " + info);
                }
                else
                {
                    Assert.Equal(visibleBytes + 234, e.Size);
                    Assert.Equal(visibleBytes + 234, stats.Bytes);
                    Assert.False(stats.SizesIncomplete);
                    Assert.False(tab.CanCountMarked);
                    Assert.DoesNotContain("lower bound", tab.StatusMarked);
                    Assert.DoesNotContain("lower bound", info);
                }
            }
            await Snapshot("counted");
            if (refresh)
            {
                listing.Refresh();
                await Wait(() => !listing.IsRefreshing && listing.State == ListingState.Complete);
                await Snapshot("refreshed");
            }
            var restoredAcl = new DirectorySecurity();
            restoredAcl.SetSecurityDescriptorSddlForm(originalAcl, AccessControlSections.Access);
            blocked.SetAccessControl(restoredAcl);
            restored = blocked.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access) == originalAcl;
            Assert.True(restored);
            vm.CountFolderSizes(tab);
            await Wait(() => tab.SizingFolders == 0);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            var final = listing.Store[listing.FindStoreIndex("counted")];
            if (final.Size != visibleBytes + 234) problems.Add("Restored access: retry did not count all bytes");
            if (tab.StatusMarked.Contains("lower bound", StringComparison.Ordinal)) problems.Add("Restored access: complete retry retains lower-bound label");
            await Wait(() => ((TextBlock)typeof(QuickViewPane).GetField("_info", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!).Text == $"Folder · {Formatters.SizeWithUnit(visibleBytes + 234)}");
        }
        finally
        {
            pane.Attach(null);
            var restoredAcl = new DirectorySecurity();
            restoredAcl.SetSecurityDescriptorSddlForm(originalAcl, AccessControlSections.Access);
            blocked.SetAccessControl(restoredAcl);
            restored = blocked.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access) == originalAcl;
            if (vm is not null) foreach (var job in vm.Services.Jobs.Jobs) job.Cancel();
            window?.Close();
            if (vm is not null) foreach (var tab in vm.Workspace.Panels.SelectMany(p => p.Tabs).ToList()) tab.Dispose();
            services?.Dispose();
            var afterHashes = new[] { Hash(visibleFile), Hash(hiddenFile) };

            // The real-file/ACL probe retains observations separately; this regression owns only its verified temp root.
            Directory.Delete(root, recursive: true);
            Assert.Equal(beforeHashes, afterHashes);
            Assert.True(restored);
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    private static async Task Wait(Func<bool> ready)
    {
        for (int i = 0; i < 500 && !ready(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready(), "Owned folder count did not settle within ten seconds.");
    }
}
