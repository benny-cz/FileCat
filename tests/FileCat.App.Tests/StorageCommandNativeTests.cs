using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Platform.Windows;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>Explicit disposable-client opt-in. Exercises the actual command through native job retirement.</summary>
public sealed class StorageCommandNativeTests
{
    private static bool RequireNativeCommandFixture(bool efs)
    {
        string optIn = efs ? "FILECAT_TEST_EFS_UI" : "FILECAT_TEST_STORAGE_UI";
        Assert.SkipUnless(Environment.GetEnvironmentVariable(optIn) == "1",
            "Explicit disposable Windows 11 client, medium integrity, NTFS command qualification.");
        bool arm64 = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_STORAGE_ARM64_JOBS") == "1";
        Assert.True(AppServices.CanQualifyNtfsStorage(arm64),
            "The native command fixture must run at medium integrity on the selected Windows 11 architecture.");
        if (arm64)
        {
            Assert.Equal("Arm64", Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_STORAGE_ABI_ARCHITECTURE"));
            Assert.Equal(Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_STORAGE_ABI_ROOT"),
                Path.TrimEndingDirectorySeparator(Path.GetTempPath()), ignoreCase: true);
        }
        return arm64;
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition()) { timeout.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); await Task.Delay(10, timeout.Token); }
        Dispatcher.UIThread.RunJobs();
    }

    private static void AcceptDecryption(Window window, string lossConsent = "I understand the selected files will lose EFS protection")
    {
        foreach (string prefix in new[] { "I accept this concurrent link-change limitation", lossConsent })
            window.GetVisualDescendants().OfType<CheckBox>().Single(control => control.IsVisible &&
                AutomationProperties.GetName(control)?.StartsWith(prefix, StringComparison.Ordinal) == true).IsChecked = true;
    }

    [AvaloniaFact]
    public async Task The_qualified_candidate_opens_Efs_protection_and_selected_file_backup_from_commands()
    {
        bool arm64 = RequireNativeCommandFixture(efs: true);
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420,
            storageTransforms: new WindowsStorageTransforms(qualifyNativeArm64: arm64).CreateEfsJobCandidate(),
            prepareFiles: folder => File.Encrypt(Path.Join(folder, "a.txt")), qualifyNativeArm64Storage: arm64);
        try
        {
            string path = Path.Join(root, "files", "a.txt");
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            listing.MarkNames(["a.txt"], true);
            Assert.True(vm.GetAvailability(CommandIds.EfsProtection).Enabled);
            Assert.True(vm.GetAvailability(CommandIds.EfsFileBackup).Enabled);

            var protecting = vm.ExecuteAsync(CommandIds.EfsProtection);
            await Until(() => window.GetVisualDescendants().OfType<RadioButton>().Any(button => AutomationProperties.GetName(button) == "Decrypt"));
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await protecting;

            int reviewGeneration = listing.Generation;
            var backingUp = vm.ExecuteAsync(CommandIds.EfsFileBackup);
            await Until(() => backingUp.IsCompleted || window.GetVisualDescendants().OfType<TextBlock>()
                .Any(control => AutomationProperties.GetAutomationId(control) == "EFS key status"));
            if (backingUp.IsCompleted) await backingUp;
            var controls = window.GetVisualDescendants().ToArray();
            var keys = controls.OfType<ComboBox>().SingleOrDefault(control => AutomationProperties.GetName(control) == "Selected file EFS certificate");
            string? status = controls.OfType<TextBlock>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == "EFS key status")?.Text;
            Assert.True(keys is not null, $"Expected the selected-file EFS key picker. Generation {reviewGeneration} to {listing.Generation}. " +
                $"Review status {status ?? "unavailable"}. Notification {vm.CurrentNotification?.Message ?? "none"}.");
            Assert.Equal(-1, keys.SelectedIndex);
            Assert.Single(keys.Items);
            Assert.Contains(controls.OfType<TextBox>(), control => control.Text?.Contains("a.txt", StringComparison.Ordinal) == true);
            Assert.False(Assert.Single(controls.OfType<Button>(), button => button.Content as string == "Back up selected key…").IsEnabled);
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await backingUp;
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
            Assert.Equal("alpha", File.ReadAllText(path));
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { ((OverlayDialogService)vm.Dialogs).CancelAll(); AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Efs_review_transfers_the_displayed_consent_to_real_encryption_and_decryption_jobs()
    {
        bool arm64 = RequireNativeCommandFixture(efs: true);
        string? setupDirectory = null;
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 760, height: 620,
            storageTransforms: new WindowsStorageTransforms(efsSetupBaseDirectory: () =>
                setupDirectory ?? throw new InvalidOperationException("The fixture must prepare its own EFS setup directory."),
                qualifyNativeArm64: arm64).CreateEfsJobCandidate(),
            qualifyNativeArm64Storage: arm64);
        try
        {
            setupDirectory = Directory.CreateDirectory(Path.Join(root, "efs-setup")).FullName;
            string path = Path.Join(root, "files", "a.txt");
            var sources = new[] { ItemRef.ForFileSystemPath(path, EntryKind.File) };
            var decisions = new List<DecisionRequest>();
            services.Jobs.DecisionRequested += pending =>
            {
                decisions.Add(pending.Request);
                pending.Resolve(pending.Request switch
                {
                    ConfirmRequest => new(DecisionAction.Proceed),
                    StorageEfsRecoveryDecision recovery => new(DecisionAction.DeclareEfsRecovery)
                        { EfsRecovery = new(recovery.Keys[0].CertificateSha1, 0) },
                    _ => new(DecisionAction.CancelJob),
                });
            };

            async Task Apply(StorageAction action, string label, string secondConsent)
            {
                bool encrypt = action == StorageAction.EncryptEfs;
                using var review = services.Jobs.CaptureStorageSelection(sources);
                using var form = new StorageChangeDialog(review, services.Io, "fixture", [false], efs: true);
                var showing = form.ShowAsync(vm.Dialogs);
                try
                {
                    form.Body.Children.OfType<RadioButton>().Single(button => AutomationProperties.GetName(button) == label).IsChecked = true;
                    await Until(() => form.Ready);
                    Assert.Equal(encrypt ? StorageObservationState.Disabled : StorageObservationState.Enabled,
                        Assert.Single(review.State.Roots).Observation?.Efs);
                    var checks = form.Body.Children.OfType<CheckBox>().Where(control => control.IsVisible).ToArray();
                    Assert.Equal(2, checks.Length);
                    Assert.All(checks, check => Assert.False(check.IsChecked));
                    Assert.Contains(checks, check => AutomationProperties.GetName(check)?.Contains(secondConsent, StringComparison.Ordinal) == true);
                    foreach (var check in checks) check.IsChecked = true;
                    var button = window.GetVisualDescendants().OfType<Button>().Single(control => control is not RadioButton && control.Content as string == label);
                    Assert.True(button.IsEnabled);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    using var choice = Assert.IsType<StorageChangeChoice>(await showing);
                    Assert.Equal(!encrypt, File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
                    var job = services.Jobs.SubmitStorage(choice.Roots, choice.Approval, _ => { }, choice.Mode);
                    await Until(() => job.State.IsFinished());
                    Assert.True(job.State == JobState.Completed,
                        $"{label} ended as {job.State}. {job.Summary} " +
                        string.Join(" ", job.StorageResult?.Reasons.Select(reason => reason.Problem.Message) ?? []));
                    Assert.Equal(1, job.StorageResult!.Counts.Changed);
                    Assert.Equal(choice.Approval.EfsConsent, job.Request.StorageTransform!.EfsConsent);
                    Assert.Equal(encrypt, File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
                    Assert.Equal("alpha", File.ReadAllText(path));
                }
                finally { ((OverlayDialogService)vm.Dialogs).CancelAll(); await showing; }
            }

            await Apply(StorageAction.EncryptEfs, "Encrypt with EFS", "remove compression from all data streams");
            await Apply(StorageAction.DecryptEfs, "Decrypt", "selected files will lose EFS protection");
            Assert.Collection(decisions, request => Assert.IsType<ConfirmRequest>(request),
                request => Assert.IsType<StorageEfsRecoveryDecision>(request));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(StorageAction.Compress, "refresh")]
    [InlineData(StorageAction.Compress, "marks")]
    [InlineData(StorageAction.Compress, "navigation")]
    [InlineData(StorageAction.DecryptEfs, "refresh")]
    [InlineData(StorageAction.DecryptEfs, "marks")]
    [InlineData(StorageAction.DecryptEfs, "navigation")]
    public async Task Real_storage_command_keeps_its_review_and_guards_completion_marks(StorageAction action, string change)
    {
        bool efs = action == StorageAction.DecryptEfs;
        bool arm64 = RequireNativeCommandFixture(efs);
        var platform = new WindowsStorageTransforms(qualifyNativeArm64: arm64);
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420,
            storageTransforms: efs ? platform.CreateEfsJobCandidate() : platform,
            qualifyNativeArm64Storage: arm64,
            prepareFiles: folder =>
            {
                string a = Path.Join(folder, "a.txt"), b = Path.Join(folder, "b.txt");
                File.WriteAllText(a, new string('A', 8192));
                File.WriteAllText(b, new string('B', 8192));
                if (efs) { File.Encrypt(a); File.Encrypt(b); }
            });
        try
        {
            var tab = vm.ActiveTab!; var listing = tab.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            var original = tab.Location!;
            string a = Path.Join(original.Path, "a.txt"), b = Path.Join(original.Path, "b.txt");
            listing.MarkNames(["a.txt", "b.txt"], true); long marks = listing.MarkRevision;
            string label = efs ? "Decrypt" : "Compress";
            vm.Execute(efs ? CommandIds.EfsProtection : CommandIds.StorageAttributes);
            IEnumerable<RadioButton> Actions() => window.GetVisualDescendants().OfType<RadioButton>()
                .Where(button => AutomationProperties.GetName(button) == label);
            await Until(() => Actions().Any());
            Assert.Single(Actions()).IsChecked = true;
            Button Primary() => window.GetVisualDescendants().OfType<Button>()
                .Single(button => button is not RadioButton && button.Content as string == label);
            if (efs) AcceptDecryption(window);
            await Until(() => Primary().IsEnabled);
            if (change == "refresh")
            {
                int generation = listing.Generation; tab.Refresh();
                await Until(() => listing.Generation != generation && listing.State == ListingState.Complete && !listing.IsRefreshing);
                Assert.Equal(marks, listing.MarkRevision);
            }
            else if (change == "marks")
            { listing.MarkNames(["b.txt"], false); listing.MarkNames(["b.txt"], true); }
            else
            {
                tab.Navigate(Location.FileSystem(root));
                await Until(() => listing.State == ListingState.Complete && tab.Location?.Path == root);
                tab.Navigate(original);
                await Until(() => listing.State == ListingState.Complete && tab.Location == original);
                listing.MarkNames(["a.txt"], true);
            }
            Assert.True(vm.Dialogs.IsOpen); Assert.True(Primary().IsEnabled);
            int reviewedGeneration = listing.Generation;
            Primary().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => services.Jobs.Jobs.Count == 1 && services.Jobs.Jobs[0].State.IsFinished() &&
                vm.CurrentNotification?.Message.Contains("changed", StringComparison.OrdinalIgnoreCase) == true);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(2, job.StorageResult!.Counts.Changed);
            Assert.Equal(action, job.Request.StorageTransform!.Action);
            Assert.Equal(StorageScope.SelectedItems, job.Request.StorageTransform!.Scope);
            var attribute = efs ? FileAttributes.Encrypted : FileAttributes.Compressed;
            Assert.Equal(!efs, File.GetAttributes(a).HasFlag(attribute));
            Assert.Equal(!efs, File.GetAttributes(b).HasFlag(attribute));
            await Until(() => listing.Generation != reviewedGeneration && !listing.IsRefreshing && listing.State == ListingState.Complete);
            Assert.Equal(change == "refresh" ? 0 : change == "marks" ? 2 : 1, listing.MarkedCount);
        }
        finally { ((OverlayDialogService)vm.Dialogs).CancelAll(); AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(StorageAction.Compress)]
    [InlineData(StorageAction.DecryptEfs)]
    public async Task Recursive_storage_refresh_updates_parent_and_descendant_tabs_and_preserves_other_marks(StorageAction action)
    {
        bool efs = action == StorageAction.DecryptEfs;
        bool arm64 = RequireNativeCommandFixture(efs);
        var platform = new WindowsStorageTransforms(qualifyNativeArm64: arm64);
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420,
            storageTransforms: efs ? platform.CreateEfsJobCandidate() : platform, qualifyNativeArm64Storage: arm64,
            prepareFiles: folder =>
            {
                string branch = Directory.CreateDirectory(Path.Join(folder, "branch")).FullName;
                string nested = Directory.CreateDirectory(Path.Join(branch, "nested")).FullName;
                string unrelated = Directory.CreateDirectory(Path.Join(folder, "branch-other")).FullName;
                string content = Path.Join(nested, "content.txt");
                File.WriteAllText(content, "scoped file");
                File.WriteAllText(Path.Join(unrelated, "other.txt"), "unrelated file");
                if (efs) foreach (string path in new[] { branch, nested, content }) File.Encrypt(path);
            });
        try
        {
            var origin = vm.ActiveTab!;
            string parent = origin.Location!.Path;
            string branch = Path.Join(parent, "branch"), nested = Path.Join(branch, "nested");
            string unrelated = Path.Join(parent, "branch-other");
            var branchTab = origin.Panel.OpenTab(Location.FileSystem(branch), activate: false);
            var nestedTab = origin.Panel.OpenTab(Location.FileSystem(nested), activate: false);
            var unrelatedTab = origin.Panel.OpenTab(Location.FileSystem(unrelated), activate: false);
            var tabs = new[] { origin, branchTab, nestedTab, unrelatedTab };
            bool ListingsSettled() => tabs.All(tab => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing);
            await Until(ListingsSettled);
            origin.Listing.MarkNames(["branch"], true);
            nestedTab.Listing.MarkNames(["content.txt"], true);
            unrelatedTab.Listing.MarkNames(["other.txt"], true);
            var navigation = tabs.Select(tab => tab.NavigationGeneration).ToArray();

            string label = efs ? "Decrypt" : "Compress";
            string command = efs ? CommandIds.EfsProtection : CommandIds.StorageAttributes;
            IEnumerable<RadioButton> Options(string name) => window.GetVisualDescendants().OfType<RadioButton>()
                .Where(button => AutomationProperties.GetName(button) == name);
            string? Refusal() => window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.IsReadOnly &&
                box.Text?.Contains("Captured locator:", StringComparison.Ordinal) == true)?.Text;
            Task OpenReview()
            {
                Assert.Same(origin, vm.ActiveTab);
                Assert.Equal(navigation, tabs.Select(tab => tab.NavigationGeneration));
                Assert.All(new[] { origin, nestedTab, unrelatedTab }, tab => Assert.Equal(1, tab.Listing.MarkedCount));
                Assert.Equal(branch, Assert.Single(origin.Listing.GetSelection()).FileSystemPath);
                var availability = vm.GetAvailability(command);
                Assert.True(availability.Enabled, availability.Reason);
                return vm.ExecuteAsync(command);
            }
            Task WaitForReview(Task request) => Until(() => Options(label).Any() || request.IsCompleted || Refusal() is not null);

            int capturedGeneration = origin.Listing.Generation;
            var opening = OpenReview();
            await WaitForReview(opening);
            // Initial filesystem notifications can arrive after the first listing completes. Reissue the
            // command once after FileCat explicitly retires that review and the original listing settles.
            if (opening.IsCompleted && origin.Listing.Generation != capturedGeneration &&
                vm.CurrentNotification?.Message == "The selected listing changed while checking. Nothing was changed.")
            {
                await opening;
                Assert.Empty(services.Jobs.Jobs);
                await Until(ListingsSettled);
                opening = OpenReview();
                await WaitForReview(opening);
            }
            if (opening.IsCompleted) await opening;
            Assert.True(Options(label).Any(), Refusal() ?? vm.CurrentNotification?.Message ?? "The storage review closed before it exposed an action.");
            await Until(ListingsSettled);
            var generations = tabs.Select(tab => tab.Listing.Generation).ToArray();
            Assert.Single(Options(label)).IsChecked = true;
            Assert.Single(Options("Selected folders and their existing contents")).IsChecked = true;
            if (efs) AcceptDecryption(window, "I understand this removes folder encryption defaults and EFS protection");
            var primary = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button is not RadioButton && button.Content as string == label);
            await Until(() => primary.IsEnabled);
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await opening;
            await Until(() => services.Jobs.Jobs.Count == 1 && services.Jobs.Jobs[0].State.IsFinished() &&
                vm.CurrentNotification?.Message.Contains("changed", StringComparison.OrdinalIgnoreCase) == true);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(StorageScope.ExistingDescendants, job.Request.StorageTransform!.Scope);
            Assert.Equal(3, job.StorageResult!.Counts.Changed);
            await Until(() => tabs.Take(3).Select((tab, index) => tab.Listing.Generation > generations[index] &&
                !tab.Listing.IsRefreshing && tab.Listing.State == ListingState.Complete).All(ready => ready));
            Assert.Equal(generations[3], unrelatedTab.Listing.Generation);
            Assert.Equal(navigation, tabs.Select(tab => tab.NavigationGeneration));
            Assert.Same(origin, vm.ActiveTab);
            Assert.Equal(0, origin.Listing.MarkedCount);
            Assert.Equal("content.txt", Assert.Single(nestedTab.Listing.GetSelection()).Name);
            Assert.Equal("other.txt", Assert.Single(unrelatedTab.Listing.GetSelection()).Name);

            var attribute = efs ? FileAttributes.Encrypted : FileAttributes.Compressed;
            Assert.All(new[] { branch, nested, Path.Join(nested, "content.txt") },
                path => Assert.Equal(!efs, File.GetAttributes(path).HasFlag(attribute)));
            Assert.False(File.GetAttributes(Path.Join(unrelated, "other.txt")).HasFlag(attribute));
        }
        finally { ((OverlayDialogService)vm.Dialogs).CancelAll(); AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(StorageAction.Compress)]
    [InlineData(StorageAction.DecryptEfs)]
    public async Task A_scoped_storage_command_refreshes_only_the_existing_find_results_and_keeps_later_marks(StorageAction action)
    {
        bool efs = action == StorageAction.DecryptEfs;
        bool arm64 = RequireNativeCommandFixture(efs);
        var platform = new WindowsStorageTransforms(qualifyNativeArm64: arm64);
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            storageTransforms: efs ? platform.CreateEfsJobCandidate() : platform, qualifyNativeArm64Storage: arm64,
            prepareFiles: folder =>
            {
                foreach (string branch in new[] { "left", "right" })
                {
                    string path = Path.Join(Directory.CreateDirectory(Path.Join(folder, branch)).FullName, "canary.bin");
                    File.WriteAllText(path, new string('A', 8192));
                    if (efs) File.Encrypt(path);
                }
            });
        FindWindow? find = null;
        try
        {
            string left = Path.Join(root, "files", "left", "canary.bin"), right = Path.Join(root, "files", "right", "canary.bin");
            var mainTab = vm.ActiveTab!;
            await Until(() => mainTab.Listing.State == ListingState.Complete);
            vm.Execute(CommandIds.FindFiles);
            await Until(() => FindWindow.OpenWindows.Count == 1);
            find = FindWindow.OpenWindows.Single();
            find.NamesBox.Text = "canary.bin";
            find.StartSearch(RefineMode.Replace);
            await Until(() => find.StorageCommandAvailability.Enabled);
            var tab = find.ResultsTab!;
            var listing = tab.Listing;
            var location = tab.Location;
            var session = find.Session;
            long navigation = tab.NavigationGeneration;
            ItemRef[] Results() => Enumerable.Range(0, listing.VisibleCount)
                .Select(index => listing.GetItemRef(listing.GetStoreIndex(index))).Where(item => item.Kind == EntryKind.File).ToArray();
            var results = Results();
            Assert.Equal(2, results.Length);
            var selected = Assert.Single(results, item => item.FileSystemPath == left);
            var other = Assert.Single(results, item => item.FileSystemPath == right);
            listing.MarkItems([selected], [0], true);
            Assert.Equal(selected, Assert.Single(listing.GetSelection()));

            string label = efs ? "Decrypt" : "Compress";
            find.Run(efs ? CommandIds.EfsProtection : CommandIds.StorageAttributes);
            RadioButton? ActionButton() => find.GetVisualDescendants().OfType<RadioButton>()
                .SingleOrDefault(button => AutomationProperties.GetName(button) == label);
            await Until(() => ActionButton() is not null);
            ActionButton()!.IsChecked = true;
            if (efs) AcceptDecryption(find);
            Button Primary() => find.GetVisualDescendants().OfType<Button>()
                .Single(button => button is not RadioButton && button.Content as string == label);
            await Until(() => Primary().IsEnabled);
            listing.MarkItems([selected, other], [0], false);
            listing.MarkItems([selected, other], [1], true);
            long marks = listing.MarkRevision;
            int generation = listing.Generation;
            Primary().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Until(() => services.Jobs.Jobs.Count == 1 && services.Jobs.Jobs[0].State.IsFinished() &&
                listing.Generation != generation && find.StorageCommandAvailability.Enabled);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(1, job.StorageResult!.Counts.Changed);
            Assert.Equal(selected, Assert.Single(job.Request.Sources));
            Assert.Same(mainTab, vm.ActiveTab);
            Assert.Same(tab, find.ResultsTab);
            Assert.Same(session, find.Session);
            Assert.Equal(location, tab.Location);
            Assert.Equal(navigation, tab.NavigationGeneration);
            Assert.Equal(marks, listing.MarkRevision);
            Assert.Equal(other, Assert.Single(listing.GetSelection()));
            Assert.Equal("canary.bin", find.NamesBox.Text);

            var attribute = efs ? FileAttributes.Encrypted : FileAttributes.Compressed;
            foreach (var result in Results())
                Assert.Equal(result.Equals(selected) != efs, ((FileAttributes)result.NativeAttributes).HasFlag(attribute));
            Assert.Equal(!efs, File.GetAttributes(left).HasFlag(attribute));
            Assert.Equal(efs, File.GetAttributes(right).HasFlag(attribute));
            Assert.Equal(new string('A', 8192), File.ReadAllText(left));
            Assert.Equal(new string('A', 8192), File.ReadAllText(right));
        }
        finally { find?.Dialogs.CancelAll(); find?.Close(); AccessibilityTests.Close(services, window, root); }
    }
}
