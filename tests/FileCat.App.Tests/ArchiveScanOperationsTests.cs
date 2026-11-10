using System.Formats.Tar;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>Plan §11.5: "Keep the operation center usable while a format scan is pending."</summary>
public sealed class ArchiveScanOperationsTests
{
    [AvaloniaFact]
    public async Task The_operation_center_stays_usable_while_a_format_scan_is_pending()
    {
        using var gate = new ManualResetEventSlim(false); int scans = 0;
        var backend = new HeldScanBackend(gate, () => Interlocked.Increment(ref scans));
        using var f = await ArchiveCreationWorkflowTests.Fixture.Open(customBackends: new([backend]));
        string tar = Path.Combine(f.Files, "owned.tar");
        using (var stream = File.Create(tar))
        using (var writer = new TarWriter(stream, TarEntryFormat.Ustar))
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "inside.txt") { DataStream = new MemoryStream("inside"u8.ToArray()) });
        var tab = f.Vm.ActiveTab!; var archive = FileCat.Archives.ArchiveProvider.ForFile(tar); tab.Navigate(archive);
        await Wait(() => tab.Location == archive && tab.Listing.State == ListingState.Complete);
        var dialogs = (OverlayDialogService)f.Vm.Dialogs;
        var operations = f.Window.GetVisualDescendants().OfType<OperationsView>().Single();
        var overlay = f.Window.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Name == "OverlayHost");
        int before = operations.ZIndex;

        var command = f.Vm.ExecuteAsync(CommandIds.MakeDirectory); // F7 in a writable archive scans it first.
        await Wait(() => f.Text().Contains("Operations stay available meanwhile (Ctrl+J).", StringComparison.Ordinal));
        Assert.Equal(1, Volatile.Read(ref scans));
        Assert.True(dialogs.IsOpen); Assert.True(dialogs.OperationsUsable);
        Assert.True(operations.ZIndex > overlay.ZIndex, "The operation center must be above the scan's backdrop.");
        var progress = f.Window.GetVisualDescendants().OfType<ProgressBar>().Single(bar => bar.IsEffectivelyVisible && bar.IsIndeterminate);
        Assert.Equal("Checking owned.tar", Avalonia.Automation.AutomationProperties.GetName(progress));

        // The operation center's shortcut opens it and moves the keyboard there; the scan stays open.
        f.Window.KeyPress(Key.J, RawInputModifiers.Control, PhysicalKey.J, null); await Settle();
        Assert.True(f.Vm.Operations.IsOpen); Assert.True(dialogs.IsOpen);
        Assert.True(Focused() is Visual inOperations && operations.IsVisualAncestorOf(inOperations), "Focus: " + Focused());
        // Panel commands remain blocked while the scan is pending: no second scan starts.
        _ = f.Vm.ExecuteAsync(CommandIds.MakeDirectory); await Settle();
        Assert.Equal(1, Volatile.Read(ref scans));
        // The same shortcut returns to the scan dialog.
        f.Window.KeyPress(Key.J, RawInputModifiers.Control, PhysicalKey.J, null); await Settle();
        Assert.True(Focused() is Visual inDialog && overlay.IsVisualAncestorOf(inDialog), "Focus: " + Focused());

        // Cancelling the scan restores the normal layering.
        Assert.IsType<Button>(f.Button("Cancel")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await f.Complete(command);
        Assert.False(dialogs.IsOpen); Assert.False(dialogs.OperationsUsable); Assert.Equal(before, operations.ZIndex);

        object? Focused() => f.Window.FocusManager?.GetFocusedElement();
        async Task Settle() { for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); f.Window.UpdateLayout(); await Task.Delay(10); } }
    }

    private static async Task Wait(Func<bool> ready)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!ready())
        {
            Dispatcher.UIThread.RunJobs();
            if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("The archive scan fixture did not settle.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A TAR route whose format scan blocks until cancelled, so the scan dialog stays open.</summary>
    private sealed class HeldScanBackend(ManualResetEventSlim gate, Action scanned) : IArchiveJobBackend
    {
        public ArchiveWriteFormat Format => ArchiveWriteFormat.Tar;
        public ArchiveJobAvailability Availability(ArchivePlan plan) => ArchiveJobAvailability.Ready;
        public ArchiveTargetReviewer? TargetReviewer { get; } = new((_, ct) =>
        {
            scanned(); gate.Wait(ct); throw new InvalidOperationException("Never released in this test.");
        }, new ArchiveAssessmentClient((_, _) => throw new InvalidOperationException("No worker in this test.")));
        public void Execute(ArchiveJobExecution execution) => throw new InvalidOperationException("Not executed in this test.");
    }
}
