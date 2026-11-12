using System.Diagnostics;
using System.IO.Compression;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using Backend = FileCat.App.Tests.ArchiveTargetUpdateUiTests.Backend;
using Fixture = FileCat.App.Tests.ArchiveCreationWorkflowTests.Fixture;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

// Ledger rows 6 and 13 (route decision of 12 November 2026): where a ZIP route is composed, ZIP creation, member commands
// and F5 take the worker profile through the same native review and job as every other worker format; elsewhere ZIP keeps
// its existing route. The scripted backend observes dispatch; the native lanes prove the real workers.
public sealed class ArchiveZipWorkerRouteTests
{
    [AvaloniaTheory]
    [InlineData(0, 0)] // Normal compression: the worker's normal Deflate
    [InlineData(1, 1)] // Fastest
    [InlineData(2, 3)] // No compression: stored
    public async Task Pack_takes_the_ZIP_worker_profile_and_its_level_where_a_ZIP_route_is_composed(int choice, int level)
    {
        var backend = new ArchiveCreationWorkflowTests.RecordingBackend(ArchiveWriteFormat.Zip);
        using var f = await Fixture.Open(customBackends: new([backend]));
        var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(ArchiveWriteFormat.Zip);
        f.Combo("Compression").SelectedIndex = choice;
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Pack"); await f.Complete(operation);
        var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
        Assert.Equal(JobState.Completed, job.State);
        var profile = Assert.IsType<ArchiveWriteProfile>(job.Request.Archive!.Profile);
        Assert.Equal(ArchiveWriteFormat.Zip, profile.Format); Assert.Equal(level, profile.CompressionLevel);
        Assert.Same(job.Request.Archive, backend.Plan);
    }

    [AvaloniaFact]
    public async Task Without_a_ZIP_route_member_commands_keep_the_existing_ZIP_route()
    {
        var backend = new Backend(ArchiveWriteFormat.Tar, 0); using var f = await Fixture.Open(customBackends: new([backend]));
        var zip = Synthetic(f); f.Vm.ActiveTab!.Navigate(zip);
        await Wait(() => f.Vm.ActiveTab.Listing.State == ListingState.Complete && f.Vm.ActiveTab.Listing.FocusName("kept"));
        Assert.Null(f.Vm.ActiveTab.Listing.GetItemRef(f.Vm.ActiveTab.Listing.FocusedStoreIndex).ArchiveRevision);
        var command = f.Vm.ExecuteAsync(CommandIds.DeletePermanent); await Wait(() => f.Button("Delete") is not null);
        f.Click("Delete"); await f.Complete(command);
        var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
        Assert.Null(job.Request.Archive!.Profile); Assert.Null(backend.Plan); Assert.Equal(0, backend.Assessments);
    }

