using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using FileCat.Remote.Sftp;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.ViewModels;

/// <summary>
/// External edit sessions for archive members and files on servers (plan §14.2, NET-003): F4 edits a private copy in
/// the configured editor; nothing is written back until an explicit Commit, which is guarded by the version the edit
/// started from. Sessions survive navigation, tab closing, and restarts; conflicts keep the edit and offer Save copy or
/// an explicit overwrite.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly Dictionary<string, FileSystemWatcher> _sessionWatchers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _announcedEdits = new(StringComparer.Ordinal);
    private sealed class SessionCommit(EditSessionRecord session, EditCommitCopy copy)
    {
        public EditSessionRecord Session { get; } = session;
        public EditCommitCopy Copy { get; } = copy;
        public bool Finishing { get; set; }
    }
    private readonly Dictionary<Job, SessionCommit> _sessionCommits = new();
    private readonly HashSet<string> _sessionActions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preparingEdits = new(StringComparer.Ordinal);

    /// <summary>Starts watching the saved sessions; returns a startup message when some have uncommitted changes.</summary>
    public string? RestoreEditSessions()
    {
        var sessions = Services.EditSessions.LoadAll();
        foreach (var s in sessions) Watch(s);
        int modified = sessions.Count(s => Services.EditSessions.StateOf(s) == EditState.Modified);
        return modified == 0 ? null
            : $"{Formatters.Plural(modified, "edit has", "edits have")} changes that are not committed yet. Review them in File → Edit sessions.";
    }

    private async Task EditArchiveMemberAsync(ItemRef item)
    {
        var folder = item.Parent;
        string member = MemberPath(item);
        if (Services.Zip.WhyReadOnly(folder) is { } reason)
        {
            Notify(reason + " Use F3 to view the member, or F5 to extract a copy.", true);
            return;
        }
        string archive = ArchiveFile(folder);
        await PrepareEditSessionAsync(item, "zip:" + archive + "/" + member,
            () => Services.EditSessions.Find(archive, member),
            (provider, ct) => Services.EditSessions.Create(provider, item, ct),
            $"Extracting \"{item.Name}\" for editing…");
    }

    /// <summary>F4 on a file on a server: a private copy, read once, edited in the configured editor.</summary>
    private async Task EditRemoteFileAsync(ItemRef item)
    {
        if (item.Parent.Session is not { } profileId || Services.FindRemoteProfile(profileId) is not { } profile) return;
        string remotePath = Services.SftpProvider.PathOf(item);
        await PrepareEditSessionAsync(item, "sftp:" + profileId + ":" + remotePath,
            () => Services.EditSessions.FindRemote(profileId, remotePath),
            (provider, ct) =>
            {
                using var content = provider.OpenContent(item) ?? throw new IOException("This item has no content to edit.");
                var revision = content.GetRevision() ?? throw new IOException("The file's revision is unavailable; copy it with F5 instead.");
                return Services.EditSessions.CreateRemote(profileId, profile.Display, remotePath, content, revision, Services.SftpProvider.GetOriginMark(item.Parent), ct);
            }, $"Copying \"{item.Name}\" from {profile.Display} for editing…");
    }

    private async Task PrepareEditSessionAsync(ItemRef item, string key, Func<EditSessionRecord?> find,
        Func<ResourceProvider, CancellationToken, EditSessionRecord> create, string progress)
    {
        if (ActiveTab is not { } tab || Services.Io.IsStopped || !_preparingEdits.Add(key)) return;
        using var scope = new PreparationScope(tab, Services.Io);
        try
        {
            Notify(progress);
            var provider = Services.Providers.For(item.Parent);
            // Do not pass cancellation to the scheduler: an active synchronous call must keep this owner
            // until it returns. The scope is checked between reads and before publishing the record/editor.
            var prepared = await Services.Io.Run(provider.GetDeviceKey(item.Parent), IoPriority.Normal, _ =>
            {
                scope.Check(); var existing = find(); scope.Check();
                if (existing is not null)
                {
                    var state = Services.EditSessions.StateOf(existing); scope.Check();
                    return (Session: existing, State: state, Created: false);
                }
                var session = create(new EditPreparationProvider(provider, scope.Check), scope.Stop.Token);
                return (Session: session, State: EditState.Unchanged, Created: true);
            });
            if (!scope.Current) return;
            var session = prepared.Session;
            if (!prepared.Created && prepared.State == EditState.Modified) { await ShowSessionAsync(session); return; }
            if (prepared.State == EditState.Missing) { Notify("The working copy is gone; discard this session and start a new edit.", true); return; }
            Watch(session); OpenSessionEditor(session, checkedExists: true);
            if (prepared.Created) Notify($"Editing a copy of \"{item.Name}\" from {session.DisplayTarget}. Save in the editor, then commit with F4 on the file again (or File → Edit sessions). Nothing is written to the source until you commit.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            if (scope.Current) Notify($"Cannot edit \"{item.Name}\": {ex.Message}", true);
        }
        finally { _preparingEdits.Remove(key); }
    }

    private sealed class EditPreparationProvider(ResourceProvider inner, Action check) : ResourceProvider
    {
        public override string Scheme => inner.Scheme;
        public override string GetDisplayPath(Location l) => inner.GetDisplayPath(l);
        public override Location? GetParent(Location l) => inner.GetParent(l);
        public override Location? GetChildLocation(Location l, in EntryData e) => inner.GetChildLocation(l, e);
        public override LocationCapabilities GetCapabilities(Location l) => inner.GetCapabilities(l);
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(l, sink, ct);
        public override IContentSource? OpenContent(ItemRef item)
        {
            check(); var source = inner.OpenContent(item);
            if (source is null) { check(); return null; }
            try { check(); return new EditPreparationContent(FileCat.Core.Content.ProgressiveContent.Sequential(source)!, check); }
            catch { source.Dispose(); throw; }
        }
    }

    private sealed class EditPreparationContent(IContentSource inner, Action check) : IContentSource, IPartialContent
    {
        public string DisplayName => inner.DisplayName;
        public bool CanSeek => inner.CanSeek;
        public string? LocalPath => inner.LocalPath;
        public long Length { get { check(); long value = inner.Length; check(); return value; } }
        public ContentRevision? GetRevision() { check(); var value = inner.GetRevision(); check(); return value; }
        public IReadOnlyList<(long Offset, long Length)> MissingRanges => inner is IPartialContent p ? p.MissingRanges : [];
        public string? Caveat => inner is IPartialContent p ? p.Caveat : null;
        public int Read(long offset, Span<byte> buffer) { check(); int n = inner.Read(offset, buffer); check(); return n; }
        public void Dispose() => inner.Dispose();
    }

    private void OpenSessionEditor(EditSessionRecord session, bool checkedExists = false)
    {
        if (!checkedExists && !File.Exists(session.WorkingPath))
        {
            Notify("The working copy is gone; discard this session and start a new edit.", true);
            return;
        }
        LaunchEditor(session.WorkingPath);
    }

    /// <summary>
    /// Watches the working copy by path, not by handle: many editors save by writing a new file and renaming it. A change
    /// is announced once per edit, not on every save.
    /// </summary>
    private void Watch(EditSessionRecord session)
    {
        if (_sessionWatchers.ContainsKey(session.Id) || Path.GetDirectoryName(session.WorkingPath) is not { } dir || !Directory.Exists(dir)) return;
        var watcher = new FileSystemWatcher(dir, Path.GetFileName(session.WorkingPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        void Changed(object? sender, FileSystemEventArgs e) => Services.Ui.Post(async () =>
        {
            await Task.Delay(750); // let the editor finish writing
            var current = Services.EditSessions.LoadAll().FirstOrDefault(s => s.Id == session.Id);
            if (current is null || Services.EditSessions.StateOf(current) != EditState.Modified || !_announcedEdits.Add(current.Id)) return;
            Notify($"\"{current.DisplayName}\" changed in the editor. Commit it to {current.DisplayTarget} with F4 on the file, or File → Edit sessions.");
        });
        watcher.Changed += Changed;
        watcher.Created += Changed;
        watcher.Renamed += (s, e) => Changed(s, e);
        watcher.EnableRaisingEvents = true;
        _sessionWatchers[session.Id] = watcher;
    }

    private void Unwatch(string id)
    {
        if (_sessionWatchers.Remove(id, out var watcher)) watcher.Dispose();
        _announcedEdits.Remove(id);
    }

    /// <summary>File → Edit sessions: every open edit with its state.</summary>
    private async Task ShowEditSessionsAsync()
    {
        var sessions = Services.EditSessions.LoadAll();
        if (sessions.Count == 0)
        {
            Notify("No edits are open. F4 on a member of a ZIP archive or on a file on a server starts one.");
            return;
        }
        var items = sessions.Select(s => new ChoiceItem(s.DisplayName, $"{Describe(Services.EditSessions.StateOf(s))} · {s.DisplayContainer}")).ToList();
        var pick = await Dialogs.ChooseAsync(new ChoiceOptions("Edit sessions", items)
        {
            Hint = "Enter shows the actions: commit, reopen the editor, save a copy, or discard.",
        });
        if (pick.Index >= 0) await ShowSessionAsync(sessions[pick.Index]);
    }

    private static string Describe(EditState state) => state switch
    {
        EditState.Modified => "changed, not committed",
        EditState.Missing => "working copy missing",
        _ => "no changes",
    };

    private async Task ShowSessionAsync(EditSessionRecord session)
    {
        var state = Services.EditSessions.StateOf(session);
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Text = session.DisplayContainer });
        body.Children.Add(new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Classes = { "muted" },
            Text = $"{Describe(state)}. Working copy: {session.WorkingPath}" +
                   (session.LastCommitUtc is { } at ? $". Last committed {at.ToLocalTime():g}." : "."),
        });
        var buttons = new List<DialogButton> { new("Close", "close", IsCancel: true), new("Discard…", "discard", IsDanger: true), new("Save copy…", "copy") };
        if (state != EditState.Missing) buttons.Add(new DialogButton("Reopen editor", "reopen", IsDefault: state != EditState.Modified));
        if (state == EditState.Modified) buttons.Add(new DialogButton("Commit", "commit", IsDefault: true));
        var answer = await Dialogs.ShowCustomAsync(session.IsRemote ? "Server file edit" : "Archive edit", body, buttons);
        switch (answer as string)
        {
            case "commit": await CommitSessionAsync(session); break;
            case "reopen": OpenSessionEditor(session); break;
            case "copy": await SaveSessionCopyAsync(session); break;
            case "discard": await DiscardSessionAsync(session, state); break;
        }
    }

    private async Task CommitSessionAsync(EditSessionRecord session)
    {
        if (Services.Io.IsStopped || !_sessionActions.Add(session.Id)) return;
        var origin = ActiveTab;
        try
        {
            if (session.IsRemote) await CommitRemoteAsync(session, origin);
            else await CommitArchiveSessionAsync(session, origin);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            if (!Services.Io.IsStopped) Notify($"Cannot commit the edit: {ex.Message}. Your working copy is kept.", true);
        }
        finally
        {
            if (!_sessionCommits.Values.Any(c => c.Session.Id == session.Id)) _sessionActions.Remove(session.Id);
        }
    }

    private void CheckSessionAction() { if (Services.Io.IsStopped) throw new OperationCanceledException(); }

    private async Task<EditCommitCopy> PrepareEditCommitAsync(EditSessionRecord session)
    {
        CheckSessionAction(); var local = Location.FileSystem(session.WorkingPath);
        var copy = await Services.Io.Run(Services.Providers.For(local).GetDeviceKey(local), IoPriority.Normal, _ =>
        {
            CheckSessionAction(); return Services.EditSessions.PrepareCommit(session, Services.Paths.TempDirectory);
        }); // Own active synchronous work until it returns, even during shutdown.
        if (Services.Io.IsStopped) { copy.Dispose(); throw new OperationCanceledException(); }
        return copy;
    }

    private void SubmitEditCommit(EditSessionRecord session, EditCommitCopy copy, JobRequest request, TabViewModel? origin)
    {
        try
        {
            CheckSessionAction(); _ = Operations; // Register the finish observer before a fast job can return.
            var job = Services.Jobs.Submit(request);
            _sessionCommits[job] = new SessionCommit(session, copy);
            if (origin is not null) Track(job, origin);
        }
        catch { copy.Dispose(); throw; }
    }

    private async Task CommitArchiveSessionAsync(EditSessionRecord session, TabViewModel? origin)
    {
        var review = await MutationIoAsync(session.ArchivePath, () => Services.EditSessions.ReviewCommit(session), CheckSessionAction);
        switch (review.Check)
        {
            case CommitCheck.ArchiveMissing:
                if (await Dialogs.ConfirmAsync("Archive not found",
                        $"{session.ArchivePath} is gone, moved, or unreadable, so the edit cannot be committed. Save your working copy somewhere else?", "Save copy…"))
                    await SaveSessionCopyAsync(session);
                return;
            case CommitCheck.ArchiveChanged:
                if (!await Dialogs.ConfirmAsync("The archive changed",
                        $"{Path.GetFileName(session.ArchivePath)} changed after you started editing, but \"{session.MemberPath}\" itself did not. Commit your edit into the archive as it is now?",
                        "Commit into current archive"))
                    return;
                CheckSessionAction();
                break;
            case CommitCheck.MemberChanged:
                var answer = await Dialogs.ShowCustomAsync("Edit conflict",
                    new TextBlock
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
                        Text = $"\"{session.MemberPath}\" was changed or removed in {Path.GetFileName(session.ArchivePath)} after you started editing. " +
                               "Committing would overwrite that change. Your edit is kept either way.",
                    },
                    [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Overwrite their change", "overwrite", IsDanger: true),
                     new DialogButton("Save my copy…", "copy", IsDefault: true)]);
                if (answer as string == "copy") await SaveSessionCopyAsync(session);
                if (answer as string != "overwrite") return;
                CheckSessionAction();
                break;
        }
        CheckSessionAction();
        var copy = await PrepareEditCommitAsync(session);
        SubmitEditCommit(session, copy, new JobRequest
        {
            Kind = JobKind.ArchiveUpdate,
            Archive = Services.EditSessions.CommitPlan(session, review, copy),
            Description = $"Commit \"{Path.GetFileName(session.MemberPath)}\" into \"{Path.GetFileName(session.ArchivePath)}\"",
        }, origin);
    }

    /// <summary>
    /// Commits to the server: the file must still be the version the edit started from (checked now, and again by the
    /// job just before it replaces the file). A changed or missing file asks first and never overwrites silently.
    /// </summary>
    private async Task CommitRemoteAsync(EditSessionRecord session, TabViewModel? origin)
    {
        if (Services.FindRemoteProfile(session.ProfileId) is not { } profile)
        {
            if (await Dialogs.ConfirmAsync("Connection not found",
                    $"The connection to {session.ServerDisplay} was removed, so the edit cannot be committed. Save your working copy somewhere else?", "Save copy…"))
                await SaveSessionCopyAsync(session);
            return;
        }
        var folder = SftpProvider.At(profile, RemotePath.Parent(session.RemotePath) ?? "/");
        ContentRevision? now;
        try
        {
            CheckSessionAction();
            now = await Services.Io.Run(Services.Providers.For(folder).GetDeviceKey(folder), IoPriority.Normal, _ =>
            { CheckSessionAction(); return RemoteRevision(folder, session.RemotePath); });
            CheckSessionAction();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (!Services.Io.IsStopped) Notify($"Cannot reach {profile.Display}: {ex.Message} Your edit is kept.", true);
            return;
        }
        ContentRevision? expected = session.RemoteBaseline;
        switch (EditSessionStore.CheckRemote(session, now))
        {
            case CommitCheck.ArchiveMissing:
                var gone = await Dialogs.ShowCustomAsync("File not on the server",
                    new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Text = $"\"{session.RemotePath}\" is no longer on {profile.Display}. Your edit is kept." },
                    [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Save my copy…", "copy"), new DialogButton("Create it again", "create", IsDefault: true)]);
                if (gone as string == "copy") await SaveSessionCopyAsync(session);
                if (gone as string != "create") return;
                expected = null;
                break;
            case CommitCheck.MemberChanged:
                var answer = await Dialogs.ShowCustomAsync("Edit conflict",
                    new TextBlock
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
                        Text = $"\"{session.RemotePath}\" changed on {profile.Display} after you started editing. Committing would overwrite that change. Your edit is kept either way.",
                    },
                    [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Overwrite their change", "overwrite", IsDanger: true),
                     new DialogButton("Save my copy…", "copy", IsDefault: true)]);
                if (answer as string == "copy") await SaveSessionCopyAsync(session);
                if (answer as string != "overwrite") return;
                expected = now;
                break;
        }
        CheckSessionAction(); var copy = await PrepareEditCommitAsync(session);
        SubmitEditCommit(session, copy, new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(copy.Path, EntryKind.File)],
            Destination = folder,
            NewName = session.DisplayName,
            ExpectedTarget = expected,
            Options = new TransferOptions { Conflicts = ConflictPolicy.Skip },
            Description = $"Commit \"{session.DisplayName}\" to {profile.Display}",
        }, origin);
    }

    /// <summary>The server file's revision now (links followed), or null when it is gone.</summary>
    private ContentRevision? RemoteRevision(Location folder, string path)
    {
        using var lease = Services.SftpProvider.Lease(folder, CancellationToken.None);
        return lease.Channel.Stat(path) is { IsDirectory: false } st ? new ContentRevision(st.Size, st.ModifiedUtc.Ticks) : null;
    }

    private void OnEditCommitFinished(Job job)
    {
        if (!_sessionCommits.TryGetValue(job, out var commit) || commit.Finishing) return;
        commit.Finishing = true;
        _ = FinishEditCommitAsync(job, commit);
    }

    private async Task FinishEditCommitAsync(Job job, SessionCommit commit)
    {
        var session = commit.Session;
        try
        {
            CheckSessionAction();
            if (job.State != JobState.Completed)
            {
                Notify($"\"{session.DisplayName}\" was not committed; your edit is kept. Details are in the operations pane (Ctrl+J).", true);
                return;
            }
            if (session.IsRemote)
            {
                var folder = job.Request.Destination!;
                var revision = await Services.Io.Run(Services.Providers.For(folder).GetDeviceKey(folder), IoPriority.Normal, _ =>
                { CheckSessionAction(); return RemoteRevision(folder, session.RemotePath); });
                CheckSessionAction();
                if (revision is not { } r || r.Length != commit.Copy.Length) throw new IOException("The committed server file is missing or changed; the previous edit baseline is kept.");
                await MutationIoAsync(session.WorkingPath, () => Services.EditSessions.CommittedRemote(session, commit.Copy.Sha256, r), CheckSessionAction);
            }
            else
                await MutationIoAsync(session.ArchivePath, () => Services.EditSessions.Committed(session, commit.Copy), CheckSessionAction);
            CheckSessionAction();
            _announcedEdits.Remove(session.Id);
            Notify($"Committed \"{session.DisplayName}\" to {session.DisplayTarget}. The edit stays open for more changes; discard it when you are done.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            if (!Services.Io.IsStopped) Notify($"The commit finished, but the session could not be updated: {ex.Message}", true);
        }
        finally
        {
            // JobFinished is delivered only after the executor and journal have returned. Failed/canceled jobs
            // release their snapshots too. Shutdown can suppress acknowledgment, never snapshot ownership.
            try { await Task.Run(commit.Copy.Dispose); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (!Services.Io.IsStopped) Notify($"The temporary commit copy could not be removed: {ex.Message}", true); }
            _sessionCommits.Remove(job); _sessionActions.Remove(session.Id);
        }
    }

    private async Task SaveSessionCopyAsync(EditSessionRecord session)
    {
        if (View.TopLevel is not { } top || !File.Exists(session.WorkingPath)) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save a copy of the edited file",
            SuggestedFileName = Path.GetFileName(session.WorkingPath),
        });
        string? path = file?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            await Task.Run(() => File.Copy(session.WorkingPath, path, overwrite: true));
            Notify($"Saved a copy to {path}. The edit session stays open.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notify($"Cannot save the copy: {ex.Message}", true); }
    }

    private async Task DiscardSessionAsync(EditSessionRecord session, EditState state)
    {
        if (Services.Io.IsStopped || !_sessionActions.Add(session.Id)) return;
        try
        {
            if (!await Dialogs.ConfirmAsync("Discard edit",
                state == EditState.Modified
                    ? $"Discard your uncommitted changes to \"{session.DisplayName}\"? The working copy is deleted; {session.DisplayTarget} keeps its current content."
                    : $"Close the edit of \"{session.DisplayName}\" and delete its working copy?",
                "Discard", danger: state == EditState.Modified))
                return;
            CheckSessionAction();
            await MutationIoAsync(session.WorkingPath, () => { Services.EditSessions.Discard(session); return true; }, CheckSessionAction);
            Unwatch(session.Id);
            Notify("The edit was discarded.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!Services.Io.IsStopped) Notify($"The working copy could not be deleted (is it still open in the editor?): {ex.Message}", true);
        }
        finally { _sessionActions.Remove(session.Id); }
    }
}
