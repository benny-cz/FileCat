using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>Explicit disposable-client opt-in. Exercises the actual command through native job retirement.</summary>
public sealed class StorageCommandNativeTests
{
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition()) { timeout.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); await Task.Delay(10, timeout.Token); }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task The_qualified_candidate_opens_Efs_protection_and_selected_file_backup_from_commands()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_UI") != "1" || !WindowsStorageTransforms.IsQualifiedHost,
            "Explicit disposable Windows 11 x64 client, medium integrity, NTFS EFS review qualification.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420,
            storageTransforms: new WindowsStorageTransforms().CreateEfsJobCandidate());
        try
        {
            string path = Path.Join(root, "files", "a.txt");
            File.Encrypt(path);
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            listing.MarkNames(["a.txt"], true);
            Assert.True(vm.GetAvailability(CommandIds.EfsProtection).Enabled);
            Assert.True(vm.GetAvailability(CommandIds.EfsFileBackup).Enabled);

            var protecting = vm.ExecuteAsync(CommandIds.EfsProtection);
            await Until(() => window.GetVisualDescendants().OfType<RadioButton>().Any(button => AutomationProperties.GetName(button) == "Decrypt"));
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await protecting;

            var backingUp = vm.ExecuteAsync(CommandIds.EfsFileBackup);
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(control => AutomationProperties.GetName(control) == "Selected file EFS certificate"));
            var controls = window.GetVisualDescendants().ToArray();
            var keys = Assert.Single(controls.OfType<ComboBox>(), control => AutomationProperties.GetName(control) == "Selected file EFS certificate");
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
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_UI") != "1" || !WindowsStorageTransforms.IsQualifiedHost,
            "Explicit disposable Windows 11 x64 client, medium integrity, NTFS EFS review qualification.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 760, height: 620,
            storageTransforms: new WindowsStorageTransforms().CreateEfsJobCandidate());
        try
        {
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
                    Assert.Equal(JobState.Completed, job.State);
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
        Assert.SkipWhen(efs ? Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_UI") != "1" || !WindowsStorageTransforms.IsQualifiedHost :
            Environment.GetEnvironmentVariable("FILECAT_TEST_STORAGE_UI") != "1" || !WindowsStorageReviewEvidence.IsSupportedHost,
            "Explicit disposable Windows 11 x64 client, medium integrity, NTFS command qualification.");
        var platform = new WindowsStorageTransforms();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420,
            storageTransforms: efs ? platform.CreateEfsJobCandidate() : platform);
        try
        {
            var tab = vm.ActiveTab!; var listing = tab.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            var original = tab.Location!;
            string a = Path.Join(original.Path, "a.txt"), b = Path.Join(original.Path, "b.txt");
            File.WriteAllText(a, new string('A', 8192)); File.WriteAllText(b, new string('B', 8192));
            if (efs) { File.Encrypt(a); File.Encrypt(b); }
            tab.Refresh(); await Until(() => listing.State == ListingState.Complete && !listing.IsRefreshing);
            listing.MarkNames(["a.txt", "b.txt"], true); long marks = listing.MarkRevision;
            string label = efs ? "Decrypt" : "Compress";
            vm.Execute(efs ? CommandIds.EfsProtection : CommandIds.StorageAttributes);
            IEnumerable<RadioButton> Actions() => window.GetVisualDescendants().OfType<RadioButton>()
                .Where(button => AutomationProperties.GetName(button) == label);
            await Until(() => Actions().Any());
            Assert.Single(Actions()).IsChecked = true;
            Button Primary() => window.GetVisualDescendants().OfType<Button>()
                .Single(button => button is not RadioButton && button.Content as string == label);
            if (efs)
            {
                CheckBox Consent(string prefix) => window.GetVisualDescendants().OfType<CheckBox>().Single(control => control.IsVisible &&
                    AutomationProperties.GetName(control)?.StartsWith(prefix, StringComparison.Ordinal) == true);
                Consent("I accept this concurrent link-change limitation").IsChecked = true;
                Consent("I understand the selected files will lose EFS protection").IsChecked = true;
            }
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
}