    [AvaloniaFact]
    public async Task F8_removes_a_ZIP_member_through_the_worker_review()
    {
        var backend = new Backend(ArchiveWriteFormat.Zip, 0) { MemberCatalog = true }; using var f = await Fixture.Open(customBackends: new([backend]));
        var zip = Synthetic(f); f.Vm.ActiveTab!.Navigate(zip);
        await Wait(() => f.Vm.ActiveTab.Listing.State == ListingState.Complete && f.Vm.ActiveTab.Listing.FocusName("kept"));
        var row = f.Vm.ActiveTab.Listing.GetItemRef(f.Vm.ActiveTab.Listing.FocusedStoreIndex);
        // The row carries the listing's token; the command resolves it to the archive's SHA-256 (ArchiveListingRevision).
        Assert.True(ArchiveListingRevision.IsToken(row.ArchiveRevision));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zip.Container!.Path))),
            f.Services.Zip.ResolveListingRevision(row.ArchiveRevision!, TestContext.Current.CancellationToken));
        var command = f.Vm.ExecuteAsync(CommandIds.DeletePermanent); await Wait(() => f.Button("Remove") is not null);
        Assert.Contains("1 stored entries", f.Text()); f.Click("Remove"); await f.Complete(command);
        await Finished(f);
        var change = Assert.Single(backend.Plan!.Changes); Assert.Equal(ArchiveChangeKind.Delete, change.Kind); Assert.Equal("kept", change.MemberPath);
        Assert.Equal(ArchiveWriteFormat.Zip, backend.Plan.Profile!.Format); Assert.Equal(0, backend.ActiveLeases);
    }

    [AvaloniaFact]
    public async Task F7_and_F2_create_and_rename_ZIP_members_through_the_worker_review()
    {
        var backend = new Backend(ArchiveWriteFormat.Zip, 0) { MemberCatalog = true }; using var f = await Fixture.Open(customBackends: new([backend]));
        var zip = Synthetic(f); var tab = f.Vm.ActiveTab!; tab.Navigate(zip);
        await Wait(() => tab.Listing.State == ListingState.Complete);
        var mkdir = f.Vm.ExecuteAsync(CommandIds.MakeDirectory); await Wait(() => f.Button("Create") is not null);
        f.Input("Folder name (use / for nested folders):").Text = "new/nested"; Dispatcher.UIThread.RunJobs(); f.Click("Create"); await f.Complete(mkdir);
        await Finished(f);
        Assert.Equal("new/nested/", Assert.Single(backend.Plan!.Changes).MemberPath); Assert.Equal(ArchiveWriteFormat.Zip, backend.Plan.Profile!.Format);

        backend.Plan = null; tab.Navigate(zip); await Wait(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("kept"));
        var rename = f.Vm.ExecuteAsync(CommandIds.Rename);
        await Wait(() => f.Window.GetVisualDescendants().OfType<TextBox>().Any(box => AutomationProperties.GetName(box) == "Rename selected item" && box.IsEffectivelyVisible));
        var editor = f.Window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Rename selected item" && box.IsEffectivelyVisible);
        editor.Text = "renamed"; editor.Focus(); f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait(() => f.Button("Rename") is not null || f.Services.Jobs.Jobs.Count == 2);
        if (f.Button("Rename") is not null) f.Click("Rename");
        await f.Complete(rename); await Finished(f);
        var change = Assert.Single(backend.Plan!.Changes); Assert.Equal(ArchiveChangeKind.Rename, change.Kind);
        Assert.Equal("kept", change.MemberPath); Assert.Equal("renamed", change.NewMemberPath); Assert.Equal(0, backend.ActiveLeases);
    }

    [AvaloniaFact]
    public async Task F5_adds_to_a_ZIP_through_the_worker_review()
    {
        var backend = new Backend(ArchiveWriteFormat.Zip, 0) { MemberCatalog = true }; using var f = await Fixture.Open(customBackends: new([backend]));
        var zip = Synthetic(f); var target = f.Vm.Workspace.Panels[1].ActiveTab!;
        target.Navigate(zip); await Wait(() => target.Listing.State == ListingState.Complete);
        var command = f.Vm.ExecuteAsync(CommandIds.Copy);
        await Wait(() => f.Button("Copy")?.IsEffectivelyEnabled == true && f.FindInput("Destination") is not null);
        f.Click("Copy"); await f.Complete(command); await Finished(f);
        var change = Assert.Single(backend.Plan!.Changes); Assert.Equal(ArchiveChangeKind.AddFile, change.Kind);
        Assert.Equal("a.txt", change.MemberPath); Assert.Equal(Path.Combine(f.Files, "a.txt"), change.SourcePath);
        Assert.Equal(ArchiveWriteFormat.Zip, backend.Plan.Profile!.Format); Assert.Equal(ArchiveAddConflictPolicy.Ask, backend.Plan.AddConflictPolicy);
    }

    // Review finding of 12 November 2026: a ZIP route composed here whose worker cannot run (an AppImage, a framework-
    // dependent build, a portable copy in a folder the user does not own) refuses ZIP changes with the worker's reason. The
    // old in-process route is never taken instead, and the archive is not touched.
    [AvaloniaFact]
    public async Task A_composed_ZIP_route_whose_worker_cannot_run_refuses_with_its_reason_and_never_takes_the_old_route()
    {
        const string why = "Archive writing is not available from the AppImage (test).";
        using var f = await Fixture.Open(customBackends: new([new Unavailable(ArchiveWriteFormat.Zip, why)]));
        var zip = Synthetic(f); byte[] original = File.ReadAllBytes(zip.Container!.Path); var tab = f.Vm.ActiveTab!;
        tab.Navigate(zip); await Wait(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("kept"));
        async Task Refused(string command)
        {
            // Repeated identical notices are counted on one entry, so a new refusal is a new entry or a new occurrence.
            var before = f.Vm.CurrentNotification;
            await f.Complete(f.Vm.ExecuteAsync(command));
            await Wait(() => f.Vm.CurrentNotification is { } now && (before is null || now.Id != before.Id || now.Occurrences != before.Occurrences));
            Assert.Contains(why, f.Vm.CurrentNotification!.Message); Assert.True(f.Vm.CurrentNotification.IsError);
            Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal(original, File.ReadAllBytes(zip.Container!.Path));
        }
        await Refused(CommandIds.DeletePermanent);
        await Refused(CommandIds.MakeDirectory);
        await Refused(CommandIds.Edit);

        // Pack to a new ZIP: the dialog says why instead of creating it through the old route.
        tab.Navigate(Location.FileSystem(f.Files)); await Wait(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("a.txt"));
        var pack = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(ArchiveWriteFormat.Zip);
        await Wait(() => f.Text().Contains(why, StringComparison.Ordinal));
        Assert.False(f.Button("Pack")!.IsEffectivelyEnabled);
        f.Click("Cancel"); await f.Complete(pack);
        Assert.Empty(f.Services.Jobs.Jobs); Assert.Empty(Directory.GetFiles(f.Files, "*.zip"));
    }

    // Review finding of 12 November 2026: an edit started on the old ZIP route before a ZIP route was composed stays that
    // edit. F4 on its member shows it (modified here: its commit, discard and copy actions) instead of reviewing the archive
    // and starting a second edit of the same member, whose commit would conflict with the first.
    [AvaloniaFact]
    public async Task F4_shows_an_edit_started_on_the_old_ZIP_route_instead_of_starting_a_second_one()
    {
        if (Environment.IsPrivilegedProcess) Assert.Skip("Edit sessions refuse an elevated token; run unelevated.");
        var backend = new Backend(ArchiveWriteFormat.Zip, 0) { MemberCatalog = true }; using var f = await Fixture.Open(customBackends: new([backend]));
        var zip = Synthetic(f); var tab = f.Vm.ActiveTab!;
        var legacy = f.Services.EditSessions.Create(f.Services.Zip, new FileCat.Core.Resources.ItemRef(zip, "kept", FileCat.Core.Resources.EntryKind.File),
            TestContext.Current.CancellationToken);
        Assert.True(legacy.IsLegacyZip); File.AppendAllText(legacy.WorkingPath, " edited before the route");
        tab.Navigate(zip); await Wait(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("kept"));
        var edit = f.Vm.ExecuteAsync(CommandIds.Edit);
        await Wait(() => f.Button("Discard…") is not null);
        Assert.Equal(0, backend.Assessments);
        Assert.Equal(legacy.Id, Assert.Single(f.Services.EditSessions.LoadAll()).Id);
        f.Click("Close"); await f.Complete(edit);
        Assert.Empty(f.Services.Jobs.Jobs);
    }

    // Windows Send to → Compressed (zipped) folder, handed to FileCat: the same route rule as Pack.
    [AvaloniaFact]
    public async Task Send_to_a_compressed_folder_takes_the_ZIP_route_where_one_is_composed()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows Send to.");
        var backend = new ArchiveCreationWorkflowTests.RecordingBackend(ArchiveWriteFormat.Zip);
        using (var f = await Fixture.Open(customBackends: new([backend])))
        {
            f.Vm.PackFromWindowsSendTo([Path.Combine(f.Files, "a.txt")], f.Vm.ActiveTab!);
            var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(new ArchiveWriteProfile(ArchiveWriteFormat.Zip, compressionLevel: 0), job.Request.Archive!.Profile);
            Assert.Equal(Path.Combine(f.Files, "a.zip"), backend.Plan!.ArchivePath);
        }
        const string why = "The archive worker cannot read FileCat's own files here (test).";
        using (var f = await Fixture.Open(customBackends: new([new Unavailable(ArchiveWriteFormat.Zip, why)])))
        {
            f.Vm.PackFromWindowsSendTo([Path.Combine(f.Files, "a.txt")], f.Vm.ActiveTab!);
            await Wait(() => f.Vm.CurrentNotification is not null);
            Assert.Contains(why, f.Vm.CurrentNotification!.Message);
            Assert.Empty(f.Services.Jobs.Jobs); Assert.Empty(Directory.GetFiles(f.Files, "*.zip"));
        }
    }

    private sealed class Unavailable(ArchiveWriteFormat format, string reason) : IArchiveJobBackend
    {
        public ArchiveWriteFormat Format => format;
        public ArchiveJobAvailability Availability(ArchivePlan plan) => new(false, reason);
        public void Execute(ArchiveJobExecution execution) => throw new InvalidOperationException("An unavailable backend never runs.");
    }

    // A real ZIP whose members match the scripted backend's catalog: folder/, folder/old, kept and two "same".
    private static Location Synthetic(Fixture f)
    {
        string path = Path.Combine(f.Root, "synthetic.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("folder/");
            foreach (var (name, text) in new[] { ("folder/old", "o"), ("kept", "k"), ("same", "1"), ("same", "2") })
            { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(text); }
        }
        return ZipProvider.ForFile(path);
    }

    private static async Task Finished(Fixture f)
    {
        var job = f.Services.Jobs.Jobs.Last(); await Wait(() => job.State.IsFinished());
        Assert.True(job.State == JobState.Completed, job.State + ": " + job.Summary + " " + JobIssue.Summarize(job.Issues));
    }

    private static async Task Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready()) { Dispatcher.UIThread.RunJobs(); if (clock.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("The ZIP route UI did not settle."); await Task.Delay(10); }
    }
}
