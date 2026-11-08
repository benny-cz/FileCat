using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.Archives;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Fixture = FileCat.App.Tests.ArchiveCreationWorkflowTests.Fixture;
using Backend = FileCat.App.Tests.ArchiveTargetUpdateUiTests.Backend;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

// Command/ownership fixtures. Synthetic listings and scripted assessments do not prove publication or hidden-header browsing.
public sealed class ArchiveMemberCommandUiTests
{
    public static IEnumerable<object[]> Profiles()
    {
        foreach (var format in new[] { ArchiveWriteFormat.Tar, ArchiveWriteFormat.TarGzip, ArchiveWriteFormat.TarBzip2, ArchiveWriteFormat.TarZstd }) yield return [format, 0];
        foreach (var format in new[] { ArchiveWriteFormat.Rar5, ArchiveWriteFormat.SevenZip }) for (int mode = 0; mode < 3; mode++) yield return [format, mode];
    }

    [AvaloniaTheory, MemberData(nameof(Profiles))]
    public async Task Folder_removal_reviews_physical_scope_and_preserves_existing_password_mode(ArchiveWriteFormat format, int mode)
    {
        var backend = new Backend(format, mode) { MemberCatalog = true }; using var f = await Fixture.Open(customBackends: new([backend]));
        var target = Setup(f, format); f.Vm.ActiveTab!.Navigate(target);
        await Wait(() => f.Vm.ActiveTab.Listing.State == ListingState.Complete && f.Vm.ActiveTab.Listing.FocusName("folder"));
        var command = f.Vm.ExecuteAsync(CommandIds.DeletePermanent);
        if (mode != 0) await Unlock(f);
        await Wait(() => f.Button("Remove") is not null);
        Assert.Contains("2 stored entries", f.Text()); Assert.Contains("directory records", f.Text());
        Assert.Empty(f.Services.Jobs.Jobs); f.Click("Remove"); await f.Complete(command);
        Assert.True(f.Services.Jobs.Jobs.Count == 1, "Removal submitted no job: " + f.Text());
        await Wait(() => f.Services.Jobs.Jobs.Single().State.IsFinished());
        Assert.Equal(JobState.Completed, f.Services.Jobs.Jobs.Single().State);
        var change = Assert.Single(backend.Plan!.Changes); Assert.Equal("folder/", change.MemberPath); Assert.Null(change.Ordinal);
        Assert.Equal((ArchiveEncryptionMode)mode, backend.Plan.Profile!.Encryption.Mode); Assert.Equal(0, backend.ActiveLeases);
    }

    [AvaloniaTheory, InlineData(false, false), InlineData(false, true), InlineData(true, false), InlineData(true, true)]
    public async Task Native_F5_F6_capture_typed_rename_and_queue_mode(bool move, bool queue)
    {
        var backend = new Backend(ArchiveWriteFormat.SevenZip, 2) { MemberCatalog = true }; using var f = await Fixture.Open(customBackends: new([backend]));
        var target = Setup(f, backend.Format); var source = f.Vm.ActiveTab!; var targetTab = f.Vm.Workspace.Panels[1].ActiveTab!;
        targetTab.Navigate(target); await Wait(() => targetTab.Listing.State == ListingState.Complete);
        var command = f.Vm.ExecuteAsync(move ? CommandIds.Move : CommandIds.Copy);
        await Wait(() => f.Button(move ? "Move" : "Copy") is not null && f.FindInput("Destination") is not null);
        f.Input("Destination").Text = target.Container!.Path + Path.DirectorySeparatorChar + "renamed.txt";
        await Wait(() => f.Text().Contains("→ as “renamed.txt”", StringComparison.Ordinal));
        f.Click(queue ? "Queue" : move ? "Move" : "Copy"); await Unlock(f); await f.Complete(command);
        await Wait(() => f.Services.Jobs.Jobs.Single().State.IsFinished());
        var job = f.Services.Jobs.Jobs.Single(); Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(move, backend.Plan!.MoveSources); Assert.Equal(queue ? QueueMode.Queue : QueueMode.Start, job.Request.Mode);
        Assert.Equal("renamed.txt", Assert.Single(backend.Plan.Changes).MemberPath); Assert.Equal(ArchiveAddConflictPolicy.Ask, backend.Plan.AddConflictPolicy);
        Assert.Equal(Path.Combine(f.Files, "a.txt"), backend.Plan.Changes.Single().SourcePath);
        Assert.Equal(0, backend.ActiveLeases); Assert.True(File.Exists(Path.Combine(f.Files, "a.txt"))); // Scripted backend does no removal.
    }

