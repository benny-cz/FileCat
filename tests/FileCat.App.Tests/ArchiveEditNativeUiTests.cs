using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Edit;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using Fixture = FileCat.App.Tests.ArchiveCreationWorkflowTests.Fixture;

namespace FileCat.App.Tests;

// Actual readers, confined assessment/rebuild and native publication through the headless F4/Commit flow.
public sealed class ArchiveEditNativeUiTests
{
    [AvaloniaTheory, MemberData(nameof(ArchiveMemberCommandUiTests.Profiles), MemberType = typeof(ArchiveMemberCommandUiTests))]
    public async Task Actual_native_F4_commit_verifies_publication_and_keeps_later_editor_saves(ArchiveWriteFormat format, int mode)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1")
            Assert.Skip("Run the explicit standard-user native edit UI lane.");
        string assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS") ?? throw new InvalidOperationException("Current native assets required.");
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets); string executable = Path.Combine(assets, "FileCat.exe");
        using var f = await Fixture.Open(customBackends: new(ArchiveCreationFormat.All.Where(value => value.Format != ArchiveWriteFormat.Zip)
            .Select(value => WindowsArchiveJobBackend.Create(value.Format, executable))), nativeWindowsOriginMarks: true);
        f.Services.Settings.Editor = new ToolDefinition { Executable = Path.Combine(f.Root, "owned-missing-editor.exe") };
        File.SetLastWriteTimeUtc(Path.Combine(f.Files, "a.txt"), DateTime.UnixEpoch.AddSeconds(1700000000));
        var create = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(format);
        if (mode != 0)
        {
            f.Combo("Password protection").SelectedIndex = mode;
            f.Input("Archive password").Text = f.Input("Confirm archive password").Text = ArchiveTargetUpdateUiTests.Secret;
        }
        await Wait(() => f.Button("Pack")?.IsEnabled == true); f.Click("Pack"); await f.Complete(create);
        var creation = Assert.Single(f.Services.Jobs.Jobs); await Finished(creation);
        string path = creation.Request.Archive!.ArchivePath;
        // Non-canonical bytes must survive exactly; origin is separate from member metadata.
        byte[] origin = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/owned%20archive\r\n"u8.ToArray();
        File.WriteAllBytes(path + ":Zone.Identifier", origin);
        var tab = f.Vm.ActiveTab!; var archive = ArchiveProvider.ForFile(path); tab.Navigate(archive);
        await Unlock(); await Wait(() => tab.Location == archive && !f.Vm.Dialogs.IsOpen &&
            tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("a.txt"));
        var available = f.Vm.GetAvailability(CommandIds.Edit); Assert.True(available.Enabled, available.Reason);
        var edit = f.Vm.ExecuteAsync(CommandIds.Edit); await Unlock(edit); await f.Complete(edit);
        var rows = f.Services.EditSessions.LoadAll();
        Assert.True(rows.Count == 1, $"F4 left {rows.Count} edit(s): \"{f.Vm.Notification}\" " +
            string.Join("; ", rows.Select(row => row.UnsupportedReason ?? row.Kind)));
        var session = rows[0]; Assert.True(session.IsNativeArchive);
        Assert.Equal("alpha", File.ReadAllText(session.WorkingPath)); Assert.Equal(origin, File.ReadAllBytes(session.WorkingPath + ":Zone.Identifier"));
        Assert.Equal(creation.Request.Archive.Profile!.Format, session.ArchiveProfile!.Format);
        Assert.Equal((ArchiveEncryptionMode)mode, session.ArchiveProfile.Encryption.Mode);
        File.WriteAllText(session.WorkingPath, "authorized archive edit é");
        string authorized = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(session.WorkingPath)));
        var commit = (Task)typeof(MainViewModel).GetMethod("CommitSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(f.Vm, [session])!;
        await Unlock(commit); await f.Complete(commit);
        Assert.Equal(2, f.Services.Jobs.Jobs.Count);
        File.WriteAllText(session.WorkingPath, "later editor save");
        var job = f.Services.Jobs.Jobs.Last(); await Finished(job);
        await Wait(() => f.Services.EditSessions.LoadAll().Single().LastCommitUtc is not null || f.Vm.Notification?.Contains("session could not be updated") == true);
        var saved = Assert.Single(f.Services.EditSessions.LoadAll());
        Assert.True(saved.LastCommitUtc is not null, f.Vm.Notification);
        Assert.Equal(authorized, saved.BaseSha256); Assert.Equal(EditState.Modified, f.Services.EditSessions.StateOf(saved));
        Assert.Equal("later editor save", File.ReadAllText(saved.WorkingPath)); Assert.Equal(origin, File.ReadAllBytes(path + ":Zone.Identifier"));
        Assert.Equal((ArchiveEncryptionMode)mode, saved.ArchiveProfile!.Encryption.Mode);
        Assert.NotNull(job.ArchiveEditPublication); Assert.Equal(saved.Baseline.Sha256Hex, job.ArchiveEditPublication.Native.Version.Sha256);
        Assert.Empty(JournalRecovery.Scan(f.Services.Paths.JournalDirectory));

        async Task Unlock(Task? pending = null)
        {
            if (mode == 0) return;
            await Wait(() => f.Button("Unlock") is not null || pending?.IsCompleted == true);
            Assert.True(f.Button("Unlock") is not null, "No password question: " + f.Vm.Notification + " UI: " + f.Text());
            f.Input("Existing archive password").Text = ArchiveTargetUpdateUiTests.Secret; f.Click("Unlock");
        }
    }
    // Plan §12.2: an archive that is missing, moved, unsupported or no longer writable keeps the working copy and the session
    // and offers Save copy and a fresh, manually chosen workflow; nothing is queued.
    [AvaloniaFact]
    public async Task A_moved_archive_keeps_the_edit_and_offers_save_copy_or_the_working_copy()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1")
            Assert.Skip("Run the explicit standard-user native edit UI lane.");
        string assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS") ?? throw new InvalidOperationException("Current native assets required.");
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets); string executable = Path.Combine(assets, "FileCat.exe");
        using var f = await Fixture.Open(customBackends: new([WindowsArchiveJobBackend.Create(ArchiveWriteFormat.Tar, executable)]), nativeWindowsOriginMarks: true);
        f.Services.Settings.Editor = new ToolDefinition { Executable = Path.Combine(f.Root, "owned-missing-editor.exe") };
        File.SetLastWriteTimeUtc(Path.Combine(f.Files, "a.txt"), DateTime.UnixEpoch.AddSeconds(1700000000));
        var create = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(ArchiveWriteFormat.Tar);
        await Wait(() => f.Button("Pack")?.IsEnabled == true); f.Click("Pack"); await f.Complete(create);
        var creation = Assert.Single(f.Services.Jobs.Jobs); await Finished(creation);
        string path = creation.Request.Archive!.ArchivePath;
        var tab = f.Vm.ActiveTab!; var archive = ArchiveProvider.ForFile(path); tab.Navigate(archive);
        await Wait(() => tab.Location == archive && !f.Vm.Dialogs.IsOpen && tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("a.txt"));
        var edit = f.Vm.ExecuteAsync(CommandIds.Edit); await f.Complete(edit);
        var session = Assert.Single(f.Services.EditSessions.LoadAll());
        File.WriteAllText(session.WorkingPath, "kept archive edit");
        tab.Navigate(Location.FileSystem(f.Root)); await Wait(() => tab.Listing.State == ListingState.Complete);
        File.Move(path, path + ".moved");
        var commit = (Task)typeof(MainViewModel).GetMethod("CommitSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(f.Vm, [session])!;
        await Wait(() => f.Button("Show working copy") is not null || commit.IsCompleted);
        Assert.True(f.Button("Show working copy") is not null, "No kept-edit choice: " + f.Vm.Notification + " UI: " + f.Text());
        Assert.NotNull(f.Button("Save my copy…")); Assert.NotNull(f.Button("Cancel"));
        Assert.Contains("Your working copy and edit session are kept.", f.Text());
        f.Click("Show working copy"); await f.Complete(commit);
        string folder = Path.GetDirectoryName(session.WorkingPath)!;
        await Wait(() => tab.Location == Location.FileSystem(folder) && tab.Listing.State == ListingState.Complete);
        Assert.True(tab.Listing.FocusName(Path.GetFileName(session.WorkingPath)));
        Assert.Single(f.Services.Jobs.Jobs); // Nothing was queued.
        var kept = Assert.Single(f.Services.EditSessions.LoadAll()); Assert.Equal(session.WorkingPath, kept.WorkingPath);
        Assert.Equal("kept archive edit", File.ReadAllText(kept.WorkingPath));
        Assert.False(File.Exists(path));
    }

    private static async Task Finished(Job job)
    {
        await Wait(() => job.State.IsFinished());
        Assert.True(job.State is JobState.Completed or JobState.CompletedWithIssues, job.State + ": " + job.Summary + " " + JobIssue.Summarize(job.Issues));
    }
    private static async Task Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            Dispatcher.UIThread.RunJobs();
            if (clock.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("Native edit UI did not settle.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
