using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class StorageEfsRecoveryDialogTests
{
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task An_Efs_recovery_prompt_defaults_to_cancel_and_binds_the_accessible_selected_certificate(bool declare, bool pending, bool nonexportable)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var job = new Job(new JobRequest
        {
            Kind = JobKind.StorageTransform,
            StorageTransform = new(StorageAction.EncryptEfs, StorageScope.SelectedItems, Guid.NewGuid()),
        }, "Encrypt with EFS", "fixture", [], []);
        StorageProblem? backupProblem = nonexportable
            ? new(StorageReasonKind.PermissionOrPolicy, "efs-key-nonexportable", "This key is non-exportable.", true) : null;
        var request = new StorageEfsRecoveryDecision("FIXTURE\\user", "S-1-5-21-1-2-3-1000",
            [new(new string('A', 40), "First test certificate"), new(new string('B', 40), "Second test certificate",
                RequiresProviderAccess: pending, BackupProblem: backupProblem)],
            "Windows canceled the backup.", null, null, SelectedCertificateSha1: pending ? new string('B', 40) : null);
        var shown = OperationDialogs.ShowDecisionAsync(vm, new(job, request));
        try
        {
            Dispatcher.UIThread.RunJobs();
            var controls = window.GetVisualDescendants().ToArray();
            var picker = Assert.Single(controls.OfType<ComboBox>(), c => AutomationProperties.GetName(c) == "Reviewed EFS certificate");
            var cancel = Assert.Single(controls.OfType<Button>(), b => b.Content as string == "Cancel operation");
            Assert.True(cancel.IsDefault);
            Assert.Contains(controls.OfType<TextBlock>(), b => b.Text == "Windows canceled the backup.");
            Assert.Contains(controls.OfType<TextBlock>(), b => b.Text?.Contains("FIXTURE\\user", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(controls.OfType<Button>(), b => b.Content as string == "Back up EFS key…");
            var useExisting = Assert.Single(controls.OfType<Button>(), b => b.Content as string == "Use existing recovery");
            if (pending)
            {
                Assert.Equal(1, picker.SelectedIndex);
                Assert.False(useExisting.IsEnabled);
                useExisting.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(shown.IsCompleted);
                Assert.DoesNotContain(controls.OfType<Button>(), b => b.Content as string == "Access EFS key…");
                picker.SelectedIndex = 0;
                Assert.True(useExisting.IsEnabled);
            }
            else picker.SelectedIndex = 1;
            if (nonexportable)
            {
                Assert.True(useExisting.IsEnabled);
                Assert.Equal(backupProblem!.Message, AutomationProperties.GetHelpText(picker));
                Assert.Contains(controls.OfType<TextBlock>(), text => text.Text == backupProblem.Message);
            }
            var selected = declare ? useExisting : cancel;
            selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var answer = await shown.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(declare ? DecisionAction.DeclareEfsRecovery : DecisionAction.CancelJob, answer.Action);
            Assert.False(answer.ApplyToAll);
            Assert.Equal(declare ? request.Keys[picker.SelectedIndex].CertificateSha1 : null, answer.EfsRecovery?.CertificateSha1);
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await shown;
            AccessibilityTests.Close(services, window, root);
        }
    }
}
