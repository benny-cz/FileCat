using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Search;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Recovery;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class AdministratorActionsTests
{
    [AvaloniaFact]
    public async Task A_denied_local_folder_offers_to_reopen_the_exact_folder_in_an_independent_window()
    {
        RequireWindows(elevated: false);
        var (services, vm, window, root) = OpenWindows();
        DirectoryInfo? fixture = null;
        byte[]? originalAcl = null;
        try
        {
            string? folder = Environment.GetEnvironmentVariable("FILECAT_ADMIN_TEST_RESTRICTED");
            if (folder is null)
            {
                fixture = Directory.CreateDirectory(Path.Combine(root, "access denied folder"));
                File.WriteAllText(Path.Combine(fixture.FullName, "admin-only.txt"), "fixture");
                var denied = fixture.GetAccessControl();
                originalAcl = denied.GetSecurityDescriptorBinaryForm();
                using var identity = WindowsIdentity.GetCurrent();
                denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
                fixture.SetAccessControl(denied);
                folder = fixture.FullName;
            }
            var offered = new TaskCompletionSource<StartupOptions>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.LaunchAdministratorWindow = (start, _) => offered.SetResult(StartupOptions.Parse(start.ArgumentList.ToArray()));
            var tab = vm.ActiveTab!;
            var location = Location.FileSystem(folder);
            tab.Navigate(location);
            var reopen = await ButtonAsync(window, "Reopen folder as administrator…");
            Assert.IsType<UnauthorizedAccessException>(tab.Listing.LoadError);
            Click(reopen);
            Click(await ButtonAsync(window, "Open as administrator"));
            var options = await offered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(options.NewInstance);
            Assert.Equal((folder, AdministratorWindow.LocalVolume(folder)!), options.AdministratorFolder);
            Assert.Empty(options.ToForwardArgs());
            Assert.Equal(location, tab.Location);

            tab.Navigate(Location.FileSystem(Path.Combine(root, "missing")));
            await Until(() => tab.Listing.State == ListingState.Failed);
            Assert.Null(tab.DeniedFolderVolume);
            Assert.False(Visible(reopen));
        }
        finally
        {
            if (originalAcl is not null)
            {
                var restored = new DirectorySecurity();
                restored.SetSecurityDescriptorBinaryForm(originalAcl, AccessControlSections.Access);
                fixture!.SetAccessControl(restored);
            }
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task The_symbolic_link_dialog_preserves_the_reviewed_draft_when_approval_is_canceled()
    {
        RequireWindows(elevated: false);
        var (services, vm, window, root) = OpenWindows();
        try
        {
            bool privilegeMissing = services.Platform.FileOperations.CanCreateSymbolicLinks == false;
            var offered = new TaskCompletionSource<AdministratorLinkDraft>(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempts = 0;
            vm.LaunchAdministratorWindow = (start, waits) =>
            {
                Assert.True(waits);
                var options = StartupOptions.Parse(start.ArgumentList.ToArray());
                Assert.True(options.NewInstance);
                Assert.Empty(options.ToForwardArgs());
                Assert.NotNull(options.AdministratorLinkReview);
                var review = options.AdministratorLinkReview.Value;
                var draft = AdministratorLinkDraft.Read(review.Path, review.Hash);
                if (Interlocked.Increment(ref attempts) == 1) throw new Win32Exception(1223);
                offered.SetResult(draft);
            };
            var tab = vm.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount == 3);
            tab.Listing.SetFocus(1);
            vm.Execute(CommandIds.CreateLink);
            await Until(() => vm.Dialogs.IsOpen);
            window.UpdateLayout();
            window.GetVisualDescendants().OfType<RadioButton>().Single(b => Equals(b.Content, "Symbolic link")).IsChecked = true;
            var path = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Link path");
            string destination = Path.Combine(root, "files", "link with spaces.txt");
            path.Text = destination;
            window.GetVisualDescendants().OfType<CheckBox>().Single(b => Equals(b.Content, "Store the target relative to the link's folder")).IsChecked = true;
            var reviewButton = await ButtonAsync(window, "Review as administrator…");
            await Until(() => reviewButton.IsEnabled);
            var create = await ButtonAsync(window, "Create");
            await Until(() => create.IsEnabled == !privilegeMissing);
            Click(reviewButton);
            await Until(() => Volatile.Read(ref attempts) == 1 && reviewButton.IsEnabled);
            Assert.True(vm.Dialogs.IsOpen);
            Assert.Equal(destination, path.Text);
            Click(reviewButton);
            var reviewed = await offered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => !vm.Dialogs.IsOpen);
            Assert.Equal(destination, reviewed.Destination);
            Assert.True(reviewed.RelativeTarget);
            Assert.Equal(Path.Combine(root, "files", "a.txt"), Assert.Single(reviewed.Targets).Path);
            Assert.Empty(services.Jobs.Jobs);
            Assert.False(File.Exists(destination));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task The_administrator_window_opens_the_requested_folder_and_creates_the_link_only_after_review()
    {
        RequireWindows(elevated: true);
        var (services, vm, window, root) = OpenWindows();
        try
        {
            string folder = Environment.GetEnvironmentVariable("FILECAT_ADMIN_TEST_RESTRICTED") ?? Path.Combine(root, "files");
            string expected = Environment.GetEnvironmentVariable("FILECAT_ADMIN_TEST_RESTRICTED") is not null ? "admin-only.txt" : "a.txt";
            var options = StartupOptions.Parse(AdministratorWindow.CreateStartInfo(folder: (folder, AdministratorWindow.LocalVolume(folder)!)).ArgumentList.ToArray());
            vm.OpenArguments(options, initial: true);
            var reopened = vm.ActiveTab!;
            Assert.Equal(Location.FileSystem(folder), reopened.Location);
            await Until(() => reopened.Listing.State == ListingState.Complete && reopened.Listing.VisibleCount > 1);
            Assert.Contains(Enumerable.Range(0, reopened.Listing.VisibleCount), i => reopened.Listing.GetVisible(i).Name == expected);
            Assert.Null(reopened.DeniedFolderVolume);

            string target = Path.Combine(root, "files", "a.txt");
            string destination = Path.Combine(root, "files", "administrator link.txt");
            var item = ItemRef.ForFileSystemPath(target, EntryKind.File);
            var draft = AdministratorLinkDraft.Capture([new(item, destination, target, "a.txt", null)], relative: true);
            await Review(draft);
            Assert.Equal("a.txt", new FileInfo(destination).LinkTarget);
            Assert.Equal("alpha", File.ReadAllText(destination));

            string hardLink = Path.Combine(root, "hard link.txt");
            await Review(AdministratorLinkDraft.Capture([item], hardLink, relative: false, LinkKind.Hard));
            File.WriteAllText(hardLink, "hard link fixture");
            Assert.Equal("hard link fixture", File.ReadAllText(target));

            string actual = Path.Combine(root, "files"), junction = Path.Combine(root, "junction");
            await Review(AdministratorLinkDraft.Capture([ItemRef.ForFileSystemPath(actual, EntryKind.Directory)], junction,
                relative: false, LinkKind.Junction));
            Assert.True(File.GetAttributes(junction).HasFlag(FileAttributes.ReparsePoint));
            Assert.Equal("hard link fixture", File.ReadAllText(Path.Combine(junction, "a.txt")));

            async Task Review(AdministratorLinkDraft link)
            {
                using var exchange = link.Stage(services.Paths.ElevationExchangeDirectory);
                Assert.Throws<InvalidDataException>(() => AdministratorLinkDraft.Read(exchange.VolumePlanPath, new string('0', 64)));
                var transferred = AdministratorLinkDraft.Read(exchange.VolumePlanPath, exchange.Hash);
                Assert.Equal(link.Kind, transferred.Kind);
                var review = vm.ReviewAdministratorLinkDraftAsync(transferred);
                var create = await ButtonAsync(window, "Create");
                await Until(() => create.IsEnabled);
                Assert.False(Path.Exists(link.Destination));
                Click(create);
                await review.WaitAsync(TimeSpan.FromSeconds(5));
                var job = services.Jobs.Jobs.Last();
                await Until(() => job.State.IsFinished());
                Assert.Equal(JobState.Completed, job.State);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Administrator_mutation_reviews_apply_the_exact_rename_create_and_recursive_attributes()
    {
        RequireWindows(elevated: true);
        var (services, vm, window, root) = OpenWindows();
        try
        {
            string folder = Path.Combine(root, "files");
            string a = Path.Combine(folder, "a.txt"), b = Path.Combine(folder, "b.txt");
            await Review(new(AdministratorTaskKind.Rename) { Paths = [a, b], Names = ["b.txt", "a.txt"] });
            Assert.Equal("alpha", File.ReadAllText(b));
            Assert.Equal("beta", File.ReadAllText(a));
            string empty = Path.Combine(folder, "new empty.txt");
            await Review(new(AdministratorTaskKind.CreateFile) { Paths = [folder], Names = ["new empty.txt"] });
            Assert.Equal(0, new FileInfo(empty).Length);
            var modified = new DateTime(2022, 1, 3, 4, 5, 6, DateTimeKind.Utc);
            await Review(new(AdministratorTaskKind.Attributes)
            {
                Paths = [folder],
                Attributes = new(FileAttributes.Hidden, 0, modified, null, Recursive: true),
            });
            Assert.True(File.GetAttributes(a).HasFlag(FileAttributes.Hidden));
            Assert.Equal(modified, File.GetLastWriteTimeUtc(empty));

            async Task Review(AdministratorTask task)
            {
                using var staged = task.Stage(services.Paths.ElevationExchangeDirectory);
                var request = AdministratorTask.Read(staged.VolumePlanPath, staged.Hash);
                int count = services.Jobs.Jobs.Count;
                var review = vm.ReviewAdministratorTaskAsync(request);
                var apply = await ButtonAsync(window, "Apply as administrator");
                Assert.Equal(count, services.Jobs.Jobs.Count);
                Click(apply);
                await review.WaitAsync(TimeSpan.FromSeconds(5));
                var job = services.Jobs.Jobs.Last();
                await Until(() => job.State.IsFinished());
                Assert.Equal(JobState.Completed, job.State);
                Assert.True(job.ItemsDone > 0, string.Join('\n', job.Issues.Select(issue => issue.Message)));
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Administrator_read_reviews_keep_the_file_registry_view_and_search_scope()
    {
        RequireWindows(elevated: true);
        var (services, vm, window, root) = OpenWindows();
        string registryPath = @"SOFTWARE\FileCatElevationTest-" + Guid.NewGuid().ToString("N");
        try
        {
            string folder = Path.Combine(root, "files");
            string selected = Directory.CreateDirectory(Path.Combine(folder, "skipped")).FullName;
            string file = Path.Combine(selected, "match.txt");
            File.WriteAllText(file, "reviewed text");
            File.WriteAllText(Path.Combine(selected, "no-match.bin"), "reviewed text");
            File.WriteAllText(Path.Combine(folder, "outside.txt"), "reviewed text");
            string excluded = Directory.CreateDirectory(Path.Combine(selected, "ignored")).FullName;
            File.WriteAllText(Path.Combine(excluded, "also-outside.txt"), "reviewed text");
            // An unrelated original root going away must not block the scoped retry.
            var criteria = new SearchCriteria { Names = "*.txt", LookIn = $"{folder};{Path.Combine(root, "missing")}", Text = "reviewed text" };
            var skip = new SearchRetryFolder(folder, selected, 1);
            var search = new AdministratorSearchDraft(criteria, DateTime.UtcNow, [@"\skipped\ignored"], [skip]);
            await Run(new(AdministratorTaskKind.Search) { Paths = [selected], Search = search });
            var find = FindWindow.OpenWindows.Last();
            try
            {
                Assert.Equal(criteria.Names, find.Criteria.Names);
                Assert.Equal(criteria.Text, find.Criteria.Text);
                var query = BuildQuery();
                var results = new ResultSet("test", "retry", "review");
                new SearchSession(query, results).Run(TestContext.Current.CancellationToken);
                Assert.Equal(1, results.Count);
            }
            finally { find.Close(); }

            await Run(new(AdministratorTaskKind.View) { Paths = [file], Alternate = true });
            var viewer = ViewerWindow.OpenWindows.Last();
            viewer.Close();

            var checksum = Run(new(AdministratorTaskKind.Checksums) { Paths = [file], Detail = "Sha512" });
            var close = await ButtonAsync(window, "Close");
            await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(box =>
                AutomationProperties.GetName(box) == "Checksums" && box.Text?.Contains("match.txt", StringComparison.Ordinal) == true));
            Click(close);
            await checksum.WaitAsync(TimeSpan.FromSeconds(5));

            using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(registryPath)) key.SetValue("FileCat", "fixture");
            await Run(new(AdministratorTaskKind.Registry) { Detail = @"HKLM\" + registryPath, Context = "64" });
            var registry = vm.ActiveTab!;
            Assert.Equal("64", registry.Location!.Session);
            await Until(() => registry.Listing.State == ListingState.Complete);
            Assert.Contains(Enumerable.Range(0, registry.Listing.VisibleCount), i => registry.Listing.GetVisible(i).Name == "FileCat");
            Assert.StartsWith(@"HKU\S-1-", MainViewModel.AdministratorRegistryKey(new Location(Schemes.Registry, @"HKCU\Software", session: "32")).Path);

            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryPath)) key.SetValue("UserValue", 23);
            var export = new AdministratorTask(AdministratorTaskKind.RegistryExport)
            {
                Detail = "reviewed.reg", Context = "64", RegistryExport =
                [MainViewModel.BindAdministratorRegistryExport(new Location(Schemes.Registry, @"HKCU\" + registryPath, session: "64"), "UserValue"),
                 new(@"HKLM\" + registryPath, null, CurrentUser: false)],
            };
            var review = Run(export);
            await ButtonAsync(window, "Choose destination");
            Click(await ButtonAsync(window, "Cancel"));
            await review.WaitAsync(TimeSpan.FromSeconds(5));
            string destination = Path.Combine(folder, export.Detail);
            Assert.False(File.Exists(destination));
            using var stagedExport = export.Stage(services.Paths.ElevationExchangeDirectory);
            var approved = AdministratorTask.Read(stagedExport.VolumePlanPath, stagedExport.Hash);
            RegistryInterchange.ExportSelections(approved.RegistryExport!.Select(item => item.ForView(approved.Context)).ToArray(), destination);
            var exported = File.ReadAllText(destination);
            Assert.Contains($"[HKEY_CURRENT_USER\\{registryPath}]", exported);
            Assert.Contains("\"UserValue\"=dword:00000017", exported);
            Assert.Contains($"[HKEY_LOCAL_MACHINE\\{registryPath}]", exported);

            SearchQuery BuildQuery()
            {
                Assert.True(criteria.TryBuildQuery(search.CapturedAtUtc, search.IgnoredFolders, null, out var query, out var error,
                    retryFolders: search.Folders), error);
                return query!;
            }
            async Task Run(AdministratorTask task)
            {
                using var staged = task.Stage(services.Paths.ElevationExchangeDirectory);
                await vm.ReviewAdministratorTaskAsync(AdministratorTask.Read(staged.VolumePlanPath, staged.Hash));
            }
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Portable_recovery_carries_the_selected_disk_interface_and_requires_a_second_review()
    {
        RequireWindows(elevated: false);
        var (services, vm, window, root) = OpenWindows();
        try
        {
            var disk = DeviceTopology.Disks().FirstOrDefault(candidate =>
                candidate.InterfacePath is not null && SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(candidate) is not null);
            if (disk is null) Assert.Skip("The VM did not expose a stable disk interface to the ordinary account.");
            var launch = new TaskCompletionSource<AdministratorTask>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.LaunchAdministratorWindow = (start, _) =>
            {
                var options = StartupOptions.Parse(start.ArgumentList.ToArray());
                Assert.True(options.NewInstance);
                Assert.Empty(options.ToForwardArgs());
                var packet = options.AdministratorRequest!.Value;
                launch.SetResult(AdministratorTask.Read(packet.Path, packet.Hash));
            };
            var panel = vm.Workspace.ActivePanel!;
            var original = panel.ActiveTab;
            var request = vm.FindDeletedOnDiskAsync(panel, disk!);
            Click(await ButtonAsync(window, "Open as administrator"));
            var review = await launch.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await request;
            Assert.Equal(AdministratorTaskKind.RecoveryDisk, review.Kind);
            Assert.Equal(disk!.InterfacePath, review.Device);
            Assert.Equal(SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(disk), review.Detail);
            Assert.Same(original, panel.ActiveTab);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Access_denied_file_operations_offer_one_deferred_review_with_only_the_denied_work()
    {
        RequireWindows(elevated: false);
        var (fixture, installed) = FileRetryFixture();
        var (services, vm, window, root) = OpenWindows(AppPaths.Resolve(dataRoot: Path.Combine(fixture, "ordinary", "state")));
        try
        {
            Assert.Equal(!installed, services.Paths.IsPortable);
            Assert.Equal(installed, ElevationBroker.Locate(services.Paths.IsPortable, out _) is not null);
            string target = Path.Combine(fixture, "protected");
            string ordinary = Path.Combine(fixture, "ordinary");
            var first = ItemRef.ForFileSystemPath(Path.Combine(ordinary, "copy me.txt"), EntryKind.File);
            var missing = ItemRef.ForFileSystemPath(Path.Combine(ordinary, "unrelated missing.txt"), EntryKind.File);
            var another = ItemRef.ForFileSystemPath(Path.Combine(ordinary, "copy me too.txt"), EntryKind.File);
            var move = ItemRef.ForFileSystemPath(Path.Combine(ordinary, "move me.txt"), EntryKind.File);
            var rename = ItemRef.ForFileSystemPath(Path.Combine(target, "rename me.txt"), EntryKind.File);
            await Run(new JobRequest { Kind = JobKind.Copy, Sources = [first, missing, another], Destination = Location.FileSystem(target),
                Options = new() { Conflicts = installed ? ConflictPolicy.Skip : ConflictPolicy.KeepBothRenameIncoming } }, [first, another]);
            await Run(new JobRequest { Kind = JobKind.Move, Sources = [move], Destination = Location.FileSystem(target),
                Options = new() { Conflicts = ConflictPolicy.Skip } }, [move]);
            await Run(new JobRequest { Kind = JobKind.Rename, Sources = [rename], NewName = "renamed.txt" }, [rename]);
            await Run(new JobRequest { Kind = JobKind.CreateDirectory, Destination = Location.FileSystem(target),
                NewName = installed ? "new folder" : @"new folder\nested" }, []);

            var recycle = new Job(new JobRequest { Kind = JobKind.Recycle, Sources = [rename] }, "Recycle", "local", [], []);
            recycle.AddIssue(new JobIssue(IssueSeverity.Error, rename.FileSystemPath!, "denied", StepOutcome.Failed) { Cause = "access" });
            recycle.SetState(JobState.Failed);
            Assert.False(ElevationPlanBuilder.OffersRetry(recycle));
            Assert.False(vm.CanDeferAdministratorFileRetry(recycle, new("Could not recycle", "denied", rename.FileSystemPath!, true, "access")));

            async Task Run(JobRequest request, ItemRef[] denied)
            {
                var administratorJob = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
                var administratorWindow = new TaskCompletionSource<(AdministratorTask Task, (string Path, string Hash) Exchange, TaskCompletionSource Closed)>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                vm.LaunchAdministratorFileRetry = (start, stopWatching) =>
                {
                    Assert.Equal("runas", start.Verb);
                    var options = StartupOptions.Parse(start.ArgumentList.ToArray());
                    Assert.True(options.NewInstance);
                    Assert.Empty(options.ToForwardArgs());
                    var exchange = options.AdministratorRequest!.Value;
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    administratorWindow.SetResult((AdministratorTask.Read(exchange.Path, exchange.Hash), exchange, closed));
                    return closed.Task.WaitAsync(stopWatching);
                };
                void CapturePlan(Job newJob)
                {
                    if (newJob.Kind != JobKind.Elevated) return;
                    newJob.Pause(); // Exercise the real installed routing, then run this plan under the other VM token.
                    administratorJob.SetResult(newJob);
                }
                services.Jobs.JobAdded += CapturePlan;
                try
                {
                    var job = services.Jobs.Submit(request);
                    await Until(() => job.State == JobState.AwaitingDecision);
                    var review = vm.ReviewDecisionAsync(job);
                    Click(await ButtonAsync(window, "Review denied items as administrator…"));
                    await review.WaitAsync(TimeSpan.FromSeconds(5));
                    await Until(() => job.State.IsFinished());
                    Assert.True(AdministratorFileRetry.HasDeniedWork(job) || job.RetriedAsAdministrator);
                    string packet = Path.Combine(ordinary, request.Kind + ".json");
                    if (installed)
                    {
                        var scheduled = await administratorJob.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        Assert.True(job.State.IsFinished());
                        Assert.Equal(denied, scheduled.Request.Sources);
                        Assert.Equal(request.Destination, scheduled.Request.Destination);
                        File.WriteAllBytes(packet, ElevationPlanCodec.Serialize(scheduled.Request.Elevation!));
                        Assert.True(job.RetriedAsAdministrator);
                        scheduled.Cancel();
                        await Until(() => !job.RetriedAsAdministrator);
                    }
                    else
                    {
                        var launch = await administratorWindow.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        await Until(() => job.RetriedAsAdministrator);
                        if (request.Kind == JobKind.Copy)
                        {
                            string? handoff = Environment.GetEnvironmentVariable("FILECAT_RETRY_CANCEL_HANDOFF");
                            if (handoff is null) AdministratorFileRetryReview.Report(launch.Exchange, approved: false);
                            else
                            {
                                File.WriteAllLines(handoff + ".tmp", [launch.Exchange.Path, launch.Exchange.Hash]);
                                File.Move(handoff + ".tmp", handoff);
                            }
                            await Until(() => vm.Operations.Jobs.Any(row => row.Job == job && row.CanRetryElevated), attempts: handoff is null ? 250 : 2000);
                            Assert.False(launch.Closed.Task.IsCompleted);

                            administratorWindow = new(TaskCreationOptions.RunContinuationsAsynchronously);
                            await vm.RetryElevatedAsync(job);
                            var reopened = await administratorWindow.Task.WaitAsync(TimeSpan.FromSeconds(5));
                            Assert.NotEqual(launch.Exchange.Path, reopened.Exchange.Path);
                            launch = reopened;
                        }
                        var task = launch.Task;
                        Assert.True(job.State.IsFinished());
                        Assert.Equal(denied.Select(item => item.FileSystemPath), task.FileRetry!.Sources);
                        Assert.Equal(request.Destination?.Path, task.FileRetry.Destination);
                        Assert.Equal(request.Options.Conflicts, task.FileRetry.Options().Conflicts);
                        File.WriteAllBytes(packet, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(task));
                        await Until(() => job.RetriedAsAdministrator);
                        AdministratorFileRetryReview.Report(launch.Exchange, approved: true);
                        launch.Closed.SetResult();
                    }
                }
                finally { services.Jobs.JobAdded -= CapturePlan; }
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Cancel_in_the_administrator_window_replies_to_the_original_users_file_review()
    {
        RequireWindows(elevated: true);
        string? handoff = Environment.GetEnvironmentVariable("FILECAT_RETRY_CANCEL_HANDOFF");
        if (handoff is null) Assert.Skip("Run with the ordinary account's live portable review in the Windows VM.");
        var exchange = File.ReadAllLines(handoff!);
        Assert.Equal(2, exchange.Length);
        var task = AdministratorTask.Read(exchange[0], exchange[1]);
        var (services, vm, window, root) = OpenWindows();
        try { await CancelReviewedFileRetryAsync(services, vm, window, task, (exchange[0], exchange[1])); }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Deferred_file_operations_apply_their_reviewed_scope_under_the_administrator_token()
    {
        RequireWindows(elevated: true);
        var (fixture, installed) = FileRetryFixture();
        var (services, vm, window, root) = OpenWindows();
        try
        {
            string target = Path.Combine(fixture, "protected");
            string ordinary = Path.Combine(fixture, "ordinary");
            foreach (var kind in new[] { JobKind.Copy, JobKind.Move, JobKind.Rename, JobKind.CreateDirectory })
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(ordinary, kind + ".json"));
                if (installed)
                {
                    var plan = ElevationPlanCodec.Parse(bytes);
                    Assert.Empty(ElevationPlanCodec.Validate(plan, DateTime.UtcNow));
                    Assert.All(ElevationPlanRunner.Run(plan, () => false, _ => { }),
                        step => Assert.True(step.Outcome == ElevatedOutcome.Committed, step.Message));
                }
                else
                {
                    var task = System.Text.Json.JsonSerializer.Deserialize<AdministratorTask>(bytes)!;
                    if (kind == JobKind.Copy)
                    {
                        using var staged = task.Stage(services.Paths.ElevationExchangeDirectory);
                        await CancelReviewedFileRetryAsync(services, vm, window, task, (staged.VolumePlanPath, staged.Hash));
                        var result = Assert.IsType<ElevationResult>(staged.ReadResult());
                        Assert.Equal(staged.Hash, result.Nonce);
                        Assert.False(result.Consented);
                        Assert.Equal(ElevationMessages.Declined, result.Refused);
                    }
                    var review = vm.ReviewAdministratorTaskAsync(task);
                    var accept = await ButtonAsync(window, "Retry as administrator");
                    Assert.DoesNotContain(services.Jobs.Jobs, job => job.Kind == kind);
                    Click(accept);
                    await review.WaitAsync(TimeSpan.FromSeconds(5));
                    var operation = services.Jobs.Jobs.Last();
                    await Until(() => operation.State.IsFinished());
                    Assert.True(operation.State == JobState.Completed, string.Join('\n', operation.Issues.Select(issue => issue.Message)));
                }
            }
            Assert.Equal("copied", File.ReadAllText(Path.Combine(target, installed ? "copy me.txt" : "copy me (2).txt")));
            if (!installed) Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "copy me.txt")));
            Assert.Equal("also copied", File.ReadAllText(Path.Combine(target, "copy me too.txt")));
            Assert.False(File.Exists(Path.Combine(target, "unrelated missing.txt")));
            Assert.Equal("moved", File.ReadAllText(Path.Combine(target, "move me.txt")));
            Assert.False(File.Exists(Path.Combine(ordinary, "move me.txt")));
            Assert.Equal("renamed", File.ReadAllText(Path.Combine(target, "renamed.txt")));
            Assert.False(File.Exists(Path.Combine(target, "rename me.txt")));
            Assert.True(Directory.Exists(Path.Combine(target, installed ? "new folder" : @"new folder\nested")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task CancelReviewedFileRetryAsync(AppServices services, MainViewModel vm, MainWindow window, AdministratorTask task,
        (string Path, string Hash) exchange)
    {
        var canceled = vm.ReviewAdministratorTaskAsync(task, exchange);
        Click(await ButtonAsync(window, "Cancel"));
        await canceled.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(services.Jobs.Jobs);
        Assert.True(window.IsVisible);
    }

    private static (string Fixture, bool Installed) FileRetryFixture()
    {
        string? fixture = Environment.GetEnvironmentVariable("FILECAT_FILE_RETRY_FIXTURE");
        if (fixture is null) Assert.Skip("Run in the Windows VM with the ordinary and protected retry folders provisioned.");
        return (fixture!, Environment.GetEnvironmentVariable("FILECAT_FILE_RETRY_INSTALLED") == "1");
    }

    private static (AppServices Services, MainViewModel Vm, MainWindow Window, string Root) OpenWindows(AppPaths? paths = null)
    {
        var factory = PlatformFactory.WindowsFactory;
        try
        {
            PlatformFactory.WindowsFactory = () => new WindowsPlatform();
            return AccessibilityTests.OpenMainWindow(statePaths: paths);
        }
        finally { PlatformFactory.WindowsFactory = factory; }
    }

    private static void RequireWindows(bool elevated)
    {
        if (!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity != elevated)
            Assert.Skip(elevated ? "Requires an administrator Windows token." : "Requires an ordinary Windows token.");
    }

    private static bool Visible(Button button) => button.IsVisible && button.GetVisualAncestors().All(a => a.IsVisible);

    private static async Task<Button> ButtonAsync(MainWindow window, string text)
    {
        Button? button = null;
        await Until(() =>
        {
            window.UpdateLayout();
            button = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, text) && Visible(b));
            return button is not null;
        });
        return button!;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task Until(Func<bool> condition, int attempts = 250)
    {
        for (int i = 0; i < attempts && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
