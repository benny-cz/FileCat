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
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_Efs_recovery_prompt_defaults_to_cancel_and_binds_the_selected_certificate(bool declare)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var job = new Job(new JobRequest
        {
            Kind = JobKind.StorageTransform,
            StorageTransform = new(StorageAction.EncryptEfs, StorageScope.SelectedItems, Guid.NewGuid()),
        }, "Encrypt with EFS", "fixture", [], []);
        var request = new StorageEfsRecoveryDecision("FIXTURE\\user", "S-1-5-21-1-2-3-1000",
            [new(new string('A', 40), "First test certificate"), new(new string('B', 40), "Second test certificate")],
            "Windows canceled the backup.", null, null);
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
            picker.SelectedIndex = 1;
            var selected = declare ? Assert.Single(controls.OfType<Button>(), b => b.Content as string == "Use existing recovery") : cancel;
            selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var answer = await shown.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(declare ? DecisionAction.DeclareEfsRecovery : DecisionAction.CancelJob, answer.Action);
            Assert.False(answer.ApplyToAll);
            Assert.Equal(declare ? request.Keys[1].CertificateSha1 : null, answer.EfsRecovery?.CertificateSha1);
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await shown;
            AccessibilityTests.Close(services, window, root);
        }
    }
}
