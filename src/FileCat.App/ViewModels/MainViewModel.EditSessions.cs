using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>
/// External edit sessions for archive members (plan §14.2, NET-003): F4 edits a private copy in the configured editor;
/// nothing is written back until an explicit Commit, which is guarded by the archive's version. Sessions survive
/// navigation, tab closing, and restarts; conflicts keep the edit and offer Save copy or an explicit rebase.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly Dictionary<string, FileSystemWatcher> _sessionWatchers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _announcedEdits = new(StringComparer.Ordinal);
    private readonly Dictionary<Job, (string SessionId, string Sha256)> _sessionCommits = new();

    /// <summary>Starts watching the saved sessions; returns a startup message when some have uncommitted changes.</summary>
    public string? RestoreEditSessions()
    {
        var sessions = Services.EditSessions.LoadAll();
        foreach (var s in sessions) Watch(s);
        int modified = sessions.Count(s => Services.EditSessions.StateOf(s) == EditState.Modified);
        return modified == 0 ? null
            : $"{Formatters.Plural(modified, "archive edit has", "archive edits have")} changes that are not committed yet. Review them in File → Edit sessions.";
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
        if (Copies(item) > 1)
        {
            Notify("This name appears more than once in the archive, so its edit could not be written back unambiguously. Extract the copy you want with F5.", true);
            return;
        }
        var existing = Services.EditSessions.Find(ArchiveFile(folder), member);
        if (existing is not null)
        {
            if (Services.EditSessions.StateOf(existing) == EditState.Modified) await ShowSessionAsync(existing);
            else OpenSessionEditor(existing);
            return;
        }
        EditSessionRecord session;
        try
        {
            Notify($"Extracting \"{item.Name}\" for editing…");
            session = await Task.Run(() => Services.EditSessions.Create(Services.Zip, item));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            Notify($"Cannot edit \"{item.Name}\": {ex.Message}", true);
            return;
        }
        Watch(session);
        OpenSessionEditor(session);
        Notify($"Editing a copy of \"{item.Name}\" from {Path.GetFileName(session.ArchivePath)}. Save in the editor, then commit with F4 on the member again (or File → Edit sessions). Nothing is written to the archive until you commit.");
    }

    private void OpenSessionEditor(EditSessionRecord session)
    {
        if (!File.Exists(session.WorkingPath))
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
            Notify($"\"{Path.GetFileName(current.MemberPath)}\" changed in the editor. Commit it to {Path.GetFileName(current.ArchivePath)} with F4 on the member, or File → Edit sessions.");
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

    /// <summary>File → Edit sessions: every open archive edit with its state.</summary>
    private async Task ShowEditSessionsAsync()
    {
        var sessions = Services.EditSessions.LoadAll();
        if (sessions.Count == 0)
        {
            Notify("No archive edits are open. F4 on a member of a ZIP archive starts one.");
            return;
        }
        var items = sessions.Select(s => new ChoiceItem(Path.GetFileName(s.MemberPath),
            $"{Describe(Services.EditSessions.StateOf(s))} · {s.MemberPath} in {s.ArchivePath}")).ToList();
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
        body.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Text = $"{session.MemberPath} in {session.ArchivePath}" });
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Classes = { "muted" },
            Text = $"{Describe(state)}. Working copy: {session.WorkingPath}" +
                   (session.LastCommitUtc is { } at ? $". Last committed {at.ToLocalTime():g}." : "."),
        });
        var buttons = new List<DialogButton> { new("Close", "close", IsCancel: true), new("Discard…", "discard", IsDanger: true), new("Save copy…", "copy") };
        if (state != EditState.Missing) buttons.Add(new DialogButton("Reopen editor", "reopen", IsDefault: state != EditState.Modified));
        if (state == EditState.Modified) buttons.Add(new DialogButton("Commit", "commit", IsDefault: true));
        var answer = await Dialogs.ShowCustomAsync("Archive edit", body, buttons);
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
        var check = await Task.Run(() => Services.EditSessions.Check(session));
        bool rebase = false;
        switch (check)
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
                rebase = true;
                break;
            case CommitCheck.MemberChanged:
                var answer = await Dialogs.ShowCustomAsync("Edit conflict",
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
                        Text = $"\"{session.MemberPath}\" was changed or removed in {Path.GetFileName(session.ArchivePath)} after you started editing. " +
                               "Committing would overwrite that change. Your edit is kept either way.",
                    },
                    [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Overwrite their change", "overwrite", IsDanger: true),
                     new DialogButton("Save my copy…", "copy", IsDefault: true)]);
                if (answer as string == "copy") await SaveSessionCopyAsync(session);
                if (answer as string != "overwrite") return;
                rebase = true;
                break;
        }
        string sha;
        ArchivePlan plan;
        try
        {
            sha = await Task.Run(() => EditSessionStore.Hash(session.WorkingPath));
            plan = Services.EditSessions.CommitPlan(session, rebase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify($"Cannot read the working copy: {ex.Message}. Close it in the editor and try again.", true);
            return;
        }
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.ArchiveUpdate,
            Archive = plan,
            Description = $"Commit \"{Path.GetFileName(session.MemberPath)}\" into \"{Path.GetFileName(session.ArchivePath)}\"",
        });
        _sessionCommits[job] = (session.Id, sha);
        if (ActiveTab is { } tab) Track(job, tab);
    }

    private void OnEditCommitFinished(Job job)
    {
        if (!_sessionCommits.Remove(job, out var commit)) return;
        var session = Services.EditSessions.LoadAll().FirstOrDefault(s => s.Id == commit.SessionId);
        if (session is null) return;
        if (job.State == JobState.Completed)
        {
            try
            {
                Services.EditSessions.Committed(session, commit.Sha256);
                _announcedEdits.Remove(session.Id);
                Notify($"Committed \"{Path.GetFileName(session.MemberPath)}\" into {Path.GetFileName(session.ArchivePath)}. The edit stays open for more changes; discard it when you are done.");
            }
            catch (IOException ex) { Notify($"The commit finished, but the session could not be updated: {ex.Message}", true); }
        }
        else Notify($"\"{Path.GetFileName(session.MemberPath)}\" was not committed; your edit is kept. Details are in the operations pane (Ctrl+J).", true);
    }

    private async Task SaveSessionCopyAsync(EditSessionRecord session)
    {
        if (View.TopLevel is not { } top || !File.Exists(session.WorkingPath)) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save a copy of the edited member",
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
        if (!await Dialogs.ConfirmAsync("Discard edit",
                state == EditState.Modified
                    ? $"Discard your uncommitted changes to \"{session.MemberPath}\"? The working copy is deleted; the archive keeps its current content."
                    : $"Close the edit of \"{session.MemberPath}\" and delete its working copy?",
                "Discard", danger: state == EditState.Modified))
            return;
        Unwatch(session.Id);
        try
        {
            await Task.Run(() => Services.EditSessions.Discard(session));
            Notify("The edit was discarded.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify($"The working copy could not be deleted (is it still open in the editor?): {ex.Message}", true);
        }
    }
}