    [AvaloniaFact]
    public async Task Wrong_password_can_retry_and_duplicate_removal_keeps_the_exact_ordinal()
    {
        var backend = new Backend(ArchiveWriteFormat.Rar5, 1) { MemberCatalog = true }; using var f = await Fixture.Open(customBackends: new([backend]));
        f.Vm.ActiveTab!.Navigate(Setup(f, backend.Format));
        await Wait(() => f.Vm.ActiveTab.Listing.State == ListingState.Complete && f.Vm.ActiveTab.Listing.FocusName("same"));
        // The second same-name row has its own captured ordinal.
        f.Vm.ActiveTab.Listing.SetFocus(f.Vm.ActiveTab.Listing.FocusedStoreIndex + 1);
        var command = f.Vm.ExecuteAsync(CommandIds.DeletePermanent);
        await Wait(() => f.Button("Unlock") is not null); var first = f.Input("Existing archive password"); first.Text = "wrong"; f.Click("Unlock");
        await Wait(() => f.Button("Unlock") is not null && f.FindInput("Existing archive password") is { } next && !ReferenceEquals(first, next));
        Assert.Equal("", first.Text); Assert.Contains("Try again", f.Text()); f.Input("Existing archive password").Text = ArchiveTargetUpdateUiTests.Secret; f.Click("Unlock");
        await Wait(() => f.Button("Remove") is not null); Assert.Contains("copy 2 of 2", f.Text()); f.Click("Remove"); await f.Complete(command);
        await Wait(() => f.Services.Jobs.Jobs.Single().State.IsFinished());
        Assert.Equal(1, Assert.Single(backend.Plan!.Changes).Ordinal); Assert.Equal(0, backend.ActiveLeases);
    }

    [AvaloniaFact]
    public async Task A_stale_listing_never_launches_assessment_or_submits_a_member_job()
    {
        var backend = new Backend(ArchiveWriteFormat.Tar, 0) { MemberCatalog = true }; using var f = await Fixture.Open(customBackends: new([backend]));
        var target = Setup(f, backend.Format); f.Vm.ActiveTab!.Navigate(target);
        await Wait(() => f.Vm.ActiveTab.Listing.State == ListingState.Complete && f.Vm.ActiveTab.Listing.FocusName("kept"));
        File.WriteAllText(target.Container!.Path, "changed owned synthetic archive");
        await f.Complete(f.Vm.ExecuteAsync(CommandIds.DeletePermanent));
        Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal(0, backend.Assessments); Assert.Equal(0, backend.ActiveLeases);
    }

    [AvaloniaFact]
    public async Task F7_rejects_collisions_in_the_same_prompt_and_accepts_nested_directory_entries()
    {
        var backend = new Backend(ArchiveWriteFormat.Tar, 0) { MemberCatalog = true }; using var f = await Fixture.Open(customBackends: new([backend]));
        f.Vm.ActiveTab!.Navigate(Setup(f, backend.Format)); await Wait(() => f.Vm.ActiveTab.Listing.State == ListingState.Complete);
        var command = f.Vm.ExecuteAsync(CommandIds.MakeDirectory); await Wait(() => f.Button("Create") is not null);
        var input = f.Input("Folder name (use / for nested folders):"); input.Text = "kept/child";
        Dispatcher.UIThread.RunJobs(); Assert.False(f.Button("Create")!.IsEnabled);
        input.Text = "new/nested"; Dispatcher.UIThread.RunJobs(); Assert.True(f.Button("Create")!.IsEnabled); f.Click("Create"); await f.Complete(command);
        await Wait(() => f.Services.Jobs.Jobs.Single().State.IsFinished());
        Assert.Equal("new/nested/", Assert.Single(backend.Plan!.Changes).MemberPath); Assert.Equal(0, backend.ActiveLeases);
    }

    private static async Task Unlock(Fixture f)
    {
        await Wait(() => f.Button("Unlock") is not null); var input = f.Input("Existing archive password");
        Assert.Equal('•', input.PasswordChar); input.Text = ArchiveTargetUpdateUiTests.Secret; f.Click("Unlock");
        await Wait(() => input.Text == "");
    }

    private static Location Setup(Fixture f, ArchiveWriteFormat format)
    {
        string path = Path.Combine(f.Root, "synthetic" + ArchiveCreationFormat.All.Single(value => value.Format == format).Extension);
        File.WriteAllText(path, "owned synthetic archive");
        f.Services.Providers.Register(new Listing(f.Services.Archives));
        return ArchiveProvider.ForFile(path);
    }
    private sealed record Revision(string? ArchiveRevision, int Ordinal = 0) : IArchiveMemberRevision { public string? ArchiveMutationProblem => null; }
    private sealed class Listing(ArchiveProvider capabilities) : ResourceProvider
    {
        public override string Scheme => Schemes.Archive;
        public override string GetDisplayPath(Location location) => capabilities.GetDisplayPath(location);
        public override Location? GetParent(Location location) => capabilities.GetParent(location);
        public override Location? GetChildLocation(Location location, in EntryData entry) => capabilities.GetChildLocation(location, entry);
        public override string GetDeviceKey(Location location) => capabilities.GetDeviceKey(location);
        public override LocationCapabilities GetCapabilities(Location location) => capabilities.GetCapabilities(location);
        public override ItemRef GetItemRef(Location location, in EntryData entry) => new(location, entry.Name, entry.Kind, entry.Size, entry.Modified)
            { Ordinal = (entry.Tag as Revision)?.Ordinal ?? 0, ArchiveRevision = (entry.Tag as Revision)?.ArchiveRevision };
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            string revision = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(location.Container!.Path)));
            sink.AddBatch([new("folder", EntryKind.Directory, -1, 0) { Tag = new Revision(revision) }, new("kept", EntryKind.File, 1, 0) { Tag = new Revision(revision) },
                new("same", EntryKind.File, 1, 0) { Tag = new Revision(revision) }, new("same", EntryKind.File, 1, 0) { Tag = new Revision(revision, 1) }]);
            return Task.CompletedTask;
        }
    }
    private static async Task Wait(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready()) { Dispatcher.UIThread.RunJobs(); if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Archive member command did not settle."); await Task.Delay(10); }
    }
}
