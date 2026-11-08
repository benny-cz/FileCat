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
        var tab = f.Vm.ActiveTab!; tab.Navigate(ArchiveProvider.ForFile(path));
        await Unlock(); await Wait(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("a.txt"));
        var edit = f.Vm.ExecuteAsync(CommandIds.Edit); await Unlock(); await f.Complete(edit);
        var session = Assert.Single(f.Services.EditSessions.LoadAll()); Assert.True(session.IsNativeArchive);
        Assert.Equal("alpha", File.ReadAllText(session.WorkingPath)); Assert.Equal(origin, File.ReadAllBytes(session.WorkingPath + ":Zone.Identifier"));
        Assert.Equal(creation.Request.Archive.Profile!.Format, session.ArchiveProfile!.Format);
        Assert.Equal((ArchiveEncryptionMode)mode, session.ArchiveProfile.Encryption.Mode);
        File.WriteAllText(session.WorkingPath, "authorized archive edit é");
        string authorized = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(session.WorkingPath)));
        var commit = (Task)typeof(MainViewModel).GetMethod("CommitSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(f.Vm, [session])!;
        await Unlock(); await f.Complete(commit);
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

        async Task Unlock()
        {
            if (mode == 0) return;
            await Wait(() => f.Button("Unlock") is not null);
            f.Input("Existing archive password").Text = ArchiveTargetUpdateUiTests.Secret; f.Click("Unlock");
        }
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
