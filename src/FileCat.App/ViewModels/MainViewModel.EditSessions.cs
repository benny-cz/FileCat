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
        foreach (var review in _recoveryReviews.Values.ToArray()) review.Stop();
        foreach (string id in _sessionWatchers.Keys.ToArray()) Unwatch(id);
    }
    private readonly HashSet<string> _announcedEdits = new(StringComparer.Ordinal);
    private sealed class SessionCommit(EditSessionRecord session, EditCommitCopy copy, ArchiveJobPassword? readbackPassword = null)
    {
        public EditSessionRecord Session { get; } = session;
        public EditCommitCopy Copy { get; } = copy;
        public ArchiveJobPassword? ReadbackPassword { get; } = readbackPassword;
        public bool Finishing { get; set; }
    }
    private readonly Dictionary<Job, SessionCommit> _sessionCommits = new();
    // Bounded checkpoints for diagnosing a completed upload whose session was not acknowledged.
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _editCommitTrace = new();
    internal string EditCommitTrace => string.Join("; ", _editCommitTrace.ToArray());
    private void TraceEditCommit(string checkpoint)
    {
        _editCommitTrace.Enqueue(checkpoint);
        while (_editCommitTrace.Count > 24) _editCommitTrace.TryDequeue(out _);
    }
    private readonly HashSet<string> _sessionActions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sessionCopyActions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preparingEdits = new(StringComparer.Ordinal);

    /// <summary>Starts watching the saved sessions; returns a startup message when some have uncommitted changes.</summary>
    public async Task<string?> RestoreEditSessionsAsync()
    {
        try
        {
            var sessions = await LoadEditSessionsAsync();
            int modified = 0, unavailable = 0, retained = 0, retainedUnknown = 0, adopted = 0, interrupted = 0, notChecked = 0, incomplete = 0;
            foreach (var session in sessions)
            {
                // Adoption currently retains the native copy while the old session remains the commit route.
                if (session.LegacyAdoption is not null) { adopted++; continue; }
                if (Interrupted(session)) { interrupted++; continue; }
                if (InUse(session)) continue; // In use for now (another FileCat window may still be copying it).
                if (NotChecked(session)) { notChecked++; continue; }
                if (IncompleteStorage(session)) { incomplete++; continue; }
                var state = await ReadEditStateAsync(session); CheckSessionAction();
                if (state == EditState.Modified) modified++;
                if (state == EditState.Unavailable) unavailable++;
                if (session.IsSupported && session.StorageRootIdentity.Length != 0)
                {
                    try { if (await HasRetainedCaptureHintAsync(session)) retained++; }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
                    { retainedUnknown++; }
                }
                if (session.IsSupported) Watch(session);
            }
            if (modified == 0 && unavailable == 0 && retained == 0 && retainedUnknown == 0 && adopted == 0 && interrupted == 0 && notChecked == 0 &&
                incomplete == 0) return null;
            return ((modified == 0 ? "" : $"{Formatters.Plural(modified, "edit has", "edits have")} changes that are not committed yet.") +
                (unavailable == 0 ? "" : $" {(Formatters.Plural(unavailable, "working copy cannot", "working copies cannot"))} be read.") +
                (interrupted == 0 ? "" : $" {Formatters.Plural(interrupted, "edit was", "edits were")} left incomplete (interrupted while being made or removed).") +
                (notChecked == 0 ? "" : $" {Formatters.Plural(notChecked, "edit", "edits")} could not be checked just now.") +
                (incomplete == 0 ? "" : $" {Formatters.Plural(incomplete, "edit folder holds files", "edit folders hold files")} without the record that speaks for them.") +
                (retained == 0 ? "" : $" {Formatters.Plural(retained, "edit has", "edits have")} retained copy folders to review.") +
                (retainedUnknown == 0 ? "" : $" {Formatters.Plural(retainedUnknown, "edit has", "edits have")} retained-copy state that could not be checked.") +
                (adopted == 0 ? "" : $" {Formatters.Plural(adopted, "adopted copy needs", "adopted copies need")} review before use.")).TrimStart() +
                " Review them in File → Edit sessions.";
        }
        catch (OperationCanceledException) { return null; }
    }

    // An old record whose adopted copy is retiring its folder is no longer the edit (the copy is).
    private Task<IReadOnlyList<EditSessionRecord>> LoadEditSessionsAsync(Action? check = null) =>
        MutationIoAsync(Services.EditSessions.Root, () => EditSessionStore.Active(Services.EditSessions.LoadAll()), check ?? CheckSessionAction);

    private Task<bool> HasRetainedCaptureHintAsync(EditSessionRecord session) =>
        MutationIoAsync(Services.EditSessions.Root,
            () => Services.EditSessions.HasRetainedNativeCaptures(session), CheckSessionAction);

    private async Task<EditState> ReadEditStateAsync(EditSessionRecord session, Action? check = null) =>
        (await ReadEditReviewAsync(session, check)).State;

    private async Task<EditWorkingReview> ReadEditReviewAsync(EditSessionRecord session, Action? check = null)
    {
        check ??= CheckSessionAction; check();
        if (!session.IsSupported) return new(EditState.Unavailable, null);
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
                return Services.EditSessions.CreateRemoteFrom(profileId, profile.Display, remotePath, provider, item,
                    Services.SftpProvider.GetOriginMark(item.Parent), ct);
            }, $"Copying \"{item.Name}\" from {profile.Display} for editing…");
    }

    private async Task PrepareEditSessionAsync(ItemRef item, string key, Func<EditSessionRecord?> find,
        Func<ResourceProvider, CancellationToken, EditSessionRecord> create, string progress)
    {
        if (ActiveTab is not { } tab || !EditSessionsActive || !_preparingEdits.Add(key)) return;
        using var scope = new PreparationScope(tab, Services.Io);
        // What a stopped or failed creation had to keep. The scheduler completes a stopped call as cancelled with an
        // exception of its own, so the creation's text is taken from its exception inside the call.
        string? kept = null; bool said = false;
        try
        {
            Notify(progress);
            var provider = Services.Providers.For(item.Parent);
            // Queued cancellation is immediate; a started call retains all borrowed owners until it returns.
            using var readLease = ProviderReadLease.Capture(Services.Providers, [item.Parent]);
            var prepared = await Services.Io.RunOwned(provider.GetDeviceKey(item.Parent), IoPriority.Normal, _ =>
            {
                scope.Check(); var existing = find(); scope.Check();
                if (existing is not null)
                {
                    return (Session: existing, State: EditState.Unchanged, Created: false);
                }
                try
                {
                    var session = create(new EditPreparationProvider(provider, scope.Check), scope.Stop.Token);
                    return (Session: session, State: EditState.Unchanged, Created: true);
                }
                catch (Exception error) when (error.Data[EditSessionStore.RetainedCreationKey] is string retained) { kept = retained; throw; }
            }, scope.Stop.Token);
            if (!scope.Current) return;
            var session = prepared.Session;
            if (session.LegacyAdoption is not null) { await ShowSessionAsync(session); return; }
            void Check() { scope.Check(); CheckSessionAction(); }
            var state = prepared.Created ? prepared.State : await ReadEditStateAsync(session, Check);
            Check();
            if (!prepared.Created && state == EditState.Modified) { await ShowSessionAsync(session); return; }
            if (state == EditState.Missing) { Notify("The working copy is gone; discard this session and start a new edit.", true); return; }
            if (state == EditState.Unavailable) { Notify("The working copy cannot be read; it is kept. Try again when the editor has finished saving.", true); return; }
            Watch(session); OpenSessionEditor(session);
            if (prepared.Created) Notify($"Editing a copy of \"{item.Name}\" from {session.DisplayTarget}. Save in the editor, then commit with F4 on the file again (or File → Edit sessions). Nothing is written to the source until you commit.");
        }
        // A stopped or failed F4 removes the copy it was making; what it could not remove exactly is kept, logged, shown in
        // File → Edit sessions as an incomplete edit, and said here (unless FileCat is closing), whatever the failure was.
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            // The member's own name and anything a failure quotes of it are the archive's text: shown as such.
            if (scope.Current)
            { Notify($"Cannot edit \"{Formatters.SafeName(item.Name)}\": {Formatters.SafeName(ex.Message)}" + (kept is null ? "" : " " + Formatters.SafeName(kept)), true); said = true; }
        }
        finally
        {
            _preparingEdits.Remove(key);
            if (!said && kept is not null && EditSessionsActive) Notify(Formatters.SafeName(kept), true);
        }
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
            catch
            {
                try { source.Dispose(); }
                catch (Exception ex) { FileCat.Core.Diagnostics.AppLog.Warn("Could not close edit content after preparation failed", ex); }
                throw;
            }
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
                Notify("No edits are open. F4 on a file inside an archive or on a server starts one.");
                return;
            }
            var items = new List<ChoiceItem>();
            foreach (var session in sessions)
            {
                string detail = Interrupted(session) ? "incomplete (interrupted while being made or removed) · kept for review"
                    : InUse(session) ? "in use (another FileCat window may still be copying it)"
                    : NotChecked(session) ? "could not be checked just now"
                    : IncompleteStorage(session) ? "files without their record · kept for review"
                    : session.LegacyAdoption is not null
                    ? "retained adopted copy · review before use · " + session.DisplayContainer
                    : $"{Describe(await ReadEditStateAsync(session))} · {session.DisplayContainer}";
                if (session.IsSupported && session.StorageRootIdentity.Length != 0)
                {
                    try { if (await HasRetainedCaptureHintAsync(session)) detail += " · retained copy folders"; }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
                    { detail += " · retained-copy state unavailable"; }
                }
                items.Add(new ChoiceItem(session.DisplayName, detail));
            }
            CheckSessionAction();
            var pick = await Dialogs.ChooseAsync(new ChoiceOptions("Edit sessions", items)
            {
                Hint = "Enter shows edit actions or recovery details for a retained copy.",
            });
            CheckSessionAction();
            if (pick.Index >= 0) await ShowSessionAsync(sessions[pick.Index]);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (EditSessionsActive) Notify($"Cannot review the edit sessions: {ex.Message}", true); }
    }

    // A native edit left incomplete (FileCat ended while it made or removed it, or kept what it could not remove exactly).
    private static bool Interrupted(EditSessionRecord session) =>
        !session.IsSupported && session.UnsupportedReason == EditSessionStore.InterruptedCreationReason;

    // A native edit whose folder is in use for now: another FileCat process (sharing this data folder) may still be copying it.
    private static bool InUse(EditSessionRecord session) =>
        !session.IsSupported && session.UnsupportedReason == EditSessionStore.CopyingElsewhereReason;

    // A native edit without its record that could not be judged just now (the edit storage was busy, or its folder could not
    // be read).
    private static bool NotChecked(EditSessionRecord session) =>
        !session.IsSupported && session.UnsupportedReason == EditSessionStore.UncheckedReason;

    // A folder that holds an edit's files without the record that speaks for them: kept whole, for the user to review.
    private static bool IncompleteStorage(EditSessionRecord session) =>
        !session.IsSupported && session.UnsupportedReason == EditSessionStore.IncompleteEditReason;

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
            if (!session.IsSupported)
            {
                string directory = Services.EditSessions.SessionDirectory(session);
                bool interrupted = Interrupted(session);
                var unsupportedActions = new List<DialogButton> { new("Close", "close", IsCancel: true, IsDefault: true), new("Open working folder", "folder") };
                if (interrupted && Services.EditSessions.SupportsInterruptedCopyRemoval)
                    unsupportedActions.Add(new("Remove unfinished copy…", "remove", IsDanger: true));
                var unsupportedAnswer = await Dialogs.ShowCustomAsync(interrupted ? "Incomplete edit"
                    : InUse(session) ? "Edit in use" : NotChecked(session) ? "Edit not checked"
                    : IncompleteStorage(session) ? "Edit without its record" : "Unsupported edit session", new TextBlock
                {
                    Text = (session.UnsupportedReason ?? "This edit session requires a different FileCat version.") +
                        "\nWorking copy: " + Formatters.SafeName(session.WorkingPath) + "\nWorking folder: " + Formatters.SafeName(directory),
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
                }, unsupportedActions);
                CheckSessionAction();
                if (unsupportedAnswer as string == "folder") Services.Shell.Open(directory);
                else if (unsupportedAnswer as string == "remove") await RemoveInterruptedCopyAsync(session);
                return;
            }
            if (session.LegacyAdoption is not null)
            {
                EditLegacyAdoptionCatalogEntry? relation = null;
                try
                {
                    relation = await MutationIoAsync(Services.EditSessions.Root,
                        () => Services.EditSessions.ReviewLegacyAdoptions(OpenLegacyAdoptionSource)
                            .FirstOrDefault(row => row.Native.Id == session.Id), CheckSessionAction);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
                { /* An unavailable source is a retained recovery state, not permission to use the copy. */ }
                CheckSessionAction();
                bool linked = relation?.State == EditLegacyAdoptionCatalogState.LinkedRetainedCopy;
                string relationText = session.LegacyRetiring
                    ? "This copy is the edit. Its original folder is being retired; finish retiring it to use this edit again."
                    : linked
                    ? "The old edit is still the active commit route. This linked native copy is retained for review."
                    : "The link to the old edit needs review. " +
                      (relation?.Reason ?? "The original could not be reviewed. Both copies are kept.");
                string directory = Services.EditSessions.SessionDirectory(session);
                string? oldDirectory = relation?.ActiveLegacy is { } active
                    ? Services.EditSessions.SessionDirectory(active) : null;
                var actions = new List<DialogButton>
                {
                    new("Close", "close", IsCancel: true, IsDefault: true),
                    new("Open working folder", "folder")
                };
                if (oldDirectory is not null) actions.Add(new("Open original folder", "original"));
                if (session.LegacyRetiring) { actions.Add(new("Finish retiring original…", "retire")); actions.Add(new("Cancel retirement…", "unretire")); }
                else if (linked) actions.Add(new("Retire original folder…", "retire"));
                var retainedAnswer = await Dialogs.ShowCustomAsync("Retained adopted edit", new TextBlock
                {
                    Text = relationText + "\nWorking copy: " + Formatters.SafeName(session.WorkingPath) +
                           "\nWorking folder: " + Formatters.SafeName(directory) +
                           (oldDirectory is null ? "" : "\nOriginal edit folder: " + Formatters.SafeName(oldDirectory)),
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
                }, actions);
                CheckSessionAction();
                if (retainedAnswer as string == "folder") Services.Shell.Open(directory);
                if (retainedAnswer as string == "original" && oldDirectory is not null) Services.Shell.Open(oldDirectory);
                if (retainedAnswer as string == "retire") await RetireOriginalFolderAsync(session);
                if (retainedAnswer as string == "unretire") await CancelRetirementAsync(session);
                return;
            }
            var state = await ReadEditStateAsync(session);
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Text = session.DisplayContainer });
            body.Children.Add(new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Classes = { "muted" },
                Text = $"{Describe(state)}. Working copy: {Formatters.SafeName(session.WorkingPath)}" +
                       (session.LastCommitUtc is { } at ? $". Last committed {at.ToLocalTime():g}." : "."),
            });
            var buttons = new List<DialogButton> { new("Close", "close", IsCancel: true), new("Discard…", "discard", IsDanger: true), new("Save copy…", "copy") };
            if (session.StorageRootIdentity.Length != 0) buttons.Add(new DialogButton("Review retained copies…", "retained"));
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
                case "retained": await ReviewSessionRetainedCopiesAsync(session); break;
                case "discard": await DiscardSessionAsync(session, state); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (EditSessionsActive) Notify($"Cannot review the edit: {ex.Message}", true); }
    }

    private static ILegacyEditSource OpenLegacyAdoptionSource(string root, EditSessionRecord old, CancellationToken ct) =>
        OperatingSystem.IsWindows()
            ? FileCat.Platform.Windows.Archives.WindowsLegacyEditSource.Open(root, old, ct)
            : UnixLegacyEditSource.Open(root, old, ct);

    private async Task ReviewSessionRetainedCopiesAsync(EditSessionRecord session)
    {
        try
        {
            CheckSessionAction();
            var reviews = await MutationIoAsync(Services.EditSessions.Root,
                () => Services.EditSessions.ReviewRetainedCaptures(session), CheckSessionAction);
            CheckSessionAction();
            if (reviews.Count == 0) { Notify("No retained copies were found for this edit."); return; }
            var items = reviews.Select(copy => new ChoiceItem(Path.GetFileName(copy.Directory),
                copy.State switch
                {
                    EditCaptureRecoveryState.Complete => "Verified retained copy",
                    EditCaptureRecoveryState.Interrupted => "Interrupted copy",
                    _ => "Needs review",
                })).ToList();
            var choice = await Dialogs.ChooseAsync(new ChoiceOptions("Retained edit copies", items)
            {
                Hint = "Select a copy to review its state and retained folder. A verified copy can be saved separately.",
            });
            CheckSessionAction();
            if (choice.Index < 0 || choice.Index >= reviews.Count) return;
            var selected = reviews[choice.Index];
            string detail = selected.Reason + "\nRetained folder: " + Formatters.SafeName(selected.Directory) +
                (selected.Length is { } length ? "\nVerified bytes: " + length : "") +
                (selected.Sha256 is { } digest ? "\nSHA-256: " + digest : "");
            if (selected.SavedCopy is { } saved)
                detail += "\nSaved copy: " + Formatters.SafeName(saved.Destination) +
                          "\n" + saved.State + ". " + saved.Reason;
            var actions = new List<DialogButton> { new("Close", "close", IsCancel: true, IsDefault: true),
                new("Open retained folder", "folder") };
            if (selected.State == EditCaptureRecoveryState.Complete && selected.SavedCopy is null &&
                Services.EditSessions.SupportsRecoveredNativeCopy)
                actions.Add(new("Save verified copy…", "save"));
            // A copy that a saved copy refers to is that copy's recovery evidence; anything unverified stays for review.
            if (selected.State is EditCaptureRecoveryState.Complete or EditCaptureRecoveryState.Interrupted && selected.SavedCopy is null &&
                Services.EditSessions.SupportsRetainedCaptureRemoval)
                actions.Add(new("Remove retained copy…", "remove"));
            var answer = await Dialogs.ShowCustomAsync("Retained edit copy", new TextBlock
            {
                Text = detail, TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
            }, actions);
            CheckSessionAction();
            if (answer as string == "folder") Services.Shell.Open(selected.Directory);
            else if (answer as string == "save") await SaveRecoveredSessionCopyAsync(session, selected);
            else if (answer as string == "remove") await RemoveRetainedSessionCopyAsync(session, selected);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { if (EditSessionsActive) Notify("The retained copies could not be reviewed: " + Formatters.SafeName(error.Message), true); }
    }

    private static string? RemoveLegacyFolder(EditLegacyRemoval request, bool probe, CancellationToken ct) =>
        OperatingSystem.IsWindows()
            ? FileCat.Platform.Windows.Archives.WindowsLegacyEditRemoval.Remove(request, probe, ct)
            : UnixLegacyEditRemoval.Remove(request, probe, ct);

    /// <summary>
    /// Plan §12.1: the adopted copy becomes the edit and its original folder is removed, object by object, after FileCat
    /// checks it again. Anything changed or unexpected in the original folder is kept and named.
    /// </summary>
    private async Task RetireOriginalFolderAsync(EditSessionRecord session)
    {
        // The original edit must not commit (rewriting its record) while its folder is retired: both ids are held.
        string? original = session.LegacyAdoption?.SourceId;
        if (!EditSessionsActive || original is null) return;
        if (_sessionActions.Contains(original) || _sessionCommits.Values.Any(c => c.Session.Id == original))
        {
            Notify("The original edit is being committed or changed. Retire its folder after that finishes.", true);
            return;
        }
        if (!_sessionActions.Add(session.Id)) return;
        _sessionActions.Add(original);
        try
        {
            if (!session.LegacyRetiring && !await Dialogs.ConfirmAsync("Retire original folder",
                $"Make this copy the edit of \"{session.DisplayName}\" and remove its original folder? FileCat checks the original " +
                "again and removes only what it copied. Close any editor that still shows the original working copy first.",
                "Retire", danger: true))
                return;
            CheckSessionAction();
            var result = await MutationIoAsync(Services.EditSessions.Root,
                () => Services.EditSessions.RetireLegacyFolder(session, OpenLegacyAdoptionSource, RemoveLegacyFolder, check: CheckSessionAction),
                CheckSessionAction);
            if (!EditSessionsActive) return;
            if (result.Complete) Notify("The original folder was removed. This copy is now the edit.");
            else Notify(Formatters.SafeName(result.Issue ?? "The original folder was kept for review."), true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { if (EditSessionsActive) Notify("The original folder was kept: " + Formatters.SafeName(error.Message), true); }
        finally { _sessionActions.Remove(session.Id); _sessionActions.Remove(original); }
    }

    /// <summary>
    /// A retirement whose original folder could not be removed (the original working copy was saved again, its record
    /// replaced) can be stopped while the original edit is intact: the original is the edit again; nothing is removed.
    /// </summary>
    private async Task CancelRetirementAsync(EditSessionRecord session)
    {
        if (!EditSessionsActive || !_sessionActions.Add(session.Id)) return;
        try
        {
            if (!await Dialogs.ConfirmAsync("Cancel retirement",
                $"Make the original edit of \"{session.DisplayName}\" the edit again? This copy is kept for review; nothing is removed.",
                "Use original"))
                return;
            CheckSessionAction();
            await MutationIoAsync(Services.EditSessions.Root, () => Services.EditSessions.CancelLegacyRetirement(session), CheckSessionAction);
            if (EditSessionsActive) Notify("The original edit is the edit again. Its copy is kept for review.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { if (EditSessionsActive) Notify("The retirement continues: " + Formatters.SafeName(error.Message), true); }
        finally { _sessionActions.Remove(session.Id); }
    }

    /// <summary>
    /// The review action for an interrupted copy (FileCat ended while it made the edit): FileCat reviews its folder again and
    /// removes exactly what the unfinished copy consists of. Anything else in the folder keeps it all.
    /// </summary>
    private async Task RemoveInterruptedCopyAsync(EditSessionRecord session)
    {
        if (!EditSessionsActive || !_sessionActions.Add(session.Id)) return;
        try
        {
            // A copy that never got as far as its name is named by its folder's id: it is then "this unfinished copy".
            string named = session.DisplayName == session.Id ? "this unfinished copy"
                : $"the unfinished copy of \"{Formatters.SafeName(session.DisplayName)}\"";
            if (!await Dialogs.ConfirmAsync("Remove unfinished copy",
                $"Remove {named}? FileCat did not finish making it, so it holds at most part of what it was copying, " +
                "and what it was copied from is unchanged. FileCat reviews its folder again first and keeps everything it cannot " +
                "account for as its own unfinished work.", "Remove", danger: true))
                return;
            CheckSessionAction();
            var result = await MutationIoAsync(Services.EditSessions.Root,
                () => Services.EditSessions.RemoveInterruptedCopy(session, check: CheckSessionAction), CheckSessionAction);
            if (!EditSessionsActive) return;
            if (result.Complete) Notify(result.MayHaveRemovedFiles ? "The unfinished copy was removed." : "The unfinished copy was already gone.");
            else Notify(Formatters.SafeName(result.Issue ?? "The unfinished copy was kept for review."), true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or
            System.ComponentModel.Win32Exception)
        { if (EditSessionsActive) Notify("The unfinished copy was kept: " + Formatters.SafeName(error.Message), true); }
        finally { _sessionActions.Remove(session.Id); }
    }

    /// <summary>
    /// The restart recovery action for one retained copy: FileCat reviews it again and removes only that copy. The edit,
    /// its working copy and its other retained copies stay.
    /// </summary>
    private async Task RemoveRetainedSessionCopyAsync(EditSessionRecord session, EditCaptureRecovery selected)
    {
        if (!EditSessionsActive || !_sessionActions.Add(session.Id)) return;
        try
        {
            string kind = selected.State == EditCaptureRecoveryState.Complete ? "verified retained copy" : "interrupted copy";
            if (!await Dialogs.ConfirmAsync("Remove retained copy",
                $"Remove this {kind} of \"{session.DisplayName}\"? FileCat checks it again and removes only this copy. " +
                "The edit and its working copy stay.", "Remove", danger: true))
                return;
            CheckSessionAction();
            var result = await MutationIoAsync(Services.EditSessions.Root,
                () => Services.EditSessions.RemoveRetainedCapture(session, selected.Directory, check: CheckSessionAction), CheckSessionAction);
            if (!EditSessionsActive) return;
            if (result.Complete) Notify("The retained copy was removed. The edit is kept.");
            else Notify(Formatters.SafeName(result.Issue ?? "The retained copy was kept for review."), true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { if (EditSessionsActive) Notify("The retained copy was kept: " + Formatters.SafeName(error.Message), true); }
        finally { _sessionActions.Remove(session.Id); }
    }

    private async Task SaveRecoveredSessionCopyAsync(EditSessionRecord session, EditCaptureRecovery selected)
    {
        if (selected.State != EditCaptureRecoveryState.Complete || selected.SavedCopy is not null ||
            !EditSessionsActive || View.TopLevel is not { } top || !_sessionCopyActions.Add(session.Id)) return;
        try
        {
            CheckSessionAction();
            using var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save verified retained edit copy",
                SuggestedFileName = Path.GetFileName(session.WorkingPath),
            });
            CheckSessionAction();
            string? destination = file?.TryGetLocalPath();
            if (destination is null) return;
            destination = Path.GetFullPath(destination);
            // Publication may succeed just before shutdown/navigation. Retain the running IO owner and return its
            // outcome without a post-publication action check, so the result is always disposed and reported.
            var location = Location.FileSystem(destination);
            var result = await Services.Io.RunOwned(Services.Providers.For(location).GetDeviceKey(location), IoPriority.Normal,
                _ =>
                {
                    CheckSessionAction();
                    return Services.EditSessions.SaveRecoveredNativeCopy(session, selected.Directory, destination,
                        check: CheckSessionAction);
                });
            var published = result.Published;
            var issue = result.Issue;
            bool recorded = result.OutcomeRecorded;
            try { result.Dispose(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { issue = issue is null ? error : new AggregateException(issue, error); }
            if (!EditSessionsActive) return;
            if (published is not null)
            {
                if (issue is null && recorded)
                    Notify($"Saved the verified retained copy to {destination}. The edit session and captured copy are kept.");
                else Notify($"A copy was published at {destination}, but its outcome needs review: " +
                    Formatters.SafeName(issue?.Message ?? "The final receipt is missing.") +
                    " The edit session and captured copy are kept.", true);
            }
            else Notify($"The save-copy outcome at {destination} needs review: " +
                Formatters.SafeName(issue?.Message ?? "Publication was not confirmed.") +
                " The edit session and captured copy are kept.", true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        { if (EditSessionsActive) Notify("The retained copy could not be saved: " + Formatters.SafeName(error.Message) +
            ". The edit session and captured copy are kept; review the destination before trying again.", true); }
        finally { _sessionCopyActions.Remove(session.Id); }
    }

    private async Task CommitSessionAsync(EditSessionRecord session)
    {
        if (!session.IsSupported) { await ShowSessionAsync(session); return; }
        if (session.LegacyAdoption is not null) { await ShowSessionAsync(session); return; }
        if (!EditSessionsActive || !_sessionActions.Add(session.Id)) return;
        var origin = ActiveTab;
        try
        {
            if (session.IsNativeArchive) await CommitNativeArchiveSessionAsync(session, origin);
            else if (session.IsRemote) await CommitRemoteAsync(session, origin);
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
            // The capture's copy of a large member asks this between its reads, so a shutdown is not held up by one:
            // what it leaves is charged, listed as a retained copy and removable, instead of blocking the close.
            CheckSessionAction();
            return Services.EditSessions.PrepareCommit(session, Services.Paths.TempDirectory, check: CheckSessionAction);
        }); // Own active synchronous work until it returns, even during shutdown.
        if (!EditSessionsActive) { copy.Dispose(); throw new OperationCanceledException(); }
        return copy;
    }

    private void SubmitEditCommit(EditSessionRecord session, EditCommitCopy copy, JobRequest request, TabViewModel? origin)
    {
        Job? registered = null;
        try
        {
            TraceEditCommit("submit-start");
            CheckSessionAction(); _ = Operations; // Register the finish observer before a fast job can return.
            var job = Services.Jobs.SubmitWithRegistration(request, admitted =>
            {
                registered = admitted;
                _sessionCommits[admitted] = new SessionCommit(session, copy);
                TraceEditCommit($"owner-registered:{admitted.ShortId}:{admitted.State}");
            });
            if (origin is not null) Track(job, origin);
        }
        catch
        {
            if (registered is not null) _sessionCommits.Remove(registered);
            TraceEditCommit("submit-error"); copy.Dispose(); throw;
        }
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
        (ContentRevision? Revision, string? Sha256) now;
        try
        {
            CheckSessionAction();
            now = await Services.Io.Run(Services.Providers.For(folder).GetDeviceKey(folder), IoPriority.Normal, ct =>
            { CheckSessionAction(); return RemoteRevisionAndHash(folder, session.RemotePath, ct); });
            CheckSessionAction();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (EditSessionsActive) Notify($"Cannot reach {profile.Display}: {ex.Message} Your edit is kept.", true);
            return;
        }
        ContentRevision? expected = session.RemoteBaseline;
        string? expectedSha256 = session.BaseSha256;
        var check = EditSessionStore.CheckRemote(session, now.Revision);
        if (check == CommitCheck.Ready && !string.Equals(now.Sha256, session.BaseSha256, StringComparison.OrdinalIgnoreCase))
            check = CommitCheck.MemberChanged;
        switch (check)
        {
            case CommitCheck.ArchiveMissing:
                var gone = await Dialogs.ShowCustomAsync("File not on the server",
                    new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Text = $"\"{session.RemotePath}\" is no longer on {profile.Display}. Your edit is kept." },
                    [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Save my copy…", "copy"), new DialogButton("Create it again", "create", IsDefault: true)]);
                if (gone as string == "copy") await SaveSessionCopyAsync(session);
                if (gone as string != "create") return;
                expected = null;
                expectedSha256 = null;
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
                expected = now.Revision;
                expectedSha256 = now.Sha256;
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
            ExpectedTargetSha256 = expectedSha256,
            EditUpload = Services.EditSessions.RemoteCommitInput(session, copy),
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

    /// <summary>Read the current remote bytes as well as metadata before asking about an edit conflict.</summary>
    private (ContentRevision? Revision, string? Sha256) RemoteRevisionAndHash(Location folder, string path, CancellationToken ct)
    {
        using var lease = Services.SftpProvider.Lease(folder, ct);
        var channel = lease.Channel;
        if (channel.Stat(path) is not { IsDirectory: false } before) return (null, null);
        byte[] digest;
        using (var stream = channel.OpenRead(path, before.Size))
            digest = Core.FileSystem.PortableFileOperations.HashStream(stream,
                System.Security.Cryptography.HashAlgorithmName.SHA256, ct,
                progress: _ => CheckSessionAction(), expectedLength: before.Size);
        var after = channel.Stat(path);
        if (after is not { IsDirectory: false } latest || latest.Size != before.Size || latest.ModifiedUtc != before.ModifiedUtc)
            throw new IOException("The server file changed while its edit baseline was checked; your edit is kept.");
        return (new ContentRevision(before.Size, before.ModifiedUtc.Ticks), Convert.ToHexString(digest));
    }

    private void OnEditCommitFinished(Job job)
    {
        if (!_sessionCommits.TryGetValue(job, out var commit))
        {
            if (job.Request.Description?.StartsWith("Commit \"", StringComparison.Ordinal) == true)
                TraceEditCommit($"completion-without-owner:{job.ShortId}:{job.State}");
            return;
        }
        if (commit.Finishing) return;
        TraceEditCommit($"completion-observed:{job.ShortId}:{job.State}");
        commit.Finishing = true;
        _ = FinishEditCommitAsync(job, commit);
    }

    private async Task FinishEditCommitAsync(Job job, SessionCommit commit)
    {
        var session = commit.Session;
        try
        {
            CheckSessionAction();
            if (job.State != JobState.Completed && !(session.IsNativeArchive && job.State == JobState.CompletedWithIssues && job.ArchiveEditPublication is not null))
            {
                Notify(job.ArchiveEditPublication is not null
                    ? $"A publication was recorded for \"{session.DisplayName}\", but this session was not acknowledged. Your working copy and previous baseline are kept. Review the operations pane (Ctrl+J)."
                    : $"The commit of \"{session.DisplayName}\" was not confirmed; your edit is kept. Details are in the operations pane (Ctrl+J).", true);
                return;
            }
            if (session.IsNativeArchive) await AcknowledgeNativeArchiveEditAsync(job, commit);
            else if (session.IsRemote)
            {
                var folder = job.Request.Destination!;
                TraceEditCommit("remote-revision-start");
                var revision = await Services.Io.Run(Services.Providers.For(folder).GetDeviceKey(folder), IoPriority.Normal, _ =>
                { CheckSessionAction(); return RemoteRevision(folder, session.RemotePath); });
                TraceEditCommit($"remote-revision-read:{revision?.Length}");
                CheckSessionAction();
                if (revision is not { } r || r.Length != commit.Copy.Length) throw new IOException("The committed server file is missing or changed; the previous edit baseline is kept.");
                TraceEditCommit("baseline-save-start");
                await MutationIoAsync(session.WorkingPath, () => Services.EditSessions.CommittedRemote(session, commit.Copy.Sha256, r), CheckSessionAction);
                TraceEditCommit("baseline-saved");
            }
            else
                await MutationIoAsync(session.ArchivePath, () => Services.EditSessions.Committed(session, commit.Copy), CheckSessionAction);
            CheckSessionAction();
            _announcedEdits.Remove(session.Id);
            Notify($"Committed \"{session.DisplayName}\" to {session.DisplayTarget}. The edit stays open for more changes; discard it when you are done.");
        }
        catch (OperationCanceledException)
        {
            TraceEditCommit($"ack-canceled:active={EditSessionsActive}");
            // Plan §12.2: the commit itself happened; say so, and that its new state was not confirmed (never silent).
            bool committed = job.State == JobState.Completed ||
                session.IsNativeArchive && job.State == JobState.CompletedWithIssues && job.ArchiveEditPublication is not null;
            if (EditSessionsActive && committed)
                Notify($"\"{session.DisplayName}\" was committed to {session.DisplayTarget}, but checking the saved result was canceled. " +
                    "The edit stays open with its previous state; review it in Edits before making more changes.", true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            TraceEditCommit($"ack-error:{ex.GetType().Name}");
            if (EditSessionsActive) Notify($"The commit finished, but the session could not be updated: {ex.Message}", true);
        }
        finally
        {
            // JobFinished is delivered only after the executor and journal have returned. Failed/canceled jobs
            // release their snapshots too. Shutdown can suppress acknowledgment, never snapshot ownership.
            try
            {
                try { await Task.Run(commit.Copy.Dispose); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { if (EditSessionsActive) Notify($"The temporary commit copy could not be removed: {ex.Message}", true); }
            }
            finally
            {
                commit.ReadbackPassword?.Dispose();
                _sessionCommits.Remove(job); _sessionActions.Remove(session.Id);
            }
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
                _ => Services.EditSessions.CaptureSaveCopy(session, Services.Paths.TempDirectory, provider, CheckSessionAction));
            // Do not pass scheduler cancellation: an active source/copy keeps its owner until the call returns.
            CheckSessionAction();
            string? lost = await MutationIoAsync(path,
                () => EditSessionStore.PublishSaveCopy(copy, path, Services.Platform.FileOperations, CheckSessionAction), CheckSessionAction);
            CheckSessionAction();
            Notify($"Saved a copy to {path}. The edit session stays open." + (lost is null ? "" : " " + lost), lost is not null);
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
            string retained = session.StorageRootIdentity.Length == 0 ? "" :
                " Any temporary copies kept for recovery will also be deleted. Files you saved separately are kept.";
            if (!await Dialogs.ConfirmAsync("Discard edit",
                state == EditState.Modified
                    ? $"Discard your uncommitted changes to \"{session.DisplayName}\"? The working copy is deleted; {session.DisplayTarget} keeps its current content." + retained
                    : $"Close the edit of \"{session.DisplayName}\" and delete its working copy?" + retained,
                "Discard", danger: state == EditState.Modified || session.StorageRootIdentity.Length != 0))
                return;
            CheckSessionAction();
            EditDiscardResult? result = null;
            bool discarded = await MutationIoAsync(session.WorkingPath, () =>
            {
                // Recheck after device admission, without another queued gap between review and deletion.
                var local = Location.FileSystem(session.WorkingPath);
                var current = Services.EditSessions.ReviewWorking(session, Services.Providers.For(local), CheckSessionAction);
                if (current != reviewed) return false;
                CheckSessionAction(); result = Services.EditSessions.DiscardReviewed(session, current, check: CheckSessionAction); return true;
            }, CheckSessionAction);
            if (!discarded)
            {
                Notify("The working copy changed or became unavailable while you reviewed the discard. Your edit is kept; review it again before discarding.", true);
                return;
            }
            if (result is not { Complete: true })
            {
                if (EditSessionsActive) Notify(Formatters.SafeName(result?.Issue ?? "Discard did not finish. Review the retained working folder."), true);
                return;
            }
            Unwatch(session.Id);
            Notify("The edit was discarded.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A refusal already says what FileCat will not do and what it keeps; only a failed deletion suggests the
            // editor. Both carry a path from FileCat's own folder, which holds the member's name, so both are sanitized.
            if (EditSessionsActive)
                Notify(ex is NotSupportedException ? Formatters.SafeName(ex.Message)
                    : "The working copy could not be deleted (is it still open in the editor?): " + Formatters.SafeName(ex.Message), true);
        }
        finally { _sessionActions.Remove(session.Id); }
    }
}
