using System.Formats.Tar;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>Plan §11.6: every surface offers what the keyboard offers; a writable archive's background menu creates folders.</summary>
public sealed class ArchiveContextMenuTests
{
    [AvaloniaFact]
    public async Task Writable_native_archive_background_offers_Create_folder_like_F7()
    {
        // A backend with a target reviewer makes TAR members writable here; the reviewer itself is never called.
        using var f = await ArchiveCreationWorkflowTests.Fixture.Open(customBackends: new([new ReviewableBackend()]));
        string tar = Path.Combine(f.Files, "owned.tar");
        using (var stream = File.Create(tar))
        using (var writer = new TarWriter(stream, TarEntryFormat.Ustar))
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "inside.txt") { DataStream = new MemoryStream("inside"u8.ToArray()) });
        var location = FileCat.Archives.ArchiveProvider.ForFile(tar);
        var panel = f.Vm.Workspace.ActivePanel!; var tab = panel.ActiveTab!;
        tab.Navigate(location);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (tab.Listing.State != ListingState.Complete)
        { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException(); await Task.Delay(10); }
        var menu = ContextMenuFactory.BuildBackground(f.Vm, panel, tab, location);
        var create = Assert.Single(((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>(), item => item.Header as string == "Create folder…");
        Assert.True(create.IsEnabled);
    }

    private sealed class ReviewableBackend : IArchiveJobBackend
    {
        public ArchiveWriteFormat Format => ArchiveWriteFormat.Tar;
        public ArchiveJobAvailability Availability(ArchivePlan plan) => ArchiveJobAvailability.Ready;
        public ArchiveTargetReviewer? TargetReviewer { get; } = new((_, _) => throw new InvalidOperationException("Not reviewed in this test."),
            new ArchiveAssessmentClient((_, _) => throw new InvalidOperationException("No worker in this test.")));
        public void Execute(ArchiveJobExecution execution) => throw new InvalidOperationException("Not executed in this test.");
    }
}
