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
        public IContentSource OpenContent(CancellationToken ct) => owner.Services.Archives.OpenReviewedMember(Target, Member.Name, password, ct);
        public void ReviewSource(CancellationToken ct)
        {
            owner.CheckSessionAction(); held.Review(Target.Native, ct); owner.CheckSessionAction();
        }
        public string WorkingIdentity(SafeFileHandle handle) => owner.Services.Archives.GetOpenedSourceIdentity(handle) ??
            throw new IOException("The private edit file has no stable native identity.");
        public void PreserveOrigin(FileStream working, CancellationToken ct)
        {
            if (OperatingSystem.IsWindows()) FileCat.Platform.Windows.Archives.WindowsArchiveEditOrigin.Copy(working, Target.Native.Origin, ct);
            else ArchiveUnixOrigin.CopyToNewWorkingFile(working.SafeFileHandle, Target.Native.Origin, ct);
        }
    }

    private sealed record NativeEditSelection(string RawPath);

    private async Task EditNativeArchiveMemberAsync(ItemRef item)
    {
        try { await PrepareNativeArchiveEditAsync(item); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { if (EditSessionsActive) Notify("Cannot edit this archive member: " + Formatters.SafeName(error.Message), true); }
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
            string raw = Services.Archives.ReviewedMemberPath(command.Target, item, command.Password, ct);
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
        var location = FileCat.Archives.ArchiveProvider.ForFile(session.ArchivePath, session.ArchiveFormat switch
        {
            ArchiveWriteFormat.Tar => FileCat.Archives.ArchiveKind.Tar,
            ArchiveWriteFormat.TarGzip => FileCat.Archives.ArchiveKind.TarGzip,
            ArchiveWriteFormat.TarBzip2 => FileCat.Archives.ArchiveKind.TarBzip2,
            ArchiveWriteFormat.TarZstd => FileCat.Archives.ArchiveKind.TarZstd,
            ArchiveWriteFormat.Rar5 => FileCat.Archives.ArchiveKind.Rar,
            ArchiveWriteFormat.SevenZip => FileCat.Archives.ArchiveKind.SevenZip,
            _ => throw new NotSupportedException("This archive edit profile is unavailable."),
        });
        using var command = await PrepareNativeArchiveCommandAsync(location, requestedProfile: session.ArchiveProfile);
        if (command is null)
        {
            CheckSessionAction();
            var answer = await Dialogs.ShowCustomAsync("Archive edit kept", new Avalonia.Controls.TextBlock
            {
                Text = "The archive was not prepared for this commit. Your working copy is kept; you can save it to a folder.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 640,
            }, [new("Cancel", "cancel", IsCancel: true), new("Save my copy…", "copy", IsDefault: true)]);
            if (answer as string == "copy") await SaveSessionCopyAsync(session);
            return;
        }
        var check = EditSessionStore.ReviewArchive(session, command.Target);
        if (check == CommitCheck.MemberChanged)
        {
            var answer = await Dialogs.ShowCustomAsync("Archive edit conflict", new Avalonia.Controls.TextBlock
            {
                Text = $"\"{session.MemberPath}\" changed, disappeared or became ambiguous. Your working copy is kept.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 640,
            }, [new("Cancel", "cancel", IsCancel: true), new("Save my copy…", "copy", IsDefault: true)]);
            if (answer as string == "copy") await SaveSessionCopyAsync(session);
            return;
        }
        bool rebase = check == CommitCheck.ArchiveChanged;
        if (rebase && !await Dialogs.ConfirmAsync("The archive changed",
            $"The complete current archive was checked. \"{session.MemberPath}\" still has the same bytes and metadata. Apply this edit to the current archive?",
            "Apply edit to current archive")) return;
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

    private async Task AcknowledgeNativeArchiveEditAsync(Job job, SessionCommit commit)
    {
        var published = job.ArchiveEditPublication ?? throw new IOException("No native publication receipt is available. The previous edit baseline is kept.");
        var reviewed = await ReviewArchiveTargetAsync(published.Path, published.Profile, commit.ReadbackPassword);
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
