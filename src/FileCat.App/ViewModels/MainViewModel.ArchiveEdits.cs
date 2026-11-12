using FileCat.App.Services;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private sealed class NativeEditSource(MainViewModel owner, ArchiveTargetReview target, IArchiveRetainedFile held,
        string rawPath, ArchiveJobPassword? password) : IArchiveEditSource
    {
        public ArchiveTargetReview Target { get; } = target;
        public ArchiveManifestMember Member { get; } = ArchiveEditEvidence.UniqueMember(target, rawPath);
        public IContentSource OpenContent(CancellationToken ct) => Target.Profile.Format == ArchiveWriteFormat.Zip
            ? owner.Services.Zip.OpenReviewedMember(Target, Member.Name, ct)
            : owner.Services.Archives.OpenReviewedMember(Target, Member.Name, password, ct);
        public void ReviewSource(CancellationToken ct)
        {
            owner.CheckSessionAction(); held.Review(Target.Native, ct); owner.CheckSessionAction();
        }
        public string WorkingIdentity(SafeFileHandle handle) => owner.Services.Archives.GetOpenedSourceIdentity(handle) ??
            throw new IOException("The private edit file has no stable native identity.");
        public void PreserveOrigin(FileStream working,string workingPath,CancellationToken ct)
        {
            if (OperatingSystem.IsWindows()) FileCat.Platform.Windows.Archives.WindowsArchiveEditOrigin.Copy(working,workingPath,Target.Native.Origin,ct);
            else ArchiveUnixOrigin.CopyToNewWorkingFile(working.SafeFileHandle, Target.Native.Origin, ct);
        }
    }

    private sealed record NativeEditSelection(string RawPath);

    private async Task EditNativeArchiveMemberAsync(ItemRef item)
    {
        // Plan §11.6: a duplicate name is explained up front, before any review or password, with extraction offered.
        if (HasDuplicateName(item))
        {
            var answer = await Dialogs.ShowCustomAsync("Edit archive member", new Avalonia.Controls.TextBlock
            {
                Text = "Several members have this name. Extract the selected copy to edit it; direct commit is unavailable for duplicate names.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 560,
            }, [new DialogButton("Cancel", "cancel", IsCancel: true, IsDefault: true), new DialogButton("Extract this copy…", "extract")]);
            if (answer as string == "extract") await TransferAsync(JobKind.Copy, [item]);
            return;
        }
        try { await PrepareNativeArchiveEditAsync(item); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { if (EditSessionsActive) Notify("Cannot edit this archive member: " + Formatters.SafeName(error.Message), true); }
    }

    private bool HasDuplicateName(ItemRef item)
    {
        if (item.Ordinal > 0) return true;
        if (ActiveTab?.Listing is not { } listing) return false;
        int same = 0;
        for (int i = 0; i < listing.VisibleCount; i++)
        {
            var entry = listing.GetVisible(i);
            if (entry.Kind == item.Kind && string.Equals(entry.Name, item.Name, StringComparison.Ordinal) && ++same > 1) return true;
        }
        return false;
    }

    private async Task PrepareNativeArchiveEditAsync(ItemRef item)
    {
        if (ActiveTab is not { } tab || item.ArchiveRevision is not { } revision)
        { Notify(item.ArchiveMutationProblem ?? "Refresh the archive before starting an edit.", true); return; }
        using var scope = new PreparationScope(tab, Services.Io);
        using var command = await PrepareNativeArchiveCommandAsync(item.Parent, expectedRevision: revision);
        if (command is null || !scope.Current) return;
        var selected = await RunArchivePreparationAsync(command.Target.Path, "Prepare archive edit",
            "Checking the exact unique member and its complete source revision.", ct =>
        {
            scope.Check();
            string raw = item.Parent.Scheme == Schemes.Zip ? Services.Zip.ReviewedMemberPath(command.Target, item)
                : Services.Archives.ReviewedMemberPath(command.Target, item, command.Password, ct);
            _ = ArchiveEditEvidence.UniqueMember(command.Target, raw); scope.Check(); return new NativeEditSelection(raw);
        });
        if (selected is null || !scope.Current) return;
        await PrepareEditSessionAsync(item, "native:" + command.Target.Native.Version.Identity + "/" + selected.RawPath,
            () => Services.EditSessions.FindArchive(command.Target.Path, command.Target.Profile.Format, command.Target.Native.Version.Identity, selected.RawPath),
            (_, ct) => Services.Jobs.ReadReviewedArchive(command.Target, held => Services.EditSessions.CreateArchive(
                new NativeEditSource(this, command.Target, held, selected.RawPath, command.Password), item.Name, ct), ct),
            $"Extracting \"{item.Name}\" for an explicit archive edit…");
    }

    private async Task CommitNativeArchiveSessionAsync(EditSessionRecord session, TabViewModel? origin)
    {
        if (session.ArchiveProfile is null) throw new NotSupportedException("This session has no supported archive profile.");
        var location = session.ArchiveFormat == ArchiveWriteFormat.Zip ? ZipProvider.ForFile(session.ArchivePath)
            : FileCat.Archives.ArchiveProvider.ForFile(session.ArchivePath, session.ArchiveFormat switch
        {
            ArchiveWriteFormat.Tar => FileCat.Archives.ArchiveKind.Tar,
            ArchiveWriteFormat.TarGzip => FileCat.Archives.ArchiveKind.TarGzip,
            ArchiveWriteFormat.TarBzip2 => FileCat.Archives.ArchiveKind.TarBzip2,
            ArchiveWriteFormat.TarZstd => FileCat.Archives.ArchiveKind.TarZstd,
            ArchiveWriteFormat.Rar5 => FileCat.Archives.ArchiveKind.Rar,
            ArchiveWriteFormat.SevenZip => FileCat.Archives.ArchiveKind.SevenZip,
            ArchiveWriteFormat.Gzip => FileCat.Archives.ArchiveKind.Gzip,
            ArchiveWriteFormat.Bzip2 => FileCat.Archives.ArchiveKind.Bzip2,
            ArchiveWriteFormat.Zstd => FileCat.Archives.ArchiveKind.Zstd,
            _ => throw new NotSupportedException("This archive edit profile is unavailable."),
        });
        using var command = await PrepareNativeArchiveCommandAsync(location, requestedProfile: session.ArchiveProfile);
        if (command is null)
        {
            CheckSessionAction();
            // Plan §12.2: an archive that is missing, moved, unsupported or no longer writable keeps the working copy and the
            // session, and offers Save copy and a fresh, manually chosen workflow (the working copy, ready to copy anywhere).
            var answer = await Dialogs.ShowCustomAsync("Archive edit kept", new Avalonia.Controls.TextBlock
            {
                Text = $"{Formatters.SafeName(Path.GetFileName(session.ArchivePath))} was not prepared for this commit: the check was stopped, or the " +
                    "archive is missing, moved, unsupported or no longer writable. Your working copy and edit session are kept. Save a copy, or " +
                    "show the working copy to copy it into an archive or folder you choose.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 640,
            }, [new("Cancel", "cancel", IsCancel: true), new("Show working copy", "show"), new("Save my copy…", "copy", IsDefault: true)]);
            if (answer as string == "copy") await SaveSessionCopyAsync(session);
            else if (answer as string == "show") ShowSessionWorkingCopy(session);
            return;
        }
        var check = EditSessionStore.ReviewArchive(session, command.Target);
        if (check == CommitCheck.MemberChanged)
        {
            var answer = await Dialogs.ShowCustomAsync("Archive edit conflict", new Avalonia.Controls.TextBlock
            {
                Text = $"\"{Formatters.SafeName(session.MemberDisplay)}\" changed, disappeared or became ambiguous. Your working copy is kept.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 640,
            }, [new("Cancel", "cancel", IsCancel: true), new("Save my copy…", "copy", IsDefault: true)]);
            if (answer as string == "copy") await SaveSessionCopyAsync(session);
            return;
        }
        bool rebase = check == CommitCheck.ArchiveChanged;
        if (rebase && !await Dialogs.ConfirmAsync("The archive changed",
            $"The complete current archive was checked. \"{Formatters.SafeName(session.MemberDisplay)}\" still has the same bytes and metadata. Apply this edit to the current archive?",
            "Apply edit to the current archive")) return;
        CheckSessionAction(); var copy = await PrepareEditCommitAsync(session);
        ArchiveJobPassword? readback = null; Job? admitted = null;
        try
        {
            var plan = EditSessionStore.CommitArchivePlan(session, command.Target, copy, rebase);
            if (command.Password is { } password)
            { using var grant = password.CreateGrant(); readback = grant.UseOnce(value => new ArchiveJobPassword(value)); }
            _ = Operations; CheckSessionAction();
            var job = Services.Jobs.SubmitArchive(plan, command.TakePassword(), description: $"Commit \"{session.DisplayName}\" into \"{session.DisplayTarget}\"",
                register: value =>
                {
                    admitted = value; _sessionCommits[value] = new SessionCommit(session, copy, readback);
                });
            readback = null;
            if (origin is not null) Track(job, origin);
        }
        catch
        {
            if (admitted is not null) _sessionCommits.Remove(admitted);
            readback?.Dispose(); copy.Dispose(); throw;
        }
    }

    /// <summary>Selects the session's working copy in the active panel; the session itself is unchanged.</summary>
    internal void ShowSessionWorkingCopy(EditSessionRecord session)
    {
        if (ActiveTab is not { } tab || Path.GetDirectoryName(session.WorkingPath) is not { } folder) return;
        tab.Navigate(Location.FileSystem(folder), Path.GetFileName(session.WorkingPath));
        Notify("Your working copy is selected. Copy it (F5) into an archive or folder you choose; the edit session stays open.");
    }

    private async Task AcknowledgeNativeArchiveEditAsync(Job job, SessionCommit commit)
    {
        var published = job.ArchiveEditPublication ?? throw new IOException("No native publication receipt is available. The previous edit baseline is kept.");
        // Plan §12.2 "Commit completed": read back what was actually published before the baseline moves.
        var reviewed = await ReviewArchiveTargetAsync(published.Path, published.Profile, commit.ReadbackPassword, title: "Check saved archive",
            message: "Reading the complete saved archive to confirm your edit before updating the saved edit baseline.");
        CheckSessionAction();
        if (reviewed is null) throw new OperationCanceledException();
        var acknowledged = await RunArchivePreparationAsync(published.Path, "Confirm archive edit",
            "Reading the actual published member before updating the saved edit baseline.", ct =>
        {
            CheckSessionAction();
            return Services.Jobs.ReadReviewedArchive(reviewed, held => Services.EditSessions.CommittedArchive(
                commit.Session, new NativeEditSource(this, reviewed, held, commit.Session.MemberPath, commit.ReadbackPassword), commit.Copy, published, ct), ct);
        });
        if (acknowledged is null) throw new OperationCanceledException();
    }
}
