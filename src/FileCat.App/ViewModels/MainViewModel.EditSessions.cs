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
    private sealed class SessionWatch
    {
        public SessionWatch(EditSessionRecord session) { Session = session; Token = Stop.Token; }
        public EditSessionRecord Session { get; }
        public FileSystemWatcher? Watcher;
        public readonly CancellationTokenSource Stop = new();
        public readonly CancellationToken Token;
        public bool Running, Pending;
    }
    private readonly Dictionary<string, SessionWatch> _sessionWatchers = new(StringComparer.Ordinal);
    private volatile bool _editSessionsStopped;
    internal bool EditSessionsActive => !_editSessionsStopped && !Services.Io.IsStopped;

    internal void StopEditSessions()
    {
        _editSessionsStopped = true;
        foreach (string id in _sessionWatchers.Keys.ToArray()) Unwatch(id);
    }
    private readonly HashSet<string> _announcedEdits = new(StringComparer.Ordinal);
    private sealed class SessionCommit(EditSessionRecord session, EditCommitCopy copy)
    {
        public EditSessionRecord Session { get; } = session;
        public EditCommitCopy Copy { get; } = copy;
        public bool Finishing { get; set; }
    }
    private readonly Dictionary<Job, SessionCommit> _sessionCommits = new();
    private readonly HashSet<string> _sessionActions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sessionCopyActions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preparingEdits = new(StringComparer.Ordinal);

    /// <summary>Starts watching the saved sessions; returns a startup message when some have uncommitted changes.</summary>
    public async Task<string?> RestoreEditSessionsAsync()
    {
        try
        {
            var sessions = await LoadEditSessionsAsync(); int modified = 0, unavailable = 0;
            foreach (var session in sessions)
            {
                var state = await ReadEditStateAsync(session); CheckSessionAction();
                if (state == EditState.Modified) modified++;
                if (state == EditState.Unavailable) unavailable++;
                Watch(session);
            }
            if (modified == 0 && unavailable == 0) return null;
            return (modified == 0 ? "" : $"{Formatters.Plural(modified, "edit has", "edits have")} changes that are not committed yet.") +
                (unavailable == 0 ? "" : $" {(Formatters.Plural(unavailable, "working copy cannot", "working copies cannot"))} be read.") +
                " Review them in File → Edit sessions.";
        }
        catch (OperationCanceledException) { return null; }
    }

    private Task<IReadOnlyList<EditSessionRecord>> LoadEditSessionsAsync(Action? check = null) =>
        MutationIoAsync(Services.EditSessions.Root, Services.EditSessions.LoadAll, check ?? CheckSessionAction);

    private async Task<EditState> ReadEditStateAsync(EditSessionRecord session, Action? check = null) =>
        (await ReadEditReviewAsync(session, check)).State;

    private async Task<EditWorkingReview> ReadEditReviewAsync(EditSessionRecord session, Action? check = null)
    {
        check ??= CheckSessionAction; check();
        var location = Location.FileSystem(session.WorkingPath); var provider = Services.Providers.For(location);
        var state = await Services.Io.Run(provider.GetDeviceKey(location), IoPriority.Normal,
            _ => Services.EditSessions.ReviewWorking(session, provider, check));
        check(); return state;
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
        if (ActiveTab is not { } tab || !EditSessionsActive || !_preparingEdits.Add(key)) return;
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
                    return (Session: existing, State: EditState.Unchanged, Created: false);
                }
                var session = create(new EditPreparationProvider(provider, scope.Check), scope.Stop.Token);
                return (Session: session, State: EditState.Unchanged, Created: true);
            });
            if (!scope.Current) return;
            var session = prepared.Session;
            void Check() { scope.Check(); CheckSessionAction(); }
            var state = prepared.Created ? prepared.State : await ReadEditStateAsync(session, Check);
            Check();
            if (!prepared.Created && state == EditState.Modified) { await ShowSessionAsync(session); return; }
            if (state == EditState.Missing) { Notify("The working copy is gone; discard this session and start a new edit.", true); return; }
            if (state == EditState.Unavailable) { Notify("The working copy cannot be read; it is kept. Try again when the editor has finished saving.", true); return; }
            Watch(session); OpenSessionEditor(session);
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

    // The caller has just completed a worker-owned state read; do not probe the path again on the UI.
    private void OpenSessionEditor(EditSessionRecord session)
    {
        if (EditSessionsActive) LaunchEditor(session.WorkingPath);
    }

    /// <summary>
    /// Watches the working copy by path, not by handle: many editors save by writing a new file and renaming it. A change
    /// is announced once per edit, not on every save.
    /// </summary>
    private void Watch(EditSessionRecord session)
    {
        if (!EditSessionsActive || _sessionWatchers.ContainsKey(session.Id)) return;
        var owner = new SessionWatch(session); _sessionWatchers[session.Id] = owner;
        _ = StartSessionWatchAsync(owner);
    }

    private bool Current(SessionWatch owner) => EditSessionsActive && !owner.Token.IsCancellationRequested &&
        _sessionWatchers.TryGetValue(owner.Session.Id, out var current) && ReferenceEquals(current, owner);

    private void CheckWatch(SessionWatch owner)
    { CheckSessionAction(); owner.Token.ThrowIfCancellationRequested(); }

    private async Task StartSessionWatchAsync(SessionWatch owner)
    {
        FileSystemWatcher? created = null;
        try
        {
            var location = Location.FileSystem(owner.Session.WorkingPath);
            created = await Services.Io.Run(Services.Providers.For(location).GetDeviceKey(location), IoPriority.Normal, _ =>
            {
                CheckWatch(owner);
                var watcher = new FileSystemWatcher(Path.GetDirectoryName(owner.Session.WorkingPath)!, Path.GetFileName(owner.Session.WorkingPath))
                { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                try
                {
                    void Changed(object? sender, FileSystemEventArgs e) => Services.Ui.Post(() => QueueSessionProbe(owner));
                    watcher.Changed += Changed; watcher.Created += Changed; watcher.Renamed += (s, e) => Changed(s, e);
                    CheckWatch(owner); watcher.EnableRaisingEvents = true; return watcher;
                }
                catch { watcher.Dispose(); throw; }
            }); // No scheduler cancellation: retain ownership until native creation returns.
            if (Current(owner)) { owner.Watcher = created; created = null; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { if (Current(owner)) Unwatch(owner.Session.Id); }
        finally { created?.Dispose(); }
    }

    private void QueueSessionProbe(SessionWatch owner)
    {
        if (!Current(owner) || _announcedEdits.Contains(owner.Session.Id)) return;
        owner.Pending = true;
        if (!owner.Running) { owner.Running = true; _ = ProbeEditSessionAsync(owner); }
    }

    private async Task ProbeEditSessionAsync(SessionWatch owner)
    {
        try
        {
            while (Current(owner) && owner.Pending && !_announcedEdits.Contains(owner.Session.Id))
            {
                owner.Pending = false;
                await Task.Delay(750, owner.Token); // One delayed owner, even during a burst of editor saves.
                CheckWatch(owner);
                var current = (await LoadEditSessionsAsync(() => CheckWatch(owner))).FirstOrDefault(s => s.Id == owner.Session.Id);
                if (current is null) return;
                var state = await ReadEditStateAsync(current, () => CheckWatch(owner));
                if (!Current(owner)) return;
                if (state == EditState.Modified && _announcedEdits.Add(current.Id))
                    Notify($"\"{current.DisplayName}\" changed in the editor. Commit it to {current.DisplayTarget} with F4 on the file, or File → Edit sessions.");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException) { }
        finally { owner.Running = false; }
    }

    private void Unwatch(string id)
    {
        if (_sessionWatchers.Remove(id, out var owner)) { owner.Stop.Cancel(); owner.Stop.Dispose(); owner.Watcher?.Dispose(); }
        _announcedEdits.Remove(id);
    }

    /// <summary>File → Edit sessions: every open edit with its state.</summary>
    private async Task ShowEditSessionsAsync()
    {
        try
        {
            var sessions = await LoadEditSessionsAsync();
            if (sessions.Count == 0)
            {
                Notify("No edits are open. F4 on a member of a ZIP archive or on a file on a server starts one.");
                return;
            }
            var items = new List<ChoiceItem>();
            foreach (var session in sessions) items.Add(new ChoiceItem(session.DisplayName, $"{Describe(await ReadEditStateAsync(session))} · {session.DisplayContainer}"));
            CheckSessionAction();
            var pick = await Dialogs.ChooseAsync(new ChoiceOptions("Edit sessions", items)
            {
                Hint = "Enter shows the actions: commit, reopen the editor, save a copy, or discard.",
            });
            CheckSessionAction();
            if (pick.Index >= 0) await ShowSessionAsync(sessions[pick.Index]);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (EditSessionsActive) Notify($"Cannot review the edit sessions: {ex.Message}", true); }
    }

    private static string Describe(EditState state) => state switch
    {
        EditState.Modified => "changed, not committed",
        EditState.Missing => "working copy missing",
        EditState.Unavailable => "working copy cannot be read completely",
        _ => "no changes",
    };

    private async Task ShowSessionAsync(EditSessionRecord session)
    {
        try
        {
            var state = await ReadEditStateAsync(session);
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
            if (state is EditState.Unchanged or EditState.Modified) buttons.Add(new DialogButton("Reopen editor", "reopen", IsDefault: state != EditState.Modified));
            if (state == EditState.Modified) buttons.Add(new DialogButton("Commit", "commit", IsDefault: true));
            var answer = await Dialogs.ShowCustomAsync(session.IsRemote ? "Server file edit" : "Archive edit", body, buttons);
            CheckSessionAction();
            switch (answer as string)
            {
                case "commit": await CommitSessionAsync(session); break;
                case "reopen":
                    var now = await ReadEditStateAsync(session); CheckSessionAction();
                    if (now is EditState.Unchanged or EditState.Modified) OpenSessionEditor(session);
                    else Notify(Describe(now) + ". Your edit is kept.", true);
                    break;
                case "copy": await SaveSessionCopyAsync(session); break;
                case "discard": await DiscardSessionAsync(session, state); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (EditSessionsActive) Notify($"Cannot review the edit: {ex.Message}", true); }
    }

    private async Task CommitSessionAsync(EditSessionRecord session)
    {
        if (!EditSessionsActive || !_sessionActions.Add(session.Id)) return;
        var origin = ActiveTab;
        try
        {
            if (session.IsRemote) await CommitRemoteAsync(session, origin);
            else await CommitArchiveSessionAsync(session, origin);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            if (EditSessionsActive) Notify($"Cannot commit the edit: {ex.Message}. Your working copy is kept.", true);
        }
        finally
        {
            if (!_sessionCommits.Values.Any(c => c.Session.Id == session.Id)) _sessionActions.Remove(session.Id);
        }
    }

    private void CheckSessionAction() { if (!EditSessionsActive) throw new OperationCanceledException(); }

    private async Task<EditCommitCopy> PrepareEditCommitAsync(EditSessionRecord session)
    {
        CheckSessionAction(); var local = Location.FileSystem(session.WorkingPath);
        var copy = await Services.Io.Run(Services.Providers.For(local).GetDeviceKey(local), IoPriority.Normal, _ =>
        {
            CheckSessionAction(); return Services.EditSessions.PrepareCommit(session, Services.Paths.TempDirectory);
        }); // Own active synchronous work until it returns, even during shutdown.
        if (!EditSessionsActive) { copy.Dispose(); throw new OperationCanceledException(); }
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
            if (EditSessionsActive) Notify($"Cannot reach {profile.Display}: {ex.Message} Your edit is kept.", true);
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
            if (EditSessionsActive) Notify($"The commit finished, but the session could not be updated: {ex.Message}", true);
        }
        finally
        {
            // JobFinished is delivered only after the executor and journal have returned. Failed/canceled jobs
            // release their snapshots too. Shutdown can suppress acknowledgment, never snapshot ownership.
            try { await Task.Run(commit.Copy.Dispose); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (EditSessionsActive) Notify($"The temporary commit copy could not be removed: {ex.Message}", true); }
            _sessionCommits.Remove(job); _sessionActions.Remove(session.Id);
        }
    }

    private async Task SaveSessionCopyAsync(EditSessionRecord session)
    {
        // A conflict dialog can call this while its commit owner is held; copies have their own one-per-session owner.
        if (!EditSessionsActive || View.TopLevel is not { } top || !_sessionCopyActions.Add(session.Id)) return;
        EditCommitCopy? copy = null;
        try
        {
            CheckSessionAction();
            using var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save a copy of the edited file",
                SuggestedFileName = Path.GetFileName(session.WorkingPath),
            });
            CheckSessionAction();
            string? path = file?.TryGetLocalPath();
            if (path is null) return;
            path = Path.GetFullPath(path);
            if (Core.FileSystem.PathUtil.SafetyComparer.Equals(path, Path.GetFullPath(session.WorkingPath)))
            { Notify("Choose a different destination from the working copy. Your edit is kept.", true); return; }

            var local = Location.FileSystem(session.WorkingPath); var provider = Services.Providers.For(local);
            copy = await Services.Io.Run(provider.GetDeviceKey(local), IoPriority.Normal,
                _ => Services.EditSessions.PrepareSaveCopy(session, Services.Paths.TempDirectory, provider, CheckSessionAction));
            // Do not pass scheduler cancellation: an active source/copy keeps its owner until the call returns.
            CheckSessionAction();
            await MutationIoAsync(path, () =>
            {
                EditSessionStore.PublishSaveCopy(copy, path, Services.Platform.FileOperations, CheckSessionAction);
                return true;
            }, CheckSessionAction);
            CheckSessionAction();
            Notify($"Saved a copy to {path}. The edit session stays open.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        { if (EditSessionsActive) Notify($"Cannot save the copy: {ex.Message}. Your edit is kept.", true); }
        finally
        {
            try { if (copy is not null) await Task.Run(copy.Dispose); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (EditSessionsActive) Notify($"The temporary save copy could not be removed: {ex.Message}", true); }
            _sessionCopyActions.Remove(session.Id);
        }
    }

    private async Task DiscardSessionAsync(EditSessionRecord session, EditState state)
    {
        if (!EditSessionsActive || !_sessionActions.Add(session.Id)) return;
        try
        {
            var reviewed = await ReadEditReviewAsync(session);
            state = reviewed.State;
            if (state == EditState.Unavailable) { Notify("The working copy cannot be read completely. Your edit is kept; try again when it is available.", true); return; }
            if (!await Dialogs.ConfirmAsync("Discard edit",
                state == EditState.Modified
                    ? $"Discard your uncommitted changes to \"{session.DisplayName}\"? The working copy is deleted; {session.DisplayTarget} keeps its current content."
                    : $"Close the edit of \"{session.DisplayName}\" and delete its working copy?",
                "Discard", danger: state == EditState.Modified))
                return;
            CheckSessionAction();
            bool discarded = await MutationIoAsync(session.WorkingPath, () =>
            {
                // Recheck after device admission, without another queued gap between review and deletion.
                var local = Location.FileSystem(session.WorkingPath);
                var current = Services.EditSessions.ReviewWorking(session, Services.Providers.For(local), CheckSessionAction);
                if (current != reviewed) return false;
                CheckSessionAction(); Services.EditSessions.Discard(session); return true;
            }, CheckSessionAction);
            if (!discarded)
            {
                Notify("The working copy changed or became unavailable while you reviewed the discard. Your edit is kept; review it again before discarding.", true);
                return;
            }
            Unwatch(session.Id);
            Notify("The edit was discarded.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (EditSessionsActive) Notify($"The working copy could not be deleted (is it still open in the editor?): {ex.Message}", true);
        }
        finally { _sessionActions.Remove(session.Id); }
    }
}
