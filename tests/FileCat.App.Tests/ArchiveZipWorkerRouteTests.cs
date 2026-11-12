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
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zip.Container!.Path))), row.ArchiveRevision);
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
