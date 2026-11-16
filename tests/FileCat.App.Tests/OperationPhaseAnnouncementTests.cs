using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

/// <summary>
/// NVDA on Windows heard no archive phase: a UI Automation trace of a 256 MiB pack (10 November) saw no event at all from
/// the operation strip's phase text, while the final notification raised its events. UI Automation routes an element's
/// event by walking up its parents, and the strip is hidden until the first operation, so a screen reader that read the
/// window before then left the strip's own peer without a parent; the phase text's immediate parent was still set, so the
/// repair for a missing parent never ran. Each phase change must now reach the window through its automation parents.
/// </summary>
public sealed class OperationPhaseAnnouncementTests
{
    [AvaloniaFact]
    public void Each_archive_phase_change_reaches_the_window_through_its_automation_parents()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var windowPeer = ControlAutomationPeer.CreatePeerForElement(window);
            // While no operation is shown, a screen reader reads the window, and the hidden phase text's parents are looked
            // up once (as UI Automation does when it first places the element).
            Read(windowPeer);
            var phase = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Width == 1 &&
                AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Polite && text.FindAncestorOfType<Views.OperationsView>() is not null &&
                AutomationProperties.GetName(text) is not "Progress of the operation");
            Assert.False(phase.IsEffectivelyVisible);
            LiveRegions.Watch(phase); // As the application's live-region tracking does when the view loads.
            var peer = ControlAutomationPeer.CreatePeerForElement(phase);
            for (AutomationPeer? up = peer; up is not null; up = up.GetParent()) { }
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate }, "Create TAR + gzip archive", "local", [], []);
            job.SetState(JobState.Running);
            var view = new JobViewModel(job); view.Refresh();
            vm.Operations.Jobs.Add(view); vm.Operations.UpdateSummary();
            Dispatcher.UIThread.RunJobs();
            var spoken = new List<string>();
            peer.PropertyChanged += (_, e) =>
            {
                if (e.Property != AutomationElementIdentifiers.NameProperty) return;
                // As UI Automation does with the event: place its source in the window by walking up its parents.
                AutomationPeer? current = peer; int depth = 0;
                while (current is not null && !ReferenceEquals(current, windowPeer) && depth++ < 256) current = current.GetParent();
                if (ReferenceEquals(current, windowPeer)) spoken.Add(peer.GetName());
            };
            foreach (var step in new[] { ArchiveJobPhase.Preparing, ArchiveJobPhase.Rebuilding, ArchiveJobPhase.Checking, ArchiveJobPhase.Saving })
            {
                job.SetArchivePhase(step); view.Refresh(); Dispatcher.UIThread.RunJobs();
            }
            Assert.True(phase.IsEffectivelyVisible);
            Assert.Equal(new[] { "Preparing", "Rebuilding", "Checking", "Saving" }.Select(text => "Create TAR + gzip archive: " + text), spoken);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static void Read(AutomationPeer peer) { foreach (var child in peer.GetChildren()) Read(child); }
}
