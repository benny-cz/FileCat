using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

/// <summary>
/// FCUX-069/070: controlled clipboard publications and delayed reads exercise the production commands.
/// Jobs run against disposable real folders: assertions include operation kind, exact destination,
/// destination contents and source survival. These tests do not simulate native desktop interoperability.
/// </summary>
public sealed class ClipboardIntentTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_file_paste_preserves_original_destination_after_same_tab_navigation(bool cut)
    {
        using var f = await Fixture.Create();
        await f.Publish(cut, "a.txt");
        var tab = f.NavigateDestination(f.B);
        f.Clipboard.HoldRead();
        var paste = f.Paste();
        await f.Clipboard.ReadStarted.Task;
        tab.Navigate(Location.FileSystem(f.C));
        f.Clipboard.ReleaseRead();
        await paste;
        var job = await f.FinishedJob();
        Assert.Equal(cut ? JobKind.Move : JobKind.Copy, job.Kind);
        Assert.Equal(f.B, job.Request.Destination!.Path);
        Assert.Equal([Path.Combine(f.A, "a.txt")], job.Request.Sources.Select(x => x.FileSystemPath));
        Assert.Equal(new Dictionary<string, string> { ["a.txt"] = "source-a" }, Manifest(f.B));
        Assert.Empty(Manifest(f.C));
        Assert.Equal(!cut, File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.Equal(Location.FileSystem(f.C), tab.Location);
        Assert.Equal(f.Clipboard.ReadCount, f.Clipboard.DisposedReads);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_copy_of_same_path_or_subset_cannot_inherit_old_cut(bool subset)
    {
        using var f = await Fixture.Create();
        await f.Publish(true, subset ? ["a.txt", "b.txt"] : ["a.txt"]);
        f.Clipboard.ExternalFiles(Path.Combine(f.A, "a.txt"));
        f.NavigateDestination(f.B);
        await f.Paste();
        var job = await f.FinishedJob();
        Assert.Equal(JobKind.Copy, job.Kind);
        Assert.Equal(f.B, job.Request.Destination!.Path);
        Assert.Equal(new Dictionary<string, string> { ["a.txt"] = "source-a" }, Manifest(f.B));
        Assert.Equal(new Dictionary<string, string> { ["a.txt"] = "source-a", ["b.txt"] = "source-b" }, Manifest(f.A));
    }

    [AvaloniaFact]
    public async Task Incomplete_external_file_conversion_uses_only_a_complete_native_copy_manifest()
    {
        var clipboard = ControlledClipboard.Create();
        var path = Path.Combine(Path.GetTempPath(), "filecat-native-drop", "a.txt");
        var coordinator = new FileClipboard(count => count == 1 ? [path] : null,
            () => 17);
        clipboard.External(new IncompleteFileTransfer(1));

        var snapshot = await coordinator.ReadAsync(clipboard.Api);

        Assert.Null(snapshot.Publication);
        Assert.Equal(path, Assert.Single(snapshot.Files).FileSystemPath);
        Assert.Equal((uint)17, snapshot.WindowsSequence);
        Assert.Equal(clipboard.ReadCount, clipboard.DisposedReads);
    }

    [AvaloniaFact]
    public async Task Native_drop_cannot_explain_multiple_unreadable_file_items()
    {
        var clipboard = ControlledClipboard.Create();
        clipboard.External(new IncompleteFileTransfer(2));
        var path = Path.Combine(Path.GetTempPath(), "filecat-native-drop", "a.txt");
        var coordinator = new FileClipboard(_ => [path], () => 17);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ReadAsync(clipboard.Api));

        Assert.Contains("complete selection", error.Message);
        Assert.Equal(clipboard.ReadCount, clipboard.DisposedReads);
    }

    [AvaloniaFact]
    public async Task Extra_windows_shell_wrapper_does_not_hide_a_complete_native_file_list()
    {
        var clipboard = ControlledClipboard.Create();
        var path = Path.Combine(Path.GetTempPath(), "filecat-native-drop", "a.txt");
        clipboard.External(new IncompleteFileTransfer(2, StorageItem(path)));
        var coordinator = new FileClipboard(_ => [path], () => 17);

        var snapshot = await coordinator.ReadAsync(clipboard.Api);

        Assert.Null(snapshot.Publication);
        Assert.Equal(path, Assert.Single(snapshot.Files).FileSystemPath);
        Assert.Equal(clipboard.ReadCount, clipboard.DisposedReads);
    }

    [AvaloniaFact]
    public async Task Native_file_drop_must_agree_with_every_resolved_backend_path()
    {
        var clipboard = ControlledClipboard.Create();
        var resolved = Path.Combine(Path.GetTempPath(), "filecat-native-drop", "a.txt");
        var native = Path.Combine(Path.GetTempPath(), "filecat-native-drop", "b.txt");
        clipboard.External(new IncompleteFileTransfer(2, StorageItem(resolved)));
        var coordinator = new FileClipboard(_ => [native], () => 17);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ReadAsync(clipboard.Api));

        Assert.Contains("complete selection", error.Message);
        Assert.Equal(clipboard.ReadCount, clipboard.DisposedReads);
    }

    [AvaloniaFact]
    public async Task Native_file_drop_is_rejected_if_clipboard_changes_during_read()
    {
        var clipboard = ControlledClipboard.Create();
        clipboard.External(new IncompleteFileTransfer(1));
        var path = Path.Combine(Path.GetTempPath(), "filecat-native-drop", "a.txt");
        uint sequence = 17;
        var coordinator = new FileClipboard(_ =>
        {
            sequence = 18;
            return [path];
        }, () => sequence);

        await Assert.ThrowsAsync<FileClipboard.ChangedException>(
            () => coordinator.ReadAsync(clipboard.Api));
        Assert.Equal(clipboard.ReadCount, clipboard.DisposedReads);
    }

    [AvaloniaFact]
    public async Task Owned_cut_never_uses_native_file_drop_to_recover_an_incomplete_backend_read()
    {
        var clipboard = ControlledClipboard.Create();
        int nativeReads = 0;
        var path = Path.Combine(Path.GetTempPath(), "filecat-native-drop", "a.txt");
        var coordinator = new FileClipboard(_ =>
        {
            nativeReads++;
            return [path];
        }, () => 17);
        var revision = coordinator.BeginWrite();
        await coordinator.PublishAsync(clipboard.Api, revision, [StorageItem(path)], [path], true);
        coordinator.EndWrite(revision);
        clipboard.ReadOverride = new IncompleteFileTransfer(1);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ReadAsync(clipboard.Api));

        Assert.Contains("complete selection", error.Message);
        Assert.Equal(0, nativeReads);
        Assert.Equal(clipboard.ReadCount, clipboard.DisposedReads);
    }

    [AvaloniaTheory]
    [InlineData("text")]
    [InlineData("empty")]
    [InlineData("image")]
    [InlineData("files")]
    public async Task Intervening_external_publications_do_not_allow_same_path_cut_revival(string kind)
    {
        using var f = await Fixture.Create();
        await f.Publish(true, "a.txt");
        var replacement = kind == "text" ? Text("not a path\nnot navigation") : kind == "files" ? Files(Path.Combine(f.A, "b.txt")) : new DataTransfer();
        if (kind == "image")
        {
            var image = new DataTransferItem();
            image.Set(DataFormat.CreateBytesApplicationFormat("org.filecat.tests.image"), new byte[] { 137, 80, 78, 71 });
            replacement.Add(image);
        }
        f.Clipboard.External(replacement);
        f.Clipboard.ExternalFiles(Path.Combine(f.A, "a.txt"));
        f.NavigateDestination(f.B);
        await f.Paste();
        Assert.Equal(JobKind.Copy, (await f.FinishedJob()).Kind);
        Assert.True(File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.Equal("source-a", File.ReadAllText(Path.Combine(f.B, "a.txt")));
    }

    [AvaloniaFact]
    public async Task Distinct_case_resources_remain_distinct_in_a_cut_publication()
    {
        using var f = await Fixture.Create();
        if (File.Exists(Path.Combine(f.A, "A.txt"))) Assert.Skip("This fixture requires a case-sensitive file system.");
        File.WriteAllText(Path.Combine(f.A, "A.txt"), "upper-case");
        await f.Publish(true, "a.txt", "A.txt");
        f.NavigateDestination(f.B);
        await f.Paste();
        Assert.Equal(JobKind.Move, (await f.FinishedJob()).Kind);
        Assert.Equal(new Dictionary<string, string> { ["a.txt"] = "source-a", ["A.txt"] = "upper-case" }, Manifest(f.B));
        Assert.False(File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.False(File.Exists(Path.Combine(f.A, "A.txt")));
    }

    [AvaloniaTheory]
    [InlineData("subset")]
    [InlineData("different-case")]
    public async Task Matching_owner_still_requires_exact_resource_set(string change)
    {
        using var f = await Fixture.Create();
        await f.Publish(true, "a.txt", "b.txt");
        f.Clipboard.ReadOverride = change == "subset"
            ? Files(Path.Combine(f.A, "a.txt"))
            : Files(Path.Combine(f.A, "A.txt"), Path.Combine(f.A, "b.txt"));
        f.NavigateDestination(f.B);
        await f.Paste();
        Assert.Empty(f.Services.Jobs.Jobs);
        Assert.Contains("clipboard changed", f.Vm.Notification!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Manifest(f.B));
        Assert.Equal(2, Manifest(f.A).Count);
    }

    [AvaloniaFact]
    public async Task Replacing_clipboard_while_cut_payload_is_pending_cancels_then_current_copy_works()
    {
        using var f = await Fixture.Create();
        await f.Publish(true, "a.txt");
        f.NavigateDestination(f.B);
        f.Clipboard.HoldPayload(DataFormat.File);
        var paste = f.Paste();
        await f.Clipboard.PayloadStarted.Task;
        f.Clipboard.ExternalFiles(Path.Combine(f.A, "a.txt"));
        f.Clipboard.ReleasePayload();
        await paste;
        Assert.Empty(f.Services.Jobs.Jobs);
        Assert.Contains("clipboard changed", f.Vm.Notification!, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(f.A, "a.txt")));
        await f.Paste();
        Assert.Equal(JobKind.Copy, (await f.FinishedJob()).Kind);
        Assert.True(File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.Equal("source-a", File.ReadAllText(Path.Combine(f.B, "a.txt")));
    }

    [AvaloniaFact]
    public async Task Newer_filecat_cut_during_pending_read_cannot_be_consumed_by_old_paste()
    {
        using var f = await Fixture.Create();
        await f.Publish(true, "a.txt");
        f.NavigateDestination(f.B);
        f.Clipboard.HoldRead();
        var oldPaste = f.Paste();
        await f.Clipboard.ReadStarted.Task;
        await f.Publish(true, "b.txt");
        f.Clipboard.ReleaseRead();
        await oldPaste;
        Assert.Empty(f.Services.Jobs.Jobs);
        f.NavigateDestination(f.C);
        await f.Paste();
        Assert.Equal(JobKind.Move, (await f.FinishedJob()).Kind);
        Assert.Equal(new Dictionary<string, string> { ["b.txt"] = "source-b" }, Manifest(f.C));
        Assert.Equal(new Dictionary<string, string> { ["a.txt"] = "source-a" }, Manifest(f.A));
        Assert.Empty(Manifest(f.B));
    }

    [AvaloniaTheory]
    [InlineData("switch-panel")]
    [InlineData("move-tab")]
    [InlineData("close-tab")]
    public async Task Pending_file_paste_has_explicit_tab_lifetime_policy(string action)
    {
        using var f = await Fixture.Create();
        await f.Publish(true, "a.txt");
        var tab = f.NavigateDestination(f.B);
        var panel = tab.Panel;
        var other = f.Vm.Workspace.Panels.Single(p => p != panel);
        panel.OpenTab(Location.FileSystem(f.C), activate: false);
        f.Clipboard.HoldRead();
        var paste = f.Paste();
        await f.Clipboard.ReadStarted.Task;
        if (action == "switch-panel") f.Vm.Workspace.Activate(other);
        else if (action == "move-tab") other.AttachTab(panel.DetachTab(tab));
        else panel.CloseTab(tab);
        f.Clipboard.ReleaseRead();
        await paste;
        if (action == "close-tab")
        {
            Assert.Empty(f.Services.Jobs.Jobs);
            Assert.Contains("tab was closed", f.Vm.Notification!);
            Assert.True(File.Exists(Path.Combine(f.A, "a.txt")));
            Assert.Empty(Manifest(f.B));
        }
        else
        {
            var job = await f.FinishedJob();
            Assert.Equal(JobKind.Move, job.Kind);
            Assert.Equal(f.B, job.Request.Destination!.Path);
            Assert.False(File.Exists(Path.Combine(f.A, "a.txt")));
            Assert.Equal("source-a", File.ReadAllText(Path.Combine(f.B, "a.txt")));
        }
        Assert.Empty(Manifest(f.C));
    }

    [AvaloniaFact]
    public async Task Disappeared_destination_cancels_paste_without_recreating_or_redirecting_it()
    {
        using var f = await Fixture.Create();
        await f.Publish(true, "a.txt");
        var tab = f.NavigateDestination(f.B);
        f.Clipboard.HoldRead();
        var paste = f.Paste();
        await f.Clipboard.ReadStarted.Task;
        tab.Navigate(Location.FileSystem(f.C));
        Directory.Delete(f.B);
        f.Clipboard.ReleaseRead();
        await paste;
        Assert.Empty(f.Services.Jobs.Jobs);
        Assert.False(Directory.Exists(f.B));
        Assert.Empty(Manifest(f.C));
        Assert.True(File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.Contains("original destination", f.Vm.Notification!);
    }

    [AvaloniaTheory]
    [InlineData("navigate")]
    [InlineData("same-location")]
    [InlineData("pending-navigation")]
    [InlineData("close")]
    public async Task Delayed_text_paste_cannot_override_newer_navigation_or_closed_tab(string action)
    {
        using var f = await Fixture.Create();
        var tab = f.NavigateDestination(f.B);
        tab.Panel.OpenTab(Location.FileSystem(f.C), activate: false);
        Directory.CreateDirectory(Path.Combine(f.B, "child"));
        f.Clipboard.External(Text("child"));
        f.Clipboard.HoldPayload(DataFormat.Text);
        var paste = f.Paste();
        await f.Clipboard.PayloadStarted.Task;
        PreparingProvider? preparing = null;
        if (action == "navigate") tab.Navigate(Location.FileSystem(f.C));
        else if (action == "same-location") tab.Navigate(Location.FileSystem(f.B));
        else if (action == "close") tab.Panel.CloseTab(tab);
        else
        {
            preparing = new PreparingProvider();
            f.Services.Providers.Register(preparing);
            tab.Navigate(new Location(preparing.Scheme, "pending"));
        }
        f.Clipboard.ReleasePayload();
        await paste;
        Assert.Empty(f.Services.Jobs.Jobs);
        if (preparing is not null)
        {
            Assert.Equal(Location.FileSystem(f.B), tab.Location);
            preparing.Ready.SetResult(Location.FileSystem(f.C));
            await Until(() => tab.Location == Location.FileSystem(f.C));
        }
        else if (action != "close")
            Assert.Equal(Location.FileSystem(action == "same-location" ? f.B : f.C), tab.Location);
    }

    [AvaloniaFact]
    public async Task Relative_text_location_uses_captured_base_and_reads_one_snapshot()
    {
        using var f = await Fixture.Create();
        var tab = f.NavigateDestination(f.B);
        Directory.CreateDirectory(Path.Combine(f.B, "child"));
        f.Clipboard.External(Text("child"));
        f.Clipboard.HoldRead();
        var paste = f.Paste();
        await f.Clipboard.ReadStarted.Task;
        // Two external owners cannot be distinguished by Avalonia's in-process ownership API. The contract
        // is one external snapshot, so files from the replacement must not mix with the captured text.
        f.Clipboard.ExternalFiles(Path.Combine(f.A, "a.txt"));
        f.Clipboard.ReleaseRead();
        await paste;
        Assert.Equal(Location.FileSystem(Path.Combine(f.B, "child")), tab.Location);
        Assert.Empty(f.Services.Jobs.Jobs);
        Assert.Equal(1, f.Clipboard.ReadCount);
        Assert.Equal(1, f.Clipboard.DisposedReads);
    }

    [AvaloniaFact]
    public async Task Cut_in_flight_and_canceled_cut_require_deliberate_recut_before_another_move()
    {
        using var f = await Fixture.Create();
        File.WriteAllText(Path.Combine(f.B, "a.txt"), "original-target");
        await f.Publish(true, "a.txt");
        f.NavigateDestination(f.B);
        await f.Paste();
        var job = await f.AwaitingDecision();
        f.NavigateDestination(f.C);
        await f.Paste();
        Assert.Single(f.Services.Jobs.Jobs);
        Assert.Contains("already being moved", f.Vm.Notification!);
        job.Cancel();
        await f.FinishedJob();
        await f.Paste();
        Assert.Single(f.Services.Jobs.Jobs);
        Assert.Contains("review or retry", f.Vm.Notification!);
        Assert.True(File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.Equal("original-target", File.ReadAllText(Path.Combine(f.B, "a.txt")));
        Assert.Empty(Manifest(f.C));
        await f.Publish(true, "a.txt");
        f.NavigateDestination(f.C);
        await f.Paste();
        var next = await f.FinishedJob(2);
        Assert.Equal(JobKind.Move, next.Kind);
        Assert.False(File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.Equal("source-a", File.ReadAllText(Path.Combine(f.C, "a.txt")));
    }

    [AvaloniaFact]
    public async Task Partial_cut_move_explains_remaining_roots_and_does_not_replay_original_clipboard()
    {
        using var f = await Fixture.Create();
        File.WriteAllText(Path.Combine(f.B, "b.txt"), "keep-target-b");
        await f.Publish(true, "a.txt", "b.txt");
        f.NavigateDestination(f.B);
        await f.Paste();
        var job = await f.AwaitingDecision();
        Assert.EndsWith("b.txt", Assert.IsType<ConflictRequest>(job.Decision!.Request).SourcePath);
        job.Decision.Resolve(new Decision(DecisionAction.Skip));
        await f.FinishedJob();
        Assert.Equal(1, job.CompletedRootCount);
        Assert.Equal(new Dictionary<string, string> { ["a.txt"] = "source-a", ["b.txt"] = "keep-target-b" }, Manifest(f.B));
        Assert.Equal(new Dictionary<string, string> { ["b.txt"] = "source-b" }, Manifest(f.A));
        f.NavigateDestination(f.C);
        await f.Paste();
        Assert.Single(f.Services.Jobs.Jobs);
        Assert.Contains("1 of 2", f.Vm.Notification!);
        Assert.Empty(Manifest(f.C));
        await f.Publish(true, "b.txt");
        f.NavigateDestination(f.C);
        await f.Paste();
        Assert.Equal(JobKind.Move, (await f.FinishedJob(2)).Kind);
        Assert.Empty(Manifest(f.A));
        Assert.Equal("source-b", File.ReadAllText(Path.Combine(f.C, "b.txt")));
    }

    [AvaloniaFact]
    public async Task Older_move_completion_cannot_consume_a_newer_cut_publication()
    {
        using var f = await Fixture.Create();
        File.WriteAllText(Path.Combine(f.B, "a.txt"), "old-target");
        await f.Publish(true, "a.txt");
        f.NavigateDestination(f.B);
        await f.Paste();
        var first = await f.AwaitingDecision();
        await f.Publish(true, "b.txt");
        first.Cancel();
        await f.FinishedJob();
        f.NavigateDestination(f.C);
        await f.Paste();
        Assert.Equal(JobKind.Move, (await f.FinishedJob(2)).Kind);
        Assert.True(File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.False(File.Exists(Path.Combine(f.A, "b.txt")));
        Assert.Equal("source-b", File.ReadAllText(Path.Combine(f.C, "b.txt")));
    }

    [AvaloniaFact]
    public async Task Fully_completed_cut_is_not_reused_as_a_second_transfer()
    {
        using var f = await Fixture.Create();
        await f.Publish(true, "a.txt");
        f.NavigateDestination(f.B);
        await f.Paste();
        await f.FinishedJob();
        f.NavigateDestination(f.C);
        await f.Paste();
        Assert.Single(f.Services.Jobs.Jobs);
        Assert.Contains("already been moved", f.Vm.Notification!);
        Assert.False(File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.Equal("source-a", File.ReadAllText(Path.Combine(f.B, "a.txt")));
        Assert.Empty(Manifest(f.C));
    }

    [AvaloniaFact]
    public async Task Clipboard_without_ownership_support_explains_copy_fallback_and_preserves_source()
    {
        using var f = await Fixture.Create();
        f.Clipboard.SupportsOwnership = false;
        await f.Publish(true, "a.txt");
        Assert.Contains("cannot preserve Cut safely", f.Vm.Notification!);
        // A later successful ownership query must not contradict the action already reported to the user.
        f.Clipboard.SupportsOwnership = true;
        f.NavigateDestination(f.B);
        await f.Paste();
        Assert.Equal(JobKind.Copy, (await f.FinishedJob()).Kind);
        Assert.True(File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.Equal("source-a", File.ReadAllText(Path.Combine(f.B, "a.txt")));
    }

    [AvaloniaFact]
    public async Task Failed_cut_admission_retains_originals_and_requires_review_before_retry()
    {
        using var f = await Fixture.Create();
        string folder = Directory.CreateDirectory(Path.Combine(f.A, "folder")).FullName;
        string descendant = Directory.CreateDirectory(Path.Combine(folder, "descendant")).FullName;
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "keep-source");
        await f.Publish(true, "folder");
        f.NavigateDestination(descendant);
        await f.Paste();
        var failed = await f.FinishedJob();
        Assert.Equal(JobKind.Move, failed.Kind);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal("keep-source", File.ReadAllText(Path.Combine(folder, "keep.txt")));
        Assert.Empty(Manifest(descendant));
        f.NavigateDestination(f.B);
        await f.Paste();
        Assert.Single(f.Services.Jobs.Jobs);
        Assert.Contains("review or retry", f.Vm.Notification!);
        Assert.Empty(Manifest(f.B));
    }

    [AvaloniaFact]
    public async Task Unconvertible_selection_keeps_previous_clipboard_and_reports_failure()
    {
        using var f = await Fixture.Create();
        f.Clipboard.External(Text("keep-this"));
        var original = f.Clipboard.Current;
        f.Storage.RefusedPath = Path.Combine(f.A, "b.txt");
        await f.Publish(true, "a.txt", "b.txt");
        Assert.Same(original, f.Clipboard.Current);
        Assert.Contains("previous clipboard was kept", f.Vm.Notification!);
        Assert.Empty(f.Services.Jobs.Jobs);
        Assert.Equal(2, Manifest(f.A).Count);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mixed_resource_selection_is_rejected_as_a_whole_and_preserves_the_clipboard(bool cut)
    {
        using var f = await Fixture.Create();
        var provider = new PreparingProvider();
        f.Services.Providers.Register(provider);
        var set = f.Services.ResultSets.Create("Mixed selection", "Local and provider references");
        set.Add(ItemRef.ForFileSystemPath(Path.Combine(f.A, "a.txt"), EntryKind.File), "local");
        set.Add(new ItemRef(new Location(provider.Scheme, "/other"), "other.txt", EntryKind.File), "provider");
        set.IsComplete = true;
        var tab = f.Vm.ActiveTab!;
        tab.Navigate(ResultSetProvider.LocationOf(set));
        await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount == 2);
        tab.Listing.MarkAll(true);
        f.Clipboard.External(Text("keep original clipboard"));
        var original = f.Clipboard.Current;
        await f.Vm.ExecuteAsync(cut ? CommandIds.CutToClipboard : CommandIds.CopyToClipboard);
        Assert.Same(original, f.Clipboard.Current);
        Assert.Equal(0, f.Clipboard.WriteCount);
        Assert.Contains("selection includes items outside the file system", f.Vm.Notification!);
        Assert.Empty(f.Services.Jobs.Jobs);
        Assert.Equal("source-a", File.ReadAllText(Path.Combine(f.A, "a.txt")));
    }

    [AvaloniaFact]
    public async Task Failed_clipboard_write_does_not_consume_the_previous_owned_cut_or_leave_paste_pending()
    {
        using var f = await Fixture.Create();
        await f.Publish(true, "a.txt");
        var original = f.Clipboard.Current;
        f.Clipboard.WriteFailure = new IOException("deterministic clipboard failure");
        await Assert.ThrowsAsync<IOException>(() => f.Publish(false, "b.txt"));
        Assert.Same(original, f.Clipboard.Current);
        f.Clipboard.WriteFailure = null;
        f.NavigateDestination(f.B);
        await f.Paste();
        Assert.Equal(JobKind.Move, (await f.FinishedJob()).Kind);
        Assert.False(File.Exists(Path.Combine(f.A, "a.txt")));
        Assert.True(File.Exists(Path.Combine(f.A, "b.txt")));
        Assert.Equal("source-a", File.ReadAllText(Path.Combine(f.B, "a.txt")));
    }

    [Fact]
    public async Task New_process_has_no_cut_authority_even_for_an_identical_retained_file_payload()
    {
        var clipboard = ControlledClipboard.Create();
        var firstProcess = new FileClipboard();
        string path = Path.Combine(Path.GetTempPath(), "cut-before-restart.txt");
        var revision = firstProcess.BeginWrite();
        await firstProcess.PublishAsync(clipboard.Api, revision, [StorageItem(path)], [path], true);
        firstProcess.EndWrite(revision);
        // Even if a test backend incorrectly retains the old in-process reference, a fresh coordinator
        // does not accept another process's historical Cut state as authorization.
        var restarted = new FileClipboard();
        var snapshot = await restarted.ReadAsync(clipboard.Api);
        Assert.Null(snapshot.Publication);
        Assert.Equal(path, Assert.Single(snapshot.Files).FileSystemPath);
        Assert.Equal(clipboard.ReadCount, clipboard.DisposedReads);
    }

    [Fact]
    public async Task Concurrent_clipboard_writes_finish_in_invocation_order_and_only_latest_owns_cut()
    {
        var clipboard = ControlledClipboard.Create();
        var coordinator = new FileClipboard();
        string a = Path.Combine(Path.GetTempPath(), "clipboard-old.txt"), b = Path.Combine(Path.GetTempPath(), "clipboard-new.txt");
        clipboard.HoldWrite();
        long oldRevision = coordinator.BeginWrite();
        var oldWrite = coordinator.PublishAsync(clipboard.Api, oldRevision, [StorageItem(a)], [a], true);
        await clipboard.WriteStarted.Task;
        long newRevision = coordinator.BeginWrite();
        var newWrite = coordinator.PublishAsync(clipboard.Api, newRevision, [StorageItem(b)], [b], false);
        clipboard.ReleaseWrite();
        Assert.Null(await oldWrite);
        Assert.True(await newWrite);
        coordinator.EndWrite(newRevision);
        var snapshot = await coordinator.ReadAsync(clipboard.Api);
        Assert.Equal(b, Assert.Single(snapshot.Files).FileSystemPath);
        Assert.False(snapshot.Publication!.Cut);
        Assert.Equal(2, clipboard.WriteCount);
    }

    private static Dictionary<string, string> Manifest(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(folder, p), File.ReadAllText, StringComparer.Ordinal);

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 700 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), "Expected clipboard/job/listing state was not reached.");
    }

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static DataTransfer Text(string value) { var data = new DataTransfer(); data.Add(DataTransferItem.CreateText(value)); return data; }
    private static DataTransfer Files(params string[] paths)
    {
        var data = new DataTransfer();
        foreach (var path in paths) data.Add(DataTransferItem.CreateFile(StorageItem(path)));
        return data;
    }

    private static IStorageItem StorageItem(string path)
    {
        IStorageItem item = Directory.Exists(path) ? DispatchProxy.Create<IStorageFolder, StorageProxy>() : DispatchProxy.Create<IStorageFile, StorageProxy>();
        ((StorageProxy)(object)item).Path = new Uri(path);
        return item;
    }

    public class StorageProxy : DispatchProxy
    {
        public Uri? Path;
        public string? RefusedPath;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Path" => Path,
            "get_Name" => System.IO.Path.GetFileName(Path!.LocalPath),
            "TryGetFileFromPathAsync" => Task.FromResult(((Uri)args![0]!).LocalPath == RefusedPath ? null : (IStorageFile)StorageItem(((Uri)args[0]!).LocalPath)),
            "TryGetFolderFromPathAsync" => Task.FromResult(((Uri)args![0]!).LocalPath == RefusedPath ? null : (IStorageFolder)StorageItem(((Uri)args[0]!).LocalPath)),
            "Dispose" => null,
            _ => throw new NotSupportedException("Unexpected storage API: " + method?.Name),
        };
    }

    public class ControlledClipboard : DispatchProxy
    {
        public IClipboard Api => (IClipboard)(object)this;
        public static ControlledClipboard Create() => (ControlledClipboard)(object)DispatchProxy.Create<IClipboard, ControlledClipboard>();
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IClipboard.ClearAsync) => ClearAsync(),
            nameof(IClipboard.FlushAsync) => FlushAsync(),
            nameof(IClipboard.TryGetDataAsync) => TryGetDataAsync(),
            nameof(IClipboard.TryGetInProcessDataAsync) => TryGetInProcessDataAsync(),
            nameof(IClipboard.SetDataAsync) => SetDataAsync((IAsyncDataTransfer?)args![0]),
            _ => throw new NotSupportedException("Unexpected clipboard API: " + method?.Name),
        };
        public IAsyncDataTransfer? Current;
        public IAsyncDataTransfer? ReadOverride;
        private IAsyncDataTransfer? _owner;
        public bool SupportsOwnership = true;
        public Exception? WriteFailure;
        private TaskCompletionSource? _readGate, _payloadGate, _writeGate;
        private DataFormat? _heldFormat;
        public TaskCompletionSource ReadStarted = NewGate(), PayloadStarted = NewGate(), WriteStarted = NewGate();
        public int ReadCount, DisposedReads, WriteCount;

        public void External(IAsyncDataTransfer data) { Current = data; _owner = null; }
        public void ExternalFiles(params string[] paths) => External(Files(paths));
        public void HoldRead() { ReadStarted = NewGate(); _readGate = NewGate(); }
        public void ReleaseRead() { _readGate?.TrySetResult(); _readGate = null; }
        public void HoldPayload(DataFormat format) { _heldFormat = format; PayloadStarted = NewGate(); _payloadGate = NewGate(); }
        public void ReleasePayload() { _payloadGate?.TrySetResult(); _payloadGate = null; }
        public void HoldWrite() { WriteStarted = NewGate(); _writeGate = NewGate(); }
        public void ReleaseWrite() { _writeGate?.TrySetResult(); _writeGate = null; }
        public Task ClearAsync() { Current = null; _owner = null; return Task.CompletedTask; }
        public Task FlushAsync() => Task.CompletedTask;
        public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() => Task.FromResult(SupportsOwnership ? _owner : null);
        public async Task SetDataAsync(IAsyncDataTransfer? dataTransfer)
        {
            WriteCount++;
            WriteStarted.TrySetResult();
            if (_writeGate is { } gate) await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (WriteFailure is { } failure) throw failure;
            Current = _owner = dataTransfer;
        }
        public async Task<IAsyncDataTransfer?> TryGetDataAsync()
        {
            ReadCount++;
            // A caller-owned wrapper captures one publication and never disposes the borrowed owner.
            var read = ReadOverride ?? Current;
            var snapshot = read is null ? null : new ReadSnapshot(this, read);
            ReadStarted.TrySetResult();
            if (_readGate is { } gate) await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return snapshot;
        }
        private sealed class ReadSnapshot : IAsyncDataTransfer
        {
            private readonly ControlledClipboard _clipboard;
            public ReadSnapshot(ControlledClipboard clipboard, IAsyncDataTransfer source)
            {
                _clipboard = clipboard;
                Formats = source.Formats.ToArray();
                Items = source.Items.Select(i => (IAsyncDataTransferItem)new ReadItem(clipboard, i)).ToArray();
            }
            public IReadOnlyList<DataFormat> Formats { get; }
            public IReadOnlyList<IAsyncDataTransferItem> Items { get; }
            public void Dispose() => _clipboard.DisposedReads++;
        }
        private sealed class ReadItem(ControlledClipboard clipboard, IAsyncDataTransferItem source) : IAsyncDataTransferItem
        {
            public IReadOnlyList<DataFormat> Formats => source.Formats;
            public async Task<object?> TryGetRawAsync(DataFormat format)
            {
                if (format.Equals(clipboard._heldFormat) && clipboard._payloadGate is { } gate)
                {
                    clipboard.PayloadStarted.TrySetResult();
                    await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                return await source.TryGetRawAsync(format);
            }
        }
    }

    private sealed class IncompleteFileTransfer : IAsyncDataTransfer
    {
        public IncompleteFileTransfer(int count, IStorageItem? firstReadable = null)
        {
            Items = Enumerable.Range(0, count).Select(index =>
                (IAsyncDataTransferItem)new TestFileItem(index == 0 ? firstReadable : null)).ToArray();
        }

        public IReadOnlyList<DataFormat> Formats { get; } = [DataFormat.File];
        public IReadOnlyList<IAsyncDataTransferItem> Items { get; }
        public void Dispose() { }

        private sealed class TestFileItem(IStorageItem? value) : IAsyncDataTransferItem
        {
            public IReadOnlyList<DataFormat> Formats { get; } = [DataFormat.File];
            public Task<object?> TryGetRawAsync(DataFormat format) => Task.FromResult<object?>(value);
        }
    }

    private sealed class PreparingProvider : ResourceProvider
    {
        public readonly TaskCompletionSource<Location> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string Scheme => "clipboard-delayed-navigation";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task<Location>? PrepareAsync(Location location, CancellationToken ct) => Ready.Task;
    }

    private sealed class ClipboardView(IViewActions inner, IClipboard clipboard) : IViewActions
    {
        public IClipboard Clipboard => clipboard;
        public TopLevel? TopLevel => inner.TopLevel;
        public void FocusActivePanel() => inner.FocusActivePanel();
        public Task<string?> RenameInlineAsync(PromptOptions options) => inner.RenameInlineAsync(options);
        public void FocusPathBox() => inner.FocusPathBox();
        public void FocusCommandLine() => inner.FocusCommandLine();
        public bool FocusCommandSearch() => inner.FocusCommandSearch();
        public void CloseWhenIdle() => inner.CloseWhenIdle();
        public void OpenMenuBar() => inner.OpenMenuBar();
        public void ShowContextMenu() => inner.ShowContextMenu();
        public void ShowNotification(string message, bool isError = false) => inner.ShowNotification(message, isError);
        public void ReloadChrome() => inner.ReloadChrome();
    }

    private sealed class Fixture : IDisposable
    {
        public required AppServices Services;
        public required MainViewModel Vm;
        public required MainWindow Window;
        public required string Root, A, B, C;
        public required StorageProxy Storage;
        public ControlledClipboard Clipboard { get; } = ControlledClipboard.Create();
        public static async Task<Fixture> Create()
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            var storage = DispatchProxy.Create<IStorageProvider, StorageProxy>();
            typeof(TopLevel).GetField("_storageProvider", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, storage);
            var f = new Fixture
            {
                Services = services, Vm = vm, Window = window, Root = root,
                A = Directory.CreateDirectory(Path.Combine(root, "A")).FullName,
                B = Directory.CreateDirectory(Path.Combine(root, "B")).FullName,
                C = Directory.CreateDirectory(Path.Combine(root, "C")).FullName,
                Storage = (StorageProxy)(object)storage,
            };
            File.WriteAllText(Path.Combine(f.A, "a.txt"), "source-a");
            File.WriteAllText(Path.Combine(f.A, "b.txt"), "source-b");
            vm.View = new ClipboardView(vm.View, f.Clipboard.Api);
            f.NavigateDestination(f.A);
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            return f;
        }
        public TabViewModel NavigateDestination(string path)
        {
            var tab = Vm.Workspace.ActiveTab!;
            tab.Navigate(Location.FileSystem(path));
            return tab;
        }
        public async Task Publish(bool cut, params string[] names)
        {
            var tab = NavigateDestination(A);
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing &&
                names.All(name => Enumerable.Range(0, tab.Listing.VisibleCount).Any(i => tab.Listing.GetVisible(i).Name.ToString() == name)));
            tab.Listing.MarkAll(false);
            tab.Listing.MarkNames(names, true);
            await Vm.ExecuteAsync(cut ? CommandIds.CutToClipboard : CommandIds.CopyToClipboard);
        }
        public Task Paste() => Vm.ExecuteAsync(CommandIds.PasteFromClipboard);
        public async Task<Job> FinishedJob(int count = 1)
        {
            await Until(() => Services.Jobs.Jobs.Count == count && Services.Jobs.Jobs.All(j => j.State.IsFinished()));
            Dispatcher.UIThread.RunJobs();
            return Services.Jobs.Jobs.OrderBy(j => j.CreatedUtc).Last();
        }
        public async Task<Job> AwaitingDecision()
        {
            await Until(() => Services.Jobs.Jobs.Any(j => j.State == JobState.AwaitingDecision));
            return Services.Jobs.Jobs.Single(j => j.State == JobState.AwaitingDecision);
        }
        public void Dispose()
        {
            Clipboard.ReleaseRead(); Clipboard.ReleasePayload(); Clipboard.ReleaseWrite();
            AccessibilityTests.Close(Services, Window, Root);
        }
    }
}
